using System.Diagnostics;
using UnchainedLauncher.Core.Services.Server.A2S;

namespace UnchainedLauncher.CLI;

internal sealed record ServerTabSnapshot(ServerSample Sample, ServerHistory History, string Status);

internal static class ServerGroupRunner {
    internal static async Task<int> RunAsync(ServerGroupOptions group, CancellationTokenSource stop,
        Func<ServerOptions, CancellationToken, Task<Process>>? launch = null,
        Func<ServerOptions, IA2S>? query = null) {
        launch ??= (options, token) => ServerLauncher.LaunchAsync(options, token);
        query ??= ServerLauncher.CreateQuery;
        using var monitoring = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        var processes = new List<Process>();
        var leases = new List<IDisposable>();
        var tasks = new List<Task>();
        var snapshots = new ServerTabSnapshot?[group.Servers.Count];
        try {
            // Reserve every instance before starting any processes.
            foreach (var entry in group.Servers) leases.Add(entry.Options.InstancePaths.AcquireLease());
            // Core preparation changes CWD and installation files. Never launch in parallel.
            foreach (var entry in group.Servers) {
                stop.Token.ThrowIfCancellationRequested();
                Console.Error.WriteLine($"Starting server {entry.Id}...");
                processes.Add(await launch(entry.Options, stop.Token));
            }

            using var dashboard = new ServerDashboard(!group.NoTui,
                tabNames: group.Servers.Select(entry => entry.Id).ToArray());
            for (var i = 0; i < processes.Count; i++) tasks.Add(MonitorAsync(i));

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (true) {
                if (dashboard.StopRequested()) stop.Cancel();
                for (var i = 0; i < snapshots.Length; i++) {
                    if (dashboard.IsInteractive && i != dashboard.SelectedTab) continue;
                    var snapshot = Volatile.Read(ref snapshots[i]);
                    if (snapshot is not null) dashboard.Render(processes[i].Id, snapshot.Sample, snapshot.History,
                        group.Servers[i].Options.ToServerLaunchOptions(), $"[{group.Servers[i].Id}] {snapshot.Status}");
                }
                if (stop.IsCancellationRequested || tasks.All(task => task.IsCompleted)) break;
                if (tasks.Any(task => task.IsFaulted))
                    await Task.WhenAll(tasks.Where(task => task.IsFaulted));
                if (!await timer.WaitForNextTickAsync(stop.Token)) break;
            }
            monitoring.Cancel();
            await Task.WhenAll(tasks);
            return stop.IsCancellationRequested ? 0 : processes.Select(process => process.ExitCode).FirstOrDefault(code => code != 0);

            async Task MonitorAsync(int index) {
                var process = processes[index];
                var monitor = new ServerMonitor(process, query(group.Servers[index].Options));
                var last = new ServerSample(DateTimeOffset.Now, TimeSpan.Zero, null, null, null, null, null, false);
                Volatile.Write(ref snapshots[index], new(last, monitor.History.Snapshot(), "Starting"));
                await monitor.RunAsync((sample, history) => {
                    last = sample;
                    Volatile.Write(ref snapshots[index], new(sample, history.Snapshot(), "Running"));
                }, monitoring.Token);
                var status = process.HasExited ? $"Exited (code {process.ExitCode}); last recorded metrics" : "Stopping";
                Volatile.Write(ref snapshots[index], new(last, monitor.History.Snapshot(stopped: process.HasExited), status));
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        finally {
            monitoring.Cancel();
            try { await Task.WhenAll(tasks); }
            catch (Exception) { /* The main loop propagates monitor failures; cleanup must still stop every process. */ }
            var failedStops = new List<int>();
            foreach (var process in processes) {
                try {
                    if (!process.HasExited) {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                }
                catch (Exception) { failedStops.Add(process.Id); }
                finally { process.Dispose(); }
            }
            foreach (var lease in leases) lease.Dispose();
            if (failedStops.Count != 0)
                throw new InvalidOperationException($"Could not stop server processes: {string.Join(", ", failedStops)}. Stop them manually.");
        }
    }
}