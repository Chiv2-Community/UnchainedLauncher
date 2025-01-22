using LanguageExt;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Semver;

namespace UnchainedLauncher.Core.JsonModels.Metadata.V3 {
    public record Mod(
        [property: JsonProperty("latest_manifest", Required = Required.Always)] ModManifest LatestManifest,
        [property: JsonProperty("releases", Required = Required.Always)] List<Release> Releases
    ) {
        public static Mod FromV2(V2.Mod input) {
            return new Mod(
                LatestManifest: ModManifest.FromV2(input.LatestManifest),
                Releases: input.Releases.ConvertAll(Release.FromV2)
            );
        }
        public Option<Release> LatestRelease =>
            Prelude.Optional(
                Releases.Where(x => x.Version.IsRelease)
                    .OrderByDescending(x => x.Version)
                    .FirstOrDefault()
            );
    }

    public record Release(
        [property: JsonProperty("tag", Required = Required.Always)] string Tag,
        [property: JsonProperty("hash", Required = Required.Always)] string ReleaseHash,
        [property: JsonProperty("pak_file_name", Required = Required.Always)] string PakFileName,
        [property: JsonProperty("release_date", Required = Required.Always)] DateTime ReleaseDate,
        [property: JsonProperty("manifest", Required = Required.Always)] ModManifest Manifest
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

        public string ReleaseUrl => $"{Manifest.RepoUrl}/releases/{Tag}";

        public SemVersion? Version {
            get { 
                SemVersion.TryParse(Tag, SemVersionStyles.AllowV, out var version);
                return version;
            }
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
        [property: JsonProperty("repo_url", Required = Required.Always)] string RepoUrl,
        [property: JsonProperty("version", Required = Required.Always)] string Version
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
        [property: JsonProperty("actor_mod", Required = Required.Always)] bool ActorMod
    );

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
        [property: JsonProperty("maps", Required = Required.Always)] List<string> Maps,
        [property: JsonProperty("options", Required = Required.Always)] OptionFlags OptionFlags
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