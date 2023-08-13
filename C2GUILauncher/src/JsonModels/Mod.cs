using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;

namespace C2GUILauncher.JsonModels {

    /// <summary>
    /// A mod is a collection of releases that share the same repo url.
    /// 
    /// The latest manifest is used to display information about the mod in the launcher list view.
    /// </summary>
    /// <param name="LatestManifest">Typically the most recent release manifest</param>
    /// <param name="Releases">A list of available releases for this mod</param>
    public record Mod(
        [property: JsonProperty("latest_manifest")] ModManifest LatestManifest,
        [property: JsonProperty("releases")] List<Release> Releases
    );

    /// <summary>
    /// A release is a specific version of a mod and contains a mod manifest as well as the assets for the mod.
    /// </summary>
    /// <param name="Tag">The git tag of this mod</param>
    /// <param name="ReleaseHash">The SHA512 hash of the pak file when it was initially uploaded</param>
    /// <param name="PakFileName">The name of the mod pak on the github release</param>
    /// <param name="ReleaseDate">The date that the pak was uploaded</param>
    /// <param name="Manifest">The metadata of the mod associated with this release</param>
    public record Release(
        [property: JsonProperty("tag")] string Tag,
        [property: JsonProperty("hash")] string ReleaseHash,
        [property: JsonProperty("pak_file_name")] string PakFileName,
        [property: JsonProperty("release_date")] DateTime ReleaseDate,
        [property: JsonProperty("manifest")] ModManifest Manifest
    );

    /// <summary>
    /// Mod types are used to indicate if a mod is for the client, server, or both.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModType {
        Client,
        Server,
        Shared
    }

    /// <summary>
    /// Mod tags are used to categorize and filter mods.
    /// The "Explicit" tag is used to indicate that the mod may have content that is not suitable for children.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModTag {
        Weapon,
        Map,
        Assets,
        Framework,
        Mod,
        Gamemode,
        Misc,
        Explicit
    }

    /// <summary>
    /// A dependency is reference to a mod version that is required for this mod to function.
    /// </summary>
    /// <param name="RepoUrl">A full https url to the github repo</param>
    /// <param name="Version">The version required</param>
    public record Dependency(
        [property: JsonProperty("repo_url")] string RepoUrl,
        [property: JsonProperty("version")] string Version
    );

    /// <summary>
    /// The mod manifest is a json file that contains information about the mod.
    /// Each version of a mod has its own manifest, and the latest version is used to display information about the mod 
    /// in the launcher list view
    /// </summary>
    /// <param name="RepoUrl">A full https url to the github repo</param>
    /// <param name="Name">The name to display the mod as</param>
    /// <param name="Description">A description for the mod, displayed when a user selects a mod in the list view</param>
    /// <param name="HomePage">An optional link to the project website page, if there is one</param>
    /// <param name="ImageUrl">An image to display along side the mod description</param>
    /// <param name="ModType">An indicator for if this mod is for client/server or both</param>
    /// <param name="Authors">The creators of this mod</param>
    /// <param name="Dependencies">The other mods that this mod depends on to function</param>
    /// <param name="Tags">A set of tags used for categorizing and filtering mods</param>
    public record ModManifest(
        [property: JsonProperty("repo_url")] string RepoUrl,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("description")] string Description,
        [property: JsonProperty("home_page")] string? HomePage,
        [property: JsonProperty("image_url")] string? ImageUrl,
        [property: JsonProperty("mod_type")] ModType ModType,
        [property: JsonProperty("authors")] List<string> Authors,
        [property: JsonProperty("dependencies")] List<Dependency> Dependencies,
        [property: JsonProperty("tags")] List<string> Tags
    );

    public record Repo(string Org, string Name);
}
