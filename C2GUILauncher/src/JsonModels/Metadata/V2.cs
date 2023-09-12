using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;

namespace C2GUILauncher.JsonModels.Metadata.V2 {
    public record Mod(
        [property: JsonProperty("latest_manifest")] ModManifest LatestManifest,
        [property: JsonProperty("releases")] List<Release> Releases
    ) {
        public static Mod FromV1(V1.Mod v1Mod) {
            return new Mod(
                LatestManifest: ModManifest.FromV1(v1Mod.LatestManifest),
                Releases: v1Mod.Releases.ConvertAll(Release.FromV1)
            );
        }
    }

    public record Release(
        [property: JsonProperty("tag")] string Tag,
        [property: JsonProperty("hash")] string ReleaseHash,
        [property: JsonProperty("pak_file_name")] string PakFileName,
        [property: JsonProperty("release_date")] DateTime ReleaseDate,
        [property: JsonProperty("manifest")] ModManifest Manifest
    ) {
        public static Release FromV1(V1.Release input) {
            return new Release(
                Tag: input.Tag,
                ReleaseHash: input.ReleaseHash,
                PakFileName: input.PakFileName,
                ReleaseDate: input.ReleaseDate,
                Manifest: ModManifest.FromV1(input.Manifest)
            );
        }
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModType {
        Client,
        Server,
        Shared
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModTag {
        Mutator, 
        Map, 
        Cosmetic, 
        Audio, 
        Model, 
        Weapon, 
        Doodad,
        Explicit
    }

    public record Dependency(
        [property: JsonProperty("repo_url")] string RepoUrl,
        [property: JsonProperty("version")] string Version
    ) {
        public static Dependency FromV1(V1.Dependency input) {
            return new Dependency(
                RepoUrl: input.RepoUrl,
                Version: input.Version
            );
        }
    }

    public record ModManifest(
        [property: JsonProperty("repo_url")] string RepoUrl,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("description")] string Description,
        [property: JsonProperty("home_page")] string? HomePage,
        [property: JsonProperty("image_url")] string? ImageUrl,
        [property: JsonProperty("mod_type")] ModType ModType,
        [property: JsonProperty("authors")] List<string> Authors,
        [property: JsonProperty("dependencies")] List<Dependency> Dependencies,
        [property: JsonProperty("tags")] List<ModTag> Tags,
        [property: JsonProperty("ag_mod")] bool AgMod
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
