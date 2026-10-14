# Preserve registry approval across unchanged Plugin Manager refreshes

- Date: 2026-10-14
- Status: accepted; implementation uncommitted
- Scope: Plugin Manager enablement from a card retained across a DLL registry refresh
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/dll-validation`
- Git provenance: `eac86594b65e693edb66b9c6bf154107916e8f86` (`v1.0.0-RC4-30-geac8659`); no tag points at HEAD; the working tree contains the uncommitted implementation and regression test
- Pull request: [#152](https://github.com/Chiv2-Community/UnchainedLauncher/pull/152), open against `main`
- Authoring agent: Annalist (GPT-5.6 Terra)
- Source: supplied conversation checkpoint; current uncommitted diff and tests inspected. No source session or response ID was supplied.
- Related records: [DLL registry package metadata and target filtering](2026-10-12-dll-registry-package-metadata-and-target-filtering.md); [Steam-install GUI startup smoke test](2026-10-14-steam-install-gui-startup-smoke.md)

## Decision summary

Allow a Plugin Manager card created before a refresh to enable its release only when the refreshed registry still contains the same key and the complete canonical release metadata is unchanged. Use the current registry object for the persisted selection.

## Context and evidence

`DllManager.SetEnabled` previously used record equality to decide whether the supplied release was registered. A registry refresh reparses nested metadata collections, so an old card could compare unequal despite representing unchanged registry approval. The checkpoint reports that this caused the Plugin Manager Enable action to reject an old card as unregistered.

The uncommitted change locates the current release by case-insensitive key, compares canonical serialized release metadata, rejects absent or changed records, then replaces the stale card object with the current registry object before saving the selection. `DllSelectionRefreshTests` exercises unchanged, modified, and revoked registry responses. The supplied checkpoint reports those GUI tests passed in Debug and Release; this review inspected the test but did not rerun it.

## Options considered

### Require record equality after refresh

Rejected. Re-parsed collection fields can make equivalent releases unequal, blocking a valid enable action without a registry change.

### Accept any card with the same release key

Rejected. A key alone would allow an obsolete card after the registry changed its hash, source URL, metadata, or approval state.

### Revalidate complete canonical metadata and use the current release

Chosen. This admits only unchanged approval while retaining rejection of modified or revoked releases and prevents persisting a stale release object.

## Consequences and follow-up

An already displayed card can be enabled after a harmless refresh. A changed or removed release remains rejected with guidance to refresh the Plugin List. Equality semantics no longer decide registry approval at this UI boundary; canonical metadata does.

The implementation and tests are uncommitted and unpushed. The corrected Enable action was reopened in an external Wine GUI build for user testing, but this record has no completed interaction result. Commit the implementation and test after that validation; keep the full-canonical comparison aligned with any future `DllRelease` approval fields.

## Reconsideration triggers and historical gaps

Reconsider the comparison representation if canonical serialization stops being stable across equivalent registry parses, or if the registry defines an explicit signed approval identity that should replace it.

The checkpoint does not supply the original exception text, the responsible implementation agent, exact test commands, raw test output, or a source response ID. The test source supports the intended acceptance and rejection cases but does not itself prove the reported run.
