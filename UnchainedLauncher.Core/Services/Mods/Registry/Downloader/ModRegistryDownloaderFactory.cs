using DiscriminatedUnions;
using LanguageExt;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Mods.Registry.Downloader {
    public static class ModRegistryDownloaderFactory {
        public static IModRegistryDownloader FromMetadata(ModRegistryDownloaderMetadata metadata) =>
            metadata switch {
                LocalFilePakDownloaderMetadata m => new LocalFilePakDownloader(m.PakReleaseDir),
                HttpPakDownloaderMetadata m => new HttpPakDownloader(m.UrlPattern),

                _ => throw new ArgumentOutOfRangeException(nameof(metadata), metadata,
                    $"Attempt to initialize unknown IModRegistryDownloader implementation: {metadata}")
            };

        public static DeserializationResult<IModRegistryDownloader> FromJson(string json) =>
            JsonHelpers.Deserialize<ModRegistryDownloaderMetadata>(json)
                .Select(FromMetadata);

        public static Option<DeserializationResult<IModRegistryDownloader>> FromJsonFile(string path) {
            if (!File.Exists(path)) return None;

            var fileContents = File.ReadAllText(path);
            return FromJson(fileContents);
        }
    }

    public static class ModRegistryExtensions {
        public static ModRegistryDownloaderMetadata ToMetadata(this IModRegistryDownloader downloader) =>
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