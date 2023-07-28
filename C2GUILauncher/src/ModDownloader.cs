using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace C2GUILauncher
{
    class ModDownloader
    {
        const string InjectorDllName = "XAPOFX1_5.dll";

        const string ModCachePath = ".mod_cache";
        const string PluginPath = "TBL\\Binaries\\Win64\\Plugins";
        const string GithubBaseURL = "https://github.com";


        public static IEnumerable<DownloadTask> DownloadModFiles(bool debug)
        {
            var downloadFileSuffix = debug ? "_dbg.dll" : ".dll";

            var coreMods = new List<DownloadTarget>() {
                new DownloadTarget($"{GithubBaseURL}/Chiv2-Community/C2PluginLoader/releases/latest/download/XAPOFX1_5{downloadFileSuffix}", Path.Combine(ModCachePath, InjectorDllName)),
                new DownloadTarget($"{GithubBaseURL}/Chiv2-Community/C2AssetLoaderPlugin/releases/latest/download/C2AssetLoaderPlugin{downloadFileSuffix}", Path.Combine(PluginPath, "C2AssetLoaderPlugin.dll")),
                new DownloadTarget($"{GithubBaseURL}/Chiv2-Community/C2ServerPlugin/releases/latest/download/C2ServerPlugin{downloadFileSuffix}", Path.Combine(PluginPath, "C2ServerPlugin.dll")),
                new DownloadTarget($"{GithubBaseURL}/Chiv2-Community/C2BrowserPlugin/releases/latest/download/C2BrowserPlugin{downloadFileSuffix}", Path.Combine(PluginPath, "C2BrowserPlugin.dll"))
            };

            return HttpHelpers.DownloadAllFiles(coreMods);
        }
    }
}
