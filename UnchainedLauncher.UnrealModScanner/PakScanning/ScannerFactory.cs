using UnchainedLauncher.UnrealModScanner.PakScanning.Processors;

namespace UnchainedLauncher.UnrealModScanner.PakScanning;

public static class ScannerFactory {
    /// <summary>
    /// Configures an orchestrator for standard mod discovery.
    /// Excludes the main game pak and uses all 4 mod-logic processors.
    /// </summary>
    public static ModScanOrchestrator CreateModScanner(IEnumerable<string> standardDirs) {
        var orchestrator = new ModScanOrchestrator();
        var dirs = standardDirs.ToList();

        // Register the 4 mod-specific processors
        orchestrator.AddModProcessor(new MarkerProcessor());
        orchestrator.AddModProcessor(new MapProcessor());
        orchestrator.AddModProcessor(new ReplacementProcessor(dirs));
        orchestrator.AddModProcessor(new ArbitraryBlueprintProcessor(dirs));

        return orchestrator;
    }

    /// <summary>
    /// Configures an orchestrator for a one-time deep dive into the main game pak.
    /// </summary>
    public static ModScanOrchestrator CreateInternalScanner() {
        var orchestrator = new ModScanOrchestrator();

        // Register only the lightweight inventory processor
        orchestrator.SetInternalProcessor(new GameInternalProcessor());

        return orchestrator;
    }
}