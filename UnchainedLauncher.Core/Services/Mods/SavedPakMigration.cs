using System.Security.Cryptography;
using System.Text.Json;

namespace UnchainedLauncher.Core.Services.Mods;

public static class SavedPakMigration {
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool SameDirectory(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                PathComparison);

    private static bool IsSavedProfile(string name) =>
        name.Equals("Saved", PathComparison)
        || name.StartsWith("Saved_", PathComparison) && ValidSuffix(name[6..]);

    private static bool ValidSuffix(string suffix) =>
        suffix.Length != 0 && suffix.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    public static IEnumerable<string> SourceDirectories(string destination, string suffix = "Unchained", PakDirMetadata? cached = null) {
        if (!ValidSuffix(suffix))
            throw new IOException("Invalid Saved directory suffix.");
        var tbl = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(destination)))!;
        yield return Path.Combine(tbl, "Saved", "Paks");
        yield return Path.Combine(tbl, "Saved_" + suffix, "Paks");
        // Installation-local profiles are bounded inputs to the same startup importer.
        RejectLinks(tbl);
        if (Directory.Exists(tbl))
            foreach (var profile in Directory.EnumerateDirectories(tbl))
                if (IsSavedProfile(Path.GetFileName(profile)))
                    yield return Path.Combine(profile, "Paks");
        // Shared user roots cannot be attributed to an installation without its cached source path.
        if (cached == null) yield break;
        var parent = Path.GetDirectoryName(Path.GetFullPath(cached.DirPath));
        var name = Path.GetFileName(parent);
        if (name == null || !IsSavedProfile(name)) yield break;
        var installationCandidate = Path.Combine(tbl, name!, "Paks");
        if (SameDirectory(installationCandidate, cached.DirPath)) yield return installationCandidate;
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(local)) yield break;
        var candidate = Path.Combine(local, "Chivalry 2", name!, "Paks");
        if (SameDirectory(candidate, cached.DirPath)) yield return candidate;
    }

    internal static void RejectLinks(string path) {
        // Volume roots are boundaries: Wine/Proton drive mappings may themselves be reparse points.
        for (var current = Path.GetFullPath(path); Path.GetDirectoryName(current) != null; current = Path.GetDirectoryName(current)!) {
            try {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Refusing Saved pak migration through a link: {current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static bool Identical(string left, string right) {
        using var a = File.OpenRead(left);
        using var b = File.OpenRead(right);
        return a.Length == b.Length && SHA256.HashData(a).SequenceEqual(SHA256.HashData(b));
    }

    private static IEnumerable<string> Files(string directory) {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory)) {
            RejectLinks(entry);
            if (Directory.Exists(entry)) {
                // Inventory/cache metadata is not a legacy payload.
                if (Path.GetFileName(entry) == ".unchained") continue;
                foreach (var file in Files(entry)) yield return file;
            }
            else if (Path.GetExtension(entry).Equals(".pak", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(entry).Equals(".sig", StringComparison.OrdinalIgnoreCase)) yield return entry;
        }
    }

    internal record Transfer(string Source, string Target, string Hash);
    internal record CleanupEvidence(string ConfigHash, string[] SourceDirectories, Transfer[] Files);

    internal static IEnumerable<string> CleanupSources(string backupRoot) {
        RejectLinks(backupRoot);
        if (!Directory.Exists(backupRoot)) return [];
        return Directory.EnumerateFiles(backupRoot, "saved-cleanup.json", SearchOption.TopDirectoryOnly)
            .SelectMany(path => ReadTransfers(path).SourceDirectories).Distinct().ToArray();
    }

    internal static bool HasPayload(string directory) {
        RejectLinks(directory);
        return Files(directory).Any();
    }

    private static CleanupEvidence ReadTransfers(string path) {
        RejectLinks(path);
        var evidence = JsonSerializer.Deserialize<CleanupEvidence>(File.ReadAllBytes(path))
            ?? throw new InvalidDataException($"Invalid Saved cleanup evidence: {path}");
        var config = Path.GetDirectoryName(path)![..^".migration-backups".Length];
        RejectLinks(config);
        if (!File.Exists(config) || ModManagerPersistence.OwnershipHash(File.ReadAllText(config)) != evidence.ConfigHash)
            throw new IOException($"Saved ownership publication is unverified. Preserve and reconcile '{path}' before starting.");
        return evidence;
    }

    internal static void RetryCleanup(string backupRoot) {
        RejectLinks(backupRoot);
        if (!Directory.Exists(backupRoot)) return;
        foreach (var path in Directory.EnumerateFiles(backupRoot, "saved-cleanup.json")) {
            foreach (var transfer in ReadTransfers(path).Files) {
                RejectLinks(transfer.Source);
                RejectLinks(transfer.Target);
                if (!File.Exists(transfer.Source)) continue;
                if (!File.Exists(transfer.Target) || Hash(transfer.Source) != transfer.Hash || Hash(transfer.Target) != transfer.Hash)
                    throw new IOException($"Saved cleanup evidence no longer matches; files preserved: {transfer.Source}");
                File.Delete(transfer.Source);
            }
        }
    }

    internal static void CompleteCleanup(string backupRoot) {
        var path = Path.Combine(backupRoot, "saved-cleanup.json");
        if (File.Exists(path)) File.Move(path, path + "." + Guid.NewGuid().ToString("N") + ".completed");
    }

    private static string Hash(string path) {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // Copy and verify first; publish ordinary ownership before consuming source evidence.
    internal static void Migrate(string destination, IEnumerable<string> sources, Action<IReadOnlyList<string>, Action<string>> publish,
        string? backupRoot = null) {
        RejectLinks(destination);
        var sourceDirectories = sources.Select(Path.GetFullPath).ToArray();
        var files = new List<(string Source, string Target)>();
        foreach (var source in sourceDirectories) {
            RejectLinks(source);
            if (!Directory.Exists(source) || SameDirectory(source, destination)) continue;
            foreach (var file in Files(source)) {
                var target = Path.Combine(destination, Path.GetFileName(file));
                RejectLinks(target);
                if (Directory.Exists(target) || File.Exists(target) && !Identical(file, target))
                    throw new IOException($"Conflicting Saved pak or signature; files preserved: '{file}' -> '{target}'.");
                files.Add((file, target));
            }
        }
        foreach (var group in files.GroupBy(f => f.Target, StringComparer.OrdinalIgnoreCase))
            if (group.Skip(1).Any(f => !Identical(group.First().Source, f.Source)))
                throw new IOException($"Conflicting Saved files; files preserved: {group.Key}");

        if (files.Count > 0) Directory.CreateDirectory(destination);
        foreach (var (source, target) in files) {
            if (!File.Exists(target)) {
                var staging = target + "." + Guid.NewGuid().ToString("N") + ".partial";
                try {
                    using (var input = File.OpenRead(source))
                    using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                        input.CopyTo(output);
                        output.Flush(true);
                    }
                    if (!Identical(source, staging)) throw new IOException($"Could not verify Saved file copy: {source}");
                    File.Move(staging, target);
                }
                finally { File.Delete(staging); }
            }
            if (!Identical(source, target)) throw new IOException($"Saved file changed during migration: {source}");
        }
        void PreserveCleanupEvidence(string configHash) {
            if (files.Count == 0) return;
            if (backupRoot == null) throw new IOException("Saved migration requires a recovery backup directory.");
            RejectLinks(backupRoot);
            Directory.CreateDirectory(backupRoot);
            // Existing incomplete transfers must be reconciled before another transfer.
            LegacyOwnershipMigration.WriteEvidence(Path.Combine(backupRoot, "saved-cleanup.json"),
                JsonSerializer.SerializeToUtf8Bytes(new CleanupEvidence(configHash, sourceDirectories,
                    files.Select(f => new Transfer(f.Source, f.Target, Hash(f.Source))).ToArray())));
        }
        publish(files.Select(f => f.Source).ToArray(), PreserveCleanupEvidence);
        foreach (var (source, target) in files) {
            RejectLinks(source);
            RejectLinks(target);
            if (!Identical(source, target)) throw new IOException($"Saved file changed during migration: {source}");
            File.Delete(source);
        }
    }
}