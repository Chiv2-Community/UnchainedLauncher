# Chivalry 2 Unchained Launcher (C2UL)

- [What is the Chivalry 2 Unchained Launcher (C2UL)?](#what-is-the-chivalry-2-unchained-launcher-c2ul)
- [Features](#features)
- [Join the Community](#community)
- [Installation](#installation)
  - [Installation Instructions](#installation-instructions)
- [General Use Instructions](#general-use-instructions)
  - [How to Use the Tool](#how-to-use-the-tool)
  - [Troubleshooting](#troubleshooting)
  - [Note](#note)
- [Developer-Focused App Structure and Details](#developer-focused-app-structure-and-details)
- [Assets](#assets)
- [License](#license)
- [Contributing](#contributing)

## What is the Chivalry 2 Unchained Launcher (C2UL)?

The Chivalry 2 Unchained Launcher (C2UL) is a graphical user interface (GUI) tool designed to enhance the Chivalry 2 gaming experience. By providing an easy way to launch the game, manage mods, and customize settings, C2UL expands the capabilities of Chivalry 2, fostering community growth and enriching the overall player experience.

## Features

- **Launch Chivalry 2**: Easily launch Chivalry 2 with custom configurations.
- **Mod Management**: Download and manage mods for Chivalry 2.
- **Customization**: Customize the launcher with various settings and preferences.

## Community

Join the [Chivalry 2 Unchained community on Discord](https://discord.gg/chiv2unchained) to connect with other players, get support, and participate in discussions.

## Installation

1. [Download the latest binary from the releases page](https://github.com/Chiv2-Community/C2GUILauncher/releases).
2. Locate your game's install (In steam usually `Steam\steamapps\common\Chivalry 2\`, Epic Games `Epic Games\Chivalry 2\`)
3. In that directory, rename `Chivalry2Launcher.exe` to `Chivalry2Launcher-ORIGINAL.exe`. _Exact naming is important._
4. Put the downloaded Unchained `Chivalry2Launcher.exe` in place of the old one.
5. Launch the game normally (either from your steam library or from Epic Games Store).

## General Use Instructions

### How to Use the Unchained Launcher

1. **Open the Launcher**: Run the C2UL from the installed location.
2. **Select Installation Type**: If the installation type is not auto-detected, select it manually from the provided options.
3. **Launch Vanilla Game**:
   - Click the "Launch Vanilla" button to start the unmodded version of Chivalry 2.
4. **Launch Modded Game**:
   - If desired, check the "Enable Debug DLLs" option for debugging purposes.
   - If you want to skip downloading plugins, check the "Disable Plugin Download" option.
   - Click the "Launch Modded" button to start the modded version of Chivalry 2. The necessary mod files will be downloaded and applied.
5. **Monitor Downloads**: If you are launching the modded game, you can monitor the download progress of the mod files in the "Downloads" section.

### Troubleshooting

- If an error occurs during the launch process, an error message will be displayed with details. Check the provided information for troubleshooting.

### Note

- The "Launch Vanilla" and "Launch Modded" buttons will be disabled during the launch process to prevent multiple launches.

---

## Developer-Focused App Structure and Details

### Main Components

- **Chivalry2Launchers**: Contains definitions for Vanilla and Modded launchers.
  - **VanillaLauncher**: Launches the original, unmodified version of Chivalry 2.
  - **ModdedLauncher**: Launches the modified version of Chivalry 2 with selected mods.
- **MainWindow**: The main window of the application, handling user interactions.
  - **Initialize**: Sets up the main window and loads user preferences.
  - **HandleLaunch**: Manages the process of launching the game, either in vanilla or modded mode.
- **ModDownloader**: Responsible for downloading mod files.
  - **DownloadMods**: Downloads the selected mods from the repository.
  - **UpdateMods**: Checks for updates to installed mods and downloads them if necessary.
- **ProcessLauncher**: Handles the launching of processes.
  - **LaunchGame**: Starts the Chivalry 2 game process with the selected options.
  - **MonitorProcess**: Keeps track of the game process and provides feedback to the user.

### Directory Structure

- **src**: Contains the main source code files.
  - **Mods**: Contains classes related to mod management.
  - **assets**: Contains assets like logos.
  - **Properties**: Contains properties and publish profiles.

### Key Files

- **Chivalry2Launchers.cs**: Defines the paths and launchers for vanilla and modded versions.
- **MainWindow.xaml.cs**: Contains the interaction logic for the main window.
- **ModDownloader.cs**: Handles the downloading of mod files.
- **ProcessLauncher.cs**: Manages the launching of processes.

## Assets

- **Chivalry 2 Unchained Logo:**
  - PNG: <img src="https://github.com/Chiv2-Community/C2GUILauncher/blob/main/C2GUILauncher/assets/chiv2-unchained-logo.png" width="200">
  - ICO: ![chiv2-unchained-logo.ico](https://github.com/Chiv2-Community/C2GUILauncher/blob/main/C2GUILauncher/assets/chiv2-unchained-logo.ico)


## License

This project is licensed under the terms of the [LICENSE](LICENSE) file.

## Contributing

Contributions are welcome! Feel free to open an issue or submit a pull request.
