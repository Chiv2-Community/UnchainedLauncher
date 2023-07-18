#pragma once

#include <filesystem>

enum InstallationType {
	NotSet = 0,
	Steam = 1,
	EpicGamesStore = 2
};

InstallationType AutoDetectInstallationType();
void DownloadFile(const std::string& url, const std::filesystem::path& outputPath);
void InstallFiles(InstallationType installationType);
void RemoveFiles(InstallationType installationType);