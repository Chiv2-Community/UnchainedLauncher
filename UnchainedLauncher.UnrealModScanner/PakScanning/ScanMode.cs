
namespace UnchainedLauncher.UnrealModScanner.PakScanning {
    public enum ScanMode {
        ModsOnly,    // Standard fast scan (excludes main pak)
        GameInternal // Deep inventory scan of the main pak (triggered manually)
    }
}