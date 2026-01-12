
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning {
    public interface IAssetProcessor {
        void Process(ScanContext ctx, PakScanResult result);
    }
}
