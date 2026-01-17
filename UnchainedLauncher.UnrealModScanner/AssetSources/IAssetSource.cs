using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.UObject;

namespace UnchainedLauncher.UnrealModScanner.AssetSources {
    public interface IAssetSource {

        IPackage Package { get; }
        string FilePath { get; }

        IEnumerable<UClass> GetClassExports();
        string GetHash(UClass classExport);
    }
}