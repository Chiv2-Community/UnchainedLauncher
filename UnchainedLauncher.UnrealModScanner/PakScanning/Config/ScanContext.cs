using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Pak.Objects;
using System.Collections.Concurrent;
using UnchainedLauncher.UnrealModScanner.Utility;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Config {
    public class ModScanResult {
        public ConcurrentDictionary<string, PakScanResult> Paks { get; } = new();
    }
    public class ScanContext {
        public IFileProvider Provider { get; }
        public IPackage Package { get; }
        public string FilePath { get; }
        public FPakEntry PakEntry { get; }

        public ScanContext(IFileProvider provider, IPackage package, string filePath, FPakEntry pakEntry) {
            Provider = provider;
            Package = package;
            FilePath = filePath;
            PakEntry = pakEntry;
        }

        public string GetHash() => HashUtility.GetAssetHash(Provider, FilePath, PakEntry);

        public string GetHash(UObject export) {
            return HashUtility.GetAssetHash(Provider, FilePath, PakEntry);
        }


        /// <summary>
        /// Returns the CDOs of all classes defined in this package.
        /// </summary>
        public IEnumerable<UObject> GetClassDefaultObjects() => PackageUtility.GetClassDefaultObjects(Package);

        public T GetSingleCDO<T>() where T : UObject => PackageUtility.GetSingleCDO<T>(Package);

        public UObject GetSingleCDO() => PackageUtility.GetSingleCDO(Package);

        /// <summary>
        /// Returns the CDOs of all classes defined in this package.
        /// </summary>
        public IEnumerable<UClass> GetClassExports() => PackageUtility.GetClassExports(Package);
        public UClass GetSingleClassExport() => PackageUtility.GetSingleClassExport(Package);

        public List<AssetProcessingError> ProcessingErrors { get; } = new();
    }

    public class AssetProcessingError {
        public string ProcessorName { get; set; } = "";
        public string PackagePath { get; set; } = "";
        public string? AssetName { get; set; }
        public string ExceptionType { get; set; } = "";
        public string Message { get; set; } = "";
    }
}