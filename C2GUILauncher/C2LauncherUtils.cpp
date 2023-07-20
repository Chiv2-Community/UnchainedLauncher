
#include <Windows.h>
#include <string>
#include <filesystem>
#include <wininet.h>
#include <filesystem>
#include <fstream>
#include <vector>
#include <numeric>
#include "C2LauncherUtils.h"

#define STEAM_PATH_SEARCH_STRING "Steam"
#define EPIC_GAMES_PATH_SEARCH_STRING "Epic Games"

#define XAPOFX_PATH "TBL/Binaries/Win64/XAPOFX1_5.dll"

void LaunchGame(const std::string args, bool modded) {
    std::filesystem::path gamePath;
    if (modded) {
        gamePath = "TBL\\Binaries\\Win64\\Chivalry2-Win64-Shipping.exe";
    } else {
        gamePath = "Chivalry2Launcher-ORIGINAL.exe";
    }
    

    STARTUPINFO startupInfo;
    PROCESS_INFORMATION processInfo;

    ZeroMemory(&startupInfo, sizeof(startupInfo));
    startupInfo.cb = sizeof(startupInfo);
    ZeroMemory(&processInfo, sizeof(processInfo));

    auto commandLine = gamePath.string() + " " + args;

    if (!CreateProcess(NULL,   // No module name (use command line)
        LPSTR(commandLine.c_str()), // Command
        NULL,           // Process handle not inheritable
        NULL,           // Thread handle not inheritable
        FALSE,          // Set handle inheritance to FALSE
        0,              // No creation flags
        NULL,           // Use parent's environment block
        NULL,           // Use parent's starting directory 
        &startupInfo,   // Pointer to STARTUPINFO structure
        &processInfo))   // Pointer to PROCESS_INFORMATION structure
    {
        auto errorMessage = GetLastError();
        auto errorMessageString = std::to_string(errorMessage);
        TCHAR buffer[256];
        GetCurrentDirectory(256, buffer);
        std::string currentDir(buffer);
        throw std::runtime_error("CreateProcess for " + commandLine + " failed. cwd: " + currentDir + " " + errorMessageString);
    }


    // Successfully created the process.  Wait for it to finish.

    WaitForSingleObject(processInfo.hProcess, INFINITE);

    // Close handles to the child process and its primary thread.
    // Some applications might keep these handles to monitor the status
    // of the child process, for example. 
    CloseHandle(processInfo.hProcess);
    CloseHandle(processInfo.hThread);
}

InstallationType AutoDetectInstallationType() {
    //TODO: watch for buffer overflow here! 
    //Modern windows permits paths longer than 256. I'm not sure if this specific function will permit them, though
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
    if(std::filesystem::exists(outputPath)) {
		std::filesystem::remove(outputPath);
	}

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

void InstallFiles(const InstallationType installationType) {
    if(installationType == InstallationType::NotSet) {
		throw std::runtime_error("Installation type not set");
    }

    DownloadFile("/C2UMP/C2PluginLoader/releases/download/latest/XAPOFX1_5.dll", XAPOFX_PATH);

    std::filesystem::path basePath = "Plugins";

    DownloadFile("/C2UMP/C2AssetLoaderPlugin/releases/download/latest/C2AssetLoaderPlugin.dll", basePath / "C2AssetLoaderPlugin.dll");
    DownloadFile("/C2UMP/C2ServerPlugin/releases/download/latest/C2ServerPlugin.dll", basePath / "C2ServerPlugin.dll");
    DownloadFile("/Chiv2-Community/C2BrowserPlugin/releases/download/latest/C2BrowserPlugin.dll", basePath / "C2BrowserPlugin.dll");

}

bool RemoveIfExists(const std::filesystem::path filePath) {
    if (std::filesystem::exists(filePath))
        return std::filesystem::remove(filePath);

    return true;
}

void RemoveFiles(const InstallationType installationType) {
    if (installationType == InstallationType::NotSet) {
        throw std::runtime_error("Installation type not set");
    }

    RemoveIfExists(XAPOFX_PATH);

    std::filesystem::path basePath = "Plugins";

    RemoveIfExists(basePath / "C2AssetLoaderPlugin.dll");
    RemoveIfExists(basePath / "C2ServerPlugin.dll");
    RemoveIfExists(basePath / "C2BrowserPlugin.dll");
}
