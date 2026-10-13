using Semver;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnchainedLauncher.Core.Services.Dlls {
    /// <summary>Only explicitly enabled, registry-backed releases may reach the injector.</summary>
    public sealed class DllManager {
        public const string IndexUrl = "https://raw.githubusercontent.com/Chiv2-Community/C2ModRegistry/refs/heads/main/dll_db/index.json";
        public const string DatabaseUrl = "https://raw.githubusercontent.com/Chiv2-Community/C2ModRegistry/refs/heads/main/dll_db";
        public const string PackageListUrl = DatabaseUrl + "/plugin_list_index.txt";
        public const string DefaultPluginId = "Chiv2-Community/UnchainedPlugin";
        private readonly HttpClient _http;
        private readonly string _root;
        private readonly string _cacheRoot;
        private readonly string _statePath;
        private readonly string _indexPath;
        private readonly Func<IReadOnlyList<string>> _registryUrls;
        private string? _approvedSources;
        private readonly Dictionary<string, string> _enabled;
        private readonly object _gate = new();
        private bool _defaultPending;
        private readonly Func<IReadOnlyList<UnchainedLauncher.Core.JsonModels.ModMetadata.Release>> _modReleases;
        public IReadOnlyList<DllRelease> Releases { get; private set; } = [];
        public event EventHandler? SelectionChanged;

        public DllManager(string pluginDirectory, string statePath, HttpClient http,
            Func<IReadOnlyList<UnchainedLauncher.Core.JsonModels.ModMetadata.Release>>? modReleases = null,
            Func<IReadOnlyList<string>>? registryUrls = null, string? cacheDirectory = null) {
            _registryUrls = registryUrls ?? (() => [DatabaseUrl]);
            _modReleases = modReleases ?? (() => []);
            var plugins = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pluginDirectory));
            _root = Path.Combine(plugins, "Managed");
            _cacheRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cacheDirectory
                ?? Path.Combine(Path.GetDirectoryName(plugins)!, ".cache", "dlls")));
            if (_cacheRoot.Equals(plugins, StringComparison.OrdinalIgnoreCase)
                || _cacheRoot.StartsWith(plugins + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("DLL cache must be outside Plugins.", nameof(cacheDirectory));
            _statePath = statePath;
            _indexPath = statePath + ".index.json";
            _http = http;
            _defaultPending = !File.Exists(statePath);
            var enabled = File.Exists(statePath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(statePath))
                  ?? throw new InvalidDataException("Invalid DLL selection file.")
                : new Dictionary<string, string>();
            _enabled = new Dictionary<string, string>(enabled, StringComparer.OrdinalIgnoreCase);
            if (File.Exists(_indexPath) && File.Exists(_indexPath + ".sources") &&
                File.ReadAllText(_indexPath + ".sources") == SourceSignature()) {
                _approvedSources = SourceSignature();
                Releases = ParseIndex(File.ReadAllText(_indexPath));
                EnableDefault();
            }
        }

        public static IReadOnlyList<DllRelease> ParseIndex(string json) {
            var entries = JsonSerializer.Deserialize<List<DllRelease>>(json)
                          ?? throw new InvalidDataException("Missing DLL index.");
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var release in entries) {
                if (release == null) throw new InvalidDataException("Null DLL release.");
                release.Validate();
                if (!keys.Add(release.Key)) throw new InvalidDataException("Duplicate DLL release.");
            }
            return entries;
        }

        public async Task RefreshAsync() {
            var sources = RegistryUrls();
            var signature = JsonSerializer.Serialize(sources);
            var merged = new Dictionary<string, DllRelease>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources) {
                foreach (var release in await ReadRegistryAsync(source)) {
                    if (merged.TryGetValue(release.Key, out var old) && Canonical(old) != Canonical(release))
                        throw new InvalidDataException($"Conflicting DLL registry records: {release.Key}");
                    merged[release.Key] = release;
                }
            }
            var json = JsonSerializer.Serialize(merged.Values.ToArray());
            var releases = ParseIndex(json);
            lock (_gate) {
                if (SourceSignature() != signature) throw new InvalidOperationException("DLL registry settings changed during refresh. Refresh again.");
                // Invalidate the old source marker before replacing the cached approvals.
                WriteAtomic(_indexPath + ".sources", "");
                WriteAtomic(_indexPath, json);
                WriteAtomic(_indexPath + ".sources", signature);
                Releases = releases;
                _approvedSources = signature;
                EnableDefault();
            }
        }

        private string[] RegistryUrls() => _registryUrls().Select(url => {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidDataException("Invalid DLL registry URL.");
            return uri.AbsoluteUri.TrimEnd('/');
        }).Distinct(StringComparer.Ordinal).OrderBy(url => url, StringComparer.Ordinal).ToArray();

        private string SourceSignature() => JsonSerializer.Serialize(RegistryUrls());

        private async Task<IReadOnlyList<DllRelease>> ReadRegistryAsync(string databaseUrl) {
            // The package list is authoritative. A stale legacy index must not
            // re-approve removed releases or block metadata/target updates.
            using var listResponse = await _http.GetAsync(databaseUrl + "/plugin_list_index.txt");
            if (listResponse.StatusCode == HttpStatusCode.NotFound) {
                using var legacyResponse = await _http.GetAsync(databaseUrl + "/index.json");
                // Mod-only registries have neither format.
                return legacyResponse.StatusCode == HttpStatusCode.NotFound ? [] : ParseIndex(await ReadResponse(legacyResponse));
            }
            var packages = (await ReadResponse(listResponse)).Split('\n').Select(line => line.Trim()).Where(line => line.Length != 0);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var merged = new Dictionary<string, DllRelease>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in packages) {
                if (!Regex.IsMatch(package, "^[A-Za-z0-9_-]+/[A-Za-z0-9_-]+$") || !seen.Add(package))
                    throw new InvalidDataException("Invalid or duplicate plugin package identifier.");
                using var packageResponse = await _http.GetAsync($"{databaseUrl}/packages/{package}.json");
                foreach (var release in ParsePackage(await ReadResponse(packageResponse), package))
                    merged.Add(release.Key, release);
            }
            return merged.Values.ToArray();
        }

        public static IReadOnlyList<DllRelease> ParsePackage(string json, string identifier) {
            if (!Regex.IsMatch(identifier, "^[A-Za-z0-9_-]+/[A-Za-z0-9_-]+$"))
                throw new InvalidDataException("Invalid plugin package identifier.");
            var parts = identifier.Split('/');
            var data = JsonSerializer.Deserialize<PluginPackage>(json) ?? throw new InvalidDataException("Missing plugin metadata.");
            if (data.LatestReleaseInfo == null || data.Releases == null || data.Releases.Count == 0)
                throw new InvalidDataException("Incomplete plugin metadata.");
            data.LatestReleaseInfo.Validate();
            if (data.LatestReleaseInfo.RepoUrl != null && !data.LatestReleaseInfo.RepoUrl.Equals($"https://github.com/{identifier}", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Plugin repository mismatch.");
            var latest = data.Releases.OrderByDescending(release => release?.ReleaseDate).FirstOrDefault(release => release?.Info != null)?.Info;
            if (latest == null || CanonicalInfo(latest) != CanonicalInfo(data.LatestReleaseInfo))
                throw new InvalidDataException("Plugin latest-release metadata mismatch.");
            // Upstream permits null info/date/filename for previously approved
            // historical releases inside an otherwise metadata-backed package.
            return ParseIndex(JsonSerializer.Serialize(data.Releases.Select(record => {
                if (record == null) throw new InvalidDataException("Null plugin release.");
                return record.ToDllRelease(parts[0], parts[1]);
            })));
        }

        private static async Task<string> ReadResponse(HttpResponseMessage response) {
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Insecure DLL metadata redirect.");
            return await response.Content.ReadAsStringAsync();
        }

        private static string Canonical(DllRelease release) => JsonSerializer.Serialize(release with {
            Organization = release.Organization.ToLowerInvariant(),
            Name = release.Name.ToLowerInvariant(),
            Tag = release.Tag ?? release.Version,
            Info = release.Info == null ? null : NormalizeInfo(release.Info)
        });

        private static PluginInfo NormalizeInfo(PluginInfo info) => info with {
            PluginDependencies = info.RequiredPlugins,
            ModDependencies = info.RequiredMods,
            ImageUrls = info.ImageUrls ?? [],
            Schema = null
        };

        private static string CanonicalInfo(PluginInfo info) => JsonSerializer.Serialize(NormalizeInfo(info));

        private void EnableDefault() {
            if (!_defaultPending) return;
            var latest = Releases.Where(r => r.Id == DefaultPluginId)
                .Where(r => !SemVersion.Parse(r.Version, SemVersionStyles.Strict).IsPrerelease)
                .OrderByDescending(r => SemVersion.Parse(r.Version, SemVersionStyles.Strict),
                    Comparer<SemVersion>.Create((a, b) => a.ComparePrecedenceTo(b))).FirstOrDefault();
            if (latest == null) return;
            _enabled[latest.Id] = latest.Version;
            Save();
            _defaultPending = false;
        }

        public bool IsEnabled(DllRelease release) {
            lock (_gate) return _enabled.TryGetValue(release.Id, out var version) && version == release.Version;
        }

        public IReadOnlyDictionary<string, string> EnabledSelections {
            get { lock (_gate) return new Dictionary<string, string>(_enabled, StringComparer.OrdinalIgnoreCase); }
        }

        // Revoking a selection never requires registry approval and never touches DLL files.
        public void Disable(string id) {
            lock (_gate) {
                if (!_enabled.Remove(id, out var previous)) return;
                try { Save(); }
                catch { _enabled[id] = previous; throw; }
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetEnabled(DllRelease release, bool enabled) {
            if (!enabled) {
                if (IsEnabled(release)) Disable(release.Id);
                return;
            }
            lock (_gate) {
                EnsureCurrentSources();
                if (!Releases.Contains(release)) throw new InvalidOperationException("Unregistered DLL.");
                var previous = _enabled.GetValueOrDefault(release.Id);
                _enabled[release.Id] = release.Version;
                try { Save(); }
                catch {
                    if (previous == null) _enabled.Remove(release.Id);
                    else _enabled[release.Id] = previous;
                    throw;
                }
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Save() => WriteAtomic(_statePath, JsonSerializer.Serialize(_enabled));

        private static void WriteAtomic(string path, string contents) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temp, contents);
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private DllRelease[] SelectedReleases(bool server = false) {
            lock (_gate) {
                var selected = EnabledForTarget(server);
                DependencyValidator.Validate(_modReleases(), selected);
                var ordered = new List<DllRelease>();
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                void Visit(DllRelease release) {
                    if (visited.Contains(release.Id)) return;
                    if (!visiting.Add(release.Id)) throw new InvalidOperationException("Circular plugin dependency.");
                    foreach (var dependency in release.Info?.RequiredPlugins ?? []) {
                        var url = new Uri(dependency.RepoUrl);
                        var id = url.AbsolutePath.Trim('/');
                        var required = selected.SingleOrDefault(candidate => candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                        if (required == null || !PluginInfo.MatchesVersion(required.Version, dependency.Version))
                            throw new InvalidOperationException($"Enable a compatible registered dependency first: {id} {dependency.Version}");
                        Visit(required);
                    }
                    visiting.Remove(release.Id);
                    visited.Add(release.Id);
                    ordered.Add(release);
                }
                foreach (var release in selected) Visit(release);
                return ordered.ToArray();
            }
        }

        private static bool AppliesToTarget(DllRelease release, bool server) =>
            release.Info?.Target is null or "shared" || release.Info.Target == (server ? "server" : "client");

        private DllRelease[] EnabledForTarget(bool? server) {
            EnsureCurrentSources();
            if (_defaultPending) throw new InvalidOperationException("The default plugin is not registered. Refresh the DLL index before launching.");
            return _enabled.Select(pair => Releases.SingleOrDefault(r => r.Id.Equals(pair.Key, StringComparison.OrdinalIgnoreCase) && r.Version == pair.Value)
                ?? throw new InvalidOperationException($"Enabled DLL is no longer registered: {pair.Key}:{pair.Value}"))
                .Where(release => server == null || AppliesToTarget(release, server.Value)).ToArray();
        }

        public IReadOnlyList<(DllRelease Current, DllRelease Latest)> GetUpdateCandidates(bool? server, bool allowUnstable) {
            lock (_gate) {
                var updates = new List<(DllRelease, DllRelease)>();
                foreach (var current in EnabledForTarget(server)) {
                    var latest = Releases.Where(release => release.Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase)
                            && (server == null || AppliesToTarget(release, server.Value))
                            && (allowUnstable || !SemVersion.Parse(release.Version, SemVersionStyles.Strict).IsPrerelease))
                        .OrderByDescending(release => SemVersion.Parse(release.Version, SemVersionStyles.Strict),
                            Comparer<SemVersion>.Create((a, b) => a.ComparePrecedenceTo(b))).FirstOrDefault();
                    if (latest != null && SemVersion.Parse(latest.Version, SemVersionStyles.Strict)
                        .ComparePrecedenceTo(SemVersion.Parse(current.Version, SemVersionStyles.Strict)) > 0)
                        updates.Add((current, latest));
                }
                return updates;
            }
        }

        private void EnsureCurrentSources() {
            if (_approvedSources != SourceSignature())
                throw new InvalidOperationException("DLL registry settings changed. Refresh Plugin Manager before launching.");
        }

        private string GetPath(DllRelease release) => Path.Combine(_root, release.Organization, release.Name, release.DllFileName ?? release.Name + ".dll");
        private string GetLegacyPath(DllRelease release) => Path.Combine(_root, release.Organization, release.Name, release.Sha256, release.DllFileName ?? release.Name + ".dll");
        private string GetCachePath(DllRelease release) => Path.Combine(_cacheRoot, release.Sha256, release.DllFileName ?? release.Name + ".dll");

        private static bool MatchesFile(string path, DllRelease release) {
            using var stream = File.OpenRead(path);
            try { ValidateBytes(stream, release); return true; }
            catch (InvalidDataException) { return false; }
        }

        private DllRelease? ManagedReleaseAt(string path) => Releases.FirstOrDefault(release =>
            (GetPath(release).Equals(path, StringComparison.OrdinalIgnoreCase)
             || GetLegacyPath(release).Equals(path, StringComparison.OrdinalIgnoreCase))
            && MatchesFile(path, release));

        public static void ValidateBytes(Stream stream, DllRelease release) {
            release.Validate();
            stream.Position = 0;
            if (stream.Length != release.Size ||
                !Convert.ToHexString(SHA256.HashData(stream)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException($"DLL size or SHA-256 mismatch: {release.Key}");
            }
            stream.Position = 0;
        }

        public async Task PrepareAsync(bool server = false, bool allowDownloads = true) {
            var selected = SelectedReleases(server);
            // Fill and validate the cache before changing any installed DLLs.
            foreach (var release in selected) {
                var path = GetPath(release);
                if (File.Exists(path) && !MatchesFile(path, release) && ManagedReleaseAt(path) == null)
                    throw new InvalidDataException($"Unrecognized DLL at managed path; file left unchanged: {path}");
                var cache = GetCachePath(release);
                if (File.Exists(cache)) {
                    using var existing = File.OpenRead(cache);
                    ValidateBytes(existing, release);
                    continue;
                }
                var installed = new[] { path, GetLegacyPath(release) }
                    .FirstOrDefault(candidate => File.Exists(candidate) && MatchesFile(candidate, release));
                if (installed != null) {
                    CopyValidated(installed, cache, release);
                    continue;
                }
                if (!allowDownloads)
                    throw new InvalidOperationException($"Automatic plugin downloads are disabled; missing registered DLL: {release.Key}");
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                var temp = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try {
                    using var response = await _http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                        throw new InvalidDataException("Insecure DLL download redirect.");
                    await using (var source = await response.Content.ReadAsStreamAsync())
                    await using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)) {
                        var buffer = new byte[81920];
                        long total = 0;
                        int count;
                        while ((count = await source.ReadAsync(buffer)) != 0) {
                            total += count;
                            if (total > release.Size) throw new InvalidDataException("DLL exceeds registered size.");
                            await target.WriteAsync(buffer.AsMemory(0, count));
                        }
                        await target.FlushAsync();
                        target.Position = 0;
                        ValidateBytes(target, release);
                    }
                    File.Move(temp, cache, true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            lock (_gate) {
                if (!SelectedReleases(server).SequenceEqual(selected))
                    throw new InvalidOperationException("Plugin selections or metadata changed during preparation. Launch again.");
                var active = selected.Select(GetPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                // Only validated files at paths owned by this manager may be archived.
                // Unknown names/versions, even inside Managed, are left untouched.
                if (Directory.Exists(_root)) {
                    foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToArray()) {
                        var release = ManagedReleaseAt(path);
                        if (release == null || (active.Contains(path) && selected.Any(candidate =>
                            GetPath(candidate).Equals(path, StringComparison.OrdinalIgnoreCase) && MatchesFile(path, candidate))))
                            continue;
                        CopyValidated(path, GetCachePath(release), release);
                        File.Delete(path);
                    }
                }
                foreach (var release in selected) {
                    var path = GetPath(release);
                    if (!File.Exists(path)) CopyValidated(GetCachePath(release), path, release);
                    using var installed = File.OpenRead(path);
                    ValidateBytes(installed, release);
                }
            }
        }

        private static void CopyValidated(string source, string destination, DllRelease release) {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)) {
                    ValidateBytes(input, release);
                    input.CopyTo(output);
                    ValidateBytes(output, release);
                }
                File.Move(temp, destination, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        // Read handles deny writes/deletion on Windows until injection completes.
        public IReadOnlyList<ValidatedDll> OpenForInjection(bool server = false) {
            var files = new List<ValidatedDll>();
            try {
                foreach (var release in SelectedReleases(server)) {
                    var path = GetPath(release);
                    var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    try { ValidateBytes(stream, release); }
                    catch { stream.Dispose(); throw; }
                    files.Add(new ValidatedDll(path, stream));
                }
                return files;
            }
            catch {
                foreach (var file in files) file.Dispose();
                throw;
            }
        }
    }

    public sealed class ValidatedDll : IDisposable {
        public string Path { get; }
        private readonly Stream _stream;
        internal ValidatedDll(string path, Stream stream) { Path = path; _stream = stream; }
        public void Dispose() => _stream.Dispose();
    }
}