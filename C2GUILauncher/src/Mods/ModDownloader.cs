using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace C2GUILauncher.Mods
{
    class CoreMods
    {
        public const string GithubBaseURL = "https://github.com";

        public const string PluginPath = "TBL\\Binaries\\Win64\\Plugins";
        public const string ModCachePath = ".mod_cache";

        public const string InjectorDllPath = $"{ModCachePath}\\XAPOFX1_5.dll";

        public const string AssetLoaderPluginPath = $"{PluginPath}\\C2AssetLoaderPlugin.dll";
        public const string ServerPluginPath = $"{PluginPath}\\C2ServerPlugin.dll";
        public const string BrowserPluginPath = $"{PluginPath}\\C2BrowserPlugin.dll";

        public const string InjectorDllURL = $"{GithubBaseURL}/Chiv2-Community/C2PluginLoader/releases/latest/download/XAPOFX1_5.dll";

        public const string AssetLoaderPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2AssetLoaderPlugin/releases/latest/download/C2AssetLoaderPlugin.dll";
        public const string ServerPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2ServerPlugin/releases/latest/download/C2ServerPlugin.dll";
        public const string BrowserPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2BrowserPlugin/releases/latest/download/C2BrowserPlugin.dll";

    }

    static class ModDownloader
    {
        const string InjectorDllName = "XAPOFX1_5.dll";

        const string PluginPath = "TBL\\Binaries\\Win64\\Plugins";


        public static IEnumerable<DownloadTask> DownloadModFiles(bool debug)
        {
            // All Chiv2-Community dll releases have an optional _dbg suffix for debug builds.
            var downloadFileSuffix = debug ? "_dbg.dll" : ".dll";

            // These are the core mods necessary for asset loading, server hosting, server browser usage, and the injector itself.
            // Please forgive the jank debug dll implementation. It'll be less jank after we aren't using hardcoded paths.
            var coreMods = new List<DownloadTarget>() {
                new DownloadTarget(CoreMods.InjectorDllURL.Replace(".dll", downloadFileSuffix), CoreMods.InjectorDllPath),
                new DownloadTarget(CoreMods.AssetLoaderPluginURL.Replace(".dll", downloadFileSuffix), CoreMods.AssetLoaderPluginPath),
                new DownloadTarget(CoreMods.ServerPluginURL.Replace(".dll", downloadFileSuffix), CoreMods.ServerPluginPath),
                new DownloadTarget(CoreMods.BrowserPluginURL.Replace(".dll", downloadFileSuffix), CoreMods.BrowserPluginPath)
            };

            return HttpHelpers.DownloadAllFiles(coreMods);
        }
    }
}
