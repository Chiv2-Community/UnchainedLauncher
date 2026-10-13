# Launcher CLI

This project launches an Unchained Chivalry 2 **client or server**, without the WPF GUI.
It uses Core for mod installation, plugin updates, signature preparation,
process launching, DLL injection, and A2S queries.

## Build and run

Install the .NET 10 SDK. From the repository root:

```powershell
dotnet build UnchainedLauncher.CLI -c Release
dotnet run --project UnchainedLauncher.CLI -c Release -- server --help
```

Launch on **Windows** with an existing Chivalry 2 installation:

```powershell
dotnet run --project UnchainedLauncher.CLI -c Release -- server `
  --chivalry2-path "C:\Games\Chivalry 2" `
  --platform Steam `
  --saved-dir-suffix MyServer `
  --name "My Unchained Server" `
  --map FFA_Courtyard
```

The path is the installation root containing `TBL`, not the Win64 directory.
The server is headless by default (`-nullrhi -unattended -nosound`); use
`--headless false --no-desync-patch` to render it (the desync patch only supports
headless servers). The plugin is installed
or updated from the latest stable UnchainedPlugin release by default. Use
`--no-plugin-update` to keep your installed `UnchainedPlugin.dll`; it must already
exist. `--allow-unstable-plugin` permits prerelease plugin updates.

For deployment without a .NET runtime:

```powershell
dotnet publish UnchainedLauncher.CLI -c Release -r win-x64 --self-contained true -o out-cli
.\out-cli\UnchainedLauncher.CLI.exe server --help
```

Help, parsing, and the unit tests also work on Linux. Actual game launching and
DLL injection require Windows; Wine/Proton launching is not implemented.

## Mod registry queries

Query metadata without installing mods, changing the game profile, or launching
the game. These commands work on Linux as well as Windows and require no game
installation or platform selection:

```text
UnchainedLauncher.CLI mod-query list
UnchainedLauncher.CLI mod-query get Chiv2-Community/Unchained-Mods
UnchainedLauncher.CLI mod-query get organization/mod-id --version v1.2.3
UnchainedLauncher.CLI mod-query versions organization/mod-id
UnchainedLauncher.CLI mod-query list --add-registry local:./registry
UnchainedLauncher.CLI mod-query get organization/mod-id --config-path ./server.yaml
```

The official registry is always included. Repeat `--add-registry` to include
GitHub, local, or Core JSON-config registries, using the same formats as launch
commands. `--config-path` reads `mod-setup.registries` from an existing launch
YAML file instead of the `--add-registry` arguments. YAML-relative paths resolve
relative to that file. Only registry configuration is used: installation,
platform, client/server settings, mod selections, and plugin policies are not
required or applied. Query operands and `--version` remain command-line inputs.

`list` displays mod ID (`organization/id`), name, and latest version. `get`
defaults to Core's latest stable semantic version; if none is stable it shows
the newest release by date. `--version` selects an exact release tag, including
prereleases. `versions` lists all tags, release dates, and stable/prerelease status.

Details include release-specific description/authors, repository/homepage,
release date/notes, pak filename, SHA-512 hash, size in bytes, declared dependency
ranges, map count/names/paths/modes, blueprints, and other manifest asset counts.
Local size is read from the filesystem; GitHub size uses an HTTP HEAD request,
never a pak download. An inaccessible pak or missing length is reported as
`unknown`, not zero. Declared dependencies are displayed, not installed or resolved.

Exit status is 0 for success, 2 for invalid arguments/configuration, and 1 for
registry/runtime errors or a missing mod/version. A failed registry during
`list` leaves the available rows visible but reports errors and exits 1 rather
than presenting a partial catalog as complete. Game arguments after `--` are
not accepted by `mod-query`. Use `mod-query <subcommand> --help` for details.

## DLL registry queries

```text
UnchainedLauncher.CLI dll-query list
UnchainedLauncher.CLI dll-query get Chiv2-Community/UnchainedPlugin
UnchainedLauncher.CLI dll-query get Chiv2-Community/UnchainedPlugin --version v1.0.0-RC2
UnchainedLauncher.CLI dll-query versions Chiv2-Community/UnchainedPlugin
UnchainedLauncher.CLI dll-query list --config-path ./server.yaml
```

DLL queries share the mod-query command definitions, configured registry object,
and `--add-registry`/`--config-path` behavior. They use the existing
[zero-infra plugin schema](https://github.com/Chiv2-Community/zero-infra-mod-registry/blob/prepare-v1/schemas/plugin.schema.json)
and live C2ModRegistry `dll_db`: `plugin_list_index.txt` and
`packages/<organization>/<id>.json`. Local registries use
`<local-directory>/dll_db/packages/...`. Pak-only registries without a DLL
database contribute no DLL packages.

Details show release-specific name/description/authors, execution target
(`client`, `server`, or `shared`), filename, **SHA-256** hash, recorded size,
download URL, plugin dependencies separately from pak mod dependencies, and
release notes. Versions include prereleases; defaults choose latest stable,
falling back to the newest dated release when no stable exists. Queries do not
download, install, or execute DLLs. Recorded hashes/sizes are registry metadata,
not a claim that an installed file has been verified.

## YAML configuration

CLI options and command-specific help are defined with System.CommandLine.
Use `server --help` or `client --help` to list the options for that command.
CLI input and typed YAML configurations share the same models and validation.
Client and server launch requests compose `SharedLaunchOptions` for installation,
platform, profile, registry/mod selection, plugin-update policy, and common game
arguments. Server-only settings are carried only by `ServerOptions`; there is no
client/server mode flag on a launch request. Platforms use Core's `InstallationType`;
`EGS` remains an alias for `EpicGamesStore` in both CLI and YAML input.

Use a configuration file instead of individual arguments:

```powershell
.\out-cli\UnchainedLauncher.CLI.exe server --config-path "C:\Configs\server.yaml"
.\out-cli\UnchainedLauncher.CLI.exe client --config-path "C:\Configs\client.yaml"
# From the repository root:
dotnet run --project UnchainedLauncher.CLI -- server --config-path "C:\Configs\server.yaml"
```

`--config-path` (or `--config-path=...`) replaces **all other launcher and game
arguments except the required `client`/`server` subcommand**. There are no CLI
overrides or merging. Put all intended options in the file. A `--config-path`
token after the game-argument separator `--` is just a game argument, not a
configuration selector. Relative installation and local/config registry paths
in YAML are resolved relative to the YAML file, not the caller's working directory.

Launcher response-file expansion (`@file`) is deliberately disabled: `@`-prefixed
option values are literal strings, and bare `@file` launcher arguments are rejected
like other unexpected positional arguments. This prevents file reads from ignored
CLI values when YAML is authoritative and keeps one library parse per invocation.
Put reusable launcher settings in YAML instead. Arguments after `--`, including
`@file`, are forwarded unchanged to the game (or ignored when YAML is selected).

`chiv2-path` and `platform` (`Steam` or `EGS`) are required. See the complete
[server example](examples/server.yaml) and [client example](examples/client.yaml).

```yaml
chiv2-path: 'C:\Games\Chivalry 2'
platform: Steam
saved-dir-suffix: MyServer
mod-setup:
  registries: [] # Official registry remains enabled, even with an empty list.
  mods: []       # organization/mod-id:exact-release-tag
  dlls: []       # Optional dll_db coordinates: organization/id:version.
