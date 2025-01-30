using DiscriminatedUnions;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods.Registry {
    public class ModRegistryCodec: DerivedJsonCodec<ModRegistryMetadata, IModRegistry> {
        public static ModRegistryCodec Instance { get; } = new ModRegistryCodec();
        
        public ModRegistryCodec() : base(ToJsonType, ToClassType) {}

        public static IModRegistry ToClassType(ModRegistryMetadata metadata) =>
            metadata switch {
                LocalModRegistryMetadata m => new LocalModRegistry(m.PakReleaseDir, ModRegistryDownloaderCodec.ToClassType(m.DownloaderMetadata)),
                GithubModRegistryMetadata m => new GithubModRegistry(m.Org, m.RepoName, ModRegistryDownloaderCodec.ToClassType(m.DownloaderMetadata)),
                AggregateModRegistryMetadata m => new AggregateModRegistry(m.Registries.Select(ToClassType).ToArray()),

                _ => throw new ArgumentOutOfRangeException(nameof(metadata), metadata,
                    $"Attempt to initialize unknown IModRegistry implementation: {metadata}")
            };

        public static ModRegistryMetadata ToJsonType(IModRegistry registry) =>
            registry switch {
                LocalModRegistry d => new LocalModRegistryMetadata(d.RegistryPath, ModRegistryDownloaderCodec.ToJsonType(d.ModRegistryDownloader)),
                GithubModRegistry d => new GithubModRegistryMetadata(d.Organization, d.RepoName, ModRegistryDownloaderCodec.ToJsonType(d.ModRegistryDownloader)),
                AggregateModRegistry d => new AggregateModRegistryMetadata(d.ModRegistries.Select(ToJsonType).ToArray()),

                _ => throw new ArgumentOutOfRangeException(nameof(registry), registry,
                    $"Attempt to extract metadata from unknown IModRegistry implementation: {registry}")
            };
    }

    [UnionTag(nameof(Kind))]
    [UnionCase(typeof(LocalModRegistryMetadata), ModRegistryMetadataKind.LocalModRegistry)]
    [UnionCase(typeof(GithubModRegistryMetadata), ModRegistryMetadataKind.GithubModRegistry)]
    [UnionCase(typeof(AggregateModRegistryMetadata), ModRegistryMetadataKind.AggregateModRegistry)]
    public abstract record ModRegistryMetadata(string Kind);
    public record LocalModRegistryMetadata(string PakReleaseDir, ModRegistryDownloaderMetadata DownloaderMetadata) : ModRegistryMetadata(ModRegistryMetadataKind.LocalModRegistry);
    public record GithubModRegistryMetadata(string Org, string RepoName, ModRegistryDownloaderMetadata DownloaderMetadata) : ModRegistryMetadata(ModRegistryMetadataKind.GithubModRegistry);
    public record AggregateModRegistryMetadata(ModRegistryMetadata[] Registries) : ModRegistryMetadata(ModRegistryMetadataKind.AggregateModRegistry);

    internal static class ModRegistryMetadataKind {
        public const string LocalModRegistry = "LocalModRegistry";
        public const string GithubModRegistry = "GithubModRegistry";
        public const string AggregateModRegistry = "AggregateModRegistry";
    }

}