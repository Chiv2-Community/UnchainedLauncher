using DiscriminatedUnions;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods.Registry.Downloader {
    public class ModRegistryDownloaderCodec: DerivedJsonCodec<ModRegistryDownloaderMetadata, IModRegistryDownloader> {
        public static ModRegistryDownloaderCodec Instance { get; } = new ModRegistryDownloaderCodec();
        
        public ModRegistryDownloaderCodec(): base(ToJsonType, ToClassType) {}

        public static IModRegistryDownloader ToClassType(ModRegistryDownloaderMetadata metadata) =>
            metadata switch {
                LocalFilePakDownloaderMetadata m => new LocalFilePakDownloader(m.PakReleaseDir),
                HttpPakDownloaderMetadata m => new HttpPakDownloader(m.UrlPattern),

                _ => throw new ArgumentOutOfRangeException(nameof(metadata), metadata,
                    $"Attempt to initialize unknown IModRegistryDownloader implementation: {metadata}")
            };

        public static ModRegistryDownloaderMetadata ToJsonType(IModRegistryDownloader downloader) =>
            downloader switch {
                LocalFilePakDownloader d => new LocalFilePakDownloaderMetadata(d.PakReleasesDir),
                HttpPakDownloader d => new HttpPakDownloaderMetadata(d.UrlPattern),

                _ => throw new ArgumentOutOfRangeException(nameof(downloader), downloader,
                    $"Attempt to extract metadata from unknown IModRegistryDownloader implementation: {downloader}")
            };
    }

    [UnionTag(nameof(Kind))]
    [UnionCase(typeof(LocalFilePakDownloaderMetadata), ModRegistryDownloaderMetadataKind.LocalFilePakDownloader)]
    [UnionCase(typeof(HttpPakDownloaderMetadata), ModRegistryDownloaderMetadataKind.HttpPakDownloader)]
    public abstract record ModRegistryDownloaderMetadata(string Kind);
    public record LocalFilePakDownloaderMetadata(string PakReleaseDir) : ModRegistryDownloaderMetadata(ModRegistryDownloaderMetadataKind.LocalFilePakDownloader);
    public record HttpPakDownloaderMetadata(string UrlPattern) : ModRegistryDownloaderMetadata(ModRegistryDownloaderMetadataKind.HttpPakDownloader);

    internal static class ModRegistryDownloaderMetadataKind {
        public const string LocalFilePakDownloader = "LocalFilePakDownloader";
        public const string HttpPakDownloader = "HttpPakDownloader";
    }
}