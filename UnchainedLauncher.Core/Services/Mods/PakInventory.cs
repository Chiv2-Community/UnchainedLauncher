using System.Text.Json;

namespace UnchainedLauncher.Core.Services.Mods {
    // Shared file ownership only. Enabled mods and cached metadata remain launcher preferences.
    internal sealed class PakInventory {
        private readonly string _directory;
        private readonly TimeProvider _timeProvider;
        private string InventoryPath => Path.Combine(_directory, "inventory.json");
        private string PendingPath => Path.Combine(_directory, "pending");

        public PakInventory(string pakDirectory, TimeProvider? timeProvider = null) {
            _directory = Path.Combine(pakDirectory, ".unchained");
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<FileStream> Lock(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_directory);
            while (true) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    // Keep the lock file: deleting it could let two processes lock different files.
                    return new FileStream(Path.Combine(_directory, "inventory.lock"),
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException ex) when (IsLockContention(ex)) {
                    // Downloads can take arbitrarily long. Only cancellation ends a healthy wait.
                    await Task.Delay(TimeSpan.FromMilliseconds(50), _timeProvider, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static bool IsLockContention(IOException exception) =>
            (exception.HResult & 0xffff) is 32 or 33 // Windows sharing/lock violation
            || (!OperatingSystem.IsWindows() && (exception.HResult & 0xffff) == 11); // Unix EAGAIN

        public List<ManagedPak> Load(IEnumerable<ManagedPak> legacyPaks) {
            if (File.Exists(PendingPath)) {
                throw new InvalidDataException(
                    $"Pak preparation was interrupted. Reconcile the pak files with '{InventoryPath}' before removing '{PendingPath}'.");
            }
            // Never fall back to a stale launcher snapshot if the shared inventory is unreadable.
            var paks = File.Exists(InventoryPath)
                ? JsonSerializer.Deserialize<List<ManagedPak>>(File.ReadAllText(InventoryPath))
                    ?? throw new InvalidDataException("Empty managed pak inventory")
                : legacyPaks.ToList();
            if (paks.Any(p => p == null || p.Coordinates == null
                    || string.IsNullOrWhiteSpace(p.Coordinates.Org)
                    || string.IsNullOrWhiteSpace(p.Coordinates.ModuleName)
                    || string.IsNullOrWhiteSpace(p.Coordinates.Version)
                    || string.IsNullOrWhiteSpace(p.PakFileName)
                    || p.PakFileName.Contains('/') || p.PakFileName.Contains('\\')
                    || !p.PakFileName.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)
                    || p.PakFileName.Equals("pakchunk0-WindowsNoEditor.pak", StringComparison.OrdinalIgnoreCase))
                || paks.Select(p => p.PakFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != paks.Count
                || paks.Select(p => p.Coordinates).Distinct().Count() != paks.Count) {
                throw new InvalidDataException("Invalid managed pak inventory");
            }
            return paks;
        }

        public void BeginChange() {
            // A crash between changing files and publishing ownership must not trigger duplicate downloads.
            using var stream = new FileStream(PendingPath, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.Flush(flushToDisk: true);
        }

        public void Save(IEnumerable<ManagedPak> paks) {
            var temporaryPath = Path.Combine(_directory, "inventory.tmp");
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(stream, paks);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, InventoryPath, overwrite: true);
            File.Delete(PendingPath);
        }
    }
}