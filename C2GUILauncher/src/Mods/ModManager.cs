using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace C2GUILauncher.Mods
{

    class CoreMods
    {
        public const string GithubBaseURL = "https://github.com";

        public const string PluginPath = "TBL\\Binaries\\Win64\\Plugins";
        public const string ModCachePath = ".mod_cache";

        public const string AssetLoaderPluginPath = $"{PluginPath}\\C2AssetLoaderPlugin.dll";
        public const string ServerPluginPath = $"{PluginPath}\\C2ServerPlugin.dll";
        public const string BrowserPluginPath = $"{PluginPath}\\C2BrowserPlugin.dll";

        public const string AssetLoaderPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2AssetLoaderPlugin/releases/latest/download/C2AssetLoaderPlugin.dll";
        public const string ServerPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2ServerPlugin/releases/latest/download/C2ServerPlugin.dll";
        public const string BrowserPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2BrowserPlugin/releases/latest/download/C2BrowserPlugin.dll";

    }

    class ModManager
    {

        public string RegistryOrg { get; }
        public string RegistryRepoName { get; }
        private GitHubClient Client { get; }
        public List<Mod> Mods { get; }

        public ModManager(string registryOrg, string registryRepoName)
        {
            RegistryOrg = registryOrg;
            RegistryRepoName = registryRepoName;
            Mods = new List<Mod>();

            Client = new GitHubClient(new ProductHeaderValue("Chiv2-Unchained-Launcher"));
        }

        public async Task UpdateModsList()
        {
            var modRepoRootURLs = new List<string>();

            // Use the github api to list the contents of the registry dir
            var allFiles = await Client.Repository.Content.GetAllContentsByRef(RegistryOrg, RegistryRepoName, "registry", "main");

            // Split by newline, filter out nulls, and rewrite the host.
            // The host rewrite is so that we can download the raw contents of the mod.json file.
            var splitByNewlineFilterNullAndRewriteHost = (string s) => s.Split('\n').Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Replace("github.com", "raw.githubusercontent.com"));


            // This gets the contents of every file in the registry dir, then breaks them up by newline to get a repo list from each file, then adds them to the modRepoRootURLs list
            var listAllReposTasks = 
                allFiles
                    .Select(file => file.Path)
                    .Select(async path => await Client.Repository.Content.GetAllContentsByRef(RegistryOrg, RegistryRepoName, path, "main")) // Get the contents of the file
                    .Select(async result => (await result)[0].Content) // Get the content of the file as a string
                    .Select(async content => splitByNewlineFilterNullAndRewriteHost(await content)) // Split by newline, filter out nulls, and rewrite the host
                    .Select(async x => modRepoRootURLs.AddRange(await x)); // Add all the repos to the list

            // Wait for the repo list to be populated
            await Task.WhenAll(listAllReposTasks);

            // Download all the mod.json files
            var downloadModManifestsResults =
                modRepoRootURLs!
                    .Select(async repoRoot => await DownloadModManifest(repoRoot)) // Download the mod.json file for every repo
                    .Where(x => x != null) // TODO: Capture these nulls and report to somewhere
                    .Select(async modMetadataString => JsonConvert.DeserializeObject<Mod>(await modMetadataString)!) // Deserialize file contents
                    .Select(async x => Mods.Add(await x));

            await Task.WhenAll(downloadModManifestsResults);
        }

        private static async Task<string> DownloadModManifest(string repoRoot)
        {
            try
            {
                try
                {
                    // Attempt to get master branch
                    return await HttpHelpers.GetRawContentsAsync(repoRoot + "/main/mod.json");
                }
                // If it 404s
                catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    // Attempt to get main branch instead
                    return await HttpHelpers.GetRawContentsAsync(repoRoot + "/master/mod.json");

                }
            }
            catch (Exception e)
            {
                // TODO: More robust error handling. This should arccumulate the failures and report them to the UI somehow.
                throw new Exception($"Failed to download {repoRoot} mod json.", e);
            }
        }

        public IEnumerable<DownloadTask> DownloadModFiles(bool debug)
        {
            // Create plugins dir. This method does nothing if the directory already exists.
            Directory.CreateDirectory(CoreMods.PluginPath);

            // All Chiv2-Community dll releases have an optional _dbg suffix for debug builds.
            var downloadFileSuffix = debug ? "_dbg.dll" : ".dll";

            // These are the core mods necessary for asset loading, server hosting, server browser usage, and the injector itself.
            // Please forgive the jank debug dll implementation. It'll be less jank after we aren't using hardcoded paths
            var coreMods = new List<DownloadTarget>() {
                new DownloadTarget(CoreMods.AssetLoaderPluginURL.Replace(".dll", downloadFileSuffix), CoreMods.AssetLoaderPluginPath),
                new DownloadTarget(CoreMods.ServerPluginURL.Replace(".dll", downloadFileSuffix), CoreMods.ServerPluginPath),
                new DownloadTarget(CoreMods.BrowserPluginURL.Replace(".dll", downloadFileSuffix), CoreMods.BrowserPluginPath)
            };

            return HttpHelpers.DownloadAllFiles(coreMods);
        }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    enum ModType
    {
        [System.Runtime.Serialization.EnumMember(Value = "PLUGIN")]
        Plugin,

        [System.Runtime.Serialization.EnumMember(Value = "HOST_ONLY_PLUGIN")]
        HostOnlyPlugin,

        [System.Runtime.Serialization.EnumMember(Value = "CLIENT_ONLY_PLUGIN")]
        ClientOnlyPlugin,

        [System.Runtime.Serialization.EnumMember(Value = "ASSETS")]
        Assets
    }
    record Mod(
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("description")] string Description,
        [property: JsonProperty("image_url")] string ImageUrl,
        [property: JsonProperty("download_url")] string DownloadUrl,
        [property: JsonProperty("mod_type")] string ModType,
        [property: JsonProperty("authors")] List<string> Authors,
        [property: JsonProperty("versions")] List<string>? Versions
    );

    record Repo(string Org, string Name);
}
