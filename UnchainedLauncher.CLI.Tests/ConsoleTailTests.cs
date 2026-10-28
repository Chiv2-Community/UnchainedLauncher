using System.Diagnostics;
using System.Text;
using UnchainedLauncher.Core.Services.Server.A2S;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

[Collection("Installation directory")]
public sealed class ConsoleTailTests {
    [Theory]
    [InlineData("immediate")]
    [InlineData("query")]
    [InlineData("cancel")]
    public async Task FinalRenderAndSnapshotContainReceivedTail(string timing) {
        using var receiver = new ForwardedConsole();
        using var process = Process.Start(new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh") {
            UseShellExecute = false,
            ArgumentList = { OperatingSystem.IsWindows() ? "/c" : "-c", timing == "immediate" ? "exit 7" : OperatingSystem.IsWindows() ? "ping -n 30 127.0.0.1 >nul" : "sleep 30" }
        })!;
        using var game = new LaunchedGame(process) { ConsoleOutput = receiver };
        using var stop = new CancellationTokenSource();
        var query = new TailQuery(async () => {
            await ReadTail(receiver);
            if (timing == "cancel") {
                stop.Cancel();
                await Task.Delay(Timeout.Infinite, stop.Token);
            }
            else { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        });
        if (timing == "immediate") { await process.WaitForExitAsync(); await ReadTail(receiver); }
        var monitor = new ServerMonitor(game, query);
        ServerHistory? snapshot = null;
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        using var dashboard = new ServerDashboard(false);
        try {
            await monitor.RunAsync((sample, history) => {
                snapshot = history.Snapshot(stopped: game.HasExited);
                dashboard.Render(game.Id, sample, history);
            }, stop.Token).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(monitor.History.ConsoleLines, line => line.Text.EndsWith("fatal-tail"));
            Assert.Contains(snapshot!.ConsoleLines, line => line.Text.EndsWith("fatal-tail"));
            Assert.Contains("fatal-tail", output.ToString());
            Assert.Empty(receiver.Drain());
        }
        finally {
            Console.SetOut(original);
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    private static Task ReadTail(ForwardedConsole receiver) => receiver.CaptureAsync(new MemoryStream(
        Encoding.UTF8.GetBytes("fatal-tail\n")));

    private sealed class TailQuery(Func<Task> action) : IA2S {
        public async Task<A2SInfo> InfoAsync() { await action(); throw new TimeoutException(); }
    }

    [Fact]
    public async Task DescendantHeldPipeDoesNotBlockFinalCapture() {
        using var pipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.In,
            System.IO.HandleInheritability.None);
        using var child = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.Out,
            pipe.GetClientHandleAsString());
        using var receiver = new ForwardedConsole();
        var read = receiver.CaptureAsync(pipe);
        await receiver.FinishAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await read.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RawCapturePreservesPartialLineWhenWriterKeepsHandleOpen() {
        using var stream = new HeldTailStream();
        using var receiver = new ForwardedConsole();
        var read = receiver.CaptureAsync(stream, CancellationToken.None);
        await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await receiver.FinishAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await read.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("raw-tail", receiver.Drain());
    }

    private sealed class HeldTailStream : MemoryStream {
        internal TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _sent;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) {
            if (!_sent) {
                _sent = true;
                return Encoding.UTF8.GetBytes("raw-tail", buffer.Span);
            }
            Waiting.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }
    }
}
