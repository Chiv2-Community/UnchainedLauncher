using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace C2GUILauncher.JsonModels.Metadata.V1 {
    public record Mod(
        [property: JsonProperty("latest_manifest")] ModManifest LatestManifest,
        [property: JsonProperty("releases")] List<Release> Releases
    );

    public record Release(
        [property: JsonProperty("tag")] string Tag,
        [property: JsonProperty("hash")] string ReleaseHash,
        [property: JsonProperty("pak_file_name")] string PakFileName,
        [property: JsonProperty("release_date")] DateTime ReleaseDate,
        [property: JsonProperty("manifest")] ModManifest Manifest
    );

    public record Dependency(
        [property: JsonProperty("repo_url")] string RepoUrl,
        [property: JsonProperty("version")] string Version
    );

    public record ModManifest(
        [property: JsonProperty("repo_url")] string RepoUrl,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("description")] string Description,
        [property: JsonProperty("home_page")] string? HomePage,
        [property: JsonProperty("image_url")] string? ImageUrl,
        [property: JsonProperty("mod_type")] string ModType,
        [property: JsonProperty("authors")] List<string> Authors,
        [property: JsonProperty("dependencies")] List<Dependency> Dependencies,
        [property: JsonProperty("tags")] List<string> Tags
    );

    public record Repo(string Org, string Name);
}
