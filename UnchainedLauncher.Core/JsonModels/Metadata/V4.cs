using System.Text.Json.Serialization;

namespace UnchainedLauncher.Core.JsonModels.Metadata.V4 {
    public record ModManifest(
        [property: JsonPropertyName("repo_url")] string RepoUrl,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("home_page")] string? HomePage,
        [property: JsonPropertyName("icon_url")] string? IconUrl,
        [property: JsonPropertyName("authors")] List<string> Authors,
        [property: JsonPropertyName("dependencies")] List<Dependency> Dependencies,
        [property: JsonPropertyName("components")] Components Components
    ) {
        public string Organization => RepoUrl.Split('/')[^2];
        public string RepoName => RepoUrl.Split('/')[^1];
    }

    public record Dependency(
        [property: JsonPropertyName("repo_url")] string RepoUrl,
        [property: JsonPropertyName("version")] string Version
    ) {
        public string Organization => RepoUrl.Split('/')[^2];
        public string RepoName => RepoUrl.Split('/')[^1];
    }

    public record Components(
        [property: JsonPropertyName("actors")] List<Actor>? Actors,
        [property: JsonPropertyName("maps")] List<Map>? Maps,
        [property: JsonPropertyName("replacements")] Replacements? Replacements
    );

    public record Actor(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("clientside_only")] bool ClientsideOnly = false,
        [property: JsonPropertyName("images")] List<string>? Images = null
    );

    public record Map(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("images")] List<string>? Images = null
    );

    public record Replacements(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("paths")] List<string> Paths,
        [property: JsonPropertyName("images")] List<string>? Images = null
    );
}
