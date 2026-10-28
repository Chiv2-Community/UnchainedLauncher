# Launcher CLI

This project launches an Unchained Chivalry 2 **client or server**, without the WPF GUI.
On Windows, the shared game launcher creates the game suspended, injects DLLs,
then resumes its main thread. Failed injection terminates the suspended game.
On Linux, the native CLI invokes Proton with a bundled Windows launch helper.
Steam and Epic Games Store/Heroic installations are supported; Epic authentication
comes from the existing Heroic/Legendary login flow, not Steam.
The helper uses the same suspended launch and injection path; the TUI stays in
the invoking Linux terminal. Only the game and helper run under Proton.
CLI servers also pass TOS-acceptance and first-load-completed INI overrides to
avoid interactive first-run screens. The CLI also initializes the game's saved profile.
Profile-local paks use the game's native `Saved_<suffix>/Paks` search directory.
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
  --starting-map FFA_Courtyard `
  --dll Chiv2-Community/UnchainedPlugin:v1.0.0-RC3 `
  --mod Chiv2-Community/Unchained-Mods:v0.1.0 `
  --mod Chiv2-Community/Chiv2Turbo:v0.0.4 `
  --server-mod /Game/Mods/AgMods/Chiv2Turbo/Chiv2Turbo.Chiv2Turbo_C
```

The path is the installation root containing `TBL`, not the Win64 directory.
The example pins registered releases.
The server is headless by default (`-nullrhi -unattended -nosound`); use
`--headless false --no-desync-patch` to render it (the desync patch only supports
headless servers). The plugin is installed
or updated from the latest stable UnchainedPlugin release by default. Use
`--no-plugin-update` to disable DLL downloads, not version selection; all required
registered DLL versions must already be installed or cached. To retain a desired
registered plugin version, pin its exact tag with
`--dll Chiv2-Community/UnchainedPlugin:<tag>`.
`--allow-unstable-plugin` permits automatic prerelease selection.

For deployment without a .NET runtime:

```powershell
dotnet publish UnchainedLauncher.CLI -c Release -r win-x64 --self-contained true -o out-cli
.\out-cli\UnchainedLauncher.CLI.exe server --help
```

### Linux with Proton

Use x86-64 Linux and publish the native CLI with its Windows helper. The example
below is for **Steam**: keep Steam running and launch through Steam once to
initialize its prefix. Epic users use the same published bundle but follow
[Epic/Heroic setup](#epic-games-store-and-heroic-on-linux) instead; Steam is not a
prerequisite for that store.

```bash
dotnet publish UnchainedLauncher.CLI -c Release -r linux-x64 --self-contained true -o out-cli-linux
./out-cli-linux/UnchainedLauncher.CLI server \
  --chivalry2-path "$HOME/.local/share/Steam/steamapps/common/Chivalry 2" \
  --platform Steam \
  --saved-dir-suffix MyServer \
  --name 'My Unchained Server' \
  --starting-map FFA_Courtyard
```

Linux publication produces one executable, including the .NET runtime and Windows
helper; .NET extracts its contents automatically at startup. Ordinary Linux builds
copy the helper beside the CLI. Do not run the Windows CLI under Proton. Press `Q`
or Ctrl+C in the native terminal to stop a server. Client launches leave the game
running independently.

Discovery uses Steam's `libraryfolders.vdf` and game app manifest to locate the
game library and its `steamapps/compatdata/1824220` directory. Tool selection
honors `STEAM_COMPAT_TOOL_PATHS` or Steam's configured compatibility-tool name.
Ambiguous or unknown tools require an explicit `--proton-path`; the CLI does not
silently pick a different installed Proton version.

Overrides for nonstandard installations:

- `--proton-path`: Proton executable or its containing directory.
- `--compat-data-path`: compatibility-data directory **containing `pfx`**, not
  `pfx` itself. Also honors `STEAM_COMPAT_DATA_PATH` and standard `WINEPREFIX`
  pointing to a Proton `pfx` directory.
- `--steam-path`: Steam client directory, also read from
  `STEAM_COMPAT_CLIENT_INSTALL_PATH`. Standard and Flatpak Steam locations are searched.
- `--proton-helper`: helper executable for development; published builds locate
  their bundled helper automatically.

The same settings are available under root-level `proton` in YAML:

```yaml
proton:
  path: /path/to/Proton/proton
  compat-data-path: /path/to/steamapps/compatdata/1824220
  steam-path: /path/to/Steam
