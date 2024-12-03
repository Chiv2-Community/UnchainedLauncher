using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnchainedLauncher.Core.JsonModels.Metadata.V2
{
    public record Mod(
        [property: JsonProperty("latest_manifest", Required = Required.Always)] ModManifest LatestManifest,
        [property: JsonProperty("releases", Required = Required.Always)] List<Release> Releases
    )
    {
        public static Mod FromV1(V1.Mod v1Mod)
        {
            return new Mod(
                LatestManifest: ModManifest.FromV1(v1Mod.LatestManifest),
                Releases: v1Mod.Releases.ConvertAll(Release.FromV1)
            );
        }
    }

    public record Release(
        [property: JsonProperty("tag", Required = Required.Always)] string Tag,
        [property: JsonProperty("hash", Required = Required.Always)] string ReleaseHash,
        [property: JsonProperty("pak_file_name", Required = Required.Always)] string PakFileName,
        [property: JsonProperty("release_date", Required = Required.Always)] DateTime ReleaseDate,
        [property: JsonProperty("manifest", Required = Required.Always)] ModManifest Manifest
    )
    {
        public static Release FromV1(V1.Release input)
        {
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
    public enum ModType
    {
        Client,
        Server,
        Shared
    }
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModTag
    {
        Mutator,
        Map,
        Cosmetic,
        Audio,
        Model,
        Weapon,
        Doodad,
    }

    public record Dependency(
        [property: JsonProperty("repo_url", Required = Required.Always)] string RepoUrl,
        [property: JsonProperty("version", Required = Required.Always)] string Version
    )
    {
        public static Dependency FromV1(V1.Dependency input)
        {
            return new Dependency(
                RepoUrl: input.RepoUrl,
                Version: input.Version
            );
        }
    }

    public record ModManifest(
        [property: JsonProperty("repo_url", Required = Required.Always)] string RepoUrl,
        [property: JsonProperty("name", Required = Required.Always)] string Name,
        [property: JsonProperty("description", Required = Required.Always)] string Description,
        [property: JsonProperty("home_page")] string? HomePage,
        [property: JsonProperty("image_url")] string? ImageUrl,
        [property: JsonProperty("mod_type", Required = Required.Always)] ModType ModType,
        [property: JsonProperty("authors", Required = Required.Always)] List<string> Authors,
        [property: JsonProperty("dependencies", Required = Required.Always)] List<Dependency> Dependencies,
        [property: JsonProperty("tags", Required = Required.Always)] List<ModTag> Tags,
        [property: JsonProperty("ag_mod", Required = Required.Always)] bool AgMod
    )
    {
        public static ModManifest FromV1(V1.ModManifest latestManifest)
        {
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
