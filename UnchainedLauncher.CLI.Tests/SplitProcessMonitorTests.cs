using System.Diagnostics;
using System.Text;
using UnchainedLauncher.Core.Services.Mods;
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
        Assert.Contains(final.ConsoleLines, line => line.Text == "supervisor-tail");
        Assert.Empty(game.ConsoleOutput!.Drain());
    }

    [Fact]
    public async Task ExitedGameDoesNotStopHealthyGroupPeerBeforeExplicitCancellation() {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "unchained-split-monitor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var fixture = await SplitProcess.StartAsync();
        using var healthy = Process.Start("/bin/sleep", "30")!;
        using var healthyObserver = Process.GetProcessById(healthy.Id);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try {
            var path = Path.Combine(root, "servers.yaml");
            File.WriteAllText(path, """
                chiv2-path: .
                platform: Steam
                no-tui: true
                servers:
                  crashed: { game-port: 7000, ping-port: 7001, query-port: 7002, rcon-port: 7003 }
                  healthy: { game-port: 7004, ping-port: 7005, query-port: 7006, rcon-port: 7007 }
                """);
            var group = Assert.IsType<ServerGroupOptions>(CommandLineOptions.Parse(["server", "--config-path", path]));
            foreach (var entry in group.Servers) entry.Options.Shared.Assets = new ProfileAssetPaths(entry.Id, root);
            var launches = 0;
            var queries = 0;
            var observedHealthyPeer = false;
            Assert.Equal(0, await ServerGroupRunner.RunAsync(group, stop,
                (_, _) => Task.FromResult(++launches == 1 ? fixture.Game : new LaunchedGame(healthy)),
                _ => ++queries == 1 ? new CallbackQuery(fixture.ExitSupervisorAsync) : new CallbackQuery(() => {
                    if (fixture.Game.HasExited) {
                        Assert.False(stop.IsCancellationRequested);
                        Assert.False(healthyObserver.HasExited);
                        observedHealthyPeer = true;
                        stop.Cancel();
                    }
                    return Task.CompletedTask;
                })).WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.True(observedHealthyPeer);
            Assert.True(stop.IsCancellationRequested);
            Assert.Contains("Exited (code 7)", output.ToString());
            Assert.Contains("supervisor-tail", output.ToString());
        }
        finally {
            Console.SetOut(original);
            if (!healthyObserver.HasExited) { healthyObserver.Kill(entireProcessTree: true); await healthyObserver.WaitForExitAsync(); }
            Directory.Delete(root, true);
        }
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
            await Game.ConsoleOutput!.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(
                "{\"version\":1,\"kind\":\"log\",\"level\":\"ERROR\",\"text\":\"supervisor-tail\",\"dropped\":0}\n")));
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