```

YAML paths resolve relative to the configuration file. Wine's known-folder API
provides `LocalAppData`; `winepath` translates paths between the native host and
the selected prefix. The CLI never assumes a Windows username or constructs
`drive_c/users/steamuser` paths. Profile paks, plugins, INI files, and caches use
the same `Saved_<suffix>` layout as Windows inside that prefix.

On Linux, selected DLLs are injected directly from content-addressed cache paths
outside `Plugins`; inventories remain per-profile. Switching versions or
deselecting a DLL does not delete old installed files or cache objects. Only the
selected descriptors are injected (unless unsafe loading is explicitly enabled).
Windows retains managed installation and handle-protected archival.

DLLs are registry-validated natively, then hash/size-checked again and held open
by the helper before injection. Per-launch IPC is private and never puts game
credentials on the helper's command line. The native monitor uses the game's
Unix PID for resource metrics and the helper for exit status. Cancellation stops
only the owned game, never the prefix-wide wineserver. If a server's CLI dies
unexpectedly, the helper stops its game after the heartbeat expires.

For Linux Epic/Heroic launches, see below. macOS game launching is unsupported.
Registry queries remain portable and do not require Proton.

### Epic Games Store and Heroic on Linux

Both `server` and `client` accept `--platform EGS` (`EpicGamesStore` is equivalent).
Use the Heroic-installed game directory for `--chivalry2-path`; the CLI does not
download the game, sign in to Epic, or select a Heroic account.

1. Install and launch the game through Heroic once using a **Proton** runner.
   This initializes its compatibility-data directory containing `pfx/system.reg`.
   Wine-only/Wine-GE prefixes are not supported by the CLI's Proton backend.
2. Supply fresh Epic launch/authentication arguments from the same Heroic/Legendary
   login session on each launch. Pass them as individual arguments after `--`.
   Selecting `EGS` alone does not authenticate; missing or expired credentials can
   leave the game at Frontend without a working server.
3. Launch the native CLI with `--platform EGS`. When the installed game path
   matches Heroic's `Peppermint` entry, the CLI reuses its selected Proton executable
   and prefix. Discovery reads native `$XDG_CONFIG_HOME/heroic` (default
   `~/.config/heroic`) and Flatpak
   `~/.var/app/com.heroicgameslauncher.hgl/config/heroic` settings.

For custom locations, stale Heroic settings, or a separate initialized server
prefix, override both `--proton-path` and `--compat-data-path`. The latter is the
directory **containing `pfx`**, not `pfx` itself. `WINEPREFIX` pointing at that
`pfx` also works. Explicit flags and Proton environment overrides take precedence
over Heroic settings. Do not substitute an uninitialized directory or a different
runner into an active prefix.

Steam installation, Steam login, and Steam game manifests are **not required for
EGS**. The CLI does not assign Chivalry 2's Steam app ID to Epic launches or choose
Epic's runner from Steam's game settings. An explicitly selected Proton build may
still require its normal host/runtime dependencies. Both stores use the same
plugin injection, structured capture, `--attach-stdio`, and `--no-spawn-console`
paths.

If using Legendary to obtain authentication, use Heroic's configured login
directory rather than a different Legendary account. For example, after installing
a compatible native `legendary` command and signing in through the same account:

```bash
# Flatpak Heroic example; native Heroic uses ~/.config/heroic/legendaryConfig/legendary.
export LEGENDARY_CONFIG_PATH="$HOME/.var/app/com.heroicgameslauncher.hgl/config/heroic/legendaryConfig/legendary"
```

This Python example obtains fresh arguments and forwards the returned list without
shell splitting or printing credentials. Replace the paths and retain your normal
server options; use `client` instead of `server` for a client launch.

```python
import json
import subprocess

reply = subprocess.run(
    ["legendary", "launch", "Peppermint", "--json"],
    check=True, capture_output=True, text=True,
)
auth = json.loads(reply.stdout)["egl_parameters"]
if not isinstance(auth, list) or not all(isinstance(arg, str) for arg in auth):
    raise SystemExit("Expected a Legendary egl_parameters argument list")
