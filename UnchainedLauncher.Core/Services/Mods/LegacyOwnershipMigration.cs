using log4net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods;

// Startup-only compatibility boundary. Backups are evidence, never live ownership.
public static class LegacyOwnershipMigration {
    private static readonly ILog Log = LogManager.GetLogger(typeof(LegacyOwnershipMigration));
    private static readonly string[] LegacyNames = ["inventory.tmp", "inventory.lock", "pending", "inventory.json"];
    public static string BackupRoot(string configPath) => Path.GetFullPath(configPath) + ".migration-backups";

    internal static void RejectUnconvertedEvidence(string directory) {
        SavedPakMigration.RejectLinks(directory);
        var paths = LegacyNames.Where(name => name != "inventory.lock").Select(name => Path.Combine(directory, ".unchained", name))
            .Concat(new[] { FilePaths.ManagedPakInventoryFileName, FilePaths.ManagedPakInventoryFileName + ".tmp" }
                .Select(name => Path.Combine(directory, name)));
        foreach (var path in paths) {
            SavedPakMigration.RejectLinks(path);
            if (File.Exists(path) || Directory.Exists(path))
                throw new IOException($"Unconverted legacy ownership in '{directory}'. Files were preserved; restart to convert or reconcile before launching.");
        }
    }

    public static (ModManager Manager, ModManagerPersistence Persistence) Load(
        string configPath, IModRegistry registry, Action<Exception>? reportSelectionSaveFailure = null,
        string? installationPakDirectory = null) {
        try { return LoadCore(configPath, registry, reportSelectionSaveFailure, installationPakDirectory); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException) {
            throw new InvalidDataException($"Invalid ownership evidence for '{configPath}'. Files were preserved; reconcile before starting.", ex);
        }
    }

    public static (ModManager Manager, ModManagerPersistence Persistence) LoadProfile(
        string configPath, IModRegistry registry, PakDir profile, IEnumerable<ReleaseCoordinates> selected) {
        try { return LoadCore(configPath, registry, null, profile.DirPath, profile, selected); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException) {
            throw new InvalidDataException($"Invalid profile ownership: {configPath}. Files were preserved.", ex);
        }
    }

