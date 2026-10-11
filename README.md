# Chivalry 2 Unchained Launcher

- [What is the Chivalry 2 Unchained Launcher?](#what-is-the-chivalry-2-unchained-launcher-c2ul)
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

## What is the Chivalry 2 Unchained Launcher?

The Chivalry 2 Unchained Launcher is a tool designed to provide an easy way to launch the game with mods, manage those mods, and host servers.

Check out [the wiki](https://unchained.wiki) for more information about modding and hosting servers.

## Features

- **Launch Chivalry 2**: Easily launch Chivalry 2 with custom configurations.
- **Switch Between Vanilla and Unchained**: Switch without moving files manually. Use Unchained for mods, plugins, and community servers.
- **Mod Management**: Download and manage mods for Chivalry 2.
- **DLL Management**: Enable registry-approved native plugins with SHA-256 and size validation.
- **Server Hosting**: Configure and host a server for your friends to join in Unchained Mode

## Community

Join the [Chivalry 2 Unchained community on Discord](https://discord.gg/chiv2unchained) to connect with other players, get support, and participate in discussions.

## Installation

### Automatic Installation (Preferred)
1. Download UnchainedLauncher.exe from the [latest release](https://github.com/Chiv2-Community/UnchainedLauncher/releases)
2. Windows may warn about a malicious file. 
    * Make sure you downloaded the file from here
    * Select "More Info" and then "Run Anyway"
3. A prompt will pop up asking if you would like to install to Steam or EGS depending on you have detected as installed.
    * If you have both installed and want to set up the launcher on EGS, select "No" on the prompt asking for a steam install. An EGS Prompt will pop up next.
4. Select Yes to install the launcher.

### Manual Installation (if automatic fails or you're on an unsupported platform and would like to test)
1. [Download the latest binary from the releases page](https://github.com/Chiv2-Community/UnchainedLauncher/releases).
2. Locate your game's install (In steam usually `Steam\steamapps\common\Chivalry 2\`, Epic Games `Epic Games\Chivalry 2\`)
3. In that directory, rename `Chivalry2Launcher.exe` to `Chivalry2Launcher-ORIGINAL.exe`. _Exact naming is important._
4. Put the downloaded Unchained `Chivalry2Launcher.exe` in place of the old one.
5. Launch the game normally (either from your steam library or from Epic Games Store).

## Plugin Manager

Manage native DLL plugins from your configured registries. UnchainedPlugin is
enabled on first use.

- **Enable**, **Disable**, or select a version to control what loads.
- **Refresh Plugin List** reloads registry data without changing selections.
- **Update Plugins** asks before updating enabled plugins in the search results.
- Click a plugin's name for its GitHub source; expand **Release notes** for its release link.
- **Enable Automatic Plugin Updates and Downloads** checks for updates and downloads missing DLLs at launch. With it off, missing DLLs block launch.
- **Enable Unstable Plugin Releases** shows prereleases and permits prerelease updates. It starts checked on prerelease launcher builds. Unchecking it does not disable selected plugins.

Plugins require **Unchained** launch. Enable required plugin dependencies yourself;
missing or incompatible dependencies block launch. Only plugins matching the
client/server target load.

DLL size and SHA-256 are checked before installation and loading. Loose DLLs are
ignored. Only enable publishers you trust: these checks do not make native code safe.
Inactive managed DLLs are cached outside `Plugins` and restored when selected.
Unknown filenames and unregistered versions stay where they are.

## Server CLI

`UnchainedLauncher.CLI` is a server-only console application that depends on Core, not WPF.
See [the CLI guide](UnchainedLauncher.CLI/README.md) for building, launch options,
registry formats, and the live server dashboard.

## License

This project is licensed under the terms of the [LICENSE](LICENSE) file.

## Contributing

Contributions are welcome! Feel free to open an issue or submit a pull request. See `CONTRIBUTING` for development-related details.
