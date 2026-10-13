using Semver;
using System.Text.Json.Serialization;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.JsonModels;

// Matches zero-infra-mod-registry prepare-v1 and C2ModRegistry/dll_db.
public sealed record DllPackage(
    [property: JsonPropertyName("latest_release_info"), JsonRequired] DllInfo LatestReleaseInfo,
    [property: JsonPropertyName("releases"), JsonRequired] List<DllRelease> Releases) {
    public DllRelease? LatestRelease => Releases.Where(release => release.Version?.IsRelease == true)
        .OrderByDescending(release => release.Version).FirstOrDefault()
        ?? Releases.OrderByDescending(release => release.ReleaseDate).FirstOrDefault();
}

public sealed record DllRelease(
    [property: JsonPropertyName("tag"), JsonRequired] string Tag,
    [property: JsonPropertyName("hash"), JsonRequired] string Hash,
    [property: JsonPropertyName("dll_file_name"), JsonRequired] string DllFileName,
    [property: JsonPropertyName("download_url"), JsonRequired] string DownloadUrl,
    [property: JsonPropertyName("size"), JsonRequired] long Size,
    [property: JsonPropertyName("release_date"), JsonRequired] DateTimeOffset ReleaseDate,
    [property: JsonPropertyName("info"), JsonRequired] DllInfo Info,
    [property: JsonPropertyName("release_notes_markdown")] string? ReleaseNotesMarkdown) {
    [JsonIgnore] public SemVersion? Version => SemVersion.TryParse(Tag, SemVersionStyles.AllowV, out var version) ? version : null;
}

public sealed record DllInfo {
    [JsonPropertyName("repo_url"), JsonRequired] public required string RepoUrl { get; init; }
    [JsonPropertyName("name"), JsonRequired] public required string Name { get; init; }
    [JsonPropertyName("description"), JsonRequired] public required string Description { get; init; }
    [JsonPropertyName("authors"), JsonRequired] public required List<string> Authors { get; init; }
    [JsonPropertyName("target")] public string Target { get; init; } = "shared";
    [JsonPropertyName("icon_url")] public string? IconUrl { get; init; }
    [JsonPropertyName("image_urls")] public List<string> ImageUrls { get; init; } = [];
    [JsonPropertyName("plugin_dependencies")] public List<Dependency> PluginDependencies { get; init; } = [];
    [JsonPropertyName("mod_dependencies")] public List<Dependency> ModDependencies { get; init; } = [];
    [JsonIgnore] public ModIdentifier Identifier => RepositoryIdentifier(RepoUrl);

    internal static ModIdentifier RepositoryIdentifier(string repoUrl) {
        if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || uri.UserInfo.Length != 0)
            throw new ArgumentException("Invalid DLL dependency/repository URL.");
        return RegistryUtils.ParseModIdentifier(uri.AbsolutePath.Trim('/'), "/")
            ?? throw new ArgumentException("Invalid DLL repository identifier.");
    }
}