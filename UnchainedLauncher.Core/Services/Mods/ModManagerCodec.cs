using DiscriminatedUnions;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods {
    public class ModManagerCodec : DerivedJsonCodec<ModManagerMetadata, ModManager> {
        public ModManagerCodec(IModRegistry registry) : base(ToJsonType, modManager => ToClassType(modManager, registry)) { }


        public static ModManager ToClassType(ModManagerMetadata metadata, IModRegistry registry) =>
            metadata switch {
                StandardModManagerMetadata m => new ModManager(
                    registry,
                    m.EnabledModReleases
                ),

                _ => throw new ArgumentOutOfRangeException(nameof(metadata), metadata,
                    $"Attempt to initialize unknown IModManager implementation: {metadata}")
            };

        public static ModManagerMetadata ToJsonType(IModManager manager) =>
            manager switch {
                ModManager m => new StandardModManagerMetadata(
                    m.EnabledModReleaseCoordinates
                ),

                _ => throw new ArgumentOutOfRangeException(nameof(manager), manager,
                    $"Attempt to extract metadata from unknown IModManager implementation: {manager}")
            };
    }

    [UnionTag(nameof(Kind))]
    [UnionCase(typeof(StandardModManagerMetadata), ModManagerMetadataKind.StandardModManager)]
    public abstract record ModManagerMetadata(string Kind);

    public record StandardModManagerMetadata(
        IEnumerable<ReleaseCoordinates> EnabledModReleases
    ) : ModManagerMetadata(ModManagerMetadataKind.StandardModManager);

    internal static class ModManagerMetadataKind {
        public const string StandardModManager = "StandardModManager";
    }
}