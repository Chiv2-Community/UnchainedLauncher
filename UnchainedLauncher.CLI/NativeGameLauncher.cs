using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;

namespace UnchainedLauncher.CLI;

internal static class NativeGameLauncher {
    internal static async Task<LaunchedGame> LaunchAsync(string executable, string installationRoot,
        LaunchOptions options, IChivalry2LaunchPreparer<LaunchOptions> preparer,
        IProcessInjector injector, bool forwardConsole) {
        var console = forwardConsole ? new ForwardedConsole() : null;
        ProcessOutputCapture? capture = null;
        CancellationTokenSource? stop = null;
        Task drain = Task.CompletedTask;
        try {
            if (console != null) {
                capture = new ProcessOutputCapture();
                stop = new CancellationTokenSource();
                drain = Task.WhenAll(console.CaptureAsync(capture.StandardOutput, stop.Token),
                    console.CaptureAsync(capture.StandardError, stop.Token));
            }
            var launcher = new UnchainedChivalry2Launcher(preparer,
                new ProcessLauncher(executable, output: capture), installationRoot, injector);
            var result = await launcher.Launch(options);
            return result.Match(
                Right: process => new LaunchedGame(process) {
                    ConsoleOutput = console,
                    CleanupOutput = () => {
                        stop?.Cancel();
                        console?.Dispose();
                        capture?.Dispose();
                        _ = drain.ContinueWith(_ => stop?.Dispose(), TaskScheduler.Default);
                    }
                },
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