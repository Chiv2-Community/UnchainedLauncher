using LanguageExt;
using Semver;
using System.Text.Json.Serialization;

namespace UnchainedLauncher.Core.JsonModels.Metadata.V4 {
    public record Mod(
        [property: JsonPropertyName("latest_manifest")] ModManifest LatestManifest,
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
        [property: JsonPropertyName("manifest")] ModManifest Manifest,
        [property: JsonPropertyName("release_notes_markdown")] string? ReleaseNotesMarkdown
    ) {
        public string ReleaseUrl => $"{Manifest.RepoUrl}/releases/{Tag}";

        public SemVersion? Version {
            get {
                SemVersion.TryParse(Tag, SemVersionStyles.AllowV, out var version);
                return version;
            }
        }
    }
    
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

        public bool ClientsideOnly =>
            (Components.Replacements != null)
            || (Components.Maps?.Count > 0)
            || (Components.Actors?.Any(x => !x.ClientsideOnly) ?? false);
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
        [property: JsonPropertyName("maps")] List<Chivalry2Map>? Maps,
        [property: JsonPropertyName("replacements")] Replacements? Replacements
    );

    public record Actor(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("clientside_only")] bool ClientsideOnly = false,
        [property: JsonPropertyName("images")] List<string>? Images = null
    );

    public record Chivalry2Map(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")]
        string Description,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("images")] List<string>? Images = null
    ) {
        public static Chivalry2Map LegacyFromName(string name) => new Chivalry2Map(name, "TODO: FIXME", name);
    }

    public record Replacements(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("paths")] List<string> Paths,
        [property: JsonPropertyName("images")] List<string>? Images = null
    );
}
