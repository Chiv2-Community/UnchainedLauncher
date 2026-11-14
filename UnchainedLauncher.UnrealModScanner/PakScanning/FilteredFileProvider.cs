using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Utils;
using System.Collections.Concurrent;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;

namespace UnchainedLauncher.UnrealModScanner.PakScanning {
    using PakContentFilter = Predicate<KeyValuePair<string, GameFile>>;

    /// <summary>
    /// Filters scan inputs and records package lookups for cache invalidation.
    /// </summary>
    public class FilteredFileProvider(
        string directory,
        SearchOption searchOption,
        Predicate<KeyValuePair<string, GameFile>>? pakContentFilter,
        string aesKey,
        VersionContainer? versions = null,
        StringComparer? pathComparer = null,
        Predicate<string>? containerFilter = null)
        : DefaultFileProvider(new DirectoryInfo(directory), [], searchOption, versions, pathComparer), IFileProvider {

        private static readonly string[] PackageExtensions = [".uasset", ".umap", ".uexp", ".ubulk", ".uptnl"];
        private readonly AsyncLocal<string?> scanOwner = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> lookups = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> ioStoreOwners = new(StringComparer.OrdinalIgnoreCase);

        public IDisposable TrackScan(string pak) {
            var previous = scanOwner.Value;
            scanOwner.Value = Path.GetFileName(pak);
            return new ScanScope(() => scanOwner.Value = previous);
        }

        private sealed class ScanScope(Action end) : IDisposable {
            public void Dispose() => end();
        }

        public string[] GetScanLookups(string pak) => lookups.TryGetValue(Path.GetFileName(pak), out var paths)
            ? paths.Keys.ToArray() : [];

        public bool ScanUsesIoStore(string pak) => ioStoreOwners.ContainsKey(Path.GetFileName(pak));

        private void TrackPackage(IPackage? package) {
            if (package is IoPackage && scanOwner.Value is { } owner) ioStoreOwners.TryAdd(owner, 0);
        }

        private void TrackLookup(string path) {
            if (scanOwner.Value is not { } owner) return;
            var paths = lookups.GetOrAdd(owner, _ => new(StringComparer.Ordinal));
            var fixedPath = FixPath(path);
            paths.TryAdd(path, 0);
            paths.TryAdd(fixedPath, 0);
            // CUE4Parse tries .umap after .uasset, and resolves payloads independently.
            var stem = fixedPath[..fixedPath.LastIndexOf('.')];
            foreach (var extension in PackageExtensions)
                paths.TryAdd(stem + extension, 0);
        }

        public new Task<IPackage> LoadPackageAsync(string path) {
            TrackLookup(path);
            return base.LoadPackageAsync(path);
        }

        public new Task<IPackage> LoadPackageAsync(GameFile file) {
            TrackLookup(file.Path);
            return base.LoadPackageAsync(file);
        }

        // Base methods are non-virtual. Package import resolution calls this interface.
        bool IFileProvider.TryLoadPackage(string path, out IPackage package) {
            TrackLookup(path);
            var loaded = base.TryLoadPackage(path, out package);
            TrackPackage(package);
            return loaded;
        }

        IPackage IFileProvider.LoadPackage(GameFile file) {
            TrackLookup(file.Path);
            var package = base.LoadPackage(file);
            TrackPackage(package);
            return package;
        }

        IPackage IFileProvider.LoadPackage(string path) {
            TrackLookup(path);
            var package = base.LoadPackage(path);
            TrackPackage(package);
            return package;
        }

        bool IFileProvider.TryGetGameFile(string path, out GameFile file) {
            TrackLookup(path);
            return base.TryGetGameFile(path, out file);
        }

        bool IFileProvider.TrySaveAsset(string path, out byte[] data) {
            TrackLookup(path);
            return base.TrySaveAsset(path, out data);
        }

        public void InitializeContainers(IEnumerable<string> paths) {
            foreach (var path in paths.Where(path => containerFilter?.Invoke(path) != false)) RegisterVfs(new FileInfo(path));
            SubmitKey(new FGuid(), new FAesKey(aesKey));
        }

        /// <summary>
        /// Applies game, scan-mode, and optional per-entry filters.
        /// </summary>
        public static FilteredFileProvider CreateFromOptions(ScanOptions options, ScanMode mode, string pakDir, PakContentFilter? extraPakFilter = null) {
            IScanFilter? pakFilter = mode switch {
                ScanMode.Mods => new IgnorePakScanFilter(options.VanillaPakNames.ToArray()),
                ScanMode.GameInternal => new SpecificPakScanFilter(options.VanillaPakNames.ToArray()),
                _ => null
            };


            var contentFilter = options.ScanFilter.With(pakFilter).CreatePakContentFilter();
            return new FilteredFileProvider(
                pakDir,
                SearchOption.TopDirectoryOnly,
                kv => contentFilter(kv) && (extraPakFilter?.Invoke(kv) ?? true),
                options.AesKey,
                new VersionContainer(options.GameVersion),
                containerFilter: mode == ScanMode.Mods
                    ? path => !options.VanillaPakNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    : null
            );
        }

        public IEnumerable<KeyValuePair<string, GameFile>> FilteredFiles {
            get => pakContentFilter != null
                ? Files.Where(kv => pakContentFilter(kv))
                : Files;
        }

        public new void Initialize() {
            var workingDir = new DirectoryInfo(directory);
            if (!workingDir.Exists)
                throw new DirectoryNotFoundException("Game directory not found.");

            // Replicate DefaultFileProvider logic with the filter injection
            var uproject = workingDir.GetFiles("*.uproject", SearchOption.TopDirectoryOnly).FirstOrDefault();
            var currentOption = uproject != null ? SearchOption.AllDirectories : searchOption;

            foreach (var file in workingDir.EnumerateFiles("*.*", currentOption)) {
                var ext = file.Extension.SubstringAfter('.').ToUpper();

                // Filter containers (PAK/UTOC)
                if (uproject == null && ext is "PAK" or "UTOC"
                    && containerFilter?.Invoke(file.FullName) != false) RegisterVfs(file);
            }

            SubmitKey(new FGuid(), new FAesKey(aesKey));
        }

        internal void InitializePak(string path, Stream stream) {
            RegisterVfs(path, [stream], null);
            SubmitKey(new FGuid(), new FAesKey(aesKey));
        }
    }
}