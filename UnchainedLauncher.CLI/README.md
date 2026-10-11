# Server CLI

This project launches an Unchained Chivalry 2 **server**, without the WPF GUI.
It uses Core for mod installation, plugin updates, signature preparation,
process launching, DLL injection, and A2S queries. Client launching is not included.

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
The server is headless (`-nullrhi -unattended -nosound`). The plugin is installed
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

The CLI stores managed-pak metadata in `<installation>\.mod_cache\cli_mod_manager.json`.
On first use it can seed inventory from the existing GUI mod-manager configuration,
but it never overwrites the GUI configuration or reuses its enabled-mod selection.
Core's installation pipeline changes the installed managed pak set and signatures.
Do not prepare a launch concurrently with the GUI or another CLI using the same
installation; multiple running servers sharing that installation must use the
same installed mod set and separate saved suffixes/ports.

## DLL compatibility behavior

Every launch recursively injects **all `.dll` files** under
`<installation>\TBL\Binaries\Win64\Plugins`, preserving the GUI behavior.

`--dll organization/dll-id:version` is accepted as a reserved option and emits a
warning. It does **not** download, filter, select, or pin DLL versions yet.
Registry-backed DLL packages and selection are tracked in
[issue #149](https://github.com/Chiv2-Community/UnchainedLauncher/issues/149).
Only put trusted DLLs in this directory.

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
| `--local-ip` | Optional local server address; also used for A2S monitoring |
| `--register` | Opt in to server-browser registration; off by default |
| `--server-browser-backend` | `https://servers.polehammer.net` |
| `--no-tui` | Periodic plain-text status instead of the dashboard |

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
