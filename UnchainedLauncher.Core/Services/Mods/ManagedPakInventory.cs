using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods {
    // Physical ownership is shared by hosts; enabled releases remain in their own configs.
    internal sealed class ManagedPakInventory {
        private readonly PakDir _pakDir;
        private readonly string _path;

        private ManagedPakInventory(PakDir pakDir) {
            _pakDir = pakDir;
            _path = Path.Combine(pakDir.DirPath, FilePaths.ManagedPakInventoryFileName);
        }

        public static ManagedPakInventory? Reconcile(IModManager manager) {
            if (manager.PakDir is not PakDir pakDir) return null;
            var inventory = new ManagedPakInventory(pakDir);
            IEnumerable<ManagedPak> owned;
            if (File.Exists(inventory._path)) {
                // Once migrated, host snapshots must not resurrect obsolete ownership.
                var decoded = JsonHelpers.Deserialize<PakDirMetadata>(File.ReadAllText(inventory._path));
                if (!decoded.Success) throw new InvalidDataException("Cannot read shared pak inventory.", decoded.Exception);
                owned = decoded.Result!.ManagedPaks;
            }
            else {
                var seeds = pakDir.ManagedPaks.ToList();
                // Existing hosts use installation-relative configs. Only migrate them for
                // that installation, never for an unrelated/custom PakDir.
                if (SameDirectory(pakDir.DirPath, FilePaths.PakDir)) {
                    var codec = new ModManagerCodec(manager.ModRegistry);
                    foreach (var path in new[] { FilePaths.ModManagerConfigPath, Path.Combine(FilePaths.ModCachePath, "cli_mod_manager.json") }) {
                        if (!File.Exists(path)) continue;
                        var decoded = codec.Deserialize(File.ReadAllText(path));
                        if (!decoded.Success) throw new InvalidDataException($"Cannot read mod cache: {path}", decoded.Exception);
                        if (decoded.Result!.PakDir is PakDir cached && SameDirectory(cached.DirPath, pakDir.DirPath))
                            seeds.AddRange(cached.ManagedPaks);
                    }
                }
                owned = seeds;
            }
            pakDir.ReconcileManagedPaks(owned);
            return inventory;
        }

        public void Save() {
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonHelpers.Serialize(new PakDirMetadata(_pakDir.DirPath, _pakDir.ManagedPaks, _pakDir.DefaultSigPath, _pakDir.CachePath)));
            File.Move(temporaryPath, _path, overwrite: true);
        }

        private static bool SameDirectory(string first, string second) => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}