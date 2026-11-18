using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;

internal static class Program {
    // Deliberately mirrors the launcher's process-lifetime retention, not a using/exit handler.
    private static InstallationInstanceGuard? _guard;

    private static async Task<int> Main(string[] args) {
        if (!InstallationStartup.TryAcquire(out _guard, out var error)) {
            Console.WriteLine(error);
            return 2;
        }

        if (args.Contains("--initialize")) {
            File.WriteAllText("initialized", "started");
            return 0;
        }

        if (args.Contains("--convert-ownership")) {
            LegacyOwnershipMigration.Load(FilePaths.ModManagerConfigPath, new AggregateModRegistry([]));
            return 0;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => {
            File.WriteAllText("late-save", "saved");
            Console.WriteLine("SAVED");
            Console.Out.Flush();
            Console.In.ReadLine();
        };
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Yield();
        Console.WriteLine("READY");
        Console.Out.Flush();
        await Console.In.ReadLineAsync();
        return 0;
    }
}