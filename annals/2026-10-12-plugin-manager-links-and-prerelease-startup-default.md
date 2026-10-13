# Plugin Manager links and prerelease startup default

- Date: 2026-10-12
- Status: accepted
- Scope: Plugin Manager navigation and unstable-plugin-release startup policy
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/dll-validation`
- Git provenance: `aa8642601f9ed490ce0462e8a492ab989c0b0140` (`v1.0.0-RC4`)
- Implementation state: uncommitted working-tree changes pinned to the Git provenance above
- Authoring agent: Annalist (GPT-5.6 Terra)
- Source: supplied conversation checkpoint; source and unit-test artifacts inspected locally; associated session `ses_1791840326_33b8d7`. No response ID was available.
- Related records: [Plugin Manager workflow and update boundaries](2026-10-12-plugin-manager-workflow-and-update-boundaries.md); [DLL registry package metadata and target filtering](2026-10-12-dll-registry-package-metadata-and-target-filtering.md)

## Decision summary

Make Plugin Manager's repository and displayed-release provenance directly
accessible in the UI, and default **Enable Unstable Plugin Releases** to enabled
for a prerelease launcher build. The user can still turn that control off during
the session. Stable builds retain a saved preference only when it was written by
the same launcher version, ignoring stale preferences from another version.

## Context and evidence

The Plugin Manager already selects registry-approved releases but did not expose
direct links equivalent to Mod Manager's GitHub navigation. The supplied
checkpoint requested repository and release-note links plus an electric-plug tab
icon. `MainWindow.xaml` now provides the `🔌` tab icon, makes the plugin name a
repository-link command, and adds an original-release-link command within the
release-notes expander.

`DllPluginVM` derives the repository URL from the registry identifier and derives
the release URL from the release currently displayed by the card. It URL-escapes
the original tag, rather than reconstructing a tag from normalized semantic
version data. This preserves tags such as `v2.0.0-RC1+build.abc`. Link failures
are reported through the existing error path and do not alter selection.

`LauncherSettings.ShouldAllowUnstablePluginReleases` returns true whenever the
running `SemVersion` is prerelease. Otherwise, it accepts `true` only if the
stored `LastLaunchVersion` equals the running version after build metadata is
removed. `SettingsCodec` uses that method when creating `SettingsVM`; saving
records the current launcher version. Unit tests cover prerelease override,
same-version stable preference retention, cross-version invalidation, build
metadata equivalence, escaped original tags, hidden enabled prereleases, and
browser-error handling.

## Options considered

### Preserve a saved false preference on prerelease builds

Rejected by the supplied request. A prerelease launcher is intended to exercise
unstable plugin releases, so a stored false value must not hide them at startup.
The control remains available for a user to opt out in the current session.

### Use normalized semantic versions for GitHub release links

Rejected. Normalization can discard the exact repository tag and produce a link
that does not identify the displayed upstream release. Using the original tag
with URL escaping preserves release provenance and supports tag characters such
as `+`.

### Add a separate navigation or prerelease-display setting

Rejected. The page uses the existing Plugin Manager setting and ordinary
commands, avoiding another persisted preference or duplicate release state.

## Consequences and follow-up

Users can inspect source and upstream notes/assets for the exact release shown,
including an enabled prerelease that is currently hidden from the version list.
Prerelease launcher builds can discover unstable candidates immediately; users
who turn the setting off can retain that choice only within that process unless a
later save/current-version policy makes it applicable.

The implementation and its reported 27 portable GUI tests in Debug and Release,
plus the Windows Release GUI build, remain uncommitted and were not rerun in this
annal review. The latest GUI output was built but not republished to
`/tmp/opencode/dll-validation-gui-local`, whose open copy predates these edits.
No live GUI or game-injection verification is available.

Reconsider this policy if prerelease launcher builds must honor persisted opt-out
across restarts, if a registry's release tags are not valid GitHub tag paths, or
if users need release links for releases no longer represented by the current
registry metadata.

## Reconciliation

This record supersedes only the narrower prerelease-eligibility statements in
the related records: a persisted unstable-release preference is no longer the
sole startup determinant when the launcher itself is prerelease. The registry,
target-filtering, manual-update, and persisted-settings decisions otherwise
remain accepted.

## Historical gaps

No issue, pull request, original request transcript, response ID, independently
retained test log, or live GUI capture was available. Reported build/test results
are checkpoint and session-memory testimony corroborated by the current tests,
not independently reproduced here.
