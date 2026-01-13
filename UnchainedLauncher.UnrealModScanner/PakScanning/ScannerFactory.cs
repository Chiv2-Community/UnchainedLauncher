using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning.Processors;

namespace UnchainedLauncher.UnrealModScanner.PakScanning;

public static class ScannerFactory {
    /// <summary>
    /// Configures an orchestrator for standard mod discovery.
    /// Excludes the main game pak and uses all 4 mod-logic processors.
    /// </summary>
    public static ModScanOrchestrator CreateModScanner(IEnumerable<string> standardDirs, ScanOptions options) {
        var orchestrator = new ModScanOrchestrator();
        var dirs = standardDirs.ToList();

        // Register the 4 mod-specific processors
        orchestrator.AddModProcessor(new MarkerProcessor());
        orchestrator.AddModProcessor(new MapProcessor());
        orchestrator.AddModProcessor(new ReplacementProcessor(dirs));
        orchestrator.AddModProcessor(new ArbitraryBlueprintProcessor(dirs));

        foreach (var processor in options.CdoProcessors) {
            orchestrator.AddModProcessor(new GenericCdoProcessor(processor.TargetClassName, processor.Properties));
        }
        foreach (var processor in options.MarkerProcessors) {
            orchestrator.AddModProcessor(new ReferenceDiscoveryProcessor(processor.MarkerClassName, processor.MapPropertyName));
        // B. Generic CDO Extraction: Mapping PropertyConfig to (string, Type)
        // Note: Since you use EExtractionMode, we map it to 'object' or 'string' 
        // depending on what your GenericCdoProcessor expects for Type.
        //var fieldTuples = processor.ReferencedBlueprintProperties
        //    .Select(p => (p.Name, typeof(object)))
        //    .ToList();

        //orchestrator.AddModProcessor(new GenericCdoProcessor(
        //    processor.MarkerClassName,
        //    fieldTuples,
        //    processor.ReferencedBlueprintProperties
        //));
        }
        //foreach (var target in options.Targets) {
        //    // If the target defines property mappings, we need a processor to find them
        //    orchestrator.AddModProcessor(new ReferenceDiscoveryProcessor(target.ClassName, target.));

        //    // 3. NEW: Generic CDO Processor
        //    // This handles the actual extraction of the fields defined in the JSON config
        //    orchestrator.AddModProcessor(new GenericCdoProcessor(target));
        //}
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