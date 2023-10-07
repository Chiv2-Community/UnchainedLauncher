# Chivalry 2 Unchained Launcher (C2UL)

- [What is the Chivalry 2 Unchained Launcher (C2UL)?](#what-is-the-chivalry-2-unchained-launcher-c2ul)
- [Features](#features)
- [Join the Community](#community)
- [Installation](#installation)
- [General Use Instructions](#general-use-instructions)
  - [How to Use the Tool](#how-to-use-the-unchained-launcher)
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
- **Switch Between Vanilla and Modded**: Seamlessly switch between vanilla and modded installs without manually moving any files around.
- **Mod Management**: Download and manage mods for Chivalry 2.
- **Customization**: Customize the launcher with various settings and preferences.
- **Server Hosting**: Configure and host a server for your friends to join

## Community

Join the [Chivalry 2 Unchained community on Discord](https://discord.gg/chiv2unchained) to connect with other players, get support, and participate in discussions.

## Installation

### Automatic Installation (Preferred)
1. Download UnchainedLauncher.exe
2. Windows may warn about a malicious file. 
    * Make sure you downloaded the file from here
    * Select "More Info" and then "Run Anyway"
3. A prompt will pop up asking if you would like to install to Steam or EGS depending on you have detected as installed.
    * If you have both installed and want to set up the launcher on EGS, select "No" on the prompt asking for a steam install. An EGS Prompt will pop up next.
4. Select Yes to install the launcher.

### Manual Installation (if automatic fails or you're on an unsupported platform and would like to test)
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
5. **Monitor Downloads**: If you are launching the modded game, you can monitor the download progress of the mod files in the "Downloads" section from the settings tab.

### Troubleshooting

- If an error occurs during the launch process, an error message will be displayed with details. Check the provided information for troubleshooting.

### Note

- The "Launch Vanilla" and "Launch Modded" buttons will be disabled during the launch process to prevent multiple launches.

## License

This project is licensed under the terms of the [LICENSE](LICENSE) file.

## Contributing

Contributions are welcome! Feel free to open an issue or submit a pull request. See `CONTRIBUTING` for development-related details.