server:
  name: My Server
  description: My server description
  game-port: 7777
  ping-port: 3075
  query-port: 7071
  rcon-port: 9001
  map: FFA_Courtyard
  mod-actors: [] # Explicit blueprint class paths to activate.
  desync-patch: true
  use-backend-banlist: true
```

Separate mode-specific schemas:
- The **subcommand**, not the document's contents, always determines mode.
- `server --config-path ...` reads the `server` mapping and completely ignores `client`.
- `client --config-path ...` reads the `client` mapping and completely ignores `server`,
  including actors, Discord, and server-only arguments; ignored blocks are not schema-validated.
- A missing active block uses that command's defaults. `client` is a mapping, not a boolean mode switch.
- Client launches omit `-server`, headless flags, and server-specific options,
  do not create a server dashboard, and return after launching without stopping the game.

Common root options are `server-browser-backend`, `update-plugin` (default true),
`allow-unstable-plugin` (default false), `no-tui` (default false), and `game-args`
(list of strings). `server.game-args` or `client.game-args` is appended to the
common list only for the matching command. The client schema currently supports
`game-args`; shared installation, platform, plugin, and mod options remain at the root.

The server section supports name, description/MOTD, password, map, local IP,
all four ports, `headless`, `register`, `mod-actors`, `desync-patch`,
`use-backend-banlist`, and `censor-mode` (`none`, `standard`, `sex`, `zealous`).
Desync patch and backend banlist **default to true in server mode**; set their
YAML values to false, or pass `--no-desync-patch` / `--no-backend-banlist`, to disable.
Rendered servers require `headless: false` **and** `desync-patch: false` in YAML,
or `--headless false --no-desync-patch` on the command line. Incompatible settings
are rejected before launch preparation; neither default is silently changed.
Local IPv4 is automatically detected from active non-loopback/non-tunnel interfaces,
preferring one with a default gateway; it falls back to another usable IPv4 or loopback.
Set `local-ip` only to override this choice (for example, with a VPN or multiple adapters).
Time/score/ticket limits, player bot count, and warmup are **INI settings**, not
part of either CLI schema; configure them in the selected saved profile.
Before a server launch, the CLI initializes the selected profile's `Game.ini` with
`bSavedHasAgreedToTOS=True` and `FirstLoadCompleted=True`, matching the GUI's startup
defaults so first-run screens do not block unattended startup. Other existing INI
settings are preserved. Client launches do not initialize or rewrite INI files.

Discord configuration is nested under the server:

```yaml
server:
  discord:
    bot-token: 'your-bot-token'
    general-channel-id: '123456789012345678'
    # Optional: admin-channel-id, dashboard-channel-id, event-log-channel-id,
    #           admin-role-id, mention-admins (default true).
