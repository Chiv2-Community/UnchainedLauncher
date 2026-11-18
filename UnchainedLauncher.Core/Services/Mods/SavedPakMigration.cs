using System.Security.Cryptography;

namespace UnchainedLauncher.Core.Services.Mods;

public static class SavedPakMigration {
    public static bool SameDirectory(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static IEnumerable<string> SourceDirectories(string destination, string suffix = "Unchained", PakDirMetadata? cached = null) {
        if (suffix.Length == 0 || suffix.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new IOException("Invalid Saved directory suffix.");
        var tbl = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(destination)))!;
        yield return Path.Combine(tbl, "Saved", "Paks");
        yield return Path.Combine(tbl, "Saved_" + suffix, "Paks");
        // Shared user roots cannot be attributed to an installation without its cached source path.
        if (cached == null) yield break;
        var parent = Path.GetDirectoryName(Path.GetFullPath(cached.DirPath));
        var name = Path.GetFileName(parent);
        if (name != "Saved" && (name == null || !name.StartsWith("Saved_", StringComparison.Ordinal)
            || name[6..].Length == 0 || name[6..].Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))) yield break;
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

    // Caller holds every participating inventory lock. Publish ownership before deleting source evidence.
    internal static void Migrate(string destination, IEnumerable<string> sources, Action<IReadOnlyList<string>> publish) {
        RejectLinks(destination);
        var files = new List<(string Source, string Target)>();
        foreach (var source in sources) {
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
        publish(files.Select(f => f.Source).ToArray());
        foreach (var (source, target) in files) {
            RejectLinks(source);
            RejectLinks(target);
            if (!Identical(source, target)) throw new IOException($"Saved file changed during migration: {source}");
            File.Delete(source);
        }
    }
}