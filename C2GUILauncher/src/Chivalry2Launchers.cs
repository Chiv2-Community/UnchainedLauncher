using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace C2GUILauncher
{
    static class Chivalry2Launchers
    {
        public const string BinDir = "TBL\\Binaries\\Win64";
        public const string GameBinPath = BinDir + "\\Chivalry2-Win64-Shipping.exe";
        public const string OriginalLauncherPath = "Chivalry2Launcher-ORIGINAL.exe";

        public static ProcessLauncher VanillaLauncher { get; } = new ProcessLauncher(OriginalLauncherPath, "");
        public static ProcessLauncher ModdedLauncher { get; } = new ProcessLauncher(GameBinPath, BinDir, new string[] { "C2PluginLoader.dll" });
    }
}
