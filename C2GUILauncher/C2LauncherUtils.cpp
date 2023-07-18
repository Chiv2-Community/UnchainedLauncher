
#include <Windows.h>
#include <string>
#include <filesystem>
#include <wininet.h>
#include <filesystem>
#include <fstream>
#include "C2LauncherUtils.h"

#define STEAM_PATH_SEARCH_STRING "Steam"
#define EPIC_GAMES_PATH_SEARCH_STRING "Epic Games"

#define XAPOFX_PATH "TBL/Binaries/Win64/XAPOFX1_5.dll"


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


#pragma comment(lib, "wininet.lib")

void DownloadFile(const std::string& url, const std::filesystem::path& outputPath) {
    HINTERNET hIntSession = InternetOpen("", INTERNET_OPEN_TYPE_DIRECT, NULL, NULL, 0);

    if (hIntSession == NULL) {
        throw std::runtime_error("InternetOpen failed");
    }

    HINTERNET hHttpSession = InternetConnect(hIntSession, "github.com", INTERNET_DEFAULT_HTTPS_PORT, NULL, NULL, INTERNET_SERVICE_HTTP, 0, 0);
    if (hHttpSession == NULL) {
        throw std::runtime_error("InternetConnect failed");
    }

    HINTERNET hHttpRequest = HttpOpenRequest(
        hHttpSession, "GET",
        std::string(url.begin(), url.end()).c_str(),
        NULL, NULL, NULL,
        INTERNET_FLAG_RELOAD | INTERNET_FLAG_SECURE, 0
    );

    if (hHttpRequest == NULL) {
        throw std::runtime_error("HttpOpenRequest failed");
    }

    BOOL isRequestSent = HttpSendRequest(hHttpRequest, NULL, 0, NULL, 0);
    if (!isRequestSent) {
        throw std::runtime_error("HttpSendRequest failed");
    }

    char buffer[4096] = { 0 };
    DWORD bytesRead = 0;

    std::ofstream outputFile(outputPath, std::ios::binary);

    while (InternetReadFile(hHttpRequest, buffer, sizeof(buffer), &bytesRead) && bytesRead > 0) {
        outputFile.write(buffer, bytesRead);
    }

    InternetCloseHandle(hHttpRequest);
    InternetCloseHandle(hHttpSession);
    InternetCloseHandle(hIntSession);
}

void InstallFiles(InstallationType installationType) {
    if(installationType == InstallationType::NotSet) {
		throw std::runtime_error("Installation type not set");
    }

    DownloadFile("/C2UMP/C2PluginLoader/releases/download/v0.0.1/XAPOFX1_5.dll", XAPOFX_PATH);

    std::filesystem::path basePath =  installationType == InstallationType::Steam ? "TBL/Binaries/Win64/Plugins" : "Plugins";

    DownloadFile("/C2UMP/C2AssetLoaderPlugin/releases/download/v0.0.5/C2AssetLoaderPlugin.dll", basePath / "C2AssetLoaderPlugin.dll");
    DownloadFile("/C2UMP/C2ServerPlugin/releases/download/v0.0.5/C2ServerPlugin.dll", basePath / "C2ServerPlugin.dll");
}

bool RemoveIfExists(std::filesystem::path filePath) {
    if (std::filesystem::exists(filePath))
        return std::filesystem::remove(filePath);

    return true;
}

void RemoveFiles(InstallationType installationType) {
    if (installationType == InstallationType::NotSet) {
        throw std::runtime_error("Installation type not set");
    }

    RemoveIfExists(XAPOFX_PATH);

    std::filesystem::path basePath = installationType == InstallationType::Steam ? "TBL/Binaries/Win64/Plugins" : "Plugins";

    RemoveIfExists(basePath / "C2AssetLoaderPlugin.dll");
    RemoveIfExists(basePath / "C2ServerPlugin.dll");
}
