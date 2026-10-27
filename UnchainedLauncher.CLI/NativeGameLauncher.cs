using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;

namespace UnchainedLauncher.CLI;

internal static class NativeGameLauncher {
    internal static async Task<LaunchedGame> LaunchAsync(string executable, string installationRoot,
        LaunchOptions options, IChivalry2LaunchPreparer<LaunchOptions> preparer,
        IProcessInjector injector, bool forwardConsole, bool attachStd = false) {
        var console = forwardConsole ? new ForwardedConsole(listen: !attachStd) : null;
        ProcessOutputCapture? capture = null;
        CancellationTokenSource? stop = null;
        Task drain = Task.CompletedTask;
        try {
            var environment = console == null || attachStd ? null : new Dictionary<string, string?> {
                ["UNCHAINED_CONSOLE_ENDPOINT"] = console.Endpoint,
                ["UNCHAINED_CONSOLE_TOKEN"] = console.Token
            };
            if (console != null && attachStd) {
                capture = new ProcessOutputCapture();
                stop = new CancellationTokenSource();
                drain = Task.WhenAll(console.CaptureAsync(capture.StandardOutput, stop.Token),
                    console.CaptureAsync(capture.StandardError, stop.Token));
            }
            var launcher = new UnchainedChivalry2Launcher(preparer,
                new ProcessLauncher(executable, environment, capture), installationRoot, injector);
            var result = await launcher.Launch(options);
            return result.Match(
                Right: process => new LaunchedGame(process) { ConsoleOutput = console,
                    CleanupOutput = () => {
                        stop?.Cancel();
                        capture?.Dispose();
                        _ = drain.ContinueWith(_ => stop?.Dispose(), TaskScheduler.Default);
                    } },
                // LaunchFailed.Message contains raw authentication arguments.
                Left: failure => throw new InvalidOperationException("Game launch or DLL injection failed. Check the installation, plugin, and mod files.", failure));
        }
        catch {
            stop?.Cancel();
            capture?.Dispose();
            await drain;
            stop?.Dispose();
            console?.Dispose();
            throw;
        }
    }
}
