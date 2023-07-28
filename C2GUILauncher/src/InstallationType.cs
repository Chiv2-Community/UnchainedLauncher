using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace C2GUILauncher.src
{
    enum InstallationType { NotSet, Steam, EpicGamesStore }

    static class InstallationTypeUtils
    {
        const string SteamPathSearchString = "Steam";
        const string EpicGamesPathSearchString = "Epic Games";

        public static InstallationType AutoDetectInstallationType()
        {
            var currentDir = Directory.GetCurrentDirectory();
            switch (currentDir)
            {
                case var _ when currentDir.Contains(SteamPathSearchString):
                    return InstallationType.Steam;
                case var _ when currentDir.Contains(EpicGamesPathSearchString):
                    return InstallationType.EpicGamesStore;
                default:
                    return InstallationType.NotSet;
            }
        }
    }
}