    private static (ModManager Manager, ModManagerPersistence Persistence) LoadCore(
        string configPath, IModRegistry registry, Action<Exception>? reportSelectionSaveFailure,
        string? installationPakDirectory, PakDir? profile = null, IEnumerable<ReleaseCoordinates>? selected = null) {
        configPath = Path.GetFullPath(configPath);
        SavedPakMigration.RejectLinks(configPath);
        if (Directory.Exists(configPath)) throw new IOException($"Mod-manager config is a directory: {configPath}");
        var original = File.Exists(configPath) ? File.ReadAllBytes(configPath) : null;
        JsonNode? document = original == null ? null : JsonNode.Parse(original)
            ?? throw new InvalidDataException($"Null mod-manager config: {configPath}");
        if (document != null && (document["Kind"]?.GetValue<string>() != "StandardModManager"
            || document["EnabledModReleases"] is not JsonArray))
            throw new InvalidDataException($"Invalid mod-manager config: {configPath}");
        var installation = Path.GetFullPath(installationPakDirectory ?? FilePaths.PakDir);
        var metadata = document?["PakDir"];
        if (metadata != null && (string.IsNullOrWhiteSpace(metadata["DirPath"]?.GetValue<string>())
            || metadata["ManagedPaks"] is not JsonArray))
            throw new InvalidDataException($"Invalid ownership snapshot: {configPath}");
        var configured = metadata?["DirPath"]?.GetValue<string>() ?? installation;
        if (profile != null && !SavedPakMigration.SameDirectory(configured, profile.DirPath))
            throw new InvalidDataException("Profile config belongs to another pak directory. Files were preserved.");
        if (profile != null && metadata?["DefaultSigPath"]?.GetValue<string>() is { } template
            && (profile.DefaultSigPath == null || !SavedPakMigration.SameDirectory(template, profile.DefaultSigPath)))
            throw new InvalidDataException("Profile config belongs to another installation. Files were preserved.");
        // A rename reservation can temporarily name both files for one release.
        if (metadata?["ManagedPaks"] is JsonNode snapshot)
            Validate(configured, snapshot.Deserialize<ManagedPak[]>() ?? throw new InvalidDataException("Null ownership snapshot."), allowDuplicateCoordinates: true);
        var cached = new PakDirMetadata(configured, []);
        var sources = profile == null ? SavedPakMigration.SourceDirectories(installation, cached: cached).ToArray() : [];
        var cleanupSources = SavedPakMigration.CleanupSources(BackupRoot(configPath)).ToArray();
        var committedTransfer = File.Exists(Path.Combine(BackupRoot(configPath), "saved-cleanup.json"));
        if (profile != null && (cleanupSources.Length != 0 || committedTransfer))
            throw new InvalidDataException("Unexpected Saved-transfer evidence in an isolated CLI profile. Files were preserved.");
        var isSaved = sources.Any(source => SavedPakMigration.SameDirectory(source, configured));
        var destination = isSaved ? installation : configured;
        var directories = new[] { configured, destination }
            .Concat(cleanupSources)
            .Concat(SavedPakMigration.SameDirectory(destination, installation) ? sources : [])
            .Select(Path.GetFullPath).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var bundles = new List<(string Directory, Dictionary<string, byte[]> Files)>();
        var inventories = new Dictionary<string, ManagedPak[]>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var cliInventories = new Dictionary<string, ManagedPak[]>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var directory in directories) {
            SavedPakMigration.RejectLinks(directory);
            var files = new Dictionary<string, byte[]>();
            var cliPath = Path.Combine(directory, FilePaths.ManagedPakInventoryFileName);
            SavedPakMigration.RejectLinks(cliPath);
            SavedPakMigration.RejectLinks(cliPath + ".tmp");
            if (Directory.Exists(cliPath) || File.Exists(cliPath + ".tmp") || Directory.Exists(cliPath + ".tmp"))
                throw new IOException($"Uncertain legacy CLI ownership: {cliPath}. Files were preserved.");
            if (File.Exists(cliPath)) {
                var cliBytes = File.ReadAllBytes(cliPath);
                var old = JsonSerializer.Deserialize<PakDirMetadata>(cliBytes)
                    ?? throw new InvalidDataException("Null legacy CLI inventory.");
                if (old.ManagedPaks == null || string.IsNullOrWhiteSpace(old.DirPath)
                    || !SavedPakMigration.SameDirectory(old.DirPath, directory))
                    throw new InvalidDataException("Legacy CLI inventory belongs to another pak directory.");
                if (profile != null && old.DefaultSigPath is { } oldTemplate
                    && (profile.DefaultSigPath == null || !SavedPakMigration.SameDirectory(oldTemplate, profile.DefaultSigPath)))
                    throw new InvalidDataException("Legacy CLI inventory belongs to another installation.");
                var records = old.ManagedPaks.ToArray();
                Validate(directory, records, allowDuplicateCoordinates: true);
                cliInventories.Add(directory, records);
                files.Add(FilePaths.ManagedPakInventoryFileName, cliBytes);
            }
            foreach (var name in LegacyNames) {
                var path = Path.Combine(directory, ".unchained", name);
                SavedPakMigration.RejectLinks(path);
                // Never open an old lock, even for backup; it contains no ownership evidence.
                if (name != "inventory.lock" && File.Exists(path)) files.Add(name, File.ReadAllBytes(path));
                if (Directory.Exists(path)) throw new IOException($"Legacy ownership path is a directory: {path}");
            }
            if (files.ContainsKey("pending") || !files.ContainsKey("inventory.json") && files.ContainsKey("inventory.tmp"))
                throw new InvalidDataException($"Uncertain legacy ownership in '{directory}'. Preserve the files and reconcile before starting.");
            if (!files.TryGetValue("inventory.json", out var bytes)) {
                if (files.Count != 0) bundles.Add((directory, files));
                continue;
            }
            ManagedPak[] paks;
            try {
                paks = JsonSerializer.Deserialize<ManagedPak[]>(bytes)
                    ?? throw new InvalidDataException("Null inventory.");
                Validate(directory, paks);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) {
                throw new InvalidDataException($"Invalid legacy ownership in '{directory}'. Files were preserved.", ex);
            }
            inventories.Add(directory, paks.Where(p => File.Exists(Path.Combine(directory, p.PakFileName))).ToArray());
            bundles.Add((directory, files));
        }
        if (!committedTransfer && document != null && inventories.TryGetValue(Path.GetFullPath(configured), out var replacement))
            document["PakDir"]!["ManagedPaks"] = JsonSerializer.SerializeToNode(replacement);

