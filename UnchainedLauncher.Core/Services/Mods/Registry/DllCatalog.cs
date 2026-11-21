using Semver;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnchainedLauncher.Core.Services.Dlls;

namespace UnchainedLauncher.Core.Services.Mods.Registry;

/// <summary>A query/selection grouping, not a registry wire format.</summary>
public sealed record DllCatalogEntry(IReadOnlyList<DllRelease> Releases) {
    public string Id => Releases[0].Id;
    public ModIdentifier Identifier => new(Releases[0].Organization, Releases[0].Name);
    public PluginInfo? LatestReleaseInfo => Releases.OrderByDescending(release => release.ReleaseDate)
        .FirstOrDefault(release => release.Info != null)?.Info;
    public DllRelease? LatestRelease => Releases.Where(release => SemVersion.Parse(release.Version, SemVersionStyles.Strict).IsRelease)
        .OrderByDescending(release => SemVersion.Parse(release.Version, SemVersionStyles.Strict)).FirstOrDefault()
        ?? Releases.OrderByDescending(release => release.ReleaseDate).FirstOrDefault();
}

public sealed record DllCatalogResult(IReadOnlyList<DllCatalogEntry> Packages, IReadOnlyList<string> Errors);

/// <summary>Groups shared, validated DLL registry records without replacing their metadata.</summary>
public static class DllCatalog {
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<DllCatalogResult> ReadAsync(AggregateModRegistry registry, CancellationToken cancellationToken) {
        var releases = new Dictionary<string, DllRelease>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var source in registry.ModRegistries) {
            try {
                if (source is GithubModRegistry github) {
                    foreach (var release in await DllManager.ReadRegistryAsync(Client, github.DllDatabaseUrl, cancellationToken)) Add(release);
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
                            foreach (var release in DllManager.ParsePackage(await File.ReadAllTextAsync(path, cancellationToken), id)) Add(release);
                        }
                        catch (Exception ex) when (IsReadFailure(ex, cancellationToken)) { errors.Add($"{source.Name}: {id}: {ex.Message}"); }
                    }
                }
                else errors.Add($"DLL queries are not supported by registry: {source.Name}");
            }
            catch (Exception ex) when (IsReadFailure(ex, cancellationToken)) { errors.Add($"{source.Name}: {ex.Message}"); }
        }
        return new DllCatalogResult(releases.Values.GroupBy(release => release.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DllCatalogEntry(group.ToArray())).OrderBy(package => package.Identifier).ToArray(), errors);

        void Add(DllRelease release) {
            if (releases.TryGetValue(release.Key, out var old) && DllManager.Canonical(old) != DllManager.Canonical(release))
                throw new InvalidDataException($"Conflicting DLL registry records: {release.Key}");
            releases[release.Key] = release;
        }
    }

    private static bool IsReadFailure(Exception ex, CancellationToken token) => ex is HttpRequestException or IOException or InvalidDataException
        or UnauthorizedAccessException or JsonException or ArgumentException or FormatException
        || ex is OperationCanceledException && !token.IsCancellationRequested;
}