# Unchained Launcher v1.0.2

Stable release containing all v1.0.2-rc1 changes and the dropdown improvements below.

## Appearance and usability

- Search any dropdown by typing; fuzzy matching narrows the options without changing the current selection until you choose a result.
- Alphabetize map dropdowns by their displayed names without changing configured map rotation order.
- Style selected search results for light and dark themes and preserve normal Tab navigation.
- Improve progress-bar and disabled-control contrast, progress alignment, and the settings divider in dark mode.
- Added **Scan unmanaged mods**, actor counts, and actor checkboxes showing version and source pak.
- Show the current mod file, file progress, and overall count during version scans. Close progress before confirmation dialogs and on failure.
- Always open the scan folder picker at the game's `TBL/Content/Paks` directory.
- Fixed scanned mod actors missing from server configuration and duplicate actor rows from the same mod file.
- Refresh available actors and maps after mod files are deleted, renamed, or removed, without dropping unrelated saved selections.
- Keep the previous complete actor/map list available if a scan fails.

## Installation and launching

- Prevent Back navigation before the first page and block navigation during page loading.
- Update Continue correctly after browsing, rescanning, or changing selected installations; reject empty selections.
- Make installation discovery more reliable, including when another store's installation information cannot be read.
- Report failed and partially failed installations accurately and offer Back/retry instead of claiming success.
- Fix the recovery Back-button label and offer to launch only successfully installed targets.
- Keep Unchained running until the Chivalry 2 client or server launched through Epic Games exits, then close it without retrying unexpected exits. Steam behavior is unchanged.

## Scanning and performance

- Reuse unchanged scans across launcher sessions and avoid rescanning unaffected mods when unrelated files change.
- Improve version detection for locally installed mods.
- Fixed replaced mod files being hidden by outdated managed-mod information or disabled-version selections.

## Technical notes

- Validate pak hashes before applying managed/disabled catalogue exclusions. Preserve distinct actor entries from different physical paks.
- Hide actors only when `bShowInGUI` is explicitly false; retain client-only and frontend exclusions.
- Persist pak hashes, detected versions, and full scan results together. Launch and server restart wait for current catalogue work without forcing rehashing.
- Track scan dependencies to invalidate affected references and shadowed assets, and prune removed cache entries.
- Read versions from versioned mod markers, with an ArgonSDK mod-actor fallback when markers provide no version.
- Add readable hash-start, completion, cache-hit, and timing logs.
- Exclude vanilla game paks from mod scans. Attribute manifest assets, hashes, and actor references only to the pak that physically contains them; missing or empty `ModActors` lists add no actors.
- Sort manifest inventories and marker references consistently, and fix parallel scan cleanup losing entries between runs.
- Publish discovered installations on the UI thread and guard empty installation/launch target lists.
- Older full-scan cache records refresh once. Reusable hashes and standalone version reads are retained.
- Metadata-based cache reuse cannot detect edits that preserve both file size and modification time. Mods without supported version metadata may still show Unknown.
- Add installer workflow regression tests to CI and wait for background scans before upgrade-test cleanup.
- Remove tracked annals and ignore their directory; correct older release-note wording.

Included PRs: [#186](https://github.com/Chiv2-Community/UnchainedLauncher/pull/186), [#187](https://github.com/Chiv2-Community/UnchainedLauncher/pull/187), [#188](https://github.com/Chiv2-Community/UnchainedLauncher/pull/188), [#189](https://github.com/Chiv2-Community/UnchainedLauncher/pull/189), [#191](https://github.com/Chiv2-Community/UnchainedLauncher/pull/191), [#192](https://github.com/Chiv2-Community/UnchainedLauncher/pull/192), [#193](https://github.com/Chiv2-Community/UnchainedLauncher/pull/193), [#194](https://github.com/Chiv2-Community/UnchainedLauncher/pull/194), [#195](https://github.com/Chiv2-Community/UnchainedLauncher/pull/195), [#196](https://github.com/Chiv2-Community/UnchainedLauncher/pull/196), [#197](https://github.com/Chiv2-Community/UnchainedLauncher/pull/197).

[Changes since v1.0.1](https://github.com/Chiv2-Community/UnchainedLauncher/compare/v1.0.1...v1.0.2)
