using System.Text;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class ForwardedConsoleTests {
    [Fact]
    public async Task StandardStreamCaptureIsBoundedAndSanitized() {
        using var console = new ForwardedConsole();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello 世界\r\n" + new string('x', 100000) + "\nunsafe\u001b[2J"));
        await console.CaptureAsync(stream);
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
    public void ServerLaunchAlwaysAttachesStdioWithoutDuplicatingExplicitArguments(string flag) {
        var defaults = CliLaunchOptions.Parse(["server", "--chivalry2-path", ".", "--platform", "Steam"]);
        Assert.Contains("--attach-stdio", defaults.ToLaunchOptions().LaunchArgs);
        var passthrough = CliLaunchOptions.Parse(["server", "--chivalry2-path", ".", "--platform", "Steam", "--", flag]);
        Assert.Equal(1, passthrough.ToLaunchOptions().LaunchArgs.Split(' ').Count(arg => arg is "--attach-stdio" or "-attach-stdio"));
    }

    [Fact]
    public void ConsoleSuppressionPreservesStandardCapture() {
        var options = Assert.IsType<ServerOptions>(CliLaunchOptions.Parse([
            "server", "--chivalry2-path", ".", "--platform", "Steam", "--no-spawn-console"]));
        Assert.True(options.Settings.NoSpawnConsole);
        Assert.Contains("--attach-stdio", options.ToLaunchOptions().LaunchArgs);
        Assert.Contains("--no-spawn-console", options.ToLaunchOptions().LaunchArgs);
    }

    [Fact]
    public void YamlCanSuppressConsoleWithoutChangingStandardCapture() {
        var path = Path.Combine(Path.GetTempPath(), "no-spawn-console-" + Guid.NewGuid().ToString("N") + ".yaml");
        try {
            File.WriteAllText(path, "chiv2-path: .\nplatform: Steam\nserver:\n  no-spawn-console: true\n");
            var options = Assert.IsType<ServerOptions>(YamlConfiguration.Load(path, client: false));
            Assert.True(options.Settings.NoSpawnConsole);
            Assert.Contains("--attach-stdio", options.ToLaunchOptions().LaunchArgs);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FormatsNativeJsonRecordsAlongsidePlainDiagnostics() {
        const string json = "{\"time\":\"2026-10-28T12:34:56.123456789+02:00\",\"level\":\"INFO\",\"module_path\":\"plugin::startup\",\"message\":\"[start] 世界\\nsecond\\u001b[2J\",\"attributes\":{\"enclosing_function\":\"start\"}}";
        using var console = new ForwardedConsole();
        using var stdout = new FragmentedStream(Encoding.UTF8.GetBytes(json + "\nplain output\n"));
        using var stderr = new MemoryStream(Encoding.UTF8.GetBytes("stderr output\n" +
            "{\"time\":\"2026-10-28T12:35:00Z\",\"level\":\"WARN\",\"target\":\"startup\",\"message\":\"warning\"}\n"));
        await Task.WhenAll(console.CaptureAsync(stdout), console.CaptureAsync(stderr));
        var lines = console.Drain();
        Assert.Contains("[12:34:56 INFO ] [plugin::startup] [start] 世界 second [2J", lines);
        Assert.Contains("plain output", lines);
        Assert.Contains("stderr output", lines);
        Assert.Contains("[12:35:00 WARN ] [startup] warning", lines);
        Assert.Equal(4, lines.Count);
    }

    [Theory]
    [InlineData("{broken json")]
    [InlineData("{\"message\":\"not a log record\"}")]
    [InlineData("{\"time\":\"invalid\",\"level\":\"INFO\",\"message\":\"hello\"}")]
    [InlineData("{\"time\":\"2026-10-28T12:34:56Z\",\"level\":7,\"message\":\"hello\"}")]
    public async Task UnrecognizedJsonRemainsPlainOutput(string text) {
        using var console = new ForwardedConsole();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await console.CaptureAsync(stream);
        Assert.Equal(text, Assert.Single(console.Drain()));
    }

    [Fact]
    public async Task ReceiverQueueAndHistorySnapshotsAreBounded() {
        using var console = new ForwardedConsole();
        var text = string.Join("\n", Enumerable.Range(0, ForwardedConsole.MaxLines + 20).Select(i => "line " + i)) + "\nlast";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await console.CaptureAsync(stream);
        var lines = console.Drain();
        Assert.Contains(lines, line => line.Contains("lines dropped by receiver"));
        Assert.DoesNotContain("line 0", lines);
        Assert.True(lines.Count <= ForwardedConsole.MaxLines + 1);
        var history = new ServerHistory();
        history.AddConsole(Enumerable.Range(0, 2000).Select(i => "history " + i));
        var snapshot = history.Snapshot();
        history.AddConsole(["newer"]);
        Assert.Equal(ServerHistory.MaxConsoleLines, snapshot.ConsoleLines.Count);
        Assert.Equal("history 1999", snapshot.ConsoleLines.Last().Text);
        Assert.Equal("newer", history.ConsoleLines.Last().Text);
        Assert.Equal(lines.Count, lines.Distinct().Count());
    }

    [Fact]
    public async Task DisposedCaptureRejectsNewStreams() {
        var console = new ForwardedConsole();
        console.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = console.CaptureAsync(Stream.Null); });
        await console.FinishAsync();
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