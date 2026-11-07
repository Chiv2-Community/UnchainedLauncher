using System.Security.Cryptography;

namespace UnchainedLauncher.Core.Services.Mods;

public static class SavedPakMigration {
    public static IEnumerable<string> SourceDirectories(string suffix = "Unchained") {
        // Only launcher/game Saved roots, never an arbitrary cached directory.
        if (suffix.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new IOException("Invalid Saved directory suffix.");
        yield return Path.GetFullPath(@"TBL\Saved\Paks");
        var gameData = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Chivalry 2");
        yield return Path.Combine(gameData, "Saved", "Paks");
        yield return Path.Combine(gameData, "Saved_" + suffix, "Paks");
    }

    public static bool SameDirectory(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<string> StartupSources(PakDirMetadata? cached) {
        var sources = SourceDirectories();
        if (cached == null) return sources;
        var savedName = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(cached.DirPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        if (savedName == null || !savedName.StartsWith("Saved_", StringComparison.OrdinalIgnoreCase)) return sources;
        var suffix = savedName[6..];
        if (suffix.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) return sources;
        var profileSources = SourceDirectories(suffix).ToArray();
        return profileSources.Any(p => SameDirectory(p, cached.DirPath)) ? sources.Concat(profileSources) : sources;
    }

    public static IReadOnlyList<ManagedPak> Migrate(string destination, IEnumerable<string> sources, PakDirMetadata? cached = null) {
        RejectLinks(destination);
        var files = new List<(string Source, string Target)>();
        foreach (var source in sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (SameDirectory(source, destination)) continue;
            RejectLinks(source);
            if (!Directory.Exists(source)) continue;
            foreach (var file in EnumerateSavedFiles(source)) {
                var extension = Path.GetExtension(file);
                if (!extension.Equals(".pak", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".sig", StringComparison.OrdinalIgnoreCase)) continue;
                RejectLinks(file);
                var target = Path.Combine(destination, Path.GetFileName(file));
                RejectLinks(target);
                if (File.Exists(target) && !Identical(file, target))
                    throw new IOException($"Conflicting Saved pak or signature; files preserved: '{file}' -> '{target}'.");
                files.Add((file, target));
            }
        }

        // Check the entire batch before changing anything, including cross-profile collisions.
        foreach (var group in files.GroupBy(f => f.Target, StringComparer.OrdinalIgnoreCase)) {
            var first = group.First().Source;
            if (group.Skip(1).Any(f => !Identical(first, f.Source)))
                throw new IOException($"Conflicting Saved files; files preserved: {group.Key}");
        }
        var ownership = new List<ManagedPak>();
        if (files.Count == 0) return ownership;
        Directory.CreateDirectory(destination);
        foreach (var (source, target) in files) {
            if (!File.Exists(target)) {
                // CreateNew never overwrites. Flush and verify before removing the source.
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
                finally {
                    File.Delete(staging);
                }
            }
            if (!Identical(source, target)) throw new IOException($"Could not verify Saved file migration: {source}");
            if (cached != null && SameDirectory(Path.GetDirectoryName(source)!, cached.DirPath))
                ownership.AddRange(cached.ManagedPaks.Where(p => p.PakFileName.Equals(Path.GetFileName(source), StringComparison.OrdinalIgnoreCase)
                    && Path.GetExtension(source).Equals(".pak", StringComparison.OrdinalIgnoreCase)));
        }
        // Keep every source until all copies verify. Interrupted cleanup retries by byte equality;
        // missing source evidence never grants ownership to a same-named destination file.
        foreach (var (source, target) in files) {
            RejectLinks(source);
            RejectLinks(target);
            if (!Identical(source, target)) throw new IOException($"Saved file changed during migration: {source}");
            File.Delete(source);
        }
        return ownership;
    }

    private static IEnumerable<string> EnumerateSavedFiles(string directory) {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory)) {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0) {
                RejectLinks(entry);
                foreach (var file in EnumerateSavedFiles(entry)) yield return file;
            }
            else {
                yield return entry;
            }
        }
    }

    private static bool Identical(string left, string right) {
        using var a = File.OpenRead(left);
        using var b = File.OpenRead(right);
        return a.Length == b.Length && SHA256.HashData(a).SequenceEqual(SHA256.HashData(b));
    }

    private static void RejectLinks(string path) {
        // The volume root is the path's boundary, not a traversed directory link (e.g. Proton's Z: mapping).
        for (var current = Path.GetFullPath(path); Path.GetDirectoryName(current) != null; current = Path.GetDirectoryName(current)!) {
            try {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Refusing Saved pak migration through a link: {current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}