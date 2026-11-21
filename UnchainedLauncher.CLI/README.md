# Launcher CLI

Launch a Chivalry 2 client or server without WPF. Windows launches directly;
Linux uses Proton and a bundled Windows helper. Both reuse Core's mod,
plugin, signature and launch services.

This branch supports command-line launch options. YAML launch configuration,
multi-server groups, map profiles and typed INI overrides are provided by the
separate `feature/server-yaml` branch.

## Build

Requires the .NET 10 SDK and an existing game installation containing `TBL`.
Run commands from the repository root.

```powershell
dotnet build UnchainedLauncher.CLI
dotnet publish UnchainedLauncher.CLI -c Release -r win-x64 -o out-cli
```

Linux publishing also requires Rust and `uvx`/`cargo-xwin` to build the Windows
helper. The MSBuild target builds and bundles that helper.

```bash
dotnet publish UnchainedLauncher.CLI -c Release -r linux-x64 -o out-cli
```

## Launch

Replace paths and release tags with registered versions for your installation.
Installing a pak does not enable its server blueprint actors; use `--server-mod`.

```powershell
.\out-cli\UnchainedLauncher.CLI.exe client `
  --chivalry2-path "C:\Games\Chivalry 2" --platform Steam `
  --dll Chiv2-Community/UnchainedPlugin:v1.0.0-RC3 `
  --mod Chiv2-Community/Unchained-Mods:v0.1.0

.\out-cli\UnchainedLauncher.CLI.exe server `
  --chivalry2-path "C:\Games\Chivalry 2" --platform Steam `
  --saved-dir-suffix MyServer --name "My Server" `
  --starting-map FFA_Courtyard --no-tui
```

Use `client --help` or `server --help` for all supported options. Launcher
options precede `--`; everything after it is forwarded literally to the game.
Response-file expansion is disabled.

- `--platform`: `Steam`, `EGS` or `EpicGamesStore`.
- Repeat `--mod organization/id:tag`, `--dll organization/id:tag` and
  `--add-registry specification` for additional selections.
- `--no-plugin-update` disables downloads; required registered DLLs must already
  be installed or cached. `--allow-unstable-plugin` permits prerelease updates.
- The CLI detaches after launching a client. It owns a server until that server
  exits or the CLI stops it.

### Linux and Proton

Use `--proton-path`, `--compat-data-path`, `--steam-path` and `--proton-helper`
to override discovery. Paths supplied through flags resolve against the current
directory. A compat-data directory must contain the Wine prefix (`pfx`).

```bash
./out-cli/UnchainedLauncher.CLI server \
  --chivalry2-path "/games/Chivalry 2" --platform Steam \
  --proton-path "/tools/Proton/proton" \
  --compat-data-path "/steamapps/compatdata/1824220" --no-tui
```

Epic installations can use Heroic's selected Proton and prefix. Authenticate
through Heroic/Legendary and forward the current Epic authentication arguments
after `--`; Steam authentication is not a substitute. Do not publish tokens.

The Windows helper creates the game suspended, injects the selected DLLs, then
resumes its main thread. Injection failure terminates the child. The native
CLI and terminal dashboard remain outside Wine.

## Server options

- Identity: `--name`, `--description`, `--password`, `--starting-map`.
- Network: `--local-ip`, `--game-port`, `--ping-port`, `--query-port`, `--rcon-port`.
  The four ports must differ. Open the required ports in your firewall.
- `--register` lists the server in the browser.
- `--headless false` enables rendering and requires `--no-desync-patch`.
- `--no-backend-banlist` disables the default backend banlist.
- `--censor-mode`: `none`, `standard`, `sex` or `zealous`.
- Discord: `--discord-bot-token` plus at least one `--discord-*-channel-id`;
  optional `--discord-admin-role-id` and `--discord-mention-admins`.
- First-run profile settings accept the TOS and mark first load complete,
  avoiding interactive server startup screens.

Servers capture stdout/stderr with `--attach-stdio`. The launcher generates
profile-local `logging.yaml` for the plugin's log4rs protocol when missing.
That logging file is not a launcher configuration. Existing logging edits and
explicit forwarded `--log4rs-yaml` settings are preserved.
`--no-spawn-console` suppresses the plugin console without disabling capture.

The dashboard shows telemetry, bounded console history and shutdown controls.
`--no-tui` uses plain-text monitoring instead. Ctrl+C stops the owned server.

## Registry queries

Queries need no game installation and do not install packages or execute DLLs.
The official registry is always included.

```text
UnchainedLauncher.CLI mod-query list
UnchainedLauncher.CLI mod-query get organization/mod --version v1.0.0
UnchainedLauncher.CLI mod-query versions organization/mod
UnchainedLauncher.CLI dll-query list --add-registry local:./registry
```

Registry specifications:

- `github:owner/repository`
- `local:/path/to/registry`
- `config:/path/to/registry_config.json` (the existing Core registry JSON schema)

Registry files use their own directory as the base for relative local paths.
Exact mod/DLL tags are preserved. Mod and native plugin dependencies are
separate; a selected pak does not implicitly grant permission to load arbitrary
DLLs.

## Storage and safety

Each saved profile has its own paks, plugins, ownership and exclusive lease.
Paks use the game's native `Saved_<suffix>/Paks` directory; suffix normalization
matches the game. Shared SHA-512 pak and SHA-256 DLL caches verify bytes and
publish immutable objects. Hard links are preferred; copy fallback is supported.
Unknown destination files are preserved.

The base game and other profiles remain untouched. Registered DLL selections
are validated before injection; native code still executes in the game.
The hidden unsafe DLL-loading permission must be explicitly passed as a
value-less launcher flag, never through forwarded game arguments.

### Pak recovery

Ambiguous or unmanaged pak files prompt for **Keep / Remove / Cancel** before
launch. The dialogue lists filenames relative to the command-selected profile's
Paks directory, with known release details.

- **Keep** retains unrecognized files and authorizes adoption of recognized
  releases as managed files. Those files can subsequently be replaced or removed
  to match the command's requested mods; Keep does not add `--mod` selections.
- **Remove** authorizes deleting the listed paks and their matching `.sig` files.
- **Cancel** or end of input stops preparation without authorizing recovery.
  Invalid input prompts again; there is no default.

Recovery requires interactive stdin, stdout and stderr. Redirected or unattended
launches cancel rather than accept piped answers. Rerun the same launch command
in a terminal, with the same `--saved-dir-suffix` if supplied, and choose explicitly.
Normal update and bad-hash refresh policies are unchanged.

## Tests and limits

```text
dotnet test UnchainedLauncher.CLI.Tests
```

Tests cover parsing, literal argument boundaries, launch composition, cache
publication, ownership, profile isolation, telemetry and Proton helpers.
Portable tests and cross-builds are not proof of native Windows game behavior.
Windows may require stopping peers before changing linked assets.