        // Decode failures must not enter the generic preference-default fallback.
        var codec = new ModManagerCodec(registry, profile == null ? installation : null);
        var manager = document == null
            ? ModManagerCodec.ToClassType(new StandardModManagerMetadata([], [], null), registry, installation)
            : codec.Deserialize(document.ToJsonString()).Result
                ?? throw new InvalidDataException($"Cannot decode mod-manager config: {configPath}");
        var paksDirectory = (PakDir)manager.PakDir;
        if (!committedTransfer && inventories.TryGetValue(Path.GetFullPath(destination), out var destinationPaks))
            paksDirectory.ReplaceOwnership(destinationPaks);
        if (profile != null) {
            profile.ReplaceOwnership(paksDirectory.ManagedPaks);
            manager = new ModManager(registry, profile, selected ?? [], manager.Mods) { NeedsMetadataRefresh = manager.NeedsMetadataRefresh };
            paksDirectory = profile;
        }
        // The older CLI inventory could lag a valid config. Never replace that config
        // with the stale list; merge only consistent, same-scope file attribution.
        foreach (var (directory, records) in cliInventories) {
            var isDestination = SavedPakMigration.SameDirectory(directory, paksDirectory.DirPath);
            var merged = isDestination ? paksDirectory.ManagedPaks.ToList()
                : inventories.TryGetValue(directory, out var prior) ? prior.ToList()
                : SavedPakMigration.SameDirectory(directory, configured) && metadata?["ManagedPaks"] != null
                    ? metadata["ManagedPaks"]!.Deserialize<ManagedPak[]>()!.ToList() : [];
            foreach (var record in records.Where(p => File.Exists(Path.Combine(directory, p.PakFileName)))) {
                var existing = merged.FirstOrDefault(p => p.PakFileName.Equals(record.PakFileName, StringComparison.OrdinalIgnoreCase));
                if (existing != null && existing.Coordinates != record.Coordinates)
                    throw new InvalidDataException("Conflicting config and legacy CLI ownership. Files were preserved.");
                if (existing == null) merged.Add(record);
            }
            if (isDestination) paksDirectory.ReplaceOwnership(merged);
            else inventories[directory] = merged.ToArray();
        }
        paksDirectory.LegacySourceOwnership = inventories;
        paksDirectory.MigrationBackupRoot = BackupRoot(configPath);
        var persistence = new ModManagerPersistence(manager, configPath, reportSelectionSaveFailure);
        var hasSavedPayload = paksDirectory.MigrateInstallationSavedPaks
            && sources.Where(Directory.Exists).Any(SavedPakMigration.HasPayload);
        if (bundles.Count != 0 || hasSavedPayload) {
            var backup = Path.Combine(BackupRoot(configPath), Guid.NewGuid().ToString("N"));
            SavedPakMigration.RejectLinks(backup);
            Directory.CreateDirectory(backup);
            if (original != null) WriteEvidence(Path.Combine(backup, "mod_manager_config.json"), original);
            for (var i = 0; i < bundles.Count; i++) {
                var bundle = Path.Combine(backup, i.ToString());
                Directory.CreateDirectory(bundle);
                WriteEvidence(Path.Combine(bundle, "source.txt"), Encoding.UTF8.GetBytes(bundles[i].Directory));
                foreach (var (name, bytes) in bundles[i].Files) WriteEvidence(Path.Combine(bundle, name), bytes);
            }
            Log.Warn($"Legacy ownership backup: {backup}");
            if (committedTransfer) SavedPakMigration.RetryCleanup(BackupRoot(configPath));
            else paksDirectory.MigrateSavedPaks().GetAwaiter().GetResult();
            persistence.Save();
            foreach (var (directory, _) in bundles) {
                // Inventory is the last consumption sentinel. A failed retirement stops startup.
                if (Directory.Exists(Path.Combine(directory, ".unchained")))
                    foreach (var name in LegacyNames) File.Delete(Path.Combine(directory, ".unchained", name));
                File.Delete(Path.Combine(directory, FilePaths.ManagedPakInventoryFileName));
            }
        }
        else {
            SavedPakMigration.RetryCleanup(BackupRoot(configPath));
            paksDirectory.MigrateSavedPaks().GetAwaiter().GetResult();
        }
        SavedPakMigration.CompleteCleanup(BackupRoot(configPath));
        paksDirectory.LegacySourceOwnership.Clear();
        return (manager, persistence);
    }

    internal static void WriteEvidence(string path, byte[] bytes) {
        SavedPakMigration.RejectLinks(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(true);
    }

    internal static void Validate(string directory, IReadOnlyList<ManagedPak> paks, bool allowDuplicateCoordinates = false) {
        if (paks.Any(p => p == null || p.Coordinates == null
            || !ValidCoordinatePart(p.Coordinates.Org)
            || !ValidCoordinatePart(p.Coordinates.ModuleName)
            // Version is an exact release tag, not a filesystem component.
            || string.IsNullOrWhiteSpace(p.Coordinates.Version)
            || string.IsNullOrWhiteSpace(p.PakFileName)
            || p.PakFileName.IndexOfAny(['/', '\\', ':', '\0']) >= 0
            || p.PakFileName.Any(c => c < 32 || "<>\"|?*".Contains(c))
            || !p.PakFileName.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)
            || p.PakFileName.Equals("pakchunk0-WindowsNoEditor.pak", StringComparison.OrdinalIgnoreCase))
            || paks.Select(p => p.PakFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != paks.Count
            || !allowDuplicateCoordinates && paks.Select(p => p.Coordinates).Distinct().Count() != paks.Count)
            throw new InvalidDataException("Invalid managed pak records.");
        foreach (var pak in paks) SavedPakMigration.RejectLinks(Path.Combine(directory, pak.PakFileName));
    }

    private static bool ValidCoordinatePart(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value is not "." and not ".."
        && !value.Any(c => c < 32 || "/\\:\0?".Contains(c));
}