using LanguageExt;
using Semver;
using System.Text.Json.Serialization;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.JsonModels.ModMetadata {
    public record Mod(
        [property: JsonPropertyName("latest_release_info")] ModInfo LatestReleaseInfo,
        [property: JsonPropertyName("releases")] List<Release> Releases
    ) {
        public Option<Release> LatestRelease =>
            Prelude.Optional(
                Releases.Where(x => x.Version?.IsRelease ?? false)
                    .OrderByDescending(x => x.Version)
                    .FirstOrDefault()
            );
    }

    public record Release(
        [property: JsonPropertyName("tag")] string Tag,
        [property: JsonPropertyName("hash")] string ReleaseHash,
        [property: JsonPropertyName("pak_file_name")] string PakFileName,
        [property: JsonPropertyName("release_date")] DateTime ReleaseDate,
        [property: JsonPropertyName("info")] ModInfo Info,
        [property: JsonPropertyName("manifest")] AssetCollections Manifest,
        [property: JsonPropertyName("release_notes_markdown")] string? ReleaseNotesMarkdown
    ) {
        public string ReleaseUrl => $"{Info.RepoUrl}/releases/{Tag}";

        public SemVersion? Version {
            get {
                SemVersion.TryParse(Tag, SemVersionStyles.AllowV, out var version);
                return version;
            }
        }
    }

    public record Dependency(
        [property: JsonPropertyName("repo_url")] string RepoUrl,
        [property: JsonPropertyName("version")] string Version
    ) {
        public string Organization => RepoUrl.Split('/')[^2];
        public string RepoName => RepoUrl.Split('/')[^1];

    }

    public record ModInfo(
        [property: JsonPropertyName("repo_url")] string RepoUrl,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("home_page")] string? HomePage,
        [property: JsonPropertyName("icon_url")] string? IconUrl,
        [property: JsonPropertyName("image_urls")] List<string> ImageUrls,
        [property: JsonPropertyName("authors")] List<string> Authors,
        [property: JsonPropertyName("dependencies")] List<Dependency> Dependencies
    ) {
        public string Organization => RepoUrl.Split('/')[^2];
        public string RepoName => RepoUrl.Split('/')[^1];

    }
}