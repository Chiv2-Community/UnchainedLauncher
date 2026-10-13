# Mod-plugin dependency warning and concise Plugin Manager docs

- Date: 2026-10-12
- Status: implemented; PR #152 open
- Scope: clientside launch blocking message for mod DLL dependencies; user-facing Plugin Manager documentation
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/dll-validation`
- Git provenance: `74db8a51b7ddce9bc1314f587d661f740a372929` (merge head); functional change `3999a7ecb87b176478139f5121f50106e7a35474`
- Tag at HEAD: none; nearest tag: `v1.0.0-RC4-6-g74db8a5`
- Pull request: [#152](https://github.com/Chiv2-Community/UnchainedLauncher/pull/152), open against `main` at `74db8a51b7ddce9bc1314f587d661f740a372929`
- Authoring agent: Annalist (GPT-5.6 Terra)
- Source: completion review of session `ses_ed7be2d91ffeonK4Di8UKbHXLI`, response `msg_132e63fa6001aDyybjQp9pUtAf`; current Git history, source, tests, and PR metadata inspected
- Related records: [PR #152 description and formatting update](2026-10-12-pr-152-description-and-formatting-update.md); [DLL-validation implementation committed and submitted](2026-10-12-dll-validation-implementation-committed-and-submitted.md)

## Decision summary

When a launch mode does not allow DLL plugin dependencies, stop before mod installation and show one grouped message. The message identifies each affected mod and its required plugin repository names and version constraints.

Reduce the README Plugin Manager section to controls, launch requirements, and the native-code safety boundary. Registry-format and cache implementation detail does not belong in the primary user documentation.

## Context, evidence, and rationale

The prior message only said that an enabled mod needed a DLL plugin. The user requested a single useful association of mod to required plugins rather than a generic warning. `Chivalry2ModsInstaller` now builds that association from enabled mods and their pak dependencies, deduplicates requirements within each mod, sorts output deterministically, chooses singular/plural wording, displays it once, and cancels the unsupported launch before installation.

The user also rejected the README's long Plugin Manager section as too verbose. The committed README now contains six control bullets plus short launch and safety notes. The implementation details remain in code, tests, PR material, and the existing annals.

`DependencyTypesTests` covers one mod, several mods, shared requirements, duplicate requirements, and requirements inherited through pak dependencies. Source-session command output reported 59 focused Core tests passing in Debug and Release and a successful Windows-targeted GUI Release build. This review verified the committed source and test coverage but did not rerun those commands.

## Options considered

### Keep a generic warning

Rejected by the user's explicit request. It does not tell the user which Plugin Manager selections are required.

### Automatically enable or download required plugins

Not selected. The established Plugin Manager boundary requires explicit user enablement and keeps downloading and validation at launch time.

### Add a custom warning view

Not selected. The requested grouped text can be represented clearly by the existing dialog interface without a new UI surface.

### Keep registry implementation details in the README

Rejected as overly verbose for primary user documentation. The shortened text retains behavior users need to operate the feature and assess its safety.

## Consequences and follow-up

Users of a launch mode that cannot load plugins receive a complete, actionable list before installation begins. The message displays dependency repository names because dependency records do not carry an independent plugin display name.

PR #152 is still open and unmerged. The merge commit incorporates the remote formatter commit `3f19f94b06bb93326a98e19931c73dd7843195b5` and resolves its conflict while retaining this functional change. Live Windows game launch and DLL injection remain unverified.

Reconsider the dialog only if plugin dependency records gain presentation metadata or a richer UI is needed for large dependency sets. Restore or relocate technical registry detail only if user documentation proves insufficient for support or contributor needs.

## Historical gaps

The supplied conversation was compacted. It contains the user requests and reported command results, but not independent CI results or live game validation. No issue or architecture-record identifier was supplied.
