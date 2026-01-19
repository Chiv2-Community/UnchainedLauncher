using LanguageExt;
using Semver;
using System.Text.Json.Serialization;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.JsonModels.Metadata.V4 {
    public record Mod(
        [property: JsonPropertyName("latest_release_info")] ModInfo LatestReleaseInfo,
        [property: JsonPropertyName("releases")] List<Release> Releases
    ) {
        public static Mod FromV3(V3.Mod input) {
            return new Mod(
                LatestReleaseInfo: ModInfo.FromV3(input.LatestManifest),
                Releases: input.Releases.ConvertAll(Release.FromV3)
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
        [property: JsonPropertyName("info")] ModInfo Info,
        [property: JsonPropertyName("manifest")] PakManifest Manifest,
        [property: JsonPropertyName("release_notes_markdown")] string? ReleaseNotesMarkdown
    ) {
        public static Release FromV3(V3.Release input) {
            return new Release(
                Tag: input.Tag,
                ReleaseHash: input.ReleaseHash,
                PakFileName: input.PakFileName,
                ReleaseDate: input.ReleaseDate,
                Info: ModInfo.FromV3(input.Manifest),
                Manifest: new PakManifest {
                    PakName = input.PakFileName,
                    PakPath =  input.PakFileName,
                    PakHash = input.ReleaseHash,
                    Inventory = new AssetCollections {
                        Arbitrary = new List<ArbitraryDto>(),
                        Maps = input.Manifest.Maps.Select(
                            mapName => new MapDto {
                                MapName = mapName
                            }
                        ).ToList(),
                        Blueprints = new List<BlueprintDto>(),
                        Markers = new List<ModMarkerDto>(),
                        Replacements = new List<ReplacementDto>()
                    }
                },
                ReleaseNotesMarkdown: input.ReleaseNotesMarkdown
            );
        }

        public string ReleaseUrl => $"{Info.RepoUrl}/releases/{Tag}";

        public SemVersion? Version {
            get {
                SemVersion.TryParse(Tag, SemVersionStyles.AllowV, out var version);
                return version;
            }
        }
    }

    public record Dependency(
        [property: JsonPropertyName("repo_url")] string RepoUrl,
        [property: JsonPropertyName("version")] string Version
    ) {
        public string Organization => RepoUrl.Split('/')[^2];
        public string RepoName => RepoUrl.Split('/')[^1];

        public static Dependency FromV3(V3.Dependency input) {
            return new Dependency(
                RepoUrl: input.RepoUrl,
                Version: input.Version
            );
        }
    }

    public record ModInfo(
        [property: JsonPropertyName("repo_url")] string RepoUrl,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("home_page")] string? HomePage,
        [property: JsonPropertyName("icon_url")] string? IconUrl,
        [property: JsonPropertyName("image_urls")] List<string>? ImageUrls,
        [property: JsonPropertyName("authors")] List<string> Authors,
        [property: JsonPropertyName("dependencies")] List<Dependency> Dependencies
    ) {
        public string Organization => RepoUrl.Split('/')[^2];
        public string RepoName => RepoUrl.Split('/')[^1];

        public static ModInfo FromV3(V3.ModManifest input) { // N.B.: ModManifest renamed to ModInfo.  Manifest is used for pak contents.
            return new ModInfo(
                RepoUrl: input.RepoUrl,
                Name: input.Name,
                Description: input.Description,
                HomePage: input.HomePage,
                IconUrl: input.ImageUrl,
                ImageUrls: input.ImageUrl == null ? new List<string>() : new List<string>(){ input.ImageUrl },
                Authors: input.Authors,
                Dependencies: input.Dependencies.ConvertAll(Dependency.FromV3)
            );
        }
    }
}