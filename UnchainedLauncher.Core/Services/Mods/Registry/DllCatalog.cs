using System.Net;
using System.Text.Json;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods.Registry;

public sealed record DllCatalogResult(IReadOnlyList<DllPackage> Packages, IReadOnlyList<string> Errors);

/// <summary>Read-only typed view of dll_db alongside the existing pak registry.</summary>
public static class DllCatalog {
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<DllCatalogResult> ReadAsync(AggregateModRegistry registry, CancellationToken cancellationToken) {
        var packages = new Dictionary<ModIdentifier, DllPackage>();
        var errors = new List<string>();
        foreach (var source in registry.ModRegistries) {
            try {
                if (source is GithubModRegistry github) {
                    var root = $"https://raw.githubusercontent.com/{github.Organization}/{github.RepoName}/refs/heads/main/dll_db";
                    using var index = await Client.GetAsync(root + "/plugin_list_index.txt", cancellationToken);
                    if (index.StatusCode == HttpStatusCode.NotFound) continue; // Pak-only registry.
                    index.EnsureSuccessStatusCode();
                    var text = await index.Content.ReadAsStringAsync(cancellationToken);
                    foreach (var id in RegistryUtils.ParseLineSeparatedPackageList(text.Trim())) {
                        cancellationToken.ThrowIfCancellationRequested();
                        try {
                            var json = await Client.GetStringAsync($"{root}/packages/{Uri.EscapeDataString(id.Org)}/{Uri.EscapeDataString(id.ModuleName)}.json", cancellationToken);
                            Add(json, id);
                        }
                        catch (Exception ex) when (IsReadFailure(ex, cancellationToken)) { errors.Add($"{source.Name}: {id.Org}/{id.ModuleName}: {ex.Message}"); }
                    }
                }
                else if (source is LocalModRegistry local) {
                    var path = Path.Combine(local.RegistryPath, "dll_db", "packages");
                    if (!Directory.Exists(path)) continue;
                    foreach (var file in Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories)) {
                        try { Add(await File.ReadAllTextAsync(file, cancellationToken), null); }
                        catch (Exception ex) when (IsReadFailure(ex, cancellationToken)) { errors.Add($"{source.Name}: {file}: {ex.Message}"); }
                    }
                }
                else errors.Add($"DLL queries are not supported by registry: {source.Name}");
            }
            catch (Exception ex) when (IsReadFailure(ex, cancellationToken)) { errors.Add($"{source.Name}: {ex.Message}"); }
        }
        return new DllCatalogResult(packages.Values.OrderBy(package => package.LatestReleaseInfo.Identifier).ToArray(), errors);

        void Add(string json, ModIdentifier? expected) {
            var decoded = JsonHelpers.Deserialize<DllPackage>(json);
            if (!decoded.Success || decoded.Result is null) throw new JsonException("Invalid DLL registry metadata.");
            var package = decoded.Result;
            ValidateInfo(package.LatestReleaseInfo);
            var id = package.LatestReleaseInfo.Identifier;
            if (expected is not null && !expected.Matches(id)) throw new JsonException("DLL metadata does not match indexed identity.");
            if (package.Releases is null || package.Releases.Any(release => release is null || release.Size < 0 || release.Info is null
                || !id.Matches(release.Info.Identifier) || string.IsNullOrWhiteSpace(release.Tag) || string.IsNullOrWhiteSpace(release.Hash)
                || string.IsNullOrWhiteSpace(release.DllFileName) || release.Info.Target is not ("client" or "server" or "shared")))
                throw new JsonException("Invalid DLL release metadata.");
            foreach (var release in package.Releases) {
                ValidateInfo(release.Info);
                if (release.Hash.Length != 64 || Convert.FromHexString(release.Hash).Length != 32)
                    throw new JsonException("DLL hash must be SHA-256.");
                if (!Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out var download) || download.Scheme != "https" || download.UserInfo.Length != 0)
                    throw new JsonException("DLL download URL must be HTTPS without credentials.");
            }
            if (packages.TryGetValue(id, out var previous)) {
                var merged = previous.Releases.Concat(package.Releases).DistinctBy(release => release.Tag).ToList();
                packages[id] = previous with { Releases = merged };
            }
            else packages.Add(id, package);
        }

        static void ValidateInfo(DllInfo? info) {
            if (info is null || string.IsNullOrWhiteSpace(info.Name) || string.IsNullOrWhiteSpace(info.Description)
                || info.Authors is null || info.Authors.Count == 0 || info.Authors.Any(string.IsNullOrWhiteSpace)
                || info.PluginDependencies is null || info.ModDependencies is null || info.Target is not ("server" or "client" or "shared"))
                throw new JsonException("Invalid DLL package information.");
            foreach (var dependency in info.PluginDependencies.Concat(info.ModDependencies)) {
                if (dependency is null || string.IsNullOrWhiteSpace(dependency.Version)) throw new JsonException("Invalid DLL dependency.");
                DllInfo.RepositoryIdentifier(dependency.RepoUrl);
            }
        }
    }

    private static bool IsReadFailure(Exception ex, CancellationToken token) => ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException
        || ex is OperationCanceledException && !token.IsCancellationRequested;
}