using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.UObject;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;

namespace UnchainedLauncher.UnrealModScanner.AssetSources {
    public sealed class ScanContextAssetSource : IAssetSource {
        public readonly ScanContext _ctx;

        public ScanContextAssetSource(ScanContext ctx) {
            _ctx = ctx;
        }
        public IPackage Package => _ctx.Package; // FIXME: Yikes

        public string FilePath => _ctx.FilePath;

        public IEnumerable<UClass> GetClassExports()
            => _ctx.GetClassExports();

        public string GetHash(UClass classExport)
            => _ctx.GetHash(classExport);
    }

}