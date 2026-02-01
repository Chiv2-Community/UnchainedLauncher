using DiscriminatedUnions;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods {

    public class ModManagerCodec : DerivedJsonCodec<ModManagerMetadata, ModManager> {
        public ModManagerCodec(IModRegistry registry) : base(ToJsonType, modManager => ToClassType(modManager, registry)) { }


        public static ModManager ToClassType(ModManagerMetadata metadata, IModRegistry registry) =>
            metadata switch {
                StandardModManagerMetadata m => new ModManager(
                    registry,
                    CreatePakDir(m.PakDir),
                    m.EnabledModReleases,
                    m.CachedMods
                ),

                _ => throw new ArgumentOutOfRangeException(nameof(metadata), metadata,
                    $"Attempt to initialize unknown IModManager implementation: {metadata}")
            };

        private static PakDir CreatePakDir(PakDirMetadata? metadata) =>
            metadata != null
                ? new PakDir(metadata.DirPath, metadata.ManagedPaks)
                : new PakDir(FilePaths.PakDir, Enumerable.Empty<ManagedPak>());

        public static ModManagerMetadata ToJsonType(IModManager manager) =>
            manager switch {
                ModManager m => new StandardModManagerMetadata(
                    m.EnabledModReleaseCoordinates,
                    m.Mods,
                    m.PakDir switch {
                        PakDir pd => new PakDirMetadata(pd.DirPath, pd.ManagedPaks),
                        _ => null
                    }
                ),

                _ => throw new ArgumentOutOfRangeException(nameof(manager), manager,
                    $"Attempt to extract metadata from unknown IModManager implementation: {manager}")
            };
    }

    [UnionTag(nameof(Kind))]
    [UnionCase(typeof(StandardModManagerMetadata), ModManagerMetadataKind.StandardModManager)]
    public abstract record ModManagerMetadata(string Kind);

    public record StandardModManagerMetadata(
        IEnumerable<ReleaseCoordinates> EnabledModReleases,
        IEnumerable<Mod>? CachedMods,
        PakDirMetadata? PakDir
    ) : ModManagerMetadata(ModManagerMetadataKind.StandardModManager);

    public record PakDirMetadata(string DirPath, IEnumerable<ManagedPak> ManagedPaks);

    internal static class ModManagerMetadataKind {
        public const string StandardModManager = "StandardModManager";
    }
}