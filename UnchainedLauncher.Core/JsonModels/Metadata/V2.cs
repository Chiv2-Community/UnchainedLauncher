using System.Text.Json.Serialization;

namespace UnchainedLauncher.Core.JsonModels.Metadata.V2 {
    public record Mod(
        [property: JsonPropertyName("latest_manifest")] ModManifest LatestManifest,
        [property: JsonPropertyName("releases")] List<Release> Releases
    ) {
        public static Mod FromV1(V1.Mod v1Mod) {
            return new Mod(
                LatestManifest: ModManifest.FromV1(v1Mod.LatestManifest),
                Releases: v1Mod.Releases.ConvertAll(Release.FromV1)
            );
        }
    }

    public record Release(
        [property: JsonPropertyName("tag")] string Tag,
        [property: JsonPropertyName("hash")] string ReleaseHash,
        [property: JsonPropertyName("pak_file_name")] string PakFileName,
        [property: JsonPropertyName("release_date")] DateTime ReleaseDate,
        [property: JsonPropertyName("manifest")] ModManifest Manifest,
        [property: JsonPropertyName("release_notes_markdown")] string? ReleaseNotesMarkdown
    ) {
        public static Release FromV1(V1.Release input) {
            return new Release(
                Tag: input.Tag,
                ReleaseHash: input.ReleaseHash,
                PakFileName: input.PakFileName,
                ReleaseDate: input.ReleaseDate,
                Manifest: ModManifest.FromV1(input.Manifest),
                ReleaseNotesMarkdown: input.ReleaseNotesMarkdown
            );
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
    }

    public record Dependency(
        [property: JsonPropertyName("repo_url")] string RepoUrl,
        [property: JsonPropertyName("version")] string Version
    ) {
        public static Dependency FromV1(V1.Dependency input) {
            return new Dependency(
                RepoUrl: input.RepoUrl,
                Version: input.Version
            );
        }
    }

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
        [property: JsonPropertyName("ag_mod")] bool AgMod
    ) {
        public static ModManifest FromV1(V1.ModManifest latestManifest) {
            return new ModManifest(
                RepoUrl: latestManifest.RepoUrl,
                Name: latestManifest.Name,
                Description: latestManifest.Description,
                HomePage: latestManifest.HomePage,
                ImageUrl: latestManifest.ImageUrl,
                ModType: Enum.Parse<ModType>(latestManifest.ModType, true),
                Authors: latestManifest.Authors,
                Dependencies: latestManifest.Dependencies.ConvertAll(Dependency.FromV1),
                Tags: new List<ModTag>(),
                AgMod: false
            );
        }
    }

    public record Repo(string Org, string Name);
}