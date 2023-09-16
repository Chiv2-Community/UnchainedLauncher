using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace C2GUILauncher.JsonModels.Metadata.V3 {
    public record Mod(
        [property: JsonProperty("latest_manifest")] ModManifest LatestManifest,
        [property: JsonProperty("releases")] List<Release> Releases
    ) {
        public static Mod FromV2(V2.Mod input) {
            return new Mod(
                LatestManifest: ModManifest.FromV2(input.LatestManifest),
                Releases: input.Releases.ConvertAll(Release.FromV2)
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
        public static Release FromV2(V2.Release input) {
            return new Release(
                Tag: input.Tag,
                ReleaseHash: input.ReleaseHash,
                PakFileName: input.PakFileName,
                ReleaseDate: input.ReleaseDate,
                Manifest: ModManifest.FromV2(input.Manifest)
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
        Library
    }

    public record Dependency(
        [property: JsonProperty("repo_url")] string RepoUrl,
        [property: JsonProperty("version")] string Version
    ) {
        public static Dependency FromV2(V2.Dependency input) {
            return new Dependency(
                RepoUrl: input.RepoUrl,
                Version: input.Version
            );
        }
    }

    public record OptionFlags(
        [property: JsonProperty("actor_mod")] bool ActorMod,
        [property: JsonProperty("global_mod")] bool GlobalMod
    );

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
        [property: JsonProperty("maps")] List<string> Maps,
        [property: JsonProperty("options")] OptionFlags OptionFlags
    ) {
        public static ModManifest FromV2(V2.ModManifest latestManifest) {
            return new ModManifest(
                RepoUrl: latestManifest.RepoUrl,
                Name: latestManifest.Name,
                Description: latestManifest.Description,
                HomePage: latestManifest.HomePage,
                ImageUrl: latestManifest.ImageUrl,
                ModType: Enum.Parse<ModType>(latestManifest.ModType.ToString()),
                Authors: latestManifest.Authors,
                Dependencies: latestManifest.Dependencies.ConvertAll(Dependency.FromV2),
                Tags: latestManifest.Tags.Select(x => Enum.Parse<ModTag>(x.ToString())).ToList(),
                Maps: latestManifest.Maps,
                OptionFlags: new OptionFlags(
                    ActorMod: latestManifest.AgMod,
                    GlobalMod: false
                )
            );
        }
    }

    public record Repo(string Org, string Name);
}
