using System.Diagnostics;
using System.Text.Json;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.CLI;

internal static class ProtonGameLauncher {
    internal static async Task<LaunchedGame> LaunchAsync(CliLaunchOptions options, LaunchOptions launch,
        DllLaunchSession dlls, CancellationToken token) {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Proton runs on Linux.");
        var runtime = options.Shared.Proton ?? throw new InvalidOperationException("Proton is not configured.");
        var directory = ProtonRuntime.CreatePrivateDirectory();
        var heartbeat = Path.Combine(directory, "heartbeat");
        var stopFile = Path.Combine(directory, "stop");
        var id = Guid.NewGuid().ToString("N");
        Process? supervisor = null;
        ForwardedConsole? console = null;
        var pulseStop = new CancellationTokenSource();
        var drainStop = new CancellationTokenSource();
        Task pulse = Task.CompletedTask;
        Task output = Task.CompletedTask, errors = Task.CompletedTask;
        try {
            if (options is ServerOptions) console = new ForwardedConsole();
            var files = new List<ProtonDll>();
            foreach (var dll in dlls.GetProtonDlls())
                files.Add(dll with { Path = await runtime.WinePathAsync(dll.Path, true, token) });
            var executable = Path.Combine(options.Shared.Chivalry2Path, FilePaths.GameBinPath);
            var arguments = UnchainedChivalry2Launcher.BuildArguments(launch);
            if (EpicLaunchArguments.Required(options.Shared.Platform, options.GameArguments)) {
                var epic = await EpicLaunchArguments.AcquireAsync(options.Shared.Chivalry2Path, token);
                arguments += " " + string.Join(" ", epic.Select(ArgumentEscaper.Escape));
            }
            var request = new ProtonLaunchRequest(await runtime.WinePathAsync(executable, true, token),
                await runtime.WinePathAsync(Path.GetDirectoryName(executable)!, true, token),
                arguments, files, options is ServerOptions);
            var requestPath = Path.Combine(directory, "request.json");
            await using (var stream = new FileStream(requestPath, new FileStreamOptions {
                Mode = FileMode.CreateNew, Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            })) await JsonSerializer.SerializeAsync(stream, request, cancellationToken: token);
            await File.WriteAllTextAsync(heartbeat, "", token);
            // The Steam.exe wrapper used by `run` discards inherited output handles.
            // `runinprefix` keeps the native pipes connected to the helper.
            var info = runtime.StartInfo([console != null ? "runinprefix" : "run", runtime.HelperPath, "launch",
                await runtime.WinePathAsync(requestPath, true, token)]);
            info.WorkingDirectory = options.Shared.Chivalry2Path;
            info.Environment["UNCHAINED_LAUNCH_ID"] = id;
            supervisor = Process.Start(info) ?? throw new InvalidOperationException("Could not start Proton.");
            output = console != null
                ? console.CaptureAsync(supervisor.StandardOutput.BaseStream, drainStop.Token)
                : supervisor.StandardOutput.BaseStream.CopyToAsync(Stream.Null, drainStop.Token);
            errors = console != null
                ? console.CaptureAsync(supervisor.StandardError.BaseStream, drainStop.Token)
                : supervisor.StandardError.BaseStream.CopyToAsync(Stream.Null, drainStop.Token);
            pulse = PulseAsync();
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
            startup.CancelAfter(TimeSpan.FromMinutes(3));
            Process? game = null;
            while (!startup.IsCancellationRequested && !supervisor.HasExited) {
                if (File.Exists(Path.Combine(directory, "ready"))) {
                    game = FindGame(id);
                    if (game != null) break;
                }
                await Task.Delay(100, startup.Token);
            }
            startup.Token.ThrowIfCancellationRequested();
            if (game == null) throw new InvalidOperationException("Proton game startup or DLL injection failed.");
            var owned = supervisor;
            _ = CleanupAsync();
            return new LaunchedGame(owned, game, async () => {
                if (owned.HasExited) return;
                RequestStop(stopFile);
                try { await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException) {
                    if (!game.HasExited) game.Kill(entireProcessTree: true);
                    if (!owned.HasExited) owned.Kill(entireProcessTree: true);
                }
            }) { ConsoleOutput = console };

            async Task PulseAsync() {
                try {
                    while (!pulseStop.IsCancellationRequested) {
                        File.SetLastWriteTimeUtc(heartbeat, DateTime.UtcNow);
                        await Task.Delay(1000, pulseStop.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { /* The helper can remove its IPC files after exiting. */ }
            }

            async Task CleanupAsync() {
                try {
                    await owned.WaitForExitAsync();
                }
                catch (InvalidOperationException) { /* The client releases its process handles after launch. */ }
                finally {
                    if (console != null) await console.FinishAsync();
                    drainStop.Cancel();
                    try { await Task.WhenAll(output, errors); }
                    catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
                    drainStop.Dispose();
                    pulseStop.Cancel();
                    await pulse;
                    pulseStop.Dispose();
                    ProtonRuntime.CleanupDirectory(directory);
                    if (options is ClientOptions) owned.Dispose();
                }
            }
        }
        catch {
            console?.Dispose();
            if (supervisor != null && !supervisor.HasExited) {
                RequestStop(stopFile);
                try { await supervisor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException) {
                    using var game = FindGame(id);
                    if (game is { HasExited: false }) game.Kill(entireProcessTree: true);
                    if (!supervisor.HasExited) supervisor.Kill(entireProcessTree: true);
                    await supervisor.WaitForExitAsync();
                }
            }
            pulseStop.Cancel();
            await pulse;
            pulseStop.Dispose();
            drainStop.Cancel();
            try { await Task.WhenAll(output, errors); }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            drainStop.Dispose();
            supervisor?.Dispose();
            ProtonRuntime.CleanupDirectory(directory);
            throw;
        }
    }

    private static void RequestStop(string path) {
        try { File.WriteAllText(path, ""); }
        catch (DirectoryNotFoundException) { /* The helper already finished and removed its IPC directory. */ }
    }

    // Wine PIDs are not Unix PIDs. Match the unique inherited launch identity, not a process name alone.
    internal static Process? FindGame(string launchId, string procRoot = "/proc") {
        foreach (var directory in Directory.EnumerateDirectories(procRoot)) {
            if (!int.TryParse(Path.GetFileName(directory), out var pid)) continue;
            try {
                var environment = File.ReadAllText(Path.Combine(directory, "environ")).Split('\0');
                if (!environment.Contains("UNCHAINED_LAUNCH_ID=" + launchId, StringComparer.Ordinal)) continue;
                var command = File.ReadAllText(Path.Combine(directory, "cmdline")).Split('\0')[0];
                if (!Path.GetFileName(command.Replace('\\', '/')).Equals("Chivalry2-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase)) continue;
                var process = Process.GetProcessById(pid);
                if (!process.HasExited) return process;
                process.Dispose();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { }
        }
        return null;
    }
}
