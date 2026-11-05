using System.Security.Cryptography;
using System.Text.RegularExpressions;
using log4net;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Services.Mods;

public class PakReleaseRecovery(IModManager manager, IUserDialogueSpawner dialogue) {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(PakReleaseRecovery));
    private record LocalPak(string Path, string Hash, Release? Release);

    public async Task<bool> Recover(IReadOnlyList<Release> requested) {
        if (requested.Count == 0) return true;
        try {
            var names = PakDir.GetInstallFileNames(requested.Select(ReleaseCoordinates.FromRelease)).ToList();
            var available = requested.Concat(manager.Mods.SelectMany(mod => mod.Releases))
                .OrderByDescending(release => release.Version)
                .ThenByDescending(release => release.ReleaseDate).ToList();
            var tracked = requested.Select(release => manager.PakDir.GetManagedPakFilePath(ReleaseCoordinates.FromRelease(release)))
                .SelectMany(path => path.ToList()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var local = new List<LocalPak>();
            foreach (var path in Directory.EnumerateFiles(manager.PakDir.DirPath, "*.pak")) {
                // Ignore vanilla game paks and manually installed files outside the launcher's naming scheme.
                if (!tracked.Contains(path) && !Regex.IsMatch(Path.GetFileName(path), @"^q[a-z]*__-__")) continue;
                using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(await SHA512.HashDataAsync(stream));
                local.Add(new LocalPak(path, hash, available.FirstOrDefault(release => HashMatches(release.ReleaseHash, hash))));
            }

            var replacements = new List<LocalPak>();
            var updates = new List<DependencyUpdate>();
            for (var i = 0; i < requested.Count; i++) {
                var wanted = requested[i];
                var coordinates = ReleaseCoordinates.FromRelease(wanted);
                var trackedPath = manager.PakDir.GetManagedPakFilePath(coordinates).IfNone("");
                var candidates = local.Where(pak => pak.Path.Equals(trackedPath, StringComparison.OrdinalIgnoreCase)
                    || MatchesDestination(pak.Path, names[i]));
                foreach (var pak in candidates.Where(pak => !HashMatches(pak.Hash, wanted.ReleaseHash))) {
                    replacements.Add(pak);
                    var reason = pak.Release == null ? $"Unrecognized file hash: {pak.Hash}. Replace with the registry-approved release."
                        : !coordinates.Matches(ModIdentifier.FromRelease(pak.Release)) ? "Replace a different mod occupying this destination"
                        : coordinates.CompareTo(ReleaseCoordinates.FromRelease(pak.Release)) < 0 ? "Downgrade" : "Upgrade";
                    updates.Add(new DependencyUpdate(wanted.Info.Name, pak.Release?.Tag, wanted.Tag, wanted.ReleaseUrl, reason));
                }
            }

            if (updates.Count > 0 && dialogue.DisplayUpdateMessage("Change installed mod releases?",
                "Existing pak files differ from the requested releases.", "Yes", "No", "Cancel", updates.Distinct()) != UserDialogueChoice.Yes)
                return false;

            var recovered = requested.Select((wanted, i) => {
                var trackedPath = manager.PakDir.GetManagedPakFilePath(ReleaseCoordinates.FromRelease(wanted)).IfNone("");
                return local.Where(pak => !replacements.Contains(pak) && HashMatches(pak.Hash, wanted.ReleaseHash))
                    .Where(pak => MatchesDestination(pak.Path, names[i]) || pak.Path.Equals(trackedPath, StringComparison.OrdinalIgnoreCase))
                    .Where(pak => !names.Where((_, index) => index != i).Any(name => MatchesDestination(pak.Path, name)))
                    .OrderBy(pak => Path.GetFileName(pak.Path) == names[i] ? 0 : 1).ToList();
            }).ToList();

            // Validate duplicate signatures before changing any files or metadata.
            foreach (var matching in recovered.Where(paks => paks.Count > 1)) {
                var referenceSig = Path.ChangeExtension(matching[0].Path, ".sig");
                foreach (var duplicate in matching.Skip(1)) {
                    var duplicateSig = Path.ChangeExtension(duplicate.Path, ".sig");
                    if (!File.Exists(duplicateSig)) continue;
                    if (!File.Exists(referenceSig) || !File.ReadAllBytes(referenceSig).SequenceEqual(File.ReadAllBytes(duplicateSig)))
                        throw new IOException($"Conflicting duplicate signature; files were preserved: {duplicateSig}");
                }
            }

            // No files or metadata change until all replacements have been approved.
            foreach (var pak in replacements.Distinct()) await DeletePak(pak);
            for (var i = 0; i < requested.Count; i++) {
                var wanted = requested[i];
                var matching = recovered[i];
                if (matching.Count == 0) continue;
                var installed = matching[0];
                manager.PakDir.TrackPak(new ManagedPak(ReleaseCoordinates.FromRelease(wanted), Path.GetFileName(installed.Path), i));
                foreach (var duplicate in matching.Skip(1)) await DeletePak(duplicate);
            }
            return true;
        }
        catch (Exception error) {
            Logger.Error($"Failed to identify installed mod releases: {error.Message}");
            dialogue.DisplayMessage($"Failed to identify installed mod releases: {error.Message}");
            return false;
        }
    }

    private static bool HashMatches(string first, string second) =>
        !string.IsNullOrWhiteSpace(first) && first.Equals(second, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesDestination(string path, string name) {
        var stem = Regex.Escape(Path.GetFileNameWithoutExtension(name.Split("__-__", 2)[1]));
        return Regex.IsMatch(Path.GetFileName(path), $@"^q[a-z]*__-__{stem}( \([0-9]+\))?\.pak$", RegexOptions.IgnoreCase);
    }

    private static async Task DeletePak(LocalPak pak) {
        using (var stream = File.OpenRead(pak.Path)) {
            if (!HashMatches(pak.Hash, Convert.ToHexString(await SHA512.HashDataAsync(stream))))
                throw new IOException($"Pak changed during release confirmation: {pak.Path}");
        }
        File.Delete(Path.ChangeExtension(pak.Path, ".sig"));
        File.Delete(pak.Path);
    }
}
