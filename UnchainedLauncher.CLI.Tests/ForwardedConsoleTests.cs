using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class ForwardedConsoleTests {
    [Fact]
    public async Task StandardStreamCaptureIsBoundedAndSanitizedWithoutAListener() {
        using var console = new ForwardedConsole();
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
    [InlineData("--attach-stdio")]
    [InlineData("-attach-stdio")]
    public void AttachStdioMapsToPluginArgumentsAndStandardCapture(string flag) {
        var direct = CliLaunchOptions.Parse(["server", "--chivalry2-path", ".", "--platform", "Steam", "--attach-stdio"]);
        Assert.True(direct.AttachStdio);
        Assert.Contains("--attach-stdio", direct.ToLaunchOptions().LaunchArgs);
        var passthrough = CliLaunchOptions.Parse(["server", "--chivalry2-path", ".", "--platform", "Steam", "--", flag]);
        Assert.True(passthrough.AttachStdio);
        Assert.False(CliLaunchOptions.Parse(["server", "--chivalry2-path", ".", "--platform", "Steam"]).AttachStdio);
    }

    [Fact]
    public void YamlCanSelectStandardOutputCapture() {
        var path = Path.Combine(Path.GetTempPath(), "attach-stdio-" + Guid.NewGuid().ToString("N") + ".yaml");
        try {
            File.WriteAllText(path, "chiv2-path: .\nplatform: Steam\nserver:\n  attach-stdio: true\n");
            var options = Assert.IsType<ServerOptions>(YamlConfiguration.Load(path, client: false));
            Assert.True(options.AttachStdio);
            Assert.Contains("--attach-stdio", options.ToLaunchOptions().LaunchArgs);
        }
        finally { File.Delete(path); }
    }
    private static byte[] Frame(string text, ulong dropped = 0) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
        new { version = 1, kind = "log", level = "INFO", text, dropped }) + "\n");

    private static byte[] Frame2(string text, string kind = "log", string? module = "UnchainedPlugin::test",
        string? function = "example", uint? line = 42, ulong dropped = 0) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
        new { version = 2, kind, level = kind == "banner" ? null : "INFO", timestamp = "2026-10-28T12:34:56.789Z",
            module, function, line, thread_id = 1234, text, dropped }) + "\n");

    [Fact]
    public async Task RendersV2MessageAndMetadataWithoutRequiringFormattedText() {
        using var console = new ForwardedConsole();
        using var stream = new FragmentedStream(Frame2("世界\nsecond\u001b[2J", dropped: 2));
        await console.ReadAsync(stream);
        var lines = console.Drain();
        Assert.Contains("[12:34:56.789 INFO ] [UnchainedPlugin::test] [example:42 thread=1234] 世界", lines);
        Assert.Contains("[console: 2 records dropped by plugin]", lines);
        Assert.Contains(lines, line => line.Contains("second"));
        Assert.DoesNotContain(lines, line => line.Any(char.IsControl));
    }

    [Fact]
    public async Task AcceptsNullableV2SourceFieldsAndUnadornedBanners() {
        using var console = new ForwardedConsole();
        using var stream = new MemoryStream(Frame2("message", module: null, function: null, line: null)
            .Concat(Frame2("banner", "banner", null, null, null)).ToArray());
        await console.ReadAsync(stream);
        var lines = console.Drain();
        Assert.Contains("[12:34:56.789 INFO ] [???] [???:? thread=1234] message", lines);
        Assert.Contains("banner", lines);
    }

    [Fact]
    public async Task AcceptsLegacyV1AndV2FramesInTheSameStream() {
        using var console = new ForwardedConsole();
        using var stream = new MemoryStream(Frame("legacy").Concat(Frame2("new message")).ToArray());
        await console.ReadAsync(stream);
        var lines = console.Drain();
        Assert.Contains("legacy", lines);
        Assert.Contains(lines, line => line.EndsWith("new message"));
    }

    [Theory]
    [InlineData("timestamp", "\"not a timestamp\"")]
    [InlineData("text", "null")]
    [InlineData("level", "\"INVALID\"")]
    [InlineData("module", "123")]
    [InlineData("function", "[]")]
    [InlineData("line", "-1")]
    [InlineData("thread_id", "4294967296")]
    [InlineData("version", "3")]
    public async Task RejectsInvalidV2MetadataWithoutFaultingMonitoring(string field, string value) {
        var frame = JsonNode.Parse(Frame2("message"))!;
        frame[field] = JsonNode.Parse(value);
        using var console = new ForwardedConsole();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(frame.ToJsonString() + "\n"));
        await console.ReadAsync(stream);
        Assert.Contains(console.Drain(), line => line.Contains("disconnected"));
    }

    [Fact]
    public async Task RejectsOversizedV2Metadata() {
        using var console = new ForwardedConsole();
        using var stream = new MemoryStream(Frame2("message", module: new string('x', 1025)));
        await console.ReadAsync(stream);
        Assert.DoesNotContain(console.Drain(), line => line.Contains("message"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsoleSuppressionDoesNotSelectStandardCapture(bool attach) {
        var args = new List<string> { "server", "--chivalry2-path", ".", "--platform", "Steam", "--no-spawn-console" };
        if (attach) args.Add("--attach-stdio");
        var options = Assert.IsType<ServerOptions>(CliLaunchOptions.Parse(args.ToArray()));
        Assert.True(options.Settings.NoSpawnConsole);
        Assert.Equal(attach, options.AttachStdio);
        Assert.Contains("--no-spawn-console", options.ToLaunchOptions().LaunchArgs);
        Assert.Equal(attach, options.ToLaunchOptions().LaunchArgs.Contains("--attach-stdio"));
    }

    [Fact]
    public void YamlCanSuppressConsoleWithoutAttachment() {
        var path = Path.Combine(Path.GetTempPath(), "no-spawn-console-" + Guid.NewGuid().ToString("N") + ".yaml");
        try {
            File.WriteAllText(path, "chiv2-path: .\nplatform: Steam\nserver:\n  no-spawn-console: true\n");
            var options = Assert.IsType<ServerOptions>(YamlConfiguration.Load(path, client: false));
            Assert.True(options.Settings.NoSpawnConsole);
            Assert.False(options.AttachStdio);
            Assert.Contains("--no-spawn-console", options.ToLaunchOptions().LaunchArgs);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PrivateReceiverNeedsNoHandshakeAndAcceptsOnlyOneStream() {
        using var console = new ForwardedConsole();
        using var stream = new MemoryStream(Frame("real plugin"));
        await console.ReadAsync(stream);
        var lines = console.Drain();
        Assert.Contains("[plugin console connected]", lines);
        Assert.Contains("real plugin", lines);
        Assert.Throws<InvalidOperationException>(() => { _ = console.ReadAsync(Stream.Null); });
    }

    [Fact]
    public async Task HandlesFragmentedUnicodeMultilineAndEscapesTerminalControls() {
        using var console = new ForwardedConsole();
        var frame = Frame("世界\r\nsecond\u001b[2J\n", 3);
        using var stream = new FragmentedStream(frame);
        await console.ReadAsync(stream);
        var lines = console.Drain();
        Assert.Contains("世界", lines);
        Assert.Contains("[console: 3 records dropped by plugin]", lines);
        Assert.DoesNotContain(lines, line => line.Any(char.IsControl));
    }

    [Fact]
    public async Task DisconnectIsReportedWithoutThrowingIntoMonitoring() {
        using var console = new ForwardedConsole();
        await console.ReadAsync(Stream.Null);
        var lines = console.Drain();
        Assert.Contains(lines, line => line.Contains("file logging remains available"));
    }

    [Fact]
    public async Task RejectsOversizedFrameWithoutAnUnboundedReadLine() {
        using var console = new ForwardedConsole();
        using var stream = new MemoryStream(new byte[ForwardedConsole.MaxFrameBytes + 1]);
        await console.ReadAsync(stream);
        var lines = console.Drain();
        Assert.Contains(lines, line => line.Contains("disconnected"));
        Assert.DoesNotContain(lines, line => line.Contains('\0'));
    }

    [Fact]
    public async Task ReceiverQueueAndHistorySnapshotsAreBounded() {
        using var console = new ForwardedConsole();
        // A single multiline frame is enqueued atomically, so draining cannot hide overflow.
        var text = string.Join("\n", Enumerable.Range(0, ForwardedConsole.MaxLines + 20).Select(i => "line " + i)) + "\nlast";
        using var stream = new MemoryStream(Frame(text));
        await console.ReadAsync(stream);
        var lines = console.Drain();
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
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(frame + "\n"));
        await console.ReadAsync(stream);
        Assert.Contains(console.Drain(), line => line.Contains("disconnected"));
        Assert.False(stream.CanRead);
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes) {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
            base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], token);
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
