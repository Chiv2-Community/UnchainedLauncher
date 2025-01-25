using DiscriminatedUnions;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;
using LanguageExt;

namespace UnchainedLauncher.Core.Services.Mods {
    public class ModManagerFactory {
        public static readonly JsonFactory<ModManagerMetadata, IModManager> Instance =
            new JsonFactory<ModManagerMetadata, IModManager>(ToClassType, ToJsonType);

        public static IModManager ToClassType(ModManagerMetadata metadata) =>
            metadata switch {
                StandardModManagerMetadata m => new ModManager(
                    ModRegistryFactory.Instance.ToClassType(m.Registry),
                    m.EnabledModReleases,
                    m.Mods
                ),

                _ => throw new ArgumentOutOfRangeException(nameof(metadata), metadata,
                    $"Attempt to initialize unknown IModManager implementation: {metadata}")
            };

        public static ModManagerMetadata ToJsonType(IModManager manager) =>
            manager switch {
                ModManager m => new StandardModManagerMetadata(
                    ModRegistryFactory.Instance.ToJsonType(m.Registry),
                    m.EnabledModReleaseCoordinates,
                    m.Mods
                ),

                _ => throw new ArgumentOutOfRangeException(nameof(manager), manager,
                    $"Attempt to extract metadata from unknown IModManager implementation: {manager}")
            };
    }

    [UnionTag(nameof(Kind))]
    [UnionCase(typeof(ModManager), ModManagerMetadataKind.StandardModManager)]
    public abstract record ModManagerMetadata(string Kind);

    public record StandardModManagerMetadata(
        ModRegistryMetadata Registry,
        IEnumerable<ReleaseCoordinates> EnabledModReleases,
        IEnumerable<Mod> Mods
    ): ModManagerMetadata(ModManagerMetadataKind.StandardModManager);

    internal static class ModManagerMetadataKind {
        public const string StandardModManager = "StandardModManager";
    }
}