
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    public interface IAssetProcessor {
        void Process(ScanContext ctx, PakScanResult result);
    }
}