using System.Diagnostics;
using UnchainedLauncher.Core.Services.Server.A2S;
using UnchainedLauncher.Core.Utilities;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class ServerMonitorTests {
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
        for (var i = 0; i < 500; i++) history.Add(Sample(i, "Map", true));
        Assert.Equal(ServerHistory.MaxSamples, history.Samples.Count);
        Assert.Equal(ServerHistory.MaxEvents, history.Events.Count);
        Assert.Equal(499, history.Samples.Last().Players);
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
        Assert.Equal("?@", ServerDashboard.Sparkline([0, null, 100], 2, 100));
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
        Assert.True(monitor.History.Events.Last().Message == "Stop requested");
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