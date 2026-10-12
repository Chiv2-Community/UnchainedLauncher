using System.Diagnostics;

namespace UnchainedLauncher.CLI;

public static class Program {
    public static async Task<int> Main(string[] args) {
        CliCommandOptions command;
        try {
            command = CommandLineOptions.Parse(args);
        }
        catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Use '--help' for usage.");
            return 2;
        }
        if (command.Help) { Console.Write(command.HelpText); return 0; }
        if (command is CliLaunchOptions launch && launch.Shared.Dlls.Count != 0) {
            Console.Error.WriteLine("Resolving explicitly selected DLL releases from dll_db; native code will execute in the game.");
            Console.Error.WriteLine("Tracking: https://github.com/Chiv2-Community/UnchainedLauncher/issues/149");
        }
        if (command is CliLaunchOptions unsafeLaunch && unsafeLaunch.Shared.UnsafeLoadAllDlls)
            Console.Error.WriteLine("DANGER: explicitly enabled injection of ALL DLLs recursively in Plugins; arbitrary native code will execute in the game.");
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += handler;
        try {
            if (command is ModQueryOptions query) {
                var registry = query.RegistryOptions.CreateRegistry();
                return query.Kind == RegistryPackageKind.Dll
                    ? await DllQuery.RunAsync(query, registry, Console.Out, Console.Error, stop.Token)
                    : await ModQuery.RunAsync(query, registry, Console.Out, Console.Error, stop.Token);
            }
            var options = (CliLaunchOptions)command;
            using var process = await ServerLauncher.LaunchAsync(options, stop.Token);
            return await SuperviseAsync(options, process, stop);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    internal static async Task<int> SuperviseAsync(CliLaunchOptions options, Process process, CancellationTokenSource stop) {
        if (options is ClientOptions) {
            // Match the GUI's client lifecycle: launch the game, then leave it
            // running independently. Only servers are owned by this console.
            Console.WriteLine($"Client launched (PID {process.Id}).");
            return 0;
        }
        if (options is not ServerOptions server) throw new ArgumentException("Expected a launch request.");
        try {
            using var dashboard = new ServerDashboard(!server.NoTui);
            var monitor = new ServerMonitor(process, ServerLauncher.CreateQuery(server));
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
}
