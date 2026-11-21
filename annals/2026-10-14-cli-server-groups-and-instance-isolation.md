# CLI server groups and instance isolation

- Date: 2026-10-14
- Status: implemented
- Scope: CLI server hosting, pak/DLL isolation, and DLL-release resolution
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `server-cli`
- Recorded HEAD: `dacbaab8694df06ab35f16c3591434796bce089d`
- Nearest tag: `v1.0.0-RC4-63-gdacbaab` (no tag points at HEAD)
- Implementation commits: [`0b039df`](https://github.com/Chiv2-Community/UnchainedLauncher/commit/0b039df2226e7c2da56d41c0fb135af3a19cecdd), [`9283596`](https://github.com/Chiv2-Community/UnchainedLauncher/commit/9283596e2e7b99fb380370bca1868670512fc19f), [`dacbaab`](https://github.com/Chiv2-Community/UnchainedLauncher/commit/dacbaab8694df06ab35f16c3591434796bce089d)
- Related PR: [#150](https://github.com/Chiv2-Community/UnchainedLauncher/pull/150)
- Source: completion checkpoint from the originating work session; source response ID was not supplied. Git commits and current sources were independently inspected by GPT-5.6 Terra.

## Decision

Support Compose-style multi-server CLI configurations through a root `servers` dictionary. Share root configuration while assigning each server a distinct profile and ports, launch instances sequentially, and monitor them in a tabbed terminal dashboard.

Give every server a private pak/plugin working area at:

`<installation>/.unchained/servers/Server-<saved-dir-suffix>/`

The launcher owns `--additional-pak-dir`, passes the instance `Paks` directory, and rejects user-supplied duplicates. It leaves global `TBL/Content/Paks` unchanged.

Resolve all explicit DLL pins before traversing plugin dependencies so the selected release does not depend on request order.

## Context and evidence

The requested feature was multi-server YAML hosting with keyboard/mouse TUI tabs and isolated server mod state. Current sources show group validation for case-insensitive server IDs, profile suffixes, and all server ports (`UnchainedLauncher.CLI/ServerGroupOptions.cs`); sequential launch because core preparation changes CWD and installation files (`ServerGroupRunner.cs`); and launcher-owned additional pak arguments (`CliLaunchOptions.cs`).

Commit `0b039df` introduced group configuration, runner, tabs, terminal mouse support, example configuration, and tests. Commit `dacbaab` added private instance paths, leases, per-instance preparation, and argument-boundary coverage. Commit `9283596` added pre-traversal pin collection and tests for explicit prerelease pins in either request order (`UnchainedLauncher.Core/Services/Mods/DllLaunchSession.cs`). The PR head ref resolves to `dacbaab`.

## Alternatives considered

- **Parallel server startup:** rejected because preparation mutates process-wide CWD and installation files; the runner explicitly starts processes sequentially.
- **User-managed `--additional-pak-dir`:** rejected to prevent an instance from bypassing its assigned pak directory or supplying duplicate arguments.
- **Moving or deleting global paks:** rejected. Existing global paks may still affect every instance; the implementation only adds an instance mount.
- **Resolving dependencies before explicit requested pins:** rejected after review found request-order-dependent DLL selection.

## Rationale and confidence

The isolation path provides separate paks, plugins, inventories, caches, and a lifetime lease per server while preserving existing installation content. Group-level uniqueness checks prevent profile and port collisions. Sequential startup avoids known shared-state hazards. Pin-first DLL selection makes explicit requests deterministic.

Rationale is high confidence where expressed in code and commit history. The originating checkpoint attributes the review finding to comment `4278970328`, fixed by `9283596`; that remote review metadata was not independently fetched.

## Consequences and follow-up

- Legacy single-server configuration remains supported; grouped servers use shared root settings plus per-server overrides.
- Interactive TUI supports Left/Right tab selection and SGR mouse clicks; `no-tui` remains available.
- Natural per-server exits are monitored and exit aggregation returns the first nonzero exit code; group cancellation stops all processes.
- Native support for `--additional-pak-dir` in UnchainedPlugin is an external prerequisite. Until released and tested, live mounting is unvalidated.
- Global `TBL/Content/Paks` are not suppressed by the additional mount and can still affect all instances.

The checkpoint reports 365 CLI and 135 portable Core tests in Debug/Release, a Windows GUI Release build, win-x64 CLI publication, and passing Windows CI run `38682323092`. Those run results were not independently reproduced for this annal. Native game launch, DLL injection, and additional-pak mounting were also not exercised because compatible native plugin support was unavailable.

## Reconsider when

Revisit the sequential-start constraint if core preparation becomes isolated from CWD and installation mutation. Revisit the pak strategy if the game or plugin gains a way to suppress global paks, or if the native plugin contract differs from the assumed `--additional-pak-dir` behavior.

## Open historical gaps

- No source conversation response ID was available for idempotent transcript provenance.
- The exact reviewer discussion and CI run were not independently retrievable during recording.
- No live Windows validation exists for the new native mount argument.
