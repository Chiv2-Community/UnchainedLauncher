using log4net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Services.Mods;

internal static class PakReleaseRecovery {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(PakReleaseRecovery));
    private record LocalPak(string Path, string Hash, Release? Release);

    internal static async Task<bool> Recover(PakDir paks, IReadOnlyList<Release> requested,
        IEnumerable<Release> known, IUserDialogueSpawner dialogue, Action beginChange) {
        if (requested.Count == 0) return true;
        try {
            var names = PakDir.GetInstallFileNames(requested.Select(ReleaseCoordinates.FromRelease)).ToList();
            var available = requested.Concat(known).OrderByDescending(release => release.Version)
                .ThenByDescending(release => release.ReleaseDate).ToList();
            var tracked = paks.ManagedPaks.ToList();
            string TrackedPath(Release release) => tracked.Where(pak => pak.Coordinates.Matches(ReleaseCoordinates.FromRelease(release)))
                .Select(pak => Path.Join(paks.DirPath, pak.PakFileName)).FirstOrDefault() ?? "";
            var trackedPaths = requested.Select(TrackedPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var local = new List<LocalPak>();
            foreach (var path in Directory.EnumerateFiles(paks.DirPath, "*.pak")) {
                if (!trackedPaths.Contains(path) && !names.Any(name => MatchesDestination(path, name))) continue;
                var hash = await HashFile(path);
                local.Add(new LocalPak(path, hash, available.FirstOrDefault(release => HashMatches(release.ReleaseHash, hash))));
            }

            var replacements = new List<LocalPak>();
            var updates = new List<DependencyUpdate>();
            for (var i = 0; i < requested.Count; i++) {
                var wanted = requested[i];
                var coordinates = ReleaseCoordinates.FromRelease(wanted);
                var verifiedTracked = local.Any(pak => pak.Path.Equals(TrackedPath(wanted), StringComparison.OrdinalIgnoreCase)
                    && HashMatches(pak.Hash, wanted.ReleaseHash));
                var candidates = local.Where(pak => pak.Path.Equals(TrackedPath(wanted), StringComparison.OrdinalIgnoreCase)
                    || MatchesDestination(pak.Path, names[i]))
                    .Where(pak => !verifiedTracked || tracked.Any(owned => owned.PakFileName.Equals(Path.GetFileName(pak.Path), StringComparison.OrdinalIgnoreCase)));
                foreach (var pak in candidates.Where(pak => !HashMatches(pak.Hash, wanted.ReleaseHash))) {
                    replacements.Add(pak);
                    var reason = pak.Release == null ? $"Unrecognized file hash: {pak.Hash}. Replace with the registry-approved release."
                        : !coordinates.Matches(ModIdentifier.FromRelease(pak.Release)) ? "Replace a different mod occupying this destination"
                        : coordinates.CompareTo(ReleaseCoordinates.FromRelease(pak.Release)) < 0 ? "Downgrade" : "Upgrade";
                    updates.Add(new DependencyUpdate($"{wanted.Info.Name} ({Path.GetFileName(pak.Path)})",
                        pak.Release?.Tag, wanted.Tag, wanted.ReleaseUrl, reason));
                }
            }

            var recovered = requested.Select((wanted, i) => local
                .Where(pak => !replacements.Contains(pak) && HashMatches(pak.Hash, wanted.ReleaseHash))
                .Where(pak => MatchesDestination(pak.Path, names[i]) || pak.Path.Equals(TrackedPath(wanted), StringComparison.OrdinalIgnoreCase))
                .Where(pak => !names.Where((_, index) => index != i).Any(name => MatchesDestination(pak.Path, name)))
                .OrderBy(pak => Path.GetFileName(pak.Path) == names[i] ? 0 : 1).ToList()).ToList();

            for (var i = 0; i < recovered.Count; i++) {
                var wanted = requested[i];
                if (recovered[i].Count == 0) continue;
                var installed = recovered[i][0];
                if (!tracked.Any(pak => pak.Coordinates == ReleaseCoordinates.FromRelease(wanted)
                    && pak.PakFileName.Equals(Path.GetFileName(installed.Path), StringComparison.OrdinalIgnoreCase)))
                    updates.Add(new DependencyUpdate(Path.GetFileName(installed.Path), installed.Release?.Tag, wanted.Tag,
                        wanted.ReleaseUrl, "Adopt verified pak: the launcher may rename, update, or remove it in future preparations"));
                foreach (var duplicate in recovered[i].Skip(1))
                    updates.Add(new DependencyUpdate(Path.GetFileName(duplicate.Path), duplicate.Release?.Tag, wanted.Tag,
                        wanted.ReleaseUrl, "Remove duplicate pak and its signature"));
            }
            if (updates.Count > 0 && dialogue.DisplayUpdateMessage("Change installed mod files?",
                $"Directory: {paks.DirPath}\nOnly approve if the listed files should be managed or removed by the launcher. No/Cancel preserves them and cancels launch.",
                "Yes", "No", "Cancel", updates.Distinct()) != UserDialogueChoice.Yes)
                return false;

            // Validate destinations, duplicate signatures, and freshness before any mutation.
            for (var i = 0; i < recovered.Count; i++) {
                var destination = Path.Join(paks.DirPath, names[i]);
                if (recovered[i].Count > 0 && (!File.Exists(destination)
                    || recovered[i][0].Path.Equals(destination, StringComparison.OrdinalIgnoreCase)
                    || replacements.Any(pak => pak.Path.Equals(destination, StringComparison.OrdinalIgnoreCase))))
                    PakDir.ValidateMoveSignatures(recovered[i][0].Path, destination);
            }
            foreach (var matching in recovered.Where(paks => paks.Count > 1)) {
                var referenceSig = Path.ChangeExtension(matching[0].Path, ".sig");
                foreach (var duplicate in matching.Skip(1)) {
                    var duplicateSig = Path.ChangeExtension(duplicate.Path, ".sig");
                    if (!File.Exists(duplicateSig)) continue;
                    if (!File.Exists(referenceSig) || !File.ReadAllBytes(referenceSig).SequenceEqual(File.ReadAllBytes(duplicateSig)))
                        throw new IOException($"Conflicting duplicate signature; files were preserved: {duplicateSig}");
                }
            }
            foreach (var pak in local) {
                if (!HashMatches(pak.Hash, await HashFile(pak.Path)))
                    throw new IOException($"Pak changed during release confirmation: {pak.Path}");
            }
            if (replacements.Count == 0 && recovered.All(paks => paks.Count == 0)) return true;
            beginChange();
            foreach (var pak in replacements.Distinct()) DeletePak(pak);
            for (var i = 0; i < requested.Count; i++) {
                var matching = recovered[i];
                if (matching.Count == 0) continue;
                paks.TrackPak(new ManagedPak(ReleaseCoordinates.FromRelease(requested[i]), Path.GetFileName(matching[0].Path), i));
                foreach (var duplicate in matching.Skip(1)) DeletePak(duplicate);
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

    private static async Task<string> HashFile(string path) {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA512.HashDataAsync(stream));
    }

    private static void DeletePak(LocalPak pak) {
        File.Delete(Path.ChangeExtension(pak.Path, ".sig"));
        File.Delete(pak.Path);
    }
}