```

Discord requires a token and at least one channel. Keep tokens/passwords private
and restrict access to configuration files; do not commit real credentials.
YAML syntax errors report a location without quoting the configuration contents.
Unknown keys, duplicate keys, invalid types, ports, modes, or coordinates fail
before preparing or launching the game. Lists accept any number of entries.

The `client` command also accepts individual common arguments, for example:

```text
UnchainedLauncher.CLI.exe client --chivalry2-path "C:\Games\Chivalry 2" --platform Steam
```

## Registries and mods

The official `github:Chiv2-Community/C2ModRegistry` registry is always included.
Repeat `--add-registry` to add any combination of Core's supported registry types:

```text
--add-registry github:owner/repository
--add-registry "local:C:\ModRegistry"
--add-registry "config:C:\Configs\registries.json"
```

- `github:` reads the repository's `main/package_db` layout, just like the GUI.
- `local:` points to an existing Core local registry, not a directory of arbitrary paks.
- `config:` reads Core's tagged registry configuration, supporting nested aggregates.
  Relative local paths inside it are resolved against the JSON file's directory.
- Relative command-line paths are resolved against the directory where you run the CLI.

Example aggregate configuration:

```json
{
  "Kind": "AggregateModRegistry",
  "Registries": [
    { "Kind": "GithubModRegistry", "Org": "owner", "RepoName": "repository" },
    { "Kind": "LocalModRegistry", "PakReleaseDir": "../LocalModRegistry" }
  ]
}
```

Request pak releases using their repository identity and **exact release tag**:

```text
--mod organization/mod-id:v1.2.3
--mod organization/another-mod:v2.0.0
```

Display names are not IDs. Core resolves dependencies and includes Unchained-Mods
automatically. Its current dependency policy selects latest stable releases rather
than enforcing dependency version ranges; the CLI rejects missing releases,
missing stable dependencies, and conflicting resolved versions rather than
launching an incomplete set. Registry failures stop launch.
If a pinned older release has different dependencies from the latest release,
Core may resolve the wrong dependency inventory; the CLI detects missing required
dependencies and refuses that launch rather than silently omitting them.

Installing a pak does **not** automatically activate all of its server actors.
As in the GUI, choose the actors you want to activate, using their blueprint class
paths from release metadata:

```text
--server-mod /Game/Mods/Example/BP_Example.BP_Example_C
```

Core shares physical managed-pak ownership between GUI and CLI through
`<installation>\TBL\Content\Paks\.unchained_managed_paks.json`, updated after mod
preparation (including partial failures). On first use it migrates ownership from
both existing host caches; later host snapshots cannot replace this inventory.
The CLI still saves its own metadata in `.mod_cache\cli_mod_manager.json`, but
never overwrites GUI configuration or reuses the GUI's enabled-mod selection.
Each launch reconciles the installed managed set with that host's selected releases;
genuinely unmanaged user paks are preserved.
Core's installation pipeline changes the installed managed pak set and signatures.
Do not prepare a launch concurrently with the GUI or another CLI using the same
installation; multiple running servers sharing that installation must use the
same installed mod set and separate saved suffixes/ports.

## DLL compatibility behavior

Both GUI and CLI resolve the required UnchainedPlugin release from `dll_db`.
`--dll organization/id:version` (or YAML `mod-setup.dlls`) additionally selects
registry releases in CLI launches. Plugin dependencies are resolved before their
dependents; target mismatches, missing releases, cycles, and conflicting versions
fail preparation. Pak dependencies enter the shared mod manager. The GUI uses
this same manager for the core plugin; a GUI DLL-selection editor is not included.

Every selected DLL must match the registry's SHA-256 and size. Downloads are
verified before installation. Existing matching root DLLs can be reused;
new downloads go into `Plugins/.dll-managed/<sha256>/` without replacing unknown
root DLLs. `Plugins/dll_manager.json` records the last successful selection.
Safe injection uses only that launch's verified allowlist, not the directory or
the saved inventory. Old managed files are retained, not automatically deleted.
`--no-plugin-update` forbids downloads and requires matching local files.
Only explicitly pinned prereleases (or `--allow-unstable-plugin` for automatic
selection) are eligible; automatic selection otherwise uses the latest stable.

Other DLLs in `<installation>\TBL\Binaries\Win64\Plugins` are not enumerated
for injection unless this exact command-line flag is explicitly supplied:

```text
--unsafe-very-dangerous-load-all-dlls
```

This is a dangerous compatibility escape hatch: it recursively injects **all**
DLLs in Plugins, executing arbitrary native code in the game. For the GUI,
pass it when starting `UnchainedLauncher.GUI.exe`; for the CLI, pass it to a
`client` or `server` command. It is hidden from generated help, has no GUI
checkbox, is never saved in settings, and is not accepted as a YAML property.
It must be explicitly present before `--`, even with `--config-path`; unlike
ordinary YAML-overridden launch settings, this process-local permission is
honored only from the actual command line. After `--` it is only a game argument
and grants no permission. It is rejected for query commands.

The unsafe escape hatch also includes retained managed releases; it can load
multiple versions and bypass normal selection. Only opt in if you trust every
DLL in this directory. Registry integrity checks establish file identity, not
that native code is safe.

## Server options

Run `server --help` for the full option list. Additional settings include:

| Option | Default / behavior |
| --- | --- |
| `--saved-dir-suffix` | `UnchainedCLI`; letters, digits, `_`, and `-` only |
| `--name`, `--description`, `--password` | Name, description/MOTD, optional password |
| `--map` | `FFA_Courtyard` |
| `--game-port` | `7777` |
| `--ping-port` | `3075` |
| `--query-port` | `7071` (A2S) |
| `--rcon-port` | `9001` |
| `--local-ip` | Optional override of auto-detected local IPv4; also used for A2S monitoring |
| `--register` | Opt in to server-browser registration; off by default |
| `--server-browser-backend` | `https://servers.polehammer.net` |
| `--no-tui` | Periodic plain-text status instead of the dashboard |
| `--headless true/false` | `true`; rendering requires `--no-desync-patch` |
| `--no-desync-patch`, `--no-backend-banlist` | Disable the default-enabled server features |
| `--censor-mode` | `none`; also `standard`, `sex`, `zealous` |
| `--discord-bot-token` | Requires at least one Discord channel |
| `--discord-admin-channel-id`, `--discord-general-channel-id` | Optional channel IDs |
| `--discord-dashboard-channel-id`, `--discord-event-log-channel-id` | Optional channel IDs |
| `--discord-admin-role-id`, `--discord-mention-admins true/false` | Optional role; mention defaults true |

