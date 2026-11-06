using System.Security.Cryptography;
using log4net;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Services.Mods;

internal static class UnmanagedPakRecovery {
    private static readonly ILog Logger = LogManager.GetLogger(nameof(UnmanagedPakRecovery));
    private record Candidate(string Path, string Hash, Release? Release, bool Managed);

    internal static async Task<bool> Recover(IPakDir paks, IReadOnlyList<Release> requested, IEnumerable<Release> known,
        IUserDialogueSpawner dialogue, Action beginChange, ICollection<Release> adopted) {
        var releases = known.OrderByDescending(release => release.Version)
            .ThenByDescending(release => release.ReleaseDate).ToList();
        var managed = paks.ManagedPaks.ToList();
        var files = new List<Candidate>();
        foreach (var path in Directory.EnumerateFiles(paks.DirPath, "*.pak")) {
            if (Path.GetFileName(path).Equals("pakchunk0-WindowsNoEditor.pak", StringComparison.OrdinalIgnoreCase)) continue;
            var owned = managed.FirstOrDefault(pak => pak.PakFileName.Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA512.HashDataAsync(stream));
            var matching = releases.Where(release => hash.Equals(release.ReleaseHash, StringComparison.OrdinalIgnoreCase)).ToList();
            // A hash verifies bytes; the recorded coordinates disambiguate shared release payloads.
            var release = matching.FirstOrDefault(release => owned?.Coordinates == ReleaseCoordinates.FromRelease(release))
                ?? matching.FirstOrDefault();
            files.Add(new Candidate(path, hash, release, owned != null));
        }

        var duplicates = new List<Candidate>();
        var unmanaged = new List<Candidate>();
        foreach (var group in files.GroupBy(file => file.Hash)) {
            var ordered = group.OrderByDescending(file => file.Managed).ThenBy(file => file.Path.Length)
                .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var retained = ordered[0];
            foreach (var duplicate in ordered.Skip(1).Where(file => !file.Managed)) {
                Logger.Info($"Detected duplicate pak: {duplicate.Path}; retained: {retained.Path}; SHA-512: {duplicate.Hash}");
                duplicates.Add(duplicate);
            }
            if (!retained.Managed) unmanaged.Add(retained);
        }

        var choice = unmanaged.Count == 0 ? UserDialogueChoice.Yes : dialogue.DisplayUpdateMessage(
            "Unmanaged paks detected", "Unmanaged paks detected. Keep them or remove them?", "Keep", "Remove", "Cancel",
            unmanaged.Select(file => new DependencyUpdate(Path.GetFileName(file.Path), null,
                file.Release?.Tag ?? "Unknown", file.Release?.ReleaseUrl ?? "", file.Release?.Info.Name ?? "Unrecognized pak")));
        if (choice != UserDialogueChoice.Yes && choice != UserDialogueChoice.No) return false;

        // Validate all inputs before deleting anything or changing ownership.
        foreach (var file in files) {
            using var stream = File.OpenRead(file.Path);
            if (!file.Hash.Equals(Convert.ToHexString(await SHA512.HashDataAsync(stream)), StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Pak changed during release confirmation: {file.Path}");
        }
        foreach (var duplicate in duplicates) {
            var retained = files.First(file => file.Hash == duplicate.Hash && !duplicates.Contains(file));
            var signature = Path.ChangeExtension(duplicate.Path, ".sig");
            var retainedSignature = Path.ChangeExtension(retained.Path, ".sig");
            if (File.Exists(signature) && (!File.Exists(retainedSignature)
                || !File.ReadAllBytes(signature).SequenceEqual(File.ReadAllBytes(retainedSignature))))
                throw new IOException($"Conflicting duplicate signature; files were preserved: {signature}");
        }
        if (choice == UserDialogueChoice.Yes) {
            var detected = unmanaged.Where(file => file.Release != null).Select(file => file.Release!)
                .OrderByDescending(release => release.Version).DistinctBy(ModIdentifier.FromRelease).ToList();
            var planned = requested.Select(release => detected.FirstOrDefault(found => ModIdentifier.FromRelease(found) == ModIdentifier.FromRelease(release)) ?? release)
                .Concat(detected.Where(found => !requested.Any(release => ModIdentifier.FromRelease(found) == ModIdentifier.FromRelease(release)))).ToList();
            var destinations = PakDir.GetInstallFileNames(planned.Select(ReleaseCoordinates.FromRelease)).ToList();
            for (var i = 0; i < planned.Count; i++) {
                var source = unmanaged.FirstOrDefault(file => file.Release == planned[i]);
                var destination = Path.Join(paks.DirPath, destinations[i]);
                if (source != null && (!File.Exists(destination) || source.Path.Equals(destination, StringComparison.OrdinalIgnoreCase)))
                    PakDir.ValidateMoveSignatures(source.Path, destination);
            }
        }
        if (duplicates.Count == 0 && unmanaged.Count == 0) return true;
        beginChange();
        foreach (var file in duplicates.Concat(choice == UserDialogueChoice.No ? unmanaged : [])) {
            File.Delete(Path.ChangeExtension(file.Path, ".sig"));
            File.Delete(file.Path);
            Logger.Info($"Removed {(duplicates.Contains(file) ? "duplicate" : "unmanaged")} pak: {file.Path}; SHA-512: {file.Hash}");
        }
        if (choice == UserDialogueChoice.Yes) {
            foreach (var file in unmanaged.Where(file => file.Release != null).OrderByDescending(file => file.Release!.Version)) {
                var release = file.Release!;
                paks.TrackPak(new ManagedPak(ReleaseCoordinates.FromRelease(release), Path.GetFileName(file.Path), managed.Count + adopted.Count));
                // Enable one detected version per mod; installation cleans up other managed versions.
                if (!adopted.Any(existing => ModIdentifier.FromRelease(existing) == ModIdentifier.FromRelease(release))) adopted.Add(release);
            }
        }
        return true;
    }
}