subprocess.run([
    "/path/to/UnchainedLauncher.CLI", "server",
    "--chivalry2-path", "/path/to/Heroic/Chivalry2",
    "--platform", "EGS", "--", *auth,
], check=True)
```

Authentication is intentionally external: this does not store credentials or add
an Epic login client. Do not save launch tokens in YAML, shell history, screenshots,
or shared logs; game arguments can still be visible to local process inspection.

### Forwarded plugin console

To use standard output instead of plugin IPC, add **`--attach-stdio`** to the
`server` command, or set `server.attach-stdio: true` in YAML. This passes the plugin
flag before initialization and captures stdout/stderr into the same TUI/plain
handler without opening a separate console. The flag explicitly wins over IPC.
To suppress plugin console creation without attaching stdio or changing capture,
use `--no-spawn-console` or `server.no-spawn-console: true` in YAML. It can be
combined with `--attach-stdio`; configured file logging is unaffected.
Windows uses private inherited pipes; Linux uses Proton `runinprefix` so the
Steam.exe wrapper cannot discard those streams. Captured server stdin is EOF,
not the TUI's keyboard. Standard-stream writes can experience pipe backpressure;
the default IPC route retains its nonblocking plugin queue.

Windows and Linux server launches provide a private inherited anonymous pipe to compatible
plugins through `UNCHAINED_CONSOLE_HANDLE` in the game's environment. No CLI flags or logging YAML overrides
are needed. The plugin forwards its console records and banner directly, without
opening its own console. Windows passes a private Unicode environment block to
the suspended game with an explicit handle whitelist; on Linux, the CLI's bundled Windows helper
creates the game pipe and relays only its records through reserved helper stdout
using Proton `runinprefix`. Game stdout/stderr are drained separately and discarded
in structured mode. There is no socket listener, token, or handshake. Neither backend
changes the launcher's environment. Older plugins keep their existing console behavior.

The helper is supplied by UnchainedLauncher, not Proton. Its request/heartbeat
protocol is private and version-coupled to the CLI; deploy the matching bundle.
Alternate launcher authors: see the plugin's
[Windows ownership recipe and Linux helper invocation](https://github.com/Chiv2-Community/UnchainedPlugin/blob/feature/console-forwarding/docs/console-forwarding.md).

Press **L** to switch between the dashboard and full console, **Page Up/Page Down**
to scroll, and **End** to resume following new output. Each server has its own
512-line history; the dashboard also shows a short preview. Plain/no-TUI mode
prints received lines. Output is currently plain text; level metadata is carried
by the protocol but is not yet used for TUI colors.

Queues, records, and JSON frames are bounded. Invalid protocol frames are rejected;
terminal controls are neutralized. Disconnects and queue loss are reported in
the pane. A slow or missing receiver never blocks the game's logging thread;
configured file logging continues. This forwards plugin console output, not
arbitrary UE/Proton stdout, and does not provide interactive console commands.

UE logging switches and custom plugin logging configuration are not added by
the launcher. Chivalry 2's shipping build does not provide UE log output.

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
UnchainedLauncher.CLI mod-query get organization/mod-id --config-path ./registry-query.yaml
```

The official registry is always included. Repeat `--add-registry` to include
GitHub, local, or Core JSON-config registries, using the same formats as launch
commands. For queries, `--config-path` reads root `mod-setup.registries` from a
query or client YAML file instead of the `--add-registry` arguments. It does not
select or merge the per-server registries in server YAML. YAML-relative paths resolve
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
server:
  saved-dir-suffix: MyServer
  mod-setup:
    registries: [] # Official registry remains enabled, even with an empty list.
    mods: []       # organization/mod-id:exact-release-tag
    dlls: []       # Optional dll_db coordinates: organization/id:version.
  name: My Server
  description: My server description
  game-port: 7777
  ping-port: 3075
  query-port: 7071
  rcon-port: 9001
  starting-map: FFA_Courtyard
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
`allow-unstable-plugin` (default false), and `game-args` (list of strings).
The root `no-tui` option (default false) is server-only.
`server.game-args` or `client.game-args` is appended to the
common list only for the matching command. The client schema currently supports
`game-args`; client installation, platform, plugin, mod, and saved-profile options remain at the root.
For servers, `mod-setup` and `saved-dir-suffix` belong inside `server` or each
`servers` entry, never at the root. A single unnamed server defaults to `UnchainedCLI`.

