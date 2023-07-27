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

        const string BinName = "Chivalry2-Win64-Shipping.exe";
        const string InjectorDllName = "XAPOFX1_5.dll";


        public static void LaunchGame(string args, bool modded)
        {
            var gamePath = modded ? Path.Combine(BinDir, "Chivalry2-Win64-Shipping.exe") : "Chivalry2Launcher-ORIGINAL.exe";

            var commandLine = gamePath + " " + args;

            var processInfo = modded
                ? CreateModdedChiv2Process(gamePath, commandLine, BinDir)
                : CreateVanillaChiv2Process(gamePath, commandLine, BinDir);

            if (processInfo == null)
            {
                var currentDir = Directory.GetCurrentDirectory();
                throw new Exception($"CreateProcess for {commandLine} failed. cwd: {currentDir}");
            }

            processInfo.WaitForExit();

            processInfo.Close();
        }

        public static Process CreateModdedChiv2Process(string executable, string args, string workingDir)
        {

            var proc = CreateVanillaChiv2Process(executable, args, workingDir);

            using (var injector = new Injector(proc))
                injector.Inject(ModCachePath + "\\" + InjectorDllName);

            return proc;
        }

        public static Process CreateVanillaChiv2Process(string executable, string args, string workingDir)
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
