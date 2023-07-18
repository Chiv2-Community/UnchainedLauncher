#pragma once

enum InstallationType {
	NotSet = 0,
	Steam = 1,
	EpicGamesStore = 2
};

InstallationType AutoDetectInstallationType();
