using Newtonsoft.Json;

namespace UnchainedLauncherCore.JsonModels.Metadata.V1 {
    public record Mod(
        [property: JsonProperty("latest_manifest", Required = Required.Always)] ModManifest LatestManifest,
        [property: JsonProperty("releases", Required = Required.Always)] List<Release> Releases
    );

    public record Release(
        [property: JsonProperty("tag", Required = Required.Always)] string Tag,
        [property: JsonProperty("hash", Required = Required.Always)] string ReleaseHash,
        [property: JsonProperty("pak_file_name", Required = Required.Always)] string PakFileName,
        [property: JsonProperty("release_date", Required = Required.Always)] DateTime ReleaseDate,
        [property: JsonProperty("manifest", Required = Required.Always)] ModManifest Manifest
    );

    public record Dependency(
        [property: JsonProperty("repo_url", Required = Required.Always)] string RepoUrl,
        [property: JsonProperty("version", Required = Required.Always)] string Version
    );

    public record ModManifest(
        [property: JsonProperty("repo_url", Required = Required.Always)] string RepoUrl,
        [property: JsonProperty("name", Required = Required.Always)] string Name,
        [property: JsonProperty("description", Required = Required.Always)] string Description,
        [property: JsonProperty("home_page")] string? HomePage,
        [property: JsonProperty("image_url")] string? ImageUrl,
        [property: JsonProperty("mod_type", Required = Required.Always)] string ModType,
        [property: JsonProperty("authors", Required = Required.Always)] List<string> Authors,
        [property: JsonProperty("dependencies", Required = Required.Always)] List<Dependency> Dependencies,
        [property: JsonProperty("tags", Required = Required.Always)] List<string> Tags
    );

    public record Repo(string Org, string Name);
}
