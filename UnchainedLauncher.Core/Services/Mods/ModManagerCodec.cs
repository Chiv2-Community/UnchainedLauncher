using DiscriminatedUnions;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods {

    public class ModManagerCodec : Codec<ModManager> {
        public static ModManagerCodec ForInstallation(IModRegistry registry) => new(registry, Path.GetFullPath(FilePaths.PakDir));

        public ModManagerCodec(IModRegistry registry, string? pakDirectory = null) : base(
            new Serializer<ModManager>(manager => JsonHelpers.Serialize(ToJsonType(manager))),
            new Deserializer<ModManager>(json => Decode(json, registry, pakDirectory))) { }

        private static DeserializationResult<ModManager> Decode(string json, IModRegistry registry, string? pakDirectory) {
            try {
                var document = JsonNode.Parse(json);
                var invalidated = false;
                if (document?["CachedMods"] is JsonArray cachedMods) {
                    for (var i = cachedMods.Count - 1; i >= 0; i--) {
                        // Strip V3 before deserialization: its maps are strings, not MapDto objects.
                        if (cachedMods[i] is JsonObject mod && mod.ContainsKey("latest_manifest")
                            && !mod.ContainsKey("latest_release_info")) {
                            cachedMods.RemoveAt(i);
                            invalidated = true;
                        }
                    }
                }
                return JsonHelpers.Deserialize<ModManagerMetadata>(document?.ToJsonString() ?? json)
                    .Map(metadata => {
                        var manager = ToClassType(metadata, registry, pakDirectory);
                        manager.NeedsMetadataRefresh |= invalidated;
                        return manager;
                    });
            }
            catch (JsonException ex) {
                return new DeserializationResult<ModManager>(null, ex);
            }
        }


        public static ModManager ToClassType(ModManagerMetadata metadata, IModRegistry registry, string? pakDirectory = null) =>
            metadata switch {
                StandardModManagerMetadata m => new ModManager(
                    registry,
                    CreatePakDir(m.PakDir, pakDirectory),
                    m.EnabledModReleases,
                    // Cached V3 metadata has no V4 release info or asset inventory. Keep
                    // enabled coordinates and managed paks, but refetch unusable metadata.
                    m.CachedMods?.Where(IsUsableCache)
                ) { NeedsMetadataRefresh = m.CachedMods?.Any(mod => !IsUsableCache(mod)) ?? false },

                _ => throw new ArgumentOutOfRangeException(nameof(metadata), metadata,
                    $"Attempt to initialize unknown IModManager implementation: {metadata}")
            };

        private static bool IsUsableCache(Mod mod) =>
            mod?.LatestReleaseInfo is { RepoUrl: not null, Authors: not null, Dependencies: not null, ImageUrls: not null }
            && mod.Releases != null
            && mod.Releases.All(r =>
                r?.Info is { RepoUrl: not null, Authors: not null, Dependencies: not null, ImageUrls: not null }
                && r.Manifest is { Maps: not null, Blueprints: not null, Markers: not null, Replacements: not null, Arbitrary: not null });

        private static PakDir CreatePakDir(PakDirMetadata? metadata, string? pakDirectory) {
            var path = pakDirectory ?? metadata?.DirPath ?? FilePaths.PakDir;
            var sameDirectory = metadata != null && SavedPakMigration.SameDirectory(metadata.DirPath, path);
            // An incoming unknown pak must not revive a stale same-name ownership entry.
            var existing = sameDirectory ? metadata!.ManagedPaks.Where(p => File.Exists(Path.Combine(path, p.PakFileName))).ToArray() : [];
            var migrated = pakDirectory == null ? [] : SavedPakMigration.Migrate(path, SavedPakMigration.StartupSources(metadata), metadata);
            return new PakDir(path, sameDirectory ? existing : migrated);
        }

        public static PakDir CreateInstallationPakDir() {
            var path = Path.GetFullPath(FilePaths.PakDir);
            SavedPakMigration.Migrate(path, SavedPakMigration.SourceDirectories());
            return new PakDir(path, []);
        }

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