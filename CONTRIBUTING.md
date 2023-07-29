## Contribute to the Development of the Unchained Launcher

### Main Components

#### Chivalry2Launchers
Contains definitions for Vanilla and Modded launchers.
- **VanillaLauncher**: Launches the original, unmodified version of Chivalry 2.
- **ModdedLauncher**: Launches the modified version of Chivalry 2 with selected mods.

#### MainWindow
The main window of the application, handling user interactions.
- **Initialize**: Sets up the main window and loads user preferences.
- **HandleLaunch**: Manages the process of launching the game, either in vanilla or modded mode.

#### ModDownloader
Responsible for downloading mod files.
- **DownloadMods**: Downloads the selected mods from the repository.
- **UpdateMods**: Checks for updates to installed mods and downloads them if necessary.

#### ProcessLauncher
Handles the launching of processes.
- **LaunchGame**: Starts the Chivalry 2 game process with the selected options.
- **MonitorProcess**: Keeps track of the game process and provides feedback to the user.

#### Inject
Handles the injection of DLLs into the game process. "_Makes all the real magic happen_"
- **InjectAll**: Injects all specified DLLs into the given process.

### Directory Structure

- **src**: Contains the main source code files.
  - **Mods**: Contains classes related to mod management.
- **Properties**: Contains properties and publish profiles.
- **assets**: Contains assets like logos and other static content.

### Key Files

- **Chivalry2Launchers.cs**: Defines the paths and launchers for vanilla and modded versions.
- **MainWindow.xaml.cs**: Contains the interaction logic for the main window.
- **ModDownloader.cs**: Handles the downloading of mod files.
- **ProcessLauncher.cs**: Manages the launching of processes.
- **Inject.cs**: Contains the code for injecting DLLs into the game process.

### Contributing Guidelines

1. **Fork the Repository**: Fork the main repository and clone it to your local machine.
2. **Create a Branch**: Create a new branch for your feature or bug fix.
3. **Make Your Changes**: Make your changes and test them thoroughly.
4. **Commit and Push**: Commit your changes and push them to your fork.
5. **Open a Pull Request**: Open a pull request against the main repository with a clear description of your changes.

### Support and Community

Join the [Chivalry 2 Unchained community on Discord](https://discord.gg/chiv2unchained) to connect with other developers, get support, and participate in discussions.

### License

This project is licensed under the terms of the [LICENSE](LICENSE) file.

### Thank You

Thank you for considering contributing to the Chivalry 2 Unchained Launcher! Your efforts help make the game more enjoyable and customizable for everyone.
