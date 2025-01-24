using System.Text.Json.Serialization;

namespace UnchainedLauncher.Core.JsonModels.Metadata.V1 {
    public record Mod(
        [property: JsonPropertyName("latest_manifest")] ModManifest LatestManifest,
        [property: JsonPropertyName("releases")] List<Release> Releases
    );

    public record Release(
        [property: JsonPropertyName("tag")] string Tag,
        [property: JsonPropertyName("hash")] string ReleaseHash,
        [property: JsonPropertyName("pak_file_name")] string PakFileName,
        [property: JsonPropertyName("release_date")] DateTime ReleaseDate,
        [property: JsonPropertyName("manifest")] ModManifest Manifest
    );

    public record Dependency(
        [property: JsonPropertyName("repo_url")] string RepoUrl,
        [property: JsonPropertyName("version")] string Version
    );

    public record ModManifest(
        [property: JsonPropertyName("repo_url")] string RepoUrl,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("home_page")] string? HomePage,
        [property: JsonPropertyName("image_url")] string? ImageUrl,
        [property: JsonPropertyName("mod_type")] string ModType,
        [property: JsonPropertyName("authors")] List<string> Authors,
        [property: JsonPropertyName("dependencies")] List<Dependency> Dependencies,
        [property: JsonPropertyName("tags")] List<string> Tags
    );

    public record Repo(string Org, string Name);
}