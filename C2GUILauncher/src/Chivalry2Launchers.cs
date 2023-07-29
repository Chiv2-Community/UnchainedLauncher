using System;
using System.Collections.Generic;
using System.IO;
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
        public const string PluginDir = BinDir + "\\Plugins";

        /// <summary>
        /// The original launcher is used to launch the game with no mods.
        /// </summary>
        public static ProcessLauncher VanillaLauncher { get; } = new ProcessLauncher(OriginalLauncherPath, Directory.GetCurrentDirectory());

        /// <summary>
        /// The modded launcher is used to launch the game with mods. The DLLs here are the relative paths to the DLLs that are to be injected.
        /// </summary>
        public static ProcessLauncher ModdedLauncher { get; } = new ProcessLauncher(GameBinPath, BinDir);
    }
}
