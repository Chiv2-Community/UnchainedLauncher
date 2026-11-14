using CUE4Parse.UE4.Assets;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Objects.UObject;
using UnchainedLauncher.UnrealModScanner.Utility;

namespace UnchainedLauncher.UnrealModScanner.AssetSources {
    public sealed class PackageAssetSource : IAssetSource {
        private readonly IPackage _package;
        private readonly GameFile _file;

        public PackageAssetSource(IPackage package, GameFile file) {
            _package = package;
            _file = file;
        }
        public IPackage Package => _package;

        public string FilePath => _file.Path;

        public IEnumerable<UClass> GetClassExports()
            => PackageUtility.GetClassExports(_package);

        public string GetHash(UClass classExport)
            => HashUtility.GetAssetHash(_file);
    }

}
