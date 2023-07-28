using Reloaded.Injector;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace C2GUILauncher
{
    enum InstallationType { NotSet, Steam, EpicGamesStore }

    internal static class C2LauncherUtils
    {
        const string SteamPathSearchString = "Steam";
        const string EpicGamesPathSearchString = "Epic Games";

        const string ModCachePath = ".mod_cache";
        const string BinDir = "TBL\\Binaries\\Win64";
        const string PluginPath = "TBL\\Binaries\\Win64\\Plugins";

        const string GameBinPath = BinDir + "\\Chivalry2-Win64-Shipping.exe";
        const string OriginalLauncherPath = "Chivalry2Launcher-ORIGINAL.exe";
        const string InjectorDllName = "XAPOFX1_5.dll";


        public static void LaunchGame(string args, bool modded)
        {
            Process process;
            if(modded)
            {
                process = LaunchProcessWithDLL(GameBinPath, args, BinDir, Path.Combine(ModCachePath, InjectorDllName));
            } else
            {
                process = LaunchProcess(OriginalLauncherPath, args, BinDir);
            }


            if (process == null)
            {
                var commandLine = modded ? GameBinPath : OriginalLauncherPath;
                var currentDir = Directory.GetCurrentDirectory();
                throw new Exception($"CreateProcess for '{commandLine} {args}' failed. cwd: {currentDir}");
            }

            process.WaitForExit();

            process.Close();
        }

        public static Process LaunchProcessWithDLL(string executable, string args, string workingDir, string dllPath)
        {

            var proc = LaunchProcess(executable, args, workingDir);

            using (var injector = new Injector(proc))
                injector.Inject(dllPath);

            return proc;
        }

        public static Process LaunchProcess(string executable, string args, string workingDir)
        {

            var proc = new Process();

            proc.StartInfo = new ProcessStartInfo()
            {
                FileName = executable,
                Arguments = args,
                WorkingDirectory = Path.GetFullPath(workingDir),
            };

            proc.Start();

            return proc;
        }

        public static InstallationType AutoDetectInstallationType()
        {
            var currentDir = Directory.GetCurrentDirectory();
            switch(currentDir)
            {
                case var _ when currentDir.Contains(SteamPathSearchString):
                    return InstallationType.Steam;
                case var _ when currentDir.Contains(EpicGamesPathSearchString):
                    return InstallationType.EpicGamesStore;
                default:
                    return InstallationType.NotSet;
            }
        }

        public static bool DownloadModFiles(bool debug) 
        {
            var baseUrl = "https://github.com/Chiv2-Community";
            var downloadFileSuffix = debug ? "_dbg.dll" : ".dll";

            var jobs = new List<Task>() {
                HttpHelpers.DownloadFileAsync($"{baseUrl}/C2PluginLoader/releases/latest/download/XAPOFX1_5{downloadFileSuffix}", Path.Combine(ModCachePath, InjectorDllName)),
                HttpHelpers.DownloadFileAsync($"{baseUrl}/C2AssetLoaderPlugin/releases/latest/download/C2AssetLoaderPlugin{downloadFileSuffix}", Path.Combine(PluginPath, "C2AssetLoaderPlugin.dll")),
                HttpHelpers.DownloadFileAsync($"{baseUrl}/C2ServerPlugin/releases/latest/download/C2ServerPlugin{downloadFileSuffix}", Path.Combine(PluginPath, "C2ServerPlugin.dll")),
                HttpHelpers.DownloadFileAsync($"{baseUrl}/C2BrowserPlugin/releases/latest/download/C2BrowserPlugin{downloadFileSuffix}", Path.Combine(PluginPath, "C2BrowserPlugin.dll"))
            };

            return Task.WhenAll(jobs).Wait(30000);
        }

    }
}
