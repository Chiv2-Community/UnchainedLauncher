using System.Diagnostics;
using UnchainedLauncher.Core.Services.Server.A2S;
using UnchainedLauncher.Core.Utilities;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class ServerMonitorTests {
    [Fact]
    public async Task AlreadyExitedProcessRecordsNormalExitWithoutQuerying() {
        var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh") {
            UseShellExecute = false,
            ArgumentList = { OperatingSystem.IsWindows() ? "/c" : "-c", "exit 7" }
        };
        using var process = Process.Start(info)!;
        await process.WaitForExitAsync();
        var monitor = new ServerMonitor(process, new PendingQuery());
        var renders = 0;
        await monitor.RunAsync((_, _) => renders++, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, renders); // Final history render, not a fabricated metrics sample.
        Assert.Empty(monitor.History.Samples);
        Assert.Equal("Server exited (code 7)", monitor.History.Events.Last().Message);
    }

    [Fact]
    public async Task CoreProgressWorksWithoutAUiSynchronizationContext() {
        await Task.Run(() => {
            Assert.Null(SynchronizationContext.Current);
            var progress = new AccumulatedMemoryProgress();
            var child = new MemoryProgress("install", 25);
            progress.AlsoTrack(child);
            Assert.Single(progress.Progresses);
            Assert.Equal(25, progress.ProgressPercentage);
            child.Report(75);
            Assert.Equal(75, progress.ProgressPercentage);
        });
    }
    private static ServerSample Sample(int? players = null, string? map = null, bool available = false) =>
        new(DateTimeOffset.Now, TimeSpan.FromHours(26), 12.5, 1024, players, players.HasValue ? 64 : null, map, available);

    [Fact]
    public void HistoryAndTimelineStayBounded() {
        var history = new ServerHistory();
        var start = DateTimeOffset.Now;
        for (var i = 0; i < ServerHistory.MaxSamples + 10; i++)
            history.Add(Sample(i, "Map", true) with { Time = start.AddSeconds(i * 10) });
        Assert.Equal(ServerHistory.MaxSamples, history.Samples.Count);
        Assert.Equal(ServerHistory.MaxEvents, history.Events.Count);
        Assert.Equal(ServerHistory.MaxSamples + 9, history.Samples.Last().Players);
    }

    [Fact]
    public void TimelineRecordsChangesButNotRepeatedIdenticalStatus() {
        var history = new ServerHistory();
        history.Add(Sample());
        Assert.Single(history.Events);
        history.Add(Sample());
        Assert.Single(history.Events);
        history.Add(Sample(0, "Map1", true));
        Assert.Equal(4, history.Events.Count);
        history.Add(Sample(0, "Map1", true));
        Assert.Equal(4, history.Events.Count);
        history.Add(Sample(2, "Map2", true));
        Assert.Equal(6, history.Events.Count);
        history.Add(Sample());
        Assert.Equal(7, history.Events.Count);
        Assert.Null(history.Samples.Last().Players);
    }

    [Fact]
    public void UnknownAndZeroPlayersAreDifferentInDashboard() {
        var history = new ServerHistory();
        Assert.Contains(ServerDashboard.FormatLines(42, Sample(), history, 80), l => l.Contains("Players unknown"));
        Assert.Contains(ServerDashboard.FormatLines(42, Sample(0, "Map", true), history, 80), l => l.Contains("Players 0/64"));
        Assert.Contains(ServerDashboard.FormatLines(42, Sample(), history, 80), l => l.Contains("1d 02:00:00"));
    }

    [Fact]
    public void SparklinesRespectWidthUnknownSamplesAndScale() {
        Assert.Equal(".?@", ServerDashboard.Sparkline([0, null, 100], 10, 100));
        Assert.Equal(".@", ServerDashboard.Sparkline([0, null, 100], 2, 100));
        Assert.Equal("..", ServerDashboard.Sparkline([0, 0], 10));
        Assert.NotEmpty(ServerDashboard.FormatLines(1, Sample(), new ServerHistory(), 1));
        Assert.DoesNotContain('\x1b', ServerDashboard.SafeText("map\x1b[2J\nInjected"));
    }

    private sealed class FailingQuery : IA2S {
        public Task<A2SInfo> InfoAsync() => Task.FromException<A2SInfo>(new TimeoutException());
    }

    private sealed class PendingQuery : IA2S {
        public Task<A2SInfo> InfoAsync() => new TaskCompletionSource<A2SInfo>().Task;
    }

    private sealed class RespondingQuery : IA2S {
        public Task<A2SInfo> InfoAsync() => Task.FromResult(new A2SInfo(17, "Server", "FFA_Courtyard", "FFA",
            "Chivalry 2", 0, 5, 64, 0, ServerType.Dedicated,
            UnchainedLauncher.Core.Services.Server.A2S.Environment.Windows, true, false));
    }

    [Fact]
    public async Task MonitorCollectsGameTypeAndResourceDetailsFromRunningProcess() {
        using var process = Process.GetCurrentProcess();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        using var dashboard = new ServerDashboard(false);
        var monitor = new ServerMonitor(process, new RespondingQuery());
        await monitor.RunAsync(dashboard, stop);
        var sample = Assert.Single(monitor.History.Samples);
        Assert.True(sample.QueryAvailable);
        Assert.Equal("FFA", sample.GameType);
        Assert.Equal("FFA_Courtyard", sample.Map);
        Assert.Equal(5, sample.Players);
        Assert.Equal(64, sample.MaxPlayers);
        Assert.NotNull(sample.HandleCount);
        Assert.True(sample.PeakMemoryMiB >= sample.MemoryMiB);
        Assert.Equal("Up", monitor.History.State);
    }

    [Fact]
    public async Task QueryFailureStillProducesResourceSamplesWithUnknownPlayers() {
        using var process = Process.GetCurrentProcess();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        using var dashboard = new ServerDashboard(false);
        var monitor = new ServerMonitor(process, new FailingQuery());
        await monitor.RunAsync(dashboard, stop);
        var sample = Assert.Single(monitor.History.Samples);
        Assert.False(sample.QueryAvailable);
        Assert.Null(sample.Players);
        Assert.NotNull(sample.MemoryMiB);
        Assert.NotNull(sample.HandleCount);
        Assert.Equal(sample.MemoryMiB, sample.PeakMemoryMiB);
        Assert.True(monitor.History.Events.Last().Message == "Stop requested");
    }

    [Fact]
    public void DashboardIncludesAllRunningServerGuiDataWithoutSecrets() {
        var options = Assert.IsType<ServerOptions>(CommandLineOptions.Parse([
            "server", "--chivalry2-path", ".", "--platform", "Steam",
            "--name", "Parity server", "--local-ip", "192.0.2.1",
            "--game-port", "7000", "--query-port", "7001", "--ping-port", "7002", "--rcon-port", "7003",
            "--server-mod", "/Game/Mods/Example.Actor_C", "--password", "must-not-display"
        ]));
        var sample = Sample(5, "FFA_Courtyard", true) with {
            GameType = "FFA",
            HandleCount = 1234,
            PeakMemoryMiB = 2048
        };
        var history = new ServerHistory();
        history.Add(sample);
        var text = string.Join('\n', ServerDashboard.FormatLines(42, sample, history, 80, options.ToServerLaunchOptions()));
        foreach (var expected in new[] { "Parity server", "PID 42", "1d 02:00:00", "Players 5/64",
            "FFA_Courtyard", "Game Type FFA", "Game Port 7000", "Query Port 7001", "Beacon Port 7002",
            "RCON Port 7003", "Local IP 192.0.2.1", "/Game/Mods/Example.Actor_C", "State Up",
            "Restarts 0", "Private memory 1024.0 MiB", "Peak memory 2048.0 MiB", "Handle Count 1234",
            "Uptime S=start U=up U", "Memory", "Players", "12 hours", "10s samples" })
            Assert.Contains(expected, text);
        Assert.DoesNotContain("must-not-display", text);
    }

    [Fact]
    public void HistorySamplesEveryTenSecondsAndKeepsUpStateThroughQueryOutages() {
        var start = DateTimeOffset.Now;
        var history = new ServerHistory();
        history.Add(Sample() with { Time = start });
        Assert.Equal("Starting", history.State);
        history.Add(Sample(0, "Map", true) with { Time = start.AddSeconds(1) });
        Assert.Single(history.Samples);
        Assert.Equal("Up", history.State);
        history.Add(Sample() with { Time = start.AddSeconds(10) });
        Assert.Equal(2, history.Samples.Count);
        Assert.True(history.Samples.First().Starting);
        Assert.False(history.Samples.Last().Starting);
        Assert.Null(history.Samples.Last().Players);
        Assert.Equal("Up", history.State);
    }

    [Fact]
    public void NarrowTerminalWrappingPreservesAllData() {
        const string line = "Mods /Game/Mods/LongActorName.Actor_C | RCON Port 7003";
        var wrapped = ServerDashboard.WrapLine(line, 12).ToArray();
        Assert.All(wrapped, part => Assert.InRange(part.Length, 1, 12));
        Assert.Equal(line, string.Concat(wrapped));
        Assert.Equal([""], ServerDashboard.WrapLine("", 1));
    }

    [Fact]
    public async Task CancellationInterruptsPendingQuery() {
        using var process = Process.GetCurrentProcess();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var dashboard = new ServerDashboard(false);
        var monitor = new ServerMonitor(process, new PendingQuery());
        await monitor.RunAsync(dashboard, stop).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(monitor.History.Samples);
        Assert.Equal("Stop requested", monitor.History.Events.Last().Message);
    }
}