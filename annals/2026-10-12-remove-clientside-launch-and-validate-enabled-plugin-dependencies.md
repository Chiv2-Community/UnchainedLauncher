# Remove Clientside launch and validate enabled plugin dependencies

- Date: 2026-10-12
- Status: implemented; PR #152 open
- Scope: launch-mode support and mod-to-plugin dependency validation
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/dll-validation`
- Git provenance: `9e19a544263fe6c9e4407dccfbd7806725edd735` (current merge head)
- Relevant commits: `f9f6657926ddb9682ea5262aeed4f6b2b87aea1a` (functional change); `9e19a544263fe6c9e4407dccfbd7806725edd735` (published merge head)
- Tag at HEAD: none; nearest tag: `v1.0.0-RC4-13-g9e19a54`
- Pull request: [#152](https://github.com/Chiv2-Community/UnchainedLauncher/pull/152), open against `main`
- Authoring agent: Annalist (GPT-5.6 Terra)
- Source: completion review of session `ses_ed7be2d91ffeonK4Di8UKbHXLI`, response `msg_132ed8c2f001zCgPuh5dRV3vpe`; current Git history and source inspected
- Related records: [Mod-plugin dependency warning and concise Plugin Manager docs](2026-10-12-mod-plugin-dependency-warning-and-concise-docs.md); [DLL registry package metadata and target filtering](2026-10-12-dll-registry-package-metadata-and-target-filtering.md)

## Decision summary

Clientside Modded launch is removed and will not be supported again. The obsolete launcher wiring and its `allowPluginDependencies` guard were deleted.

Plugin dependencies are now checked during DLL launch preparation against the plugins already enabled by the user. Compatible enabled plugins allow launch. Missing or incompatible requirements fail launch with one deterministic, grouped message listing only unsatisfied requirements by mod.

## Context and evidence

The earlier implementation blocked a launch mode whenever any selected mod declared a plugin dependency. The user identified the flaw: that guard would still block a launch after the required plugins had been manually enabled. The subsequent clarification established that the formerly cited Clientside Modded launch does not exist and must never be restored.

Commit `f9f6657` removes the unused Clientside launcher construction from `App.xaml.cs` and removes the installer flag and pre-install warning from `Chivalry2ModsInstaller`. It moves grouped missing-dependency reporting into `DependencyValidator`, which compares each declared dependency with enabled selected plugin versions. The same commit updates the README feature list to name only Vanilla and Unchained modes.

The source session reported 60 focused Core tests, 27 portable GUI tests, and a Windows-targeted Release build passing before the final merge. This review verified the committed implementation and tests but did not rerun them. Live Windows game launch and DLL injection remain unverified.

## Options considered

### Keep the launch-mode guard

Rejected. It treats every declared dependency as absent and therefore prevents launch even when the user enabled a compatible plugin.

### Retain Clientside Modded launch as a disabled compatibility path

Rejected by the user's explicit statement that it is removed permanently. Retaining its setup or policy flag would preserve dead behavior and misleading terminology.

### Automatically enable missing plugins

Not selected. Plugin activation remains an explicit user choice. Validation reports only what is missing or incompatible.

## Consequences and follow-up

Only Vanilla and Unchained are supported launch modes. The Plugin Manager remains the place where users choose plugin versions. A mod requiring a plugin can launch through Unchained when the selected plugin satisfies its version constraint; otherwise the grouped failure explains which selections are required.

The earlier record's claim that an unsupported launch mode receives the grouped warning before installation is reversed. The grouping behavior survives, but it is now emitted by dependency validation for unmet requirements, not by a Clientside-mode guard.

Reconsider only if a supported launch path is deliberately introduced that cannot inject plugins. Such a path would need an explicit, separately documented dependency policy.

## Historical gaps

The supplied conversation was compacted. It establishes the user decision and reports validation results, but does not provide independent CI output or live-game validation. No issue or architecture-record identifier was supplied.
