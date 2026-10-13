# DLL registry package metadata and target filtering

- Date: 2026-10-12
- Status: accepted
- Scope: native DLL plugin discovery, validation, launch preparation, injection, and GUI presentation
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/dll-validation`
- Git provenance: `aa8642601f9ed490ce0462e8a492ab989c0b0140` (`v1.0.0-RC4`)
- Implementation state: uncommitted working-tree changes pinned to the Git provenance above
- Authoring agent: Annalist (GPT-5.6 Terra)
- Source: supplied conversation checkpoint; uncommitted source and test artifacts inspected locally. No source session or response ID was available in the supplied checkpoint.

## Decision summary

The launcher will treat `dll_db/plugin_list_index.txt` and the package records it
lists as the authoritative native-plugin registry format. It will fall back to
legacy `dll_db/index.json` only when no package list exists. Native-plugin
selection remains user-pinned, while client/server/shared eligibility is applied
for each preparation and injection operation rather than persisted as a separate
selection.

Package parsing supports the current C2ModRegistry and zero-infra-mod-registry
`prepare-v1` schema, including `$schema`, release-specific metadata, target,
asset filename, and typed mod/plugin dependencies. Historical releases in a
metadata-backed package may omit metadata, release date, or filename, but must
still pass SHA-256 and size validation.

The DLL Manager will own the existing persisted plugin-update controls: **Enable
Automatic Plugin Updates and Downloads** and **Enable Unstable Plugin
Releases**. It will not introduce a separate "Show prereleases" setting. When
automatic updates are enabled, launch refreshes registry metadata, offers an
upgrade only for enabled plugins, and then downloads missing selected releases.
When disabled, launch requires an already installed, validated managed DLL. The
unstable-release setting both permits unstable update candidates and controls
their normal visibility without unselecting a previously enabled prerelease.

## Context and evidence

The supplied checkpoint states that the branch was updated for the current
C2ModRegistry/zero-infra package metadata and DLL launch behavior, preserving
existing uncommitted DLL-manager and GUI work and leaving `server-cli` at
`1482f30`. Local Git inspection confirms this branch is at the stated base
commit, has the release tag `v1.0.0-RC4`, and has no working-tree change under
`server-cli`.

The uncommitted implementation makes the package list authoritative in
`UnchainedLauncher.Core/Services/Dlls/DllManager.cs` and documents the same
contract in `README.md`. Its parser models `$schema`, targets, filenames and
nullable historical fields in `DllRelease.cs`. It also validates package
identity, HTTPS metadata/download URLs, SHA-256, size, dependencies, and source
consistency. The supplied checkpoint identifies the upstream schema reference as
zero-infra-mod-registry `prepare-v1` at
`09c388d24a4f78ca62cc7ded99a62d14aadba327` and the live C2ModRegistry reference
as `main` at `a2a0b42121ec9839af1bc431afd43ee511998228`.

## Options considered

### Use the legacy index as authoritative or merge it with packages

Rejected. A stale legacy record could re-approve a package release removed from
the package list or conflict with updated target/metadata. The implementation
uses legacy metadata only as a compatibility fallback when the package list is
absent.

### Reject incomplete historical package releases

Rejected. Package history may retain a valid binary hash and size while lacking
newer metadata fields. Those releases remain usable with safe filename fallback
and integrity verification.

### Persist a client/server choice with each enabled plugin

Rejected. A version selection is independent of the launch side. Eligibility is
instead filtered at `PrepareAsync` and `OpenForInjection`, keeping the selected
version intact for a subsequent launch on the other side.

### Inject arbitrary DLLs found in the plugin directory

Rejected. Injection is limited to enabled, registered, freshly validated managed
files. Loose files are explicitly ignored.

### Add a separate prerelease-display preference

Rejected. The existing unstable-plugin-release preference is persisted and
already expresses whether prereleases may be used. Reusing it avoids divergent
saved settings for listing and update behavior.

## Rationale and consequences

The package list is the registry's current source of approval, so prioritizing it
prevents stale compatibility data from silently expanding the trusted native-code
set. Per-operation target filtering lets one configuration serve client and
server launch paths without injecting an inapplicable DLL. Dependency ordering
and validation cause a launch to fail before downloads or injection when a
required compatible dependency is absent or has the wrong target.

The tradeoff is that users must refresh after registry-source changes, explicitly
enable dependencies, and cannot use an old cache after changing sources. Native
code remains inherently trusted code: hash and size checks establish registry
integrity, not safety or sandboxing.

The implementation additionally replaces directory enumeration with
`DllManager`-backed injection, uses Unicode `LoadLibraryW`, checks remote-thread
exit status, and releases injection handles. GUI cards expose per-release target,
filename, release date, notes, and dependency details.

The settings integration is implemented by making `SettingsVM` implement the
DLL Manager's `IPluginSettings` interface. `DllLaunchPreparer` refreshes and
offers target-compatible enabled-plugin upgrades only when the automatic-update
setting is on, and passes that setting as the download permission to
`DllManager.PrepareAsync`. The GUI removes the controls from the Settings view
and binds the DLL Manager controls directly to those same settings properties.

## Implementation and validation status

Uncommitted implementation spans the DLL manager and models, launch preparer,
injector, metadata dependency handling, GUI setup/view model/views, unit tests,
and README. The checkpoint reports successful live-registry parsing for
`v0.5.0`, `v1.0.0-RC1`, and `v1.0.0-RC2`; focused Core and GUI test passes in
Debug and Release; and successful Windows project builds. Those results are
checkpoint testimony, not independently reproduced in this annal review.

This review attempted the focused Core test command on Linux. It did not reach
test execution because a transitive Windows-targeted project requires
`EnableWindowsTargeting=true` (`NETSDK1100`). Package warnings also report the
pre-existing `log4net` 3.0.1 moderate advisory (`NU1902`).

Live Windows DLL injection and full GUI interaction are not verified. No commit,
pull request, issue, or release implements this decision yet.

The requested DLL Manager UI preamble about UnchainedPlugin being enabled by
default is absent from `MainWindow.xaml`; however, the new README section still
contains that sentence. This documentation mismatch was observed in the
uncommitted work and has not been corrected by this historical review.

## Reconsideration triggers and open questions

Reconsider the parser and trust boundary if C2ModRegistry changes the package
index or package schema, if stable release metadata ceases to distinguish target
or filename, or if Windows integration testing exposes launch/injection failures.

Missing history: no original design discussion, issue/PR, commit implementing
the work, source session ID, response ID, or independently retained Windows test
logs were available. The decision is recorded from the supplied checkpoint and
the current uncommitted artifacts; its implementation must be reviewed and
committed separately.

## Subsequent developments (2026-10-12)

The UI terminology and workflow were subsequently refined without changing this
record's registry, validation, target-filtering, or persisted-settings decision.
The user-facing **DLL Manager** is now **Plugin Manager**, with Mod Manager-like
search, sorting, refresh, and confirmation-based manual updates. See [Plugin
Manager workflow and update boundaries](2026-10-12-plugin-manager-workflow-and-update-boundaries.md).
The former "DLL Manager" references in this record describe the same component
and are superseded as user-facing terminology only.

## Subsequent developments (2026-10-12)

The claim at lines 29-36 that the persisted unstable-release setting alone
controls normal visibility is superseded at startup for prerelease launcher
builds. Such builds now force the existing setting enabled for the session;
stable builds continue to use an applicable same-version saved preference. See
[Plugin Manager links and prerelease startup default](2026-10-12-plugin-manager-links-and-prerelease-startup-default.md). The decision against a separate
prerelease-display preference remains unchanged.

## Subsequent developments (2026-10-12)

The implementation-state claim at lines 117-130 is superseded: this work was
committed as `c8bd89504e6f4a789456882b4ca6c97b1e8c577e`, pushed on
`feature/dll-validation`, and submitted as [PR #152](https://github.com/Chiv2-Community/UnchainedLauncher/pull/152).
It is implemented and reviewable, but not merged or released. See [DLL-validation
implementation committed and submitted](2026-10-12-dll-validation-implementation-committed-and-submitted.md).
