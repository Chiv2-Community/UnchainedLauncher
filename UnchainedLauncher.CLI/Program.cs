namespace UnchainedLauncher.CLI;

public static class Program {
    public static async Task<int> Main(string[] args) {
        ServerOptions options;
        try {
            options = ServerOptions.Parse(args);
        }
        catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Use 'server --help' for usage.");
            return 2;
        }
        if (options.Help) { Console.WriteLine(ServerOptions.HelpText); return 0; }
        if (options.Dlls.Count != 0) {
            Console.Error.WriteLine("WARNING: --dll version selection is NOT implemented; ALL DLLs in TBL/Binaries/Win64/Plugins will be injected, regardless of the requested IDs/versions.");
            Console.Error.WriteLine("Tracking: https://github.com/Chiv2-Community/UnchainedLauncher/issues/149");
        }
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += handler;
        try {
            using var process = await ServerLauncher.LaunchAsync(options, stop.Token);
            try {
                using var dashboard = new ServerDashboard(!options.NoTui);
                var monitor = new ServerMonitor(process, ServerLauncher.CreateQuery(options));
                await monitor.RunAsync(dashboard, stop);
                return stop.IsCancellationRequested ? 0 : process.ExitCode;
            }
            finally {
                // The CLI owns its server: cancellation and monitor failures must not
                // leave an unobserved server running. Kill is Core's existing stop behavior.
                if (!process.HasExited) {
                    Console.Error.WriteLine("Stopping server...");
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }
}