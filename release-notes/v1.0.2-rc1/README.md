# Unchained Launcher v1.0.2-rc1

Prerelease for testing; v1.0.1 remains the latest stable release.

## Unmanaged mods and server configuration

- Added **Scan unmanaged mods**, actor counts, and actor checkboxes showing version and source pak.
- Fixed scanned mod actors missing from server configuration and duplicate rows for the same actor in the same pak; actors from different paks remain distinct.
- Fixed replacement paks being hidden by stale managed-mod metadata or disabled-version selections. Scanned actors and maps now reflect validated pak contents.
- Hide actors only when `bShowInGUI` is explicitly false; retain client-only and frontend exclusions.
- Refresh scan results and actor/map catalogues after pak deletion, rename, or removal. Clear confirmed-deleted selections while preserving other unavailable saved selections.
- Always open the scan folder picker at the game's `TBL/Content/Paks` directory.

## Scanning and performance

- Reuse unchanged pak hashes, detected versions, and full scan results across launcher sessions. Launch and server restart wait for current catalogue work without forcing rehashing.
- Preserve cached scans when unrelated paks change; invalidate scans affected by changed files, references, or shadowed assets, and prune removed entries.
- Improve mod-version detection using versioned mod markers, with an ArgonSDK mod-actor fallback when markers provide no version.
- Show the current pak, file progress, and overall pak count during uncached version scans. Close progress before confirmation dialogs and on failure.
- Add readable hash-start, completion, cache-hit, and timing logs.
- Exclude vanilla game paks from mod scans. Attribute manifest assets, hashes, and actor references only to the pak that physically contains them; missing or empty `ModActors` lists add no actors.
- Sort manifest inventories and marker references consistently, and fix parallel scan cleanup losing entries between runs.
- Report scan failures without discarding the previous complete catalogue.

## Installer

- Prevent Back navigation before the first page and block navigation during page loading.
- Update Continue correctly after browsing, rescanning, or changing selected installations; reject empty selections.
- Discover installations without modifying UI-bound lists from a worker thread. Preserve valid results when another store's metadata cannot be read.
- Report failed and partially failed installations accurately and offer Back/retry instead of claiming success.
- Refresh the recovery Back-button label and guard empty launch candidates; offer to launch only successfully installed targets.

## Launching and appearance

- Keep Unchained running until the Chivalry 2 client or server launched through Epic Games exits, then close it without retrying unexpected exits. Steam behavior is unchanged.
- Improve progress-bar and disabled-control contrast, progress alignment, and the settings divider in dark mode.

## Maintenance and upgrade notes

- Add installer workflow regression tests to CI and wait for background scans before upgrade-test cleanup.
- Remove tracked annals and ignore their directory; correct older release-note wording.
- Older full-scan cache records refresh once. Reusable hashes and standalone version reads are retained.
- Metadata-based cache reuse cannot detect edits that preserve both file size and modification time. Mods without supported version metadata may still show Unknown.

Included PRs: [#186](https://github.com/Chiv2-Community/UnchainedLauncher/pull/186), [#187](https://github.com/Chiv2-Community/UnchainedLauncher/pull/187), [#188](https://github.com/Chiv2-Community/UnchainedLauncher/pull/188), [#189](https://github.com/Chiv2-Community/UnchainedLauncher/pull/189), [#191](https://github.com/Chiv2-Community/UnchainedLauncher/pull/191), [#192](https://github.com/Chiv2-Community/UnchainedLauncher/pull/192), [#193](https://github.com/Chiv2-Community/UnchainedLauncher/pull/193), [#194](https://github.com/Chiv2-Community/UnchainedLauncher/pull/194), [#195](https://github.com/Chiv2-Community/UnchainedLauncher/pull/195).

[Changes since v1.0.1](https://github.com/Chiv2-Community/UnchainedLauncher/compare/v1.0.1...v1.0.2-rc1)
