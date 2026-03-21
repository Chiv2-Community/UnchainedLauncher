
namespace UnchainedLauncher.UnrealModScanner.PakScanning.Config {
    public enum ScanMode {
        Mods,         // Standard fast scan (excludes main pak)
        GameInternal, // Deep inventory scan of the main pak
        All           // All pak files
    }
}