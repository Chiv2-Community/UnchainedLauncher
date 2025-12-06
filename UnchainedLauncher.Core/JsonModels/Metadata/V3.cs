using LanguageExt;
using Semver;
using System.Text.Json.Serialization;

namespace UnchainedLauncher.Core.JsonModels.Metadata.V3 {
    public record Mod(
        [property: JsonPropertyName("latest_manifest")] ModManifest LatestManifest,
        [property: JsonPropertyName("releases")] List<Release> Releases
    ) {
        public static Mod FromV2(V2.Mod input) {
            return new Mod(
                LatestManifest: ModManifest.FromV2(input.LatestManifest),
                Releases: input.Releases.ConvertAll(Release.FromV2)
            );
        }
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
        public static Release FromV2(V2.Release input) {
            return new Release(
                Tag: input.Tag,
                ReleaseHash: input.ReleaseHash,
                PakFileName: input.PakFileName,
                ReleaseDate: input.ReleaseDate,
                Manifest: ModManifest.FromV2(input.Manifest),
                ReleaseNotesMarkdown: input.ReleaseNotesMarkdown
            );
        }

        public string ReleaseUrl => $"{Manifest.RepoUrl}/releases/{Tag}";

        public SemVersion? Version {
            get {
                SemVersion.TryParse(Tag, SemVersionStyles.AllowV, out var version);
                return version;
            }
        }
    }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModType {
        Client,
        Server,
        Shared
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModTag {
        Mutator,
        Map,
        Cosmetic,
        Audio,
        Model,
        Weapon,
        Doodad,
        Library
    }

    public record Dependency(
        [property: JsonPropertyName("repo_url")] string RepoUrl,
        [property: JsonPropertyName("version")] string Version
    ) {
        public string Organization => RepoUrl.Split('/')[^2];
        public string RepoName => RepoUrl.Split('/')[^1];

        public static Dependency FromV2(V2.Dependency input) {
            return new Dependency(
                RepoUrl: input.RepoUrl,
                Version: input.Version
            );
        }
    }

    public record OptionFlags(
        [property: JsonPropertyName("actor_mod")] bool ActorMod
    );

    public record ModManifest(
        [property: JsonPropertyName("repo_url")] string RepoUrl,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("home_page")] string? HomePage,
        [property: JsonPropertyName("image_url")] string? ImageUrl,
        [property: JsonPropertyName("mod_type")] ModType ModType,
        [property: JsonPropertyName("authors")] List<string> Authors,
        [property: JsonPropertyName("dependencies")] List<Dependency> Dependencies,
        [property: JsonPropertyName("tags")] List<ModTag> Tags,
        [property: JsonPropertyName("maps")] List<string> Maps,
        [property: JsonPropertyName("options")] OptionFlags OptionFlags
    ) {
        public string Organization => RepoUrl.Split('/')[^2];
        public string RepoName => RepoUrl.Split('/')[^1];

        public static ModManifest FromV2(V2.ModManifest input) {
            return new ModManifest(
                RepoUrl: input.RepoUrl,
                Name: input.Name,
                Description: input.Description,
                HomePage: input.HomePage,
                ImageUrl: input.ImageUrl,
                ModType: Enum.Parse<ModType>(input.ModType.ToString()),
                Authors: input.Authors,
                Dependencies: input.Dependencies.ConvertAll(Dependency.FromV2),
                Tags: input.Tags.Select(x => Enum.Parse<ModTag>(x.ToString())).ToList(),
                Maps: new List<string>(),
                OptionFlags: new OptionFlags(
                    ActorMod: input.AgMod
                )
            );
        }
    }
}