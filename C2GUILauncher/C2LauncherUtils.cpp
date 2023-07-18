
#include <Windows.h>
#include <string>
#include "C2LauncherUtils.h"

#define STEAM_PATH_SEARCH_STRING "Steam"
#define EPIC_GAMES_PATH_SEARCH_STRING "Epic Games"

InstallationType AutoDetectInstallationType() {
    TCHAR buffer[256];
    GetCurrentDirectory(256, buffer);
    std::string currentDir(buffer);

    if (currentDir.find(STEAM_PATH_SEARCH_STRING) != std::string::npos) {
        return InstallationType::Steam;
    }

    if (currentDir.find(EPIC_GAMES_PATH_SEARCH_STRING) != std::string::npos) {
        return InstallationType::EpicGamesStore;
    }

    return InstallationType::NotSet;
}