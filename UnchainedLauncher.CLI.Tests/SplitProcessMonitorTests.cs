using System.Diagnostics;
using System.Text;
using UnchainedLauncher.CLI.Launching;
using UnchainedLauncher.CLI.Monitoring;
using UnchainedLauncher.CLI.Terminal;
using UnchainedLauncher.Core.Services.Server.A2S;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

[Collection("Installation directory")]
public sealed class SplitProcessMonitorTests {
    [Fact]
    public async Task ExitedGameStillWaitsForSupervisorAndRetainsExitCodeAndTail() {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = await SplitProcess.StartAsync();
        using var game = fixture.Game;
        var queried = false;
        var monitor = new ServerMonitor(game, new CallbackQuery(async () => {
            queried = true;
            Assert.False(game.HasExited);
            await fixture.ExitSupervisorAsync();
        }));
        ServerHistory? final = null;
        await monitor.RunAsync((_, history) => final = history.Snapshot(stopped: game.HasExited),
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(queried);
        Assert.True(game.HasExited);
        Assert.Equal(7, game.ExitCode);
        Assert.Equal("Server exited (code 7)", final!.Events.Last().Message);
        Assert.Contains(final.ConsoleLines, line => line.Text.EndsWith("supervisor-tail"));
        Assert.Empty(game.ConsoleOutput!.Drain());
    }

    private sealed class CallbackQuery(Func<Task> action) : IA2S {
        public async Task<A2SInfo> InfoAsync() { await action(); throw new TimeoutException(); }
    }

    private sealed class SplitProcess(Process supervisor, LaunchedGame game) : IDisposable {
        private readonly Process _observer = Process.GetProcessById(supervisor.Id);
        internal LaunchedGame Game { get; } = game;

        internal static async Task<SplitProcess> StartAsync() {
            using var child = Process.Start("/bin/sleep", "30")!;
            var metrics = Process.GetProcessById(child.Id);
            _ = metrics.HasExited; // Acquire the independent metrics handle before game exit.
            child.Kill();
            await child.WaitForExitAsync();
            Assert.True(metrics.HasExited);
            Assert.Throws<InvalidOperationException>(() => metrics.StartTime);
            var supervisor = Process.Start(new ProcessStartInfo("/bin/sh") {
                UseShellExecute = false,
                RedirectStandardInput = true,
                ArgumentList = { "-c", "read release; exit 7" }
            })!;
            Assert.False(supervisor.HasExited);
            return new SplitProcess(supervisor, new LaunchedGame(supervisor, metrics) {
                ConsoleOutput = new ForwardedConsole()
            });
        }

        internal async Task ExitSupervisorAsync() {
            await Game.ConsoleOutput!.CaptureAsync(new MemoryStream(Encoding.UTF8.GetBytes("supervisor-tail\n")));
            await supervisor.StandardInput.WriteLineAsync("release");
            await supervisor.StandardInput.FlushAsync();
            await supervisor.WaitForExitAsync();
        }

        public void Dispose() {
            if (!_observer.HasExited) { _observer.Kill(entireProcessTree: true); _observer.WaitForExit(); }
            _observer.Dispose();
            supervisor.Dispose();
        }
    }
}