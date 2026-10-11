using System.ComponentModel;
using System.Diagnostics;
using UnchainedLauncher.Core.Services.Server.A2S;

namespace UnchainedLauncher.CLI;

public record ServerSample(DateTimeOffset Time, TimeSpan Uptime, double? CpuPercent,
    double? MemoryMiB, int? Players, int? MaxPlayers, string? Map, bool QueryAvailable);
public record TimelineEvent(DateTimeOffset Time, string Message);

public sealed class ServerHistory {
    public const int MaxSamples = 120;
    public const int MaxEvents = 50;
    private readonly Queue<ServerSample> _samples = new();
    private readonly Queue<TimelineEvent> _events = new();
    public IReadOnlyCollection<ServerSample> Samples => _samples;
    public IReadOnlyCollection<TimelineEvent> Events => _events;

    public void AddEvent(DateTimeOffset time, string message) {
        _events.Enqueue(new TimelineEvent(time, message));
        while (_events.Count > MaxEvents) _events.Dequeue();
    }

    public void Add(ServerSample sample) {
        var previous = _samples.LastOrDefault();
        if (previous is null || previous.QueryAvailable != sample.QueryAvailable)
            AddEvent(sample.Time, sample.QueryAvailable ? "A2S responding" : "A2S unavailable; waiting for server query response");
        if (sample.QueryAvailable) {
            if (previous?.Map != sample.Map) AddEvent(sample.Time, $"Map: {sample.Map}");
            if (previous?.Players != sample.Players) AddEvent(sample.Time, $"Players: {sample.Players}/{sample.MaxPlayers}");
        }
        _samples.Enqueue(sample);
        while (_samples.Count > MaxSamples) _samples.Dequeue();
    }
}

public sealed class ServerMonitor(Process process, IA2S query) {
    public ServerHistory History { get; } = new();

    public async Task RunAsync(ServerDashboard dashboard, CancellationTokenSource stop) {
        var started = new DateTimeOffset(process.StartTime);
        var previousTime = Stopwatch.GetTimestamp();
        var previousCpu = process.TotalProcessorTime;
        History.AddEvent(DateTimeOffset.Now, $"Server started (PID {process.Id})");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try {
            while (!stop.IsCancellationRequested && !process.HasExited) {
                if (dashboard.StopRequested()) { stop.Cancel(); break; }
                A2SInfo? info = null;
                try {
                    info = await query.InfoAsync().WaitAsync(stop.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
                catch (Exception) {
                    // Missing/invalid A2S replies are normal during startup and outages.
                    // Never convert an unavailable query into a zero player count.
                }
                if (process.HasExited) break;
                process.Refresh();
                double? cpu = null;
                double? memory = null;
                try {
                    var nowTicks = Stopwatch.GetTimestamp();
                    var elapsed = Stopwatch.GetElapsedTime(previousTime, nowTicks).TotalSeconds;
                    var totalCpu = process.TotalProcessorTime;
                    cpu = Math.Clamp((totalCpu - previousCpu).TotalSeconds / elapsed / System.Environment.ProcessorCount * 100, 0, 100);
                    memory = process.PrivateMemorySize64 / 1048576d;
                    previousCpu = totalCpu;
                    previousTime = nowTicks;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) {
                    if (process.HasExited) break;
                }
                var now = DateTimeOffset.Now;
                var sample = new ServerSample(now, now - started, cpu, memory,
                    info?.Players, info?.MaxPlayers, info?.Map, info is not null);
                History.Add(sample);
                dashboard.Render(process.Id, sample, History);
                if (!await timer.WaitForNextTickAsync(stop.Token)) break;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally {
            var message = stop.IsCancellationRequested ? "Stop requested" : process.HasExited
                ? $"Server exited (code {process.ExitCode})" : "Monitoring ended";
            History.AddEvent(DateTimeOffset.Now, message);
            Console.Error.WriteLine(message);
        }
    }
}