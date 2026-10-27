using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class ForwardedConsoleTests {
    [Fact]
    public async Task StandardStreamCaptureIsBoundedAndSanitizedWithoutAListener() {
        using var console = new ForwardedConsole(listen: false);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello 世界\r\n" + new string('x', 100000) + "\nunsafe\u001b[2J"));
        await console.CaptureAsync(stream, CancellationToken.None);
        var lines = console.Drain();
        Assert.Equal(3, lines.Count);
        Assert.Equal("hello 世界", lines[0]);
        Assert.EndsWith(" [truncated]", lines[1]);
        Assert.True(lines[1].Length < 17000);
        Assert.DoesNotContain('\u001b', lines[2]);
    }

    [Theory]
    [InlineData("--attach-std")]
    [InlineData("-attach-std")]
    public void AttachStdMapsToPluginArgumentsAndStandardCapture(string flag) {
        var direct = CliLaunchOptions.Parse(["server", "--chivalry2-path", ".", "--platform", "Steam", "--attach-std"]);
        Assert.True(direct.AttachStd);
        Assert.Contains("--attach-std", direct.ToLaunchOptions().LaunchArgs);
        var passthrough = CliLaunchOptions.Parse(["server", "--chivalry2-path", ".", "--platform", "Steam", "--", flag]);
        Assert.True(passthrough.AttachStd);
        Assert.False(CliLaunchOptions.Parse(["server", "--chivalry2-path", ".", "--platform", "Steam"]).AttachStd);
    }

    [Fact]
    public void YamlCanSelectStandardOutputCapture() {
        var path = Path.Combine(Path.GetTempPath(), "attach-std-" + Guid.NewGuid().ToString("N") + ".yaml");
        try {
            File.WriteAllText(path, "chiv2-path: .\nplatform: Steam\nserver:\n  attach-std: true\n");
            var options = Assert.IsType<ServerOptions>(YamlConfiguration.Load(path, client: false));
            Assert.True(options.AttachStd);
            Assert.Contains("--attach-std", options.ToLaunchOptions().LaunchArgs);
        }
        finally { File.Delete(path); }
    }
    private static async Task<TcpClient> Connect(ForwardedConsole console, string? token = null) {
        var client = new TcpClient();
        await client.ConnectAsync(IPEndPoint.Parse(console.Endpoint));
        var hello = JsonSerializer.Serialize(new { version = 1, kind = "hello", token = token ?? console.Token });
        await client.GetStream().WriteAsync(Encoding.UTF8.GetBytes(hello + "\n"));
        return client;
    }

    private static async Task Authenticate(TcpClient client) {
        var ack = new byte[3];
        await client.GetStream().ReadExactlyAsync(ack).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("OK\n", Encoding.ASCII.GetString(ack));
    }

    private static async Task<List<string>> Collect(ForwardedConsole console, Func<List<string>, bool> done) {
        List<string> lines = [];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!done(lines)) {
            lines.AddRange(console.Drain());
            if (!done(lines)) await Task.Delay(10, timeout.Token);
        }
        return lines;
    }

    private static byte[] Frame(string text, ulong dropped = 0) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
        new { version = 1, kind = "log", level = "INFO", text, dropped }) + "\n");

    [Fact]
    public async Task InvalidTokenCannotPublishAndDoesNotPreventRealPluginConnecting() {
        using var console = new ForwardedConsole();
        using (var bad = await Connect(console, new string('0', 64))) {
            var ack = new byte[1];
            Assert.Equal(0, await bad.GetStream().ReadAsync(ack).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        }
        Assert.Empty(console.Drain());
        using var good = await Connect(console);
        await Authenticate(good);
        await good.GetStream().WriteAsync(Frame("real plugin"));
        var lines = await Collect(console, lines => lines.Contains("real plugin"));
        Assert.Contains("[plugin console connected]", lines);
    }

    [Fact]
    public async Task HandlesFragmentedUnicodeMultilineAndEscapesTerminalControls() {
        using var console = new ForwardedConsole();
        using var client = await Connect(console);
        await Authenticate(client);
        var frame = Frame("世界\r\nsecond\u001b[2J\n", 3);
        foreach (var part in frame.Chunk(3)) await client.GetStream().WriteAsync(part);
        var lines = await Collect(console, lines => lines.Any(line => line.Contains("second")));
        Assert.Contains("世界", lines);
        Assert.Contains("[console: 3 records dropped by plugin]", lines);
        Assert.DoesNotContain(lines, line => line.Any(char.IsControl));
        Assert.DoesNotContain(lines, line => line.Contains(console.Token));
    }

    [Fact]
    public async Task DisconnectIsReportedWithoutThrowingIntoMonitoring() {
        using var console = new ForwardedConsole();
        var client = await Connect(console);
        await Authenticate(client);
        client.Dispose();
        var lines = await Collect(console, lines => lines.Any(line => line.Contains("disconnected")));
        Assert.Contains(lines, line => line.Contains("file logging remains available"));
    }

    [Fact]
    public async Task RejectsOversizedFrameWithoutAnUnboundedReadLine() {
        using var console = new ForwardedConsole();
        using var client = await Connect(console);
        await Authenticate(client);
        await client.GetStream().WriteAsync(new byte[ForwardedConsole.MaxFrameBytes + 1]);
        var lines = await Collect(console, lines => lines.Any(line => line.Contains("disconnected")));
        Assert.DoesNotContain(lines, line => line.Contains('\0'));
    }

    [Fact]
    public async Task ReceiverQueueAndHistorySnapshotsAreBounded() {
        using var console = new ForwardedConsole();
        using var client = await Connect(console);
        await Authenticate(client);
        // A single multiline frame is enqueued atomically, so draining cannot hide overflow.
        var text = string.Join("\n", Enumerable.Range(0, ForwardedConsole.MaxLines + 20).Select(i => "line " + i)) + "\nlast";
        await client.GetStream().WriteAsync(Frame(text));
        var lines = await Collect(console, lines => lines.Contains("last"));
        Assert.Contains(lines, line => line.Contains("lines dropped by receiver"));
        Assert.DoesNotContain("line 0", lines);
        Assert.True(lines.Count <= ForwardedConsole.MaxLines + 2);
        var history = new ServerHistory();
        history.AddConsole(Enumerable.Range(0, 2000).Select(i => "history " + i));
        var snapshot = history.Snapshot();
        history.AddConsole(["newer"]);
        Assert.Equal(ServerHistory.MaxConsoleLines, snapshot.ConsoleLines.Count);
        Assert.Equal("history 1999", snapshot.ConsoleLines.Last().Text);
        Assert.Equal("newer", history.ConsoleLines.Last().Text);
        Assert.Equal(lines.Count, lines.Distinct().Count());
    }

    [Theory]
    [InlineData("{\"version\":2,\"kind\":\"log\"}")]
    [InlineData("{\"version\":1,\"kind\":\"log\"}")]
    [InlineData("{bad json}")]
    [InlineData("[]")]
    [InlineData("{\"version\":1,\"kind\":\"log\",\"dropped\":-1,\"text\":\"bad\"}")]
    public async Task MalformedProtocolDoesNotFaultMonitoring(string frame) {
        using var console = new ForwardedConsole();
        using var client = await Connect(console);
        await Authenticate(client);
        await client.GetStream().WriteAsync(Encoding.UTF8.GetBytes(frame + "\n"));
        await Collect(console, lines => lines.Any(line => line.Contains("disconnected")));
    }

    [Fact]
    public void DashboardIncludesConsolePreviewAndNeutralizesControlCharacters() {
        var history = new ServerHistory();
        history.AddConsole(["[INFO] forwarded message"]);
        var lines = ServerDashboard.FormatLines(1,
            new ServerSample(DateTimeOffset.Now, TimeSpan.Zero, null, null, null, null, null, false), history, 100);
        Assert.Contains("[INFO] forwarded message", lines);
        Assert.DoesNotContain('\u001b', ServerDashboard.SafeText("unsafe\u001b[2J"));
    }
}