The server section supports name, description/MOTD, password, `starting-map`, local IP,
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
dedicated launcher flags; configure them in the selected saved profile or `server.ini`.
Use `starting-map` / `--starting-map`; the former `map` / `--map` names are rejected.
Before a server launch, the CLI initializes the selected profile's `Game.ini` with
`bSavedHasAgreedToTOS=True` and `FirstLoadCompleted=True`, matching the GUI's startup
defaults so first-run screens do not block unattended startup. Other existing INI
settings are preserved. Client launches do not initialize or rewrite INI files.
Chivalry removes hyphens from the suffix in its `Saved_<suffix>` directory;
the CLI initializes that directory. Plugin log paths retain the original suffix.

### Map rotation and profiles

```yaml
map-profiles:
  courtyard:
    map-name: FFA_Courtyard

servers:
  first:
    map-rotation:
      - courtyard
      - map-name: FFA_Wardenglade
  second:
    game-port: 7778
    ping-port: 3076
    query-port: 7072
    rcon-port: 9002
    map-rotation:
      - courtyard
      - map-name: FFA_Bazaar
```

- Put `map-rotation` inside `server` or each `servers` entry.
- Entries are inline `{map-name: ...}` objects or exact, case-sensitive keys from root `map-profiles`.
- A bare string is a profile reference, not a map name.
- Order and repeated entries are preserved.
- The first entry is the default starting map.
- Set `starting-map` to a rotation map-name to start at its first occurrence, matching case-insensitively.
- Map names accept short names or package paths, without travel options.
- An explicit rotation must contain at least one entry.
- Rotation setup replaces only `MapList` and `MapListIndex` in the server's saved `Game.ini`.
- Omitting `map-rotation` preserves the profile's existing rotation and the normal `starting-map` default.
- `MapList` is forbidden in `ini`, including typed and `raw` forms.
- `MapListIndex` cannot be set through `ini` when a rotation is configured.
- Profile and inline map objects currently accept only `map-name`.
- Per-map INI and actors remain deferred to [#154](https://github.com/Chiv2-Community/UnchainedLauncher/issues/154) and [#155](https://github.com/Chiv2-Community/UnchainedLauncher/issues/155).
- Server-level `mod-actors` still apply to every map.

### INI overrides

Nest `ini` under `server` or a named `servers` entry. Its shape is
`file category -> section -> field -> value`:

```yaml
server:
  ini:
    Game:
      /Script/TBL.TBLGameMode:
        BotBackfillEnabled: true
        ClassLimits:
          - Class: Poleman
            ClassLimitPercent: 10
          - Class: Archer
            ClassLimitPercent: 0
        SomeUnknownOption:
          type: String
          value: example
        SomeOtherUnknownOption:
          type: raw
          value: SomeStructuredThing(foo, bar, 10, baz, false)
```

- Known fields infer their type from Core's INI models. They also accept
  `{value: ...}` or `{type: Boolean, value: true}`. An explicit known-field type
  must match its definition, unless it is `raw`.
- Unknown fields require both `type` and `value`; a bare value fails with instructions.
- Types are case-insensitive: `String`, `Boolean` (`Bool`), `Int32` (`Int`),
  `Single` (`Float`), `Double`, `Decimal`, `CharacterClass`, `ClassLimit`,
  `AutoBalance`, and `CallbackList`. Append `[]` for an array, for example
  `{type: 'String[]', value: [one, two]}`. Quote array type names in YAML flow mappings.
- Records accept named members or positional lists in constructor order:
  `ClassLimit` uses `[Class, ClassLimitPercent]`; `AutoBalance` uses
  `[MinNumPlayers, MaxNumPlayers, AllowedNumPlayersDifference]`.
  `CallbackList` accepts a list of strings. Numbers use invariant decimal notation;
  enum values must be named members. Values are not rescaled (10 stays 10).
- Typed values patch only selected keys in the server's saved-profile INI files.
  Arrays replace that field's existing entries; `[]` explicitly clears it.
  Other keys and sections are preserved. These changes persist after the server exits;
  omitting a field on a later launch does not undo it.
- Use `map-rotation`, not an INI `MapList` override, to configure maps.
- `raw` requires a string and bypasses type parsing and validation. Its exact value
  is escaped for transport in one `-ini:` argument, not written to the profile.
  It cannot override an array: Unreal's command-line INI overrides do not support arrays.
- File categories have no path or `.ini` extension (`Game`, `Engine`, etc.).
  All overrides are validated before preparation. Explicit INI `ServerName` and
  startup-screen fields take precedence over the launcher's generated defaults.
  Forwarded `game-args` remain explicit user arguments; avoid conflicting INI overrides there.

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

### Multiple servers

Use a `servers` dictionary instead of the single `server` block:

```yaml
chiv2-path: 'C:\Games\Chivalry 2'
platform: Steam
servers:
  courtyard:
    name: Courtyard FFA
    starting-map: FFA_Courtyard
    game-port: 7777
    ping-port: 3075
    query-port: 7071
    rcon-port: 9001
  arena:
    name: Arena FFA
    starting-map: FFA_Courtyard
    game-port: 7778
    ping-port: 3076
    query-port: 7072
    rcon-port: 9002
```

Run `UnchainedLauncher.CLI.exe server --config-path servers.yaml`.
See [the multi-server example](examples/servers.yaml). The existing `server` schema
and individual CLI launch arguments still launch one server.

- Keys are tab IDs: unique, case-insensitive letters, digits, underscores, and hyphens.
- Each entry accepts the same fields as `server`, including its own `mod-setup`,
  `ini`, actors, arguments, password, and Discord settings. Installation, platform,
  plugin-update policy, and common arguments remain shared at the root.
  Registries and pak/DLL selections are independent; verified bytes share the cache.
- Profiles default to the server key (`courtyard`, `arena`). Set `saved-dir-suffix` inside an
  entry to override it. Profile names must be distinct, ignoring case and hyphens.
- Active paks live under `%LOCALAPPDATA%/Chivalry 2/Saved_<suffix>/Paks`;
  DLLs remain under `%LOCALAPPDATA%/Chivalry 2/Saved_<suffix>/unchained/plugins`.
  Hyphens are removed from the saved suffix, matching the game. This also applies to
  clients; the default client suffix is `unchained`. A per-profile lock prevents concurrent
  CLI processes from using the same profile. Plugin injection never scans another
  instance's directory, including with the unsafe flag.
- The game discovers profile paks without an additional plugin argument.
  Managed filenames use `<mod>_<order>_P.pak`, using Unreal's native patch priority
  instead of alphabetical prefixes. Orders start at zero in dependency order;
  higher numbers override lower numbers, so dependents override their dependencies
  and all managed mods override vanilla assets. Collision counters stay before
  the order suffix, for example `SomeMod (1)_2_P.pak`.
  Signatures use the installation's base
  signature as a read-only template; no base pak is copied or modified.
- Relaunch with the desired `--mod` selections to install them into `Paks`.
  Old files under `unchained/paks` are not migrated or deleted automatically.
- Profile-local discovery does not suppress game-wide paks
  already present in `TBL/Content/Paks`. Existing global mods may still affect all
  instances. The launcher does not move or delete them during instance setup.
- Shared caches live under `%LOCALAPPDATA%/Chivalry 2/unchained/cache/{paks,dlls}`.
  Pak objects are keyed by SHA-512 and downloaded once under a per-object lock.
  Profiles use hard links when supported, or copies from cache across volumes or
  unsupported filesystems. Cache objects and linked paks must not be edited in place.
  Signatures stay profile-local. Windows may prevent removal or reordering of a
  linked pak while another process holds that file open; stop affected servers first.
- Assign all four ports to each server. Every port must be distinct across the
  entire group, even when local IPs differ. Defaults are not automatically offset.
- All entries are validated before launch. Startup is sequential because Core
  preparation changes the installation and working directory. A startup failure
  stops previously launched servers; it does not leave a partial group running.
- After startup, one server exiting does not stop the others. Its tab retains the
  exit code and last metrics. Once all exit, the CLI returns the first nonzero exit
  code in dictionary order, or zero if all succeeded. Q/Ctrl+C stops the entire group.

Do not mix `server` and `servers` in one document. The client command and registry
queries ignore the inactive `servers` block.

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

Core records physical managed-pak ownership in each active pak directory's
`.unchained_managed_paks.json`, updated after preparation, including partial failures.
That inventory is shared by hosts using the same profile; it does not authorize
changes to another profile or to legacy installation-wide paks.
The CLI saves its metadata in the profile's `unchained/cli_mod_manager.json`, but
never overwrites GUI configuration or reuses the GUI's enabled-mod selection.
Each launch reconciles the installed managed set with that host's selected releases;
genuinely unmanaged user paks are preserved.
Core's installation pipeline changes the installed managed pak set and signatures.
The GUI installs paks in each launch profile's `Saved_<suffix>/Paks` directory,
using a shared GUI mod selection and shared `Saved_unchained/unchained/plugins` DLLs.
CLI mod and DLL selections remain request-specific. CLI profiles
need separate saved suffixes/ports; CLI startup
preparation remains sequential because some Core operations change CWD and metadata.

## DLL compatibility behavior

GUI and CLI share DLL registry validation, managed storage, caching, and injection.
The CLI selects UnchainedPlugin from `dll_db` for each launch; GUI selections stay
in the Plugin Manager's saved profile.
`--dll organization/id:version` (or each server's YAML `mod-setup.dlls`) additionally selects
registry releases in CLI launches. Plugin dependencies are resolved before their
dependents; target mismatches, missing releases, cycles, and conflicting versions
fail preparation. Pak dependencies enter the shared mod manager.

Every selected DLL must match the registry's SHA-256 and size. Downloads are
verified before installation. Active DLLs use
`plugins/Managed/<organization>/<repository>/<dll_file_name>` under the selected
profile's `unchained` directory; inactive registered versions use the shared
`unchained/cache/dlls/<sha256>/<dll_file_name>`. Switching versions restores
validated cached files without downloading again.
Active DLLs use hard links to validated cached bytes where supported, with an atomic
copy fallback across volumes or on unsupported filesystems. Concurrent preparations
serialize downloads per cache object. Never edit cached or linked DLLs in place.
If Windows blocks removal of a DLL linked to a running peer, stop that peer before
changing versions; preparation fails rather than modifying shared bytes.
Registered files in historical `Managed` hash directories or `.dll-managed`
directories migrate after validation.
Loose DLLs, unknown filenames, and unregistered versions stay untouched and are
not loaded in safe mode. CLI selections never overwrite the GUI plugin profile.
`plugins/dll_manager.json` records the last successful CLI selection.
Safe injection uses only that launch's verified allowlist, not the directory or
the saved inventory. The unsafe load-all option retains other DLLs for injection.
`--no-plugin-update` forbids downloads but permits restoring cached versions.
It does not retain the installed version; pin the desired registered tag with
`--dll Chiv2-Community/UnchainedPlugin:<tag>` and install or cache every required DLL.
Only explicitly pinned prereleases (or `--allow-unstable-plugin` for automatic
selection) are eligible; automatic selection otherwise uses the latest stable.

Other DLLs in the selected profile's `unchained/plugins` directory are not enumerated
for injection unless this exact command-line flag is explicitly supplied. Legacy
installation-wide plugin directories are left untouched and are never scanned:

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
| `--saved-dir-suffix` | client: `unchained`; server: `UnchainedCLI`; letters, digits, `_`, and `-` only |
| `--name`, `--description`, `--password` | Name, description/MOTD, optional password |
| `--starting-map` | `FFA_Courtyard` |
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

- Server name, PID, process uptime, and restart count (zero; no automatic restart).
- Game, query, beacon, and RCON ports, local IP, and configured mod actors.
- CPU use as a percentage of total host logical CPU capacity (0–100%).
- Private process memory and observed peak memory in MiB, plus handle count.
- A2S player/max-player counts, current map, game type, and query availability.
- Bounded rolling CPU/memory/player sparklines and an uptime-state strip
  (up to 12 hours, sampled every ten seconds, scaled to the terminal width).
- A bounded timeline of launch, map/player changes, and query outages/recovery.

Metrics refresh approximately once per second. During startup or query failure,
player count and map are **unknown**, not zero; `?` marks missing chart samples.
The timeline represents observed changes, not per-player join/leave identities or
the full game log. The uptime strip shows `S` until the first successful query,
then `U` while the process runs, including query outages, matching the GUI.
Network throughput and RCON controls are not included yet.

Press **Q** in the TUI or **Ctrl+C** to stop. **Page Up**/**Page Down** scroll wrapped content
and **Home** returns to the top; narrow terminals do not discard long mod lists.
With multiple servers, **Left**/**Right** select tabs, wrapping at either end.
Click a visible tab with the primary mouse button in terminals supporting SGR mouse
reports (including xterm-compatible terminals and Windows Terminal). Keyboard
navigation remains available when mouse reporting is unsupported. Mouse capture
and Windows console modes are restored on exit.
Redirected input/output and dumb terminals
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
Proton tests cover Steam metadata, library symlinks, tool/prefix overrides, private
IPC directories, argument boundaries, and separate game/supervisor metrics.
Windows and Linux CI test and publish the CLI separately from the GUI. Linux
artifacts include the Windows Proton helper; CI does not launch the actual game.
