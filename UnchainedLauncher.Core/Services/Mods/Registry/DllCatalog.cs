using System.Text.Json;
using System.Text.RegularExpressions;
using UnchainedLauncher.Core.JsonModels;
using ManagedDllManager = UnchainedLauncher.Core.Services.Dlls.DllManager;
using ManagedDllRelease = UnchainedLauncher.Core.Services.Dlls.DllRelease;
using PluginInfo = UnchainedLauncher.Core.Services.Dlls.PluginInfo;

namespace UnchainedLauncher.Core.Services.Mods.Registry;

public sealed record DllCatalogResult(IReadOnlyList<DllPackage> Packages, IReadOnlyList<string> Errors);

/// <summary>CLI query projection of the shared, validated DLL registry records.</summary>
public static class DllCatalog {
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<DllCatalogResult> ReadAsync(AggregateModRegistry registry, CancellationToken cancellationToken) {
        var releases = new Dictionary<string, ManagedDllRelease>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var source in registry.ModRegistries) {
            try {
                if (source is GithubModRegistry github) {
                    foreach (var release in await ManagedDllManager.ReadRegistryAsync(Client, github.DllDatabaseUrl, cancellationToken)) Add(release);
                }
                else if (source is LocalModRegistry local) {
                    var root = Path.Combine(local.RegistryPath, "dll_db");
                    var packages = Path.Combine(root, "packages");
                    var index = Path.Combine(root, "plugin_list_index.txt");
                    if (!Directory.Exists(packages) && !File.Exists(index)) continue;
                    var ids = File.Exists(index)
                        ? (await File.ReadAllLinesAsync(index, cancellationToken)).Select(line => line.Trim()).Where(line => line.Length != 0).ToArray()
                        : Directory.GetFiles(packages, "*.json", SearchOption.AllDirectories)
                            .Select(file => Path.GetRelativePath(packages, file)[..^5].Replace(Path.DirectorySeparatorChar, '/')).ToArray();
                    if (ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length)
                        throw new InvalidDataException("Duplicate plugin package identifier.");
                    foreach (var id in ids) {
                        try {
                            if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]+/[A-Za-z0-9_-]+$"))
                                throw new InvalidDataException("Invalid plugin package identifier.");
                            var path = Path.Combine(packages, id.Replace('/', Path.DirectorySeparatorChar) + ".json");
                            foreach (var release in ManagedDllManager.ParsePackage(await File.ReadAllTextAsync(path, cancellationToken), id)) Add(release);
                        }
                        catch (Exception ex) when (IsReadFailure(ex, cancellationToken)) { errors.Add($"{source.Name}: {id}: {ex.Message}"); }
                    }
                }
                else errors.Add($"DLL queries are not supported by registry: {source.Name}");
            }
            catch (Exception ex) when (IsReadFailure(ex, cancellationToken)) { errors.Add($"{source.Name}: {ex.Message}"); }
        }
        return new DllCatalogResult(releases.Values.GroupBy(release => release.Id, StringComparer.OrdinalIgnoreCase)
            .Select(Project).OrderBy(package => package.LatestReleaseInfo.Identifier).ToArray(), errors);

        void Add(ManagedDllRelease release) {
            if (releases.TryGetValue(release.Key, out var old) && ManagedDllManager.Canonical(old) != ManagedDllManager.Canonical(release))
                throw new InvalidDataException($"Conflicting DLL registry records: {release.Key}");
            releases[release.Key] = release;
        }
    }

    private static DllPackage Project(IEnumerable<ManagedDllRelease> group) {
        var records = group.ToArray();
        var id = records[0].Id;
        var latest = records.OrderByDescending(release => release.ReleaseDate).FirstOrDefault(release => release.Info != null)?.Info;
        DllInfo Info(PluginInfo? info) => new() {
            RepoUrl = "https://github.com/" + id,
            Name = info?.Name ?? records[0].Name,
            Description = info?.Description ?? "Metadata unavailable for this historical release.",
            Authors = info?.Authors.ToList() ?? [],
            Target = info?.Target ?? "shared",
            IconUrl = info?.IconUrl,
            ImageUrls = info?.ImageUrls?.ToList() ?? [],
            PluginDependencies = info?.RequiredPlugins.ToList() ?? [],
            ModDependencies = info?.RequiredMods.ToList() ?? []
        };
        return new DllPackage(Info(latest), records.Select(release => new DllRelease(
            release.Tag ?? release.Version, release.Sha256, release.EffectiveFileName, release.DownloadUrl,
            release.Size, release.ReleaseDate ?? DateTimeOffset.MinValue, Info(release.Info), release.ReleaseNotesMarkdown) {
            RegisteredRelease = release
        }).ToList());
    }

    private static bool IsReadFailure(Exception ex, CancellationToken token) => ex is HttpRequestException or IOException or InvalidDataException
        or UnauthorizedAccessException or JsonException or ArgumentException or FormatException
        || ex is OperationCanceledException && !token.IsCancellationRequested;
}