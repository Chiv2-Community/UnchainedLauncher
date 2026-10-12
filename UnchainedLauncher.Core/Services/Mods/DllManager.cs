using System.Security.Cryptography;
using LanguageExt;
using Semver;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;

namespace UnchainedLauncher.Core.Services.Mods;

/// <summary>Registry-approved DLL preparation and the process-local injection allowlist.</summary>
public sealed class DllManager : IChivalry2LaunchPreparer<LaunchOptions>, IProcessInjector {
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly AggregateModRegistry _registry;
    private readonly IModManager _mods;
    private readonly string _directory;
    private readonly bool _unsafe;
    private readonly IReadOnlyList<ReleaseCoordinates> _requested;
    private readonly CancellationToken _cancellationToken;
    private readonly HttpClient _client;
    private string[] _paths = [];
    private bool _prepared;
    private IReadOnlyList<DllRelease> _preparedReleases = [];

    public DllManager(AggregateModRegistry registry, IModManager mods, string directory,
        IEnumerable<ReleaseCoordinates>? requested = null, bool unsafeLoadAllDlls = false,
        CancellationToken cancellationToken = default, HttpClient? client = null) {
        _registry = registry;
        _mods = mods;
        _directory = Path.GetFullPath(directory);
        _requested = requested?.ToArray() ?? [];
        _unsafe = unsafeLoadAllDlls;
        _cancellationToken = cancellationToken;
        _client = client ?? Client;
    }

    public static bool Satisfies(DllRelease release, string requirement) {
        if (release.Version is null) return false;
        // The registry uses comma-separated comparisons; npm ranges use spaces.
        return SatisfiesVersion(release.Version, requirement);
    }

    public static bool SatisfiesVersion(SemVersion version, string requirement) => requirement.Split(',').All(part => {
        var normalized = System.Text.RegularExpressions.Regex.Replace(part.Trim(), @"^(\^|>=|<=|==|!=|>|<)?v", "$1");
        if (normalized.StartsWith("!=", StringComparison.Ordinal))
            return !SemVersionRange.ParseNpm("=" + normalized[2..], includeAllPrerelease: true).Contains(version);
        return SemVersionRange.ParseNpm(normalized.Replace("==", "="), includeAllPrerelease: true).Contains(version);
    });

    public static IReadOnlyList<DllRelease> Resolve(IReadOnlyList<DllPackage> packages,
        IEnumerable<ReleaseCoordinates> requested, bool server, bool allowUnstable) {
        var selected = new Dictionary<string, DllRelease>(StringComparer.OrdinalIgnoreCase);
        var visiting = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<DllRelease>();
        var roots = requested.ToList();
        if (!roots.Any(root => root.Org.Equals("Chiv2-Community", StringComparison.OrdinalIgnoreCase)
            && root.ModuleName.Equals("UnchainedPlugin", StringComparison.OrdinalIgnoreCase)))
            roots.Insert(0, new ReleaseCoordinates("Chiv2-Community", "UnchainedPlugin", "latest"));
        foreach (var root in roots) Visit(root.Org + "/" + root.ModuleName, root.Version, exact: true);
        return ordered;

        void Visit(string id, string requirement, bool exact) {
            var package = packages.FirstOrDefault(p => (p.LatestReleaseInfo.Identifier.Org + "/" + p.LatestReleaseInfo.Identifier.ModuleName).Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"DLL package not found: {id}");
            bool Accept(DllRelease r) => exact ? requirement == "latest" || r.Tag == requirement : Satisfies(r, requirement);
            if (selected.TryGetValue(id, out var previous)) {
                if (visiting.Contains(id)) throw new InvalidOperationException($"Cyclic DLL dependency: {id}");
                if (!Accept(previous)) throw new InvalidOperationException($"Conflicting DLL requirements: {id}");
                return;
            }
            var candidates = package.Releases.Where(Accept);
            if (!exact || requirement == "latest") candidates = candidates.Where(r => allowUnstable || r.Version?.IsRelease == true);
            var release = candidates.OrderByDescending(r => r.Version).ThenByDescending(r => r.ReleaseDate).FirstOrDefault()
                ?? throw new InvalidOperationException($"DLL release not found: {id}:{requirement}");
            if (release.Info.Target != "shared" && release.Info.Target != (server ? "server" : "client"))
                throw new InvalidOperationException($"DLL target does not match launch: {id}");
            selected.Add(id, release);
            visiting.Add(id);
            foreach (var dependency in release.Info.PluginDependencies) {
                var dep = ModIdentifier.FromDependency(dependency);
                Visit(dep.Org + "/" + dep.ModuleName, dependency.Version, exact: false);
            }
            visiting.Remove(id);
            ordered.Add(release);
        }
    }

