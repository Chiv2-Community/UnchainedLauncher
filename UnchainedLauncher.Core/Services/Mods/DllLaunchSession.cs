using LanguageExt;
using Semver;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Dlls;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using DllRelease = UnchainedLauncher.Core.JsonModels.DllRelease;
using ManagedDllManager = UnchainedLauncher.Core.Services.Dlls.DllManager;

namespace UnchainedLauncher.Core.Services.Mods;

/// <summary>Resolves a CLI request, then uses the shared DLL manager for storage and injection.</summary>
public sealed class DllLaunchSession : IChivalry2LaunchPreparer<LaunchOptions>, IProcessInjector {
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly AggregateModRegistry _registry;
    private readonly IModManager _mods;
    private readonly string _directory;
    private readonly string? _cacheDirectory;
    private readonly bool _unsafe;
    private readonly IReadOnlyList<ReleaseCoordinates> _requested;
    private readonly CancellationToken _cancellationToken;
    private readonly HttpClient _client;
    private ManagedDllManager? _manager;
    private bool _prepared;
    private bool _server;

    public DllLaunchSession(AggregateModRegistry registry, IModManager mods, string directory,
        IEnumerable<ReleaseCoordinates>? requested = null, bool unsafeLoadAllDlls = false,
        CancellationToken cancellationToken = default, HttpClient? client = null, string? cacheDirectory = null) {
        _registry = registry;
        _mods = mods;
        _directory = Path.GetFullPath(directory);
        _cacheDirectory = cacheDirectory;
        _requested = requested?.ToArray() ?? [];
        _unsafe = unsafeLoadAllDlls;
        _cancellationToken = cancellationToken;
        _client = client ?? Client;
    }

    public static bool Satisfies(DllRelease release, string requirement) => release.Version is { } version && SatisfiesVersion(version, requirement);

    public static bool SatisfiesVersion(SemVersion version, string requirement) => PluginInfo.MatchesVersion(version.ToString(), requirement);

