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

        /// <summary>
        /// The original launcher is used to launch the game with no mods.
        /// </summary>
        public static ProcessLauncher VanillaLauncher { get; } = new ProcessLauncher(OriginalLauncherPath, "");

        /// <summary>
        /// The modded launcher is used to launch the game with mods. The DLLs here are the relative paths to the DLLs that are to be injected. Just a stub right now.
        /// </summary>
        public static ProcessLauncher ModdedLauncher { get; } = new ProcessLauncher(GameBinPath, BinDir, new string[] { "XAPO.dll" });
    }
}
