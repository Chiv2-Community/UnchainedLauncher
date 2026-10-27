using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;

namespace UnchainedLauncher.CLI;

internal static class NativeGameLauncher {
    internal static async Task<LaunchedGame> LaunchAsync(string executable, string installationRoot,
        LaunchOptions options, IChivalry2LaunchPreparer<LaunchOptions> preparer,
        IProcessInjector injector, bool forwardConsole, bool attachStd = false) {
        var console = forwardConsole ? new ForwardedConsole() : null;
        ProcessOutputCapture? capture = null;
        CancellationTokenSource? stop = null;
        Task drain = Task.CompletedTask;
        try {
            var environment = new Dictionary<string, string?> {
                ["UNCHAINED_CONSOLE_HANDLE"] = null,
                ["UNCHAINED_CONSOLE_ENDPOINT"] = null,
                ["UNCHAINED_CONSOLE_TOKEN"] = null
            };
            if (console != null) {
                capture = new ProcessOutputCapture(forwardConsole: !attachStd);
                stop = new CancellationTokenSource();
                drain = attachStd
                    ? Task.WhenAll(console.CaptureAsync(capture.StandardOutput, stop.Token),
                        console.CaptureAsync(capture.StandardError, stop.Token))
                    : Task.WhenAll(console.ReadAsync(capture.ConsoleOutput!, stop.Token),
                        capture.StandardOutput.CopyToAsync(Stream.Null, stop.Token),
                        capture.StandardError.CopyToAsync(Stream.Null, stop.Token));
            }
            var launcher = new UnchainedChivalry2Launcher(preparer,
                new ProcessLauncher(executable, environment, capture), installationRoot, injector);
            var result = await launcher.Launch(options);
            return result.Match(
                Right: process => new LaunchedGame(process) { ConsoleOutput = console,
                    CleanupOutput = () => {
                        stop?.Cancel();
                        console?.Dispose();
                        capture?.Dispose();
                        _ = drain.ContinueWith(_ => stop?.Dispose(), TaskScheduler.Default);
                    } },
                // LaunchFailed.Message contains raw authentication arguments.
                Left: failure => throw new InvalidOperationException("Game launch or DLL injection failed. Check the installation, plugin, and mod files.", failure));
        }
        catch {
            stop?.Cancel();
            capture?.Dispose();
            try { await drain; }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
            stop?.Dispose();
            console?.Dispose();
            throw;
        }
    }
}
