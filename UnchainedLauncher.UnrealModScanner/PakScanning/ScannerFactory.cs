using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning.Orchestrators;
using UnchainedLauncher.UnrealModScanner.PakScanning.Processors;
using UnchainedLauncher.UnrealModScanner.PakScanning.Processors.Obsolete;

namespace UnchainedLauncher.UnrealModScanner.PakScanning;

public static class ScannerFactory {
    /// <summary>
    /// Configures an orchestrator for standard mod discovery.
    /// Excludes the main game pak and uses all 4 mod-logic processors.
    /// </summary>
    public static ModScanOrchestrator CreateModScanner(ScanOptions options) {
        var processors = new List<IAssetProcessor> {
            // new MarkerProcessor(),
            new MapProcessor(), 
            new ReplacementProcessor(options.VanillaAssetPaths), 
            new ArbitraryAssetProcessor(options.VanillaAssetPaths)
        };

        processors.AddRange(
            options.CdoProcessors
                .Select(processor => new GenericCdoProcessor(processor.TargetClassName, processor.Properties))
        );
        processors.AddRange(
            options.MarkerProcessors
                .Select(processor => new ReferenceDiscoveryProcessor(processor.MarkerClassName, processor.MapPropertyName))
        );

        return new ModScanOrchestrator(processors);
    }
}