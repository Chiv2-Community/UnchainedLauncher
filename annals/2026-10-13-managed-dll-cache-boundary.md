# Managed DLL cache boundary

- Date: 2026-10-13
- Status: implemented; PR #152 open
- Scope: on-disk lifecycle for registry-validated native DLL plugins
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/dll-validation`
- Git provenance: `ce7cefd4cdd89ce31e8e0faa34faa00801d91e6c` (Cache inactive managed DLLs outside Plugins without moving unknown files)
- Tag at commit: none; nearest tag: `v1.0.0-RC4-20-gce7cefd`
- Pull request: [#152](https://github.com/Chiv2-Community/UnchainedLauncher/pull/152), open against `main` at the recorded head
- Authoring agent: Annalist (GPT-5.6 Terra)
- Source: completion review of the supplied compacted conversation checkpoint; commit, working tree, and prior annals inspected locally. No source session or response ID was supplied.
- Related records: [DLL registry package metadata and target filtering](2026-10-12-dll-registry-package-metadata-and-target-filtering.md); [PR #152 review fixes and clean review](2026-10-12-pr-152-review-fixes-and-clean-review.md)

## Decision summary

Keep active registry-validated DLLs in `Plugins/Managed/<organization>/<repository>/<filename>`. Cache inactive validated managed DLLs outside `Plugins`, at `Win64/.cache/dlls/<sha256>/<filename>`, and restore them when selected. Do not move or adopt unknown filenames, loose DLLs, or files whose version is no longer registered.

## Context and evidence

The existing managed layout embedded the release SHA in the active path, leaving inactive managed versions under `Plugins`. The completed work needed version switching without repeated downloads while preserving the registry-validation boundary: only files demonstrably owned by a currently registered release may be relocated or injected.

Commit `ce7cefd4` changes `DllManager` to fill and validate the external cache before changing active files, archive only validated managed paths, restore selected cached releases into the active path, and reject cache locations inside `Plugins`. The cache and installed copies are verified against release size and SHA-256 before use. The implementation also detects selection or metadata changes during download and leaves the active set unchanged on a failed download.

The commit adds regression coverage for version switching without downloading, migration of legacy SHA-path managed files, disabled and revoked releases, unknown files, corrupt cache data, failed downloads, concurrent selection changes, and the external-cache constraint. The supplied checkpoint reports 71 portable Core tests in Debug and Release, 36 GUI tests in Debug and Release, a Windows-targeted Release GUI build, and 24 focused CLI tests passing. This annal did not rerun those tests. `git show --check` found no whitespace errors, and the working tree was clean at recording.

## Options considered

### Keep inactive validated DLLs under `Plugins`

Rejected. It leaves inactive managed versions in the directory intended for active injection and does not establish a separate cache boundary.

### Move every DLL found in `Plugins` or `Managed`

Rejected. A filename or directory shape is not proof of registry ownership. Moving unknown or revoked files would alter user data and could make unvalidated native code appear managed.

### Cache each managed release by SHA-256 outside `Plugins`

Chosen. The release digest uniquely scopes a validated cache artifact, permits offline restoration of a selected known version, and keeps only the selected managed DLLs in the active directory.

## Consequences and follow-up

Changing a selection can restore a previously validated cached version without a network download. Cache corruption fails closed; it cannot be restored or injected. A legacy managed file is migrated only when it validates against a current registry release. Loose files, unknown names, and unregistered versions remain in place and are never adopted for injection.

This is an implementation refinement of the registry-validated loading decision, not a reversal of it. The separate `server-cli` worktree was intentionally not modified. Live Windows game launch and DLL injection remain unverified. Reconsider the cache location or layout only if game deployment constraints make `Win64/.cache/dlls` unsuitable, while retaining the requirement that cache storage stay outside `Plugins` and file movement require current registry validation.

## Historical gaps

The source conversation was supplied only as a checkpoint, so the original discussion of alternatives and test command output is unavailable. The chosen behavior and its safeguards are established by the implementation and tests in `ce7cefd4`; the stated test results are checkpoint evidence rather than independently rerun evidence.
