using log4net;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Services.Mods;

internal static class UnmanagedPakRecovery {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(UnmanagedPakRecovery));
    private record Candidate(string Path, string Hash, Release? Release, bool Managed);

    internal static async Task<bool> Recover(PakDir paks, IEnumerable<Release> known,
        IUserDialogueSpawner dialogue, Action beginChange, ICollection<Release> adopted, bool alwaysKeepUnmanagedPaks, PakReadCache cache,
        Action<IReadOnlyList<Release>> preparePlan,
        Action<Action, IReadOnlyList<ManagedPak>>? stageChanges = null) {
        var kept = new KeptUnmanagedPaks(paks.DirPath);
        kept.Load();
        var releases = known.OrderByDescending(release => release.Version)
            .ThenByDescending(release => release.ReleaseDate).ToList();
        var managed = paks.ManagedPaks.ToList();
        var files = new List<Candidate>();
        foreach (var path in Directory.EnumerateFiles(paks.DirPath, "*.pak")) {
            if (Path.GetFileName(path).Equals("pakchunk0-WindowsNoEditor.pak", StringComparison.OrdinalIgnoreCase)) continue;
            var owned = managed.FirstOrDefault(pak => pak.PakFileName.Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
            var hash = await cache.Hash(path);
            var matching = releases.Where(release => hash.Equals(release.ReleaseHash, StringComparison.OrdinalIgnoreCase)).ToList();
            // A hash verifies bytes; the recorded coordinates disambiguate shared release payloads.
            var release = matching.FirstOrDefault(release => owned?.Coordinates == ReleaseCoordinates.FromRelease(release))
                ?? matching.FirstOrDefault();
            files.Add(new Candidate(path, hash, release, owned != null));
        }

        var duplicates = new List<(Candidate Duplicate, Candidate Retained)>();
        var unmanaged = new List<Candidate>();
        foreach (var group in files.GroupBy(file => file.Hash)) {
            var ordered = group.OrderByDescending(file => file.Managed).ThenBy(file => file.Path.Length)
                .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var retained = ordered[0];
            foreach (var duplicate in ordered.Skip(1).Where(file => !file.Managed)) {
                Logger.Info($"Detected duplicate pak: {duplicate.Path}; retained: {retained.Path}; SHA-512: {duplicate.Hash}");
                duplicates.Add((duplicate, retained));
            }
            if (!retained.Managed) unmanaged.Add(retained);
        }

        var unconfirmed = unmanaged.Where(file => file.Release != null || !kept.Contains(Path.GetFileName(file.Path), file.Hash)).ToList();
        var updates = new List<DependencyUpdate>();
        foreach (var file in unconfirmed.Where(_ => !alwaysKeepUnmanagedPaks)) {
            var version = file.Release?.Tag ?? await cache.Version(file.Path, file.Hash) ?? "Unknown";
            updates.Add(new DependencyUpdate(Path.GetFileName(file.Path), null, version,
                file.Release?.ReleaseUrl ?? "", file.Release?.Info.Name ?? "Unrecognized pak"));
        }
        var choice = alwaysKeepUnmanagedPaks || unconfirmed.Count == 0 ? UserDialogueChoice.Yes : dialogue.DisplayUpdateMessage(
            "Unmanaged paks detected", "Unmanaged paks detected. Keep them or remove them?", "Keep", "Remove", "Cancel",
            updates);
        if (choice != UserDialogueChoice.Yes && choice != UserDialogueChoice.No) return false;
        var detected = choice == UserDialogueChoice.Yes
            ? unmanaged.Where(file => file.Release != null).Select(file => file.Release!)
                .OrderByDescending(release => release.Version).DistinctBy(ModIdentifier.FromRelease).ToList()
            : [];
        preparePlan(detected);

        var adoptedPaks = new List<ManagedPak>();
        if (choice == UserDialogueChoice.Yes) {
            foreach (var file in unmanaged.Where(file => file.Release != null).OrderByDescending(file => file.Release!.Version)) {
                var release = file.Release!;
                adoptedPaks.Add(new ManagedPak(ReleaseCoordinates.FromRelease(release), Path.GetFileName(file.Path), managed.Count + adopted.Count));
                // Enable one detected version per mod; installation cleans up other managed versions.
                if (!adopted.Any(existing => ModIdentifier.FromRelease(existing) == ModIdentifier.FromRelease(release))) adopted.Add(release);
            }
        }
        void Apply() {
            // Recheck after all confirmations, before deleting files or changing ownership.
            foreach (var file in files) cache.Validate(file.Path, file.Hash);
            foreach (var (duplicate, retained) in duplicates) {
                var signature = Path.ChangeExtension(duplicate.Path, ".sig");
                var retainedSignature = Path.ChangeExtension(retained.Path, ".sig");
                if (File.Exists(signature) && (!File.Exists(retainedSignature)
                    || !File.ReadAllBytes(signature).SequenceEqual(File.ReadAllBytes(retainedSignature))))
                    throw new IOException($"Conflicting duplicate signature; files were preserved: {signature}");
            }
            if (duplicates.Count > 0 || unconfirmed.Count > 0) beginChange();
            foreach (var (file, kind) in duplicates.Select(pair => (pair.Duplicate, "duplicate"))
                .Concat((choice == UserDialogueChoice.No ? unconfirmed : []).Select(file => (file, "unmanaged")))) {
                File.Delete(Path.ChangeExtension(file.Path, ".sig"));
                File.Delete(file.Path);
                Logger.Info($"Removed {kind} pak: {file.Path}; SHA-512: {file.Hash}");
            }
            foreach (var pak in adoptedPaks) paks.TrackPak(pak);
            kept.Save(unmanaged.Where(file => file.Release == null && (choice == UserDialogueChoice.Yes || !unconfirmed.Contains(file)))
                .ToDictionary(file => Path.GetFileName(file.Path), file => file.Hash, StringComparer.OrdinalIgnoreCase));
        }
        if (stageChanges == null) Apply();
        else stageChanges(Apply, adoptedPaks);
        return true;
    }
}