    public async Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) {
        _prepared = false;
        _paths = [];
        _cancellationToken.ThrowIfCancellationRequested();
        var catalog = await DllCatalog.ReadAsync(_registry, _cancellationToken);
        if (catalog.Errors.Count != 0) throw new InvalidOperationException("DLL registry loading failed: " + string.Join("; ", catalog.Errors));
        var releases = Resolve(catalog.Packages, _requested.Concat(options.EnabledDlls), options.ServerLaunchOptions.IsSome, options.AllowUnstablePluginReleases);
        if (releases.Any(r => r.Info.ModDependencies.Count != 0)) {
            var fetched = await _mods.UpdateModsList();
            if (fetched.HasErrors) throw new InvalidOperationException("Pak registry loading failed while resolving DLL dependencies.");
        }
        // Resolve pak requirements into the shared mod manager before its installer runs.
        foreach (var dependency in releases.SelectMany(r => r.Info.ModDependencies)) {
            var id = ModIdentifier.FromDependency(dependency);
            var existing = _mods.GetEnabledAndDependencyReleases().Where(r => r.Info.Organization == id.Org && r.Info.RepoName == id.ModuleName).ToArray();
            if (existing.Length != 0) {
                if (existing.Any(r => r.Version is null || !SatisfiesVersion(r.Version, dependency.Version)))
                    throw new InvalidOperationException("DLL pak dependency conflicts with selected mods.");
                continue;
            }
            var mod = _mods.Mods.FirstOrDefault(m => m.LatestReleaseInfo.Organization == id.Org && m.LatestReleaseInfo.RepoName == id.ModuleName);
            var release = mod?.Releases.Where(r => SemVersion.TryParse(r.Tag, SemVersionStyles.AllowV, out var v)
                && SatisfiesVersion(v, dependency.Version))
                .OrderByDescending(r => SemVersion.Parse(r.Tag, SemVersionStyles.AllowV)).FirstOrDefault()
                ?? throw new InvalidOperationException($"DLL pak dependency unavailable: {id.Org}/{id.ModuleName}");
            var coordinate = ReleaseCoordinates.FromRelease(release);
            if (!_mods.EnableModRelease(coordinate)) throw new InvalidOperationException("Could not enable DLL pak dependency.");
        }
        var resolvedPaks = _mods.GetEnabledAndDependencyReleases().ToArray();
        foreach (var dependency in releases.SelectMany(r => r.Info.ModDependencies)) {
            var id = ModIdentifier.FromDependency(dependency);
            var matches = resolvedPaks.Where(r => r.Info.Organization == id.Org && r.Info.RepoName == id.ModuleName).ToArray();
            if (matches.Length == 0 || matches.Any(r => r.Version is null || !SatisfiesVersion(r.Version, dependency.Version)))
                throw new InvalidOperationException("Resolved pak dependencies do not satisfy DLL requirements.");
        }
        if (resolvedPaks.GroupBy(r => (r.Info.Organization, r.Info.RepoName)).Any(g => g.Select(r => r.Tag).Distinct().Count() > 1))
            throw new InvalidOperationException("Resolved pak dependencies contain conflicting versions.");
        var paths = releases.Select(r => SafeFileName(r.DllFileName)).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length) throw new InvalidOperationException("DLL filename collision.");
        Directory.CreateDirectory(_directory);
        for (var i = 0; i < releases.Count; i++) {
            _cancellationToken.ThrowIfCancellationRequested();
            var release = releases[i];
            var legacyPath = Path.Combine(_directory, paths[i]);
            if (File.Exists(legacyPath) && await MatchesFileAsync(legacyPath, release)) {
                paths[i] = legacyPath;
                continue;
            }
            var path = Path.Combine(_directory, ".dll-managed", release.Hash.ToLowerInvariant(), paths[i]);
            paths[i] = path;
            if (File.Exists(path) && await MatchesFileAsync(path, release)) continue;
            if (!options.CheckForDependencyUpdates) throw new InvalidOperationException("DLL missing or not registry-approved with dependency updates disabled.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = Path.Combine(_directory, ".dll-download-" + Guid.NewGuid().ToString("N"));
            try {
                using var response = await _client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, _cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri is { Scheme: not "https" })
                    throw new InvalidOperationException("DLL download redirected to an insecure URL.");
                if (response.Content.Headers.ContentLength is { } length && length != release.Size)
                    throw new InvalidOperationException("DLL download size does not match registry.");
                await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await response.Content.CopyToAsync(destination, _cancellationToken);
                if (!await MatchesFileAsync(temporary, release)) throw new InvalidOperationException("DLL download size or SHA-256 mismatch.");
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        _paths = paths;
        _preparedReleases = releases;
        var inventory = System.Text.Json.JsonSerializer.Serialize(releases.Select((release, i) => new {
            Repository = release.Info.RepoUrl,
            release.Tag,
            release.Hash,
            release.Size,
            Path = _paths[i]
        }));
        var inventoryPath = Path.Combine(_directory, "dll_manager.json");
        var inventoryTemp = inventoryPath + "." + Guid.NewGuid().ToString("N");
        try {
            await File.WriteAllTextAsync(inventoryTemp, inventory);
            File.Move(inventoryTemp, inventoryPath, overwrite: true);
        }
        finally { if (File.Exists(inventoryTemp)) File.Delete(inventoryTemp); }
        _prepared = true;
        return options;
    }

    public static string SafeFileName(string name) {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\') || name.Contains(':')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0
            || name.Any(char.IsControl) || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid DLL filename.");
        return name;
    }

    public static async Task<bool> MatchesFileAsync(string path, DllRelease release) {
        if (new FileInfo(path).Length != release.Size) return false;
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file)).Equals(release.Hash, StringComparison.OrdinalIgnoreCase);
    }

    public bool Inject(System.Diagnostics.Process process) {
        if (!_prepared || _paths.Any(p => !File.Exists(p))) return false;
        for (var i = 0; i < _paths.Length; i++)
            if (!MatchesFileAsync(_paths[i], _preparedReleases[i]).GetAwaiter().GetResult()) return false;
        return new DllInjector(_directory, _unsafe, _paths).Inject(process);
    }
}
