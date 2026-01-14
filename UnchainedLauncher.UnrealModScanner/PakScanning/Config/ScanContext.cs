using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Pak.Objects;
using System.Collections.Concurrent;
using UnchainedLauncher.UnrealModScanner.Utility;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Config {
    public class ModScanResult {
        public ConcurrentDictionary<string, PakScanResult> Paks { get; } = new();
    }
    public record ScanContext(IFileProvider Provider, IPackage Package, string FilePath, FPakEntry PakEntry) {
        public string GetHash() => HashUtility.GetAssetHash(Provider, FilePath, PakEntry);
    }
}