    public static IReadOnlyList<DllRelease> Resolve(IReadOnlyList<DllPackage> packages,
        IEnumerable<ReleaseCoordinates> requested, bool server, bool allowUnstable) {
        var selected = new Dictionary<string, DllRelease>(StringComparer.OrdinalIgnoreCase);
        var roots = requested.ToList();
        if (!roots.Any(root => root.Org.Equals("Chiv2-Community", StringComparison.OrdinalIgnoreCase)
            && root.ModuleName.Equals("UnchainedPlugin", StringComparison.OrdinalIgnoreCase)))
            roots.Insert(0, new ReleaseCoordinates("Chiv2-Community", "UnchainedPlugin", "latest"));
        var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(root => root.Version != "latest")) {
            var id = root.Org + "/" + root.ModuleName;
            if (pins.TryGetValue(id, out var pin) && pin != root.Version)
                throw new InvalidOperationException($"Conflicting DLL requirements: {id}");
            pins[id] = root.Version;
        }
        if (!Search(roots.Select(root => (root.Org + "/" + root.ModuleName, root.Version, true, Array.Empty<string>())).ToArray(), out var error))
            throw new InvalidOperationException(error);
        var ordered = new List<DllRelease>();
        var emitted = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots) Emit(root.Org + "/" + root.ModuleName);
        return ordered;

        bool Search(IReadOnlyList<(string Id, string Requirement, bool Exact, string[] Path)> pending, out string? failure) {
            failure = null;
            if (pending.Count == 0) return true;
            var (id, requirement, exact, path) = pending[0];
            if (path.Contains(id, StringComparer.OrdinalIgnoreCase)) {
                failure = $"Cyclic DLL dependency: {id}";
                return false;
            }
            var package = packages.FirstOrDefault(p => (p.LatestReleaseInfo.Identifier.Org + "/" + p.LatestReleaseInfo.Identifier.ModuleName).Equals(id, StringComparison.OrdinalIgnoreCase));
            if (package == null) {
                failure = $"DLL package not found: {id}";
                return false;
            }
            bool Accept(DllRelease r) => exact ? requirement == "latest" || r.Tag == requirement : Satisfies(r, requirement);
            if (selected.TryGetValue(id, out var previous)) {
                if (Accept(previous)) return Search(pending.Skip(1).ToArray(), out failure);
                failure = $"Conflicting DLL requirements: {id}";
                return false;
            }
            var pinned = pins.TryGetValue(id, out var tag);
            var candidates = package.Releases.Where(Accept).Where(release => !pinned || release.Tag == tag)
                .Where(release => release.Info.Target == "shared" || release.Info.Target == (server ? "server" : "client"));
            if (!pinned) candidates = candidates.Where(r => allowUnstable || r.Version?.IsRelease == true);
            failure = $"DLL release not found for launch target: {id}:{requirement}";
            foreach (var release in candidates.OrderByDescending(r => r.Version).ThenByDescending(r => r.ReleaseDate)) {
                selected.Add(id, release);
                // Keep later roots in the same search so their constraints can reselect earlier dependencies.
                List<(string Id, string Requirement, bool Exact, string[] Path)> next = [];
                foreach (var dependency in release.Info.PluginDependencies) {
                    var dep = ModIdentifier.FromDependency(dependency);
                    next.Add((dep.Org + "/" + dep.ModuleName, dependency.Version, false, [.. path, id]));
                }
                next.AddRange(pending.Skip(1));
                if (Search(next, out failure)) return true;
                selected.Remove(id);
            }
            return false;
        }

        void Emit(string id) {
            if (!emitted.Add(id)) return;
            var release = selected[id];
            foreach (var dependency in release.Info.PluginDependencies) {
                var dep = ModIdentifier.FromDependency(dependency);
                Emit(dep.Org + "/" + dep.ModuleName);
            }
            ordered.Add(release);
        }
    }

    public async Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) {
        _prepared = false;
        _manager = null;
        _cancellationToken.ThrowIfCancellationRequested();
        var catalog = await DllCatalog.ReadAsync(_registry, _cancellationToken);
        if (catalog.Errors.Count != 0) throw new InvalidOperationException("DLL registry loading failed: " + string.Join("; ", catalog.Errors));
        _server = options.ServerLaunchOptions.IsSome;
        var releases = Resolve(catalog.Packages, _requested.Concat(options.EnabledDlls), _server, options.AllowUnstablePluginReleases);
        if (releases.Any(r => r.Info.ModDependencies.Count != 0)) {
            var fetched = await _mods.UpdateModsList();
            if (fetched.HasErrors) throw new InvalidOperationException("Pak registry loading failed while resolving DLL dependencies.");
        }
        var selectedPaks = _mods.GetEnabledAndDependencyReleases().ToArray();
        var paksToEnable = new List<Release>();
        foreach (var requirements in releases.SelectMany(r => r.Info.ModDependencies)
                     .GroupBy(d => d.RepoUrl, StringComparer.OrdinalIgnoreCase)) {
            var id = ModIdentifier.FromDependency(requirements.First());
            bool Accept(Release release) => release.Version is { } version &&
                requirements.All(dependency => SatisfiesVersion(version, dependency.Version));
            var existing = selectedPaks.Where(r =>
                r.Info.RepoUrl.Equals(requirements.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (existing.Length != 0) {
                if (existing.Any(r => !Accept(r)))
                    throw new InvalidOperationException("DLL pak dependency conflicts with selected mods.");
                continue;
            }
            var mod = _mods.Mods.FirstOrDefault(m => m.LatestReleaseInfo.RepoUrl.Equals(requirements.Key, StringComparison.OrdinalIgnoreCase));
            var release = mod?.Releases.Where(Accept)
                .OrderByDescending(r => r.Version).FirstOrDefault()
                ?? throw new InvalidOperationException($"DLL pak dependency unavailable: {id.Org}/{id.ModuleName}");
            paksToEnable.Add(release);
        }
        foreach (var release in paksToEnable) {
            if (!_mods.EnableModRelease(ReleaseCoordinates.FromRelease(release)))
                throw new InvalidOperationException("Could not enable DLL pak dependency.");
        }
        var resolvedPaks = _mods.GetEnabledAndDependencyReleases().ToArray();
        if (resolvedPaks.GroupBy(r => r.Info.RepoUrl, StringComparer.OrdinalIgnoreCase).Any(g => g.Select(r => r.Tag).Distinct().Count() > 1))
            throw new InvalidOperationException("Resolved pak dependencies contain conflicting versions.");
        foreach (var release in releases) SafeFileName(release.DllFileName);
        if (releases.Select(release => release.DllFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != releases.Count)
            throw new InvalidOperationException("DLL filename collision.");
        _manager = ManagedDllManager.CreateForLaunch(_directory,
            catalog.Packages.SelectMany(package => package.Releases).Select(release => release.ToManagedRelease()).ToArray(),
            releases.Select(release => release.ToManagedRelease()).ToArray(), _client,
            () => _mods.GetEnabledAndDependencyReleases().ToArray(), _cacheDirectory);
        try { await _manager.PrepareAsync(_server, options.CheckForDependencyUpdates, _cancellationToken); }
        catch (InvalidDataException ex) { throw new InvalidOperationException(ex.Message, ex); }
        var files = _manager.OpenForInjection(_server);
        string[] paths;
        try { paths = files.Select(file => file.Path).ToArray(); }
        finally { foreach (var file in files) file.Dispose(); }
        var inventory = System.Text.Json.JsonSerializer.Serialize(releases.Select((release, i) => new {
            Repository = release.Info.RepoUrl,
            release.Tag,
            release.Hash,
            release.Size,
            Path = paths[i]
        }));
        var inventoryPath = Path.Combine(_directory, "dll_manager.json");
        Directory.CreateDirectory(_directory);
        var temporary = inventoryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            await File.WriteAllTextAsync(temporary, inventory, _cancellationToken);
            File.Move(temporary, inventoryPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        _prepared = true;
        return options;
    }

    public static string SafeFileName(string name) {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\') || name.Contains(':')
            || name.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 || name.Any(char.IsControl)
            || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid DLL filename.");
        return name;
    }

    public static Task<bool> MatchesFileAsync(string path, DllRelease release) {
        using var file = File.OpenRead(path);
        try { ManagedDllManager.ValidateBytes(file, release.ToManagedRelease()); return Task.FromResult(true); }
        catch (InvalidDataException) { return Task.FromResult(false); }
    }

    public bool Inject(System.Diagnostics.Process process) => Inject(process, _server);
    public IReadOnlyList<ProtonDll> GetProtonDlls() {
        if (!_prepared || _manager == null) throw new InvalidOperationException("DLL preparation is incomplete.");
        var files = _manager.OpenForInjection(_server);
        try {
            var descriptors = files.Select(file => new ProtonDll(file.Path, file.Size, file.Sha256)).ToList();
            var managed = files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in _unsafe ? new DllInjector(_directory, true).GetDllPaths(_server) : []) {
                if (managed.Contains(path)) continue;
                using var stream = File.OpenRead(path);
                descriptors.Add(new ProtonDll(path, stream.Length,
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))));
            }
            return descriptors;
        }
        finally { foreach (var file in files) file.Dispose(); }
    }
    public bool Inject(System.Diagnostics.Process process, bool server) => _prepared && server == _server && _manager != null
        && new DllInjector(_manager, _directory, _unsafe).Inject(process, server);
}
