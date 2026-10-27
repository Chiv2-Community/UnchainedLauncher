using System.ComponentModel;
using System.Diagnostics;
using UnchainedLauncher.Core.Services.Server.A2S;

namespace UnchainedLauncher.CLI;

public record ServerSample(DateTimeOffset Time, TimeSpan Uptime, double? CpuPercent,
    double? MemoryMiB, int? Players, int? MaxPlayers, string? Map, bool QueryAvailable,
    string? GameType = null, int? HandleCount = null, double? PeakMemoryMiB = null) {
    public bool Starting { get; init; }
}
public record TimelineEvent(DateTimeOffset Time, string Message);

public sealed class ServerHistory {
    public const int MaxSamples = 12 * 60 * 60 / 10;
    public const int MaxEvents = 50;
    public const int MaxConsoleLines = 512;
    private readonly Queue<(long Sequence, string Text)> _console = new();
    private long _consoleSequence;
    public IReadOnlyCollection<(long Sequence, string Text)> ConsoleLines => _console;

    internal void AddConsole(IEnumerable<string> lines) {
        foreach (var line in lines) {
            _console.Enqueue((++_consoleSequence, line));
            while (_console.Count > MaxConsoleLines) _console.Dequeue();
        }
    }
    private readonly Queue<ServerSample> _samples = new();
    private readonly Queue<TimelineEvent> _events = new();
    private ServerSample? _previous;
    private bool _hasQueryResponse;
    private bool _stopped;
    public string State => _stopped ? "Down" : _hasQueryResponse ? "Up" : "Starting";
    public IReadOnlyCollection<ServerSample> Samples => _samples;
    public IReadOnlyCollection<TimelineEvent> Events => _events;

    internal ServerHistory Snapshot(bool stopped = false) {
        var snapshot = new ServerHistory { _hasQueryResponse = _hasQueryResponse, _stopped = stopped };
        foreach (var sample in _samples) snapshot._samples.Enqueue(sample);
        foreach (var entry in _events) snapshot._events.Enqueue(entry);
        foreach (var entry in _console) snapshot._console.Enqueue(entry);
        snapshot._consoleSequence = _consoleSequence;
        return snapshot;
    }

    public void AddEvent(DateTimeOffset time, string message) {
        _events.Enqueue(new TimelineEvent(time, message));
        while (_events.Count > MaxEvents) _events.Dequeue();
    }

    public void Add(ServerSample sample) {
        var previous = _previous;
        if (previous is null || previous.QueryAvailable != sample.QueryAvailable)
            AddEvent(sample.Time, sample.QueryAvailable ? "A2S responding" : "A2S unavailable; waiting for server query response");
        if (sample.QueryAvailable) {
            if (previous?.Map != sample.Map) AddEvent(sample.Time, $"Map: {sample.Map}");
            if (previous?.Players != sample.Players) AddEvent(sample.Time, $"Players: {sample.Players}/{sample.MaxPlayers}");
        }
        _previous = sample;
        _hasQueryResponse |= sample.QueryAvailable;
        if (_samples.Count != 0 && sample.Time - _samples.Last().Time < TimeSpan.FromSeconds(10)) return;
        _samples.Enqueue(sample with { Starting = !_hasQueryResponse });
        while (_samples.Count > MaxSamples) _samples.Dequeue();
    }
}

public sealed class ServerMonitor(LaunchedGame process, IA2S query) {
    public ServerMonitor(Process process, IA2S query) : this(new LaunchedGame(process), query) { }
    public ServerHistory History { get; } = new();

    public async Task RunAsync(ServerDashboard dashboard, CancellationTokenSource stop) {
        await RunAsync((sample, history) => {
            if (dashboard.StopRequested()) stop.Cancel();
            dashboard.Render(process.Id, sample, history);
        }, stop.Token);
        Console.Error.WriteLine(History.Events.Last().Message);
    }

    internal async Task RunAsync(Action<ServerSample, ServerHistory> render, CancellationToken stop) {
        try {
            DateTimeOffset started;
            TimeSpan previousCpu;
            try {
                started = new DateTimeOffset(process.Metrics.StartTime);
                previousCpu = process.Metrics.TotalProcessorTime;
            }
            catch (Exception ex) when ((ex is InvalidOperationException or Win32Exception) && process.HasExited) {
                return;
            }
            var previousTime = Stopwatch.GetTimestamp();
            double? peakMemory = null;
            History.AddEvent(DateTimeOffset.Now, $"Server started (PID {process.Id})");
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (!stop.IsCancellationRequested && !process.HasExited) {
                A2SInfo? info = null;
                try {
                    info = await query.InfoAsync().WaitAsync(stop);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
                catch (Exception) {
                    // Missing/invalid A2S replies are normal during startup and outages.
                    // Never convert an unavailable query into a zero player count.
                }
                if (process.HasExited) break;
                double? cpu = null;
                double? memory = null;
                int? handles = null;
                try {
                    process.Metrics.Refresh();
                    var nowTicks = Stopwatch.GetTimestamp();
                    var elapsed = Stopwatch.GetElapsedTime(previousTime, nowTicks).TotalSeconds;
                    var totalCpu = process.Metrics.TotalProcessorTime;
                    cpu = Math.Clamp((totalCpu - previousCpu).TotalSeconds / elapsed / System.Environment.ProcessorCount * 100, 0, 100);
                    memory = process.Metrics.PrivateMemorySize64 / 1048576d;
                    peakMemory = Math.Max(peakMemory ?? 0, memory.Value);
                    handles = process.Metrics.HandleCount;
                    previousCpu = totalCpu;
                    previousTime = nowTicks;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) {
                    if (process.HasExited) break;
                }
                var now = DateTimeOffset.Now;
                var sample = new ServerSample(now, now - started, cpu, memory,
                    info?.Players, info?.MaxPlayers, info?.Map, info is not null,
                    info?.GameType, handles, peakMemory);
                History.Add(sample);
                History.AddConsole(process.ConsoleOutput?.Drain() ?? []);
                render(sample, History);
                if (!await timer.WaitForNextTickAsync(stop)) break;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally {
            if (process.ConsoleOutput != null) {
                await process.ConsoleOutput.FinishAsync();
                History.AddConsole(process.ConsoleOutput.Drain());
            }
            var message = stop.IsCancellationRequested ? "Stop requested" : process.HasExited
                ? $"Server exited (code {process.ExitCode})" : "Monitoring ended";
            History.AddEvent(DateTimeOffset.Now, message);
            var last = History.Samples.LastOrDefault() ?? new ServerSample(
                DateTimeOffset.Now, TimeSpan.Zero, null, null, null, null, null, false);
            render(last, History);
        }
    }
}