Ports must be distinct and between 1 and 65535. Configure firewall/port forwarding
for your deployment; this CLI does not change firewall rules. Existing INI files
in the selected saved profile supply settings that are not exposed as flags.

Pass additional game/plugin arguments after `--`; they are forwarded with escaping:

```text
UnchainedLauncher.CLI.exe server --chivalry2-path "C:\Games\Chivalry 2" --platform Steam -- -log
```

`--platform Steam` creates `steam_appid.txt` with Chivalry 2's app ID if it is missing;
Steam must be available for authentication. `--platform EGS` does not obtain Epic
credentials. Supply the current Epic launch/authentication arguments after `--`
from your existing EGS launch flow. Selecting EGS alone does not authenticate you.
Avoid exposing authentication arguments and passwords in shared scripts or process listings.

## Live dashboard and shutdown

While the server is running, the dashboard displays:

- PID and process uptime.
- CPU use as a percentage of total host logical CPU capacity (0–100%).
- Private process memory in MiB.
- A2S player/max-player counts, current map, and query availability.
- Bounded rolling CPU/memory/player sparklines (up to 120 samples).
- A bounded timeline of launch, map/player changes, and query outages/recovery.

Metrics are sampled approximately once per second. During startup or query failure,
player count and map are **unknown**, not zero; `?` marks missing chart samples.
The timeline represents observed changes, not per-player join/leave identities or
the full game log. Network throughput and RCON controls are not included yet.

Press **Q** in the TUI or **Ctrl+C** to stop. Redirected input/output and dumb terminals
automatically use plain-text status (every ten seconds). The CLI owns its server:
stopping it terminates the game process, matching Core's current stop behavior;
there is no detach mode or automatic restart. Natural exits return the server's
exit code, argument errors return `2`, and launch/monitor failures return `1`.

## Tests

```text
dotnet test UnchainedLauncher.CLI.Tests -c Debug
dotnet test UnchainedLauncher.CLI.Tests -c Release
```

Tests cover parsing, Core option mapping, registry configuration, dependency checks,
bounded history, unavailable telemetry, and monitor cancellation without a game installation.
Windows CI builds, tests, and publishes the CLI separately from the GUI.
