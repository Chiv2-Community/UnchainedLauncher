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
- **Switch Between Vanilla, Client Side Modded, Unchained**: Seamlessly switch between vanilla and modded installs without manually moving any files around. Use unchained mode to connect to community hosted servers.
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

UnchainedPlugin is enabled by default on first use. Each plugin has one card with
a version dropdown, like the Mod Manager. Click **Enable** to use its newest
visible version, or choose a version from the dropdown to enable that version.
Click **Disable** to stop loading it. Only one version per plugin can be enabled.
Click a plugin's name to view its source repository on GitHub. Expand **Release
notes** and click **View original release notes on GitHub** to open the displayed
version's release page, including its original notes and downloadable assets.
**Refresh Plugin List** retrieves releases without changing your selected versions.
Search matches plugin names, repositories, descriptions, and authors. Sort uses
the same enabled-first, alphabetical, latest-release, and newest-package modes
as Mod Manager. **Update Plugins** checks enabled plugins in the current search
results and asks before selecting newer registered versions; it does not enable
disabled plugins or download DLLs immediately. Downloads follow your settings
at the next Unchained launch.
The original **Enable Automatic Plugin Updates and Downloads** and **Enable
Unstable Plugin Releases** controls are at the bottom of Plugin Manager and use the same
saved settings as before. When automatic updates are enabled, Unchained launch
refreshes the registry and offers newer versions of enabled plugins for confirmation.
Declining keeps the selected version; disabled plugins are not enabled automatically.
Unstable releases are eligible for update offers only when their control is checked.
With automatic updates/downloads disabled, launch uses only installed, validated
DLLs; missing files cancel launch rather than downloading them.
Prereleases have a badge and are hidden unless **Enable Unstable Plugin Releases** is checked.
Prerelease launcher builds start with this control checked, even if it was saved
as unchecked. You can uncheck it for the current session; stable builds retain
the existing saved preference for the same launcher version.
Hiding a prerelease does not disable it; use **Disable** to stop loading it.
Plugin Manager uses the GitHub registries configured in the existing registry settings.
After adding or removing a registry, click **Refresh Plugin List** before launching.
Each registry may publish plugins under `dll_db`; mod-only registries are supported.
Plugin Manager reads `dll_db/plugin_list_index.txt` and each listed
`dll_db/packages/<organization>/<repository>.json`, matching C2ModRegistry's
current format and zero-infra-mod-registry's `prepare-v1` plugin schema.
The package list is authoritative: the legacy `dll_db/index.json` is read only
when the package list does not exist. A removed release is not restored from a
stale legacy index. Existing selections remain pinned when registry metadata is
migrated to the package format. Previously approved historical releases may have
null metadata, dates, or filenames; these are supported inside metadata-backed
packages and retain SHA-256/size checks.
Matching duplicate releases are combined; conflicting records cancel refresh.
Cached approvals are usable offline only while the configured registry sources match.
Local pak directories do not approve DLLs.
Cards show the name, description, authors, target, asset filename, release date,
tag, notes and dependencies from the release's
`plugin.json`. Dependencies must be explicitly enabled at a compatible registered
version; the launcher does not enable them automatically. Missing or incompatible
dependencies cancel launch. DLL dependencies are injected before the plugins using them.
`target` is `client`, `server`, or `shared` (the default when omitted).
Client/server plugins are downloaded and injected only for their declared side;
shared plugins apply to both. Enabled versions stay selected when launching the
other side, but do not load there. A dependency must be enabled and applicable
to the same launch side; otherwise launch is cancelled before DLL downloads.
Pak dependency repository names can contain dots, while plugin repository names
follow the stricter plugin schema.

Both `mod.json` and `plugin.json` can declare `mod_dependencies` (pak mods) and
`plugin_dependencies` (DLL plugins), using `repo_url` and `version` for each entry.
Existing mods' `dependencies` are treated as `mod_dependencies`. Plugin dependencies
must be explicitly enabled in Plugin Manager. Mods requiring DLL plugins need
Unchained launch; the clientside-only launcher cannot load their plugins.

At Unchained launch, when automatic downloads are enabled, missing enabled DLLs are downloaded from the separate DLL
database in your configured registries. Their size and SHA-256 are checked before installation
and again immediately before injection. Validation failure cancels launch.
Loose DLLs in `TBL\Binaries\Win64\Plugins` are ignored; existing files are not
automatically trusted or deleted. Managed downloads live under `Plugins\Managed`
and use the registered `dll_file_name` (or `<repository>.dll` for historical
records without a filename).
Selections and the last valid index are saved under `.mod_cache` so previously
installed, validated releases can be used offline.

If a managed DLL fails validation, remove that release's file under
`Plugins\Managed` and launch again to download it afresh. Do not bypass the check.
Only enable publishers you trust: integrity validation does not sandbox native code
or establish that a DLL is harmless. First use needs network access and the DLL
index must be deployed to the registry's `main` branch before this launcher is used.

## License

This project is licensed under the terms of the [LICENSE](LICENSE) file.

## Contributing

Contributions are welcome! Feel free to open an issue or submit a pull request. See `CONTRIBUTING` for development-related details.
