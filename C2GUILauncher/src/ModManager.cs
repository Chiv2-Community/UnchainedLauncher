using C2GUILauncher.JsonModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Eventing.Reader;
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

        public const string AssetLoaderPluginPath = $"{FilePaths.PluginDir}\\C2AssetLoaderPlugin.dll";
        public const string ServerPluginPath = $"{FilePaths.PluginDir}\\C2ServerPlugin.dll";
        public const string BrowserPluginPath = $"{FilePaths.PluginDir}\\C2BrowserPlugin.dll";

        public const string AssetLoaderPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2AssetLoaderPlugin/releases/latest/download/C2AssetLoaderPlugin.dll";
        public const string ServerPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2ServerPlugin/releases/latest/download/C2ServerPlugin.dll";
        public const string BrowserPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2BrowserPlugin/releases/latest/download/C2BrowserPlugin.dll";

    }

    public class ModManager
    {

        public string RegistryOrg { get; }
        public string RegistryRepoName { get; }
        public ObservableCollection<Mod> Mods { get; }
        public ObservableCollection<Release> EnabledModReleases { get; }

        public ModManager(string registryOrg, string registryRepoName, ObservableCollection<Mod> baseModList, ObservableCollection<Release> enabledMods)
        {
            RegistryOrg = registryOrg;
            RegistryRepoName = registryRepoName;
            Mods = baseModList;
            EnabledModReleases = enabledMods;
        }

        public Release? GetCurrentlyEnabledReleaseForMod(Mod mod)
        {
            return EnabledModReleases.FirstOrDefault(x => mod.Releases.Contains(x));
        }

        public void DisableModRelease(Release release)
        {
            EnabledModReleases.Remove(release);
        }

        public ModEnableResult EnableModRelease(Release release)
        {
            var associatedMod = this.Mods.First(Mods => Mods.Releases.Contains(release));

            if (associatedMod == null)
                return ModEnableResult.Fail("Selected release not found in mod list: " + release.Manifest.Name + " @" + release.Tag);

            var result = ModEnableResult.Success;

            var enabledModRelease = GetCurrentlyEnabledReleaseForMod(associatedMod);

            if (enabledModRelease != null)
            {
                if (enabledModRelease == release)
                    return ModEnableResult.Success;

                result += ModEnableResult.Warn("Mod already enabled with different version: " + enabledModRelease.Manifest.Name + " @" + enabledModRelease.Tag);
            }

            foreach (var dependency in release.Manifest.Dependencies)
            {
                var dependencyRelease = 
                    this.Mods
                    .First(mod => mod.LatestManifest.RepoUrl == dependency.RepoUrl)?.Releases
                    .First(release => release.Tag == dependency.Version);

                if (dependencyRelease == null)
                    result += ModEnableResult.Fail("Dependency not found: " + dependency.RepoUrl + " @" + dependency.Version);
                else 
                    result += EnableModRelease(dependencyRelease);

            }

            EnabledModReleases.Add(release);
            return result;
        }

        public async Task UpdateModsList()
        {
            Mods.Clear();

            await Task.Delay(100);

            var manifest = new ModManifest(
                "https://github.com/Chiv2-Community-X/sex",
                "sex",
                "sex mod.",
                null,
                null,
                ModType.Shared,
                "ur mum, ur dad",
                new List<Dependency>() { new Dependency("https://github.com/Chiv2-Community/ArgonSDKCoreUtils", "v0.1.0") },
                new List<ModTag> { ModTag.Explicit, ModTag.Assets, ModTag.Misc }.Select(x => x.ToString()).ToList()
            );


            Mods.Add(
                new Mod(
                    manifest,
                    new List<Release>() { new Release("v1.0.0", "abcd", DateTime.Now, manifest)}
                )
            );

            await Task.Delay(100);

            manifest = new ModManifest(
                "https://github.com/Nihilianth/C2LightsabersMod",
                "Lightsaber Mod",
                "High viz lightsabers",
                null,
                null,
                ModType.Shared,
                "Nihilianth",
                new List<Dependency>() { },
                new List<ModTag> { ModTag.Mod }.Select(x => x.ToString()).ToList()
            );


            Mods.Add(
                new Mod(
                    manifest,
                    new List<Release>() { new Release("v1.0.0", "abcd", DateTime.Now, manifest) }
                )
            );

            manifest = new ModManifest(
                "https://github.com/Chiv2-Community/ArgonSDKCoreUtils",
                "ArgonSDK Core Utils",
                "ArgonSDK Core Utilities. Convienent helpers provided by the Chivalry2 Community",
                null,
                null,
                ModType.Shared,
                "Nihilianth, DrLong",
                new List<Dependency>(),
                new List<ModTag> { ModTag.Misc }.Select(x => x.ToString()).ToList()
            );


            Mods.Add(
                new Mod(
                    manifest,
                    new List<Release>() { new Release("v0.1.0", "abcd", DateTime.Now, manifest) }
                )
            );

            await Task.Delay(100);
        }

        public IEnumerable<DownloadTask> DownloadModFiles(bool debug)
        {
            // Create plugins dir. This method does nothing if the directory already exists.
            Directory.CreateDirectory(FilePaths.PluginDir);

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


    public record ModEnableResult(bool Successful, List<string> Failures, List<string> Warnings)
    {
        public static ModEnableResult Success => new ModEnableResult(true, new List<string>(), new List<string>());
        public static ModEnableResult Fail(string failure) => new ModEnableResult(false, new List<string>() { failure }, new List<string>());
        public static ModEnableResult Fails(List<string> failures) => new ModEnableResult(false, failures, new List<string>());
        public static ModEnableResult Warn(string warning) => new ModEnableResult(false, new List<string>(), new List<string>() { warning });
        public static ModEnableResult Warns(List<string> warnings) => new ModEnableResult(false, new List<string>(), warnings);

        public static ModEnableResult operator +(ModEnableResult a, ModEnableResult b)
        {
            return new ModEnableResult(
                a.Successful && b.Successful, 
                a.Failures.Concat(b.Failures).ToList(), 
                a.Warnings.Concat(b.Warnings).ToList()
            );
        }
    }
}
