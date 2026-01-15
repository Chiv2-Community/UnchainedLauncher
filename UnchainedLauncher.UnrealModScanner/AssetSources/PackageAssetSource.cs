using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.UObject;
using UnchainedLauncher.UnrealModScanner.Utility;

namespace UnchainedLauncher.UnrealModScanner.AssetSources {
    public sealed class PackageAssetSource : IAssetSource {
        private readonly IPackage _package;

        public PackageAssetSource(IPackage package) {
            _package = package;
        }
        public IPackage Package => _package; // FIXME: Yikes

        // FIXME: check this
        public string FilePath => _package.Name;

        public IEnumerable<UClass> GetClassExports()
            => PackageUtility.GetClassExports(_package);

        // FIXME: check this
        public string GetHash(UClass classExport)
            => HashUtility.GetAssetHash(_package.Provider, _package.Name, classExport);
    }

}