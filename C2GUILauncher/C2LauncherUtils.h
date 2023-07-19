#pragma once

#include <filesystem>

enum InstallationType {
	NotSet = 0,
	Steam = 1,
	EpicGamesStore = 2
};

InstallationType AutoDetectInstallationType();
void DownloadFile(const std::string& url, const std::filesystem::path& outputPath);
void InstallFiles(const InstallationType installationType);
void RemoveFiles(const InstallationType installationType);
void LaunchGame(const std::string args, bool modded);