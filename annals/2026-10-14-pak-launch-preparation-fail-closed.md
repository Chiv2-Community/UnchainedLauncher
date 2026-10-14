# Fail closed on pak launch preparation

- Date: 2026-10-14
- Status: accepted; implementation uncommitted
- Scope: Chivalry 2 pak installation, removal, signature preparation, and retry launch sequencing
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/dll-validation`
- Git provenance: `eac86594b65e693edb66b9c6bf154107916e8f86` (`v1.0.0-RC4-30-geac8659`); no tag points at HEAD; working tree has the uncommitted implementation and regression test
- Pull request: [#152](https://github.com/Chiv2-Community/UnchainedLauncher/pull/152), open against `main`
- Authoring agent: Annalist (GPT-5.6 Terra)
- Source: supplied conversation checkpoint; session `ses_1791983494_36d7ab` identified in the matching project memory; retained Wine launch log at `/home/jbarber/.cache/opencode/dll-validation-tmp/pr152-launch-gui.log`; current uncommitted diff and prior annals inspected. A source response ID was not supplied.
- Related record: [Steam-install GUI startup smoke test](2026-10-14-steam-install-gui-startup-smoke.md)

## Decision summary

Complete every pak preparation stage before starting Chivalry 2, and cancel the launch on any pak installation or removal error. When the preferred managed pak filename is occupied by an unmanaged file, retain the managed pak's existing collision-safe filename rather than moving it over the user-owned file.

## Context and evidence

The retained Wine log shows the initial launch installed `q__-__Unchained-Mods (1).pak` at 08:40:25, completed signature setup, and launched the game at 08:40:26. The game exited with `-67`, triggering its single retry. During that retry, the installer tried to move the managed `(1).pak` file to `q__-__Unchained-Mods.pak` and failed at 08:40:29; the prior behavior nevertheless proceeded to signature setup and launched a second game process at 08:40:44.

The uncommitted implementation changes `AndThen` to await each preparer and pass its prepared options to the next one. It closes installer progress in `finally`, displays an error, and returns no launch options when installation reports errors. Pak removal failures are yielded as installation errors. Managed filename and unmanaged-file comparisons are exact, case-insensitive filename comparisons; an existing managed collision-safe name is preserved when the preferred path is unmanaged.

The new uncommitted `PakLaunchPreparationTests.cs` covers repeated preparation with an occupied user-owned filename, failed move and removal cancellation, progress closure, and sequential preparer execution. The supplied checkpoint reports that a portable harness passed 108 tests in Debug and Release and that a Windows-targeted GUI Release build passed. A direct focused project-test attempt in this review was blocked on Linux by `NETSDK1100` because a transitive GUI project requires `EnableWindowsTargeting`; it did not execute the new tests.

## Options considered

### Continue after pak setup errors

Rejected. The log demonstrates that continuing produced a second game launch after a failed managed-pak move, so the executable could run with incomplete or inconsistent preparation.

### Reclaim the preferred filename on every preparation

Rejected. The preferred filename can be occupied by an unmanaged, user-owned pak. Reclaiming it would overwrite or require moving a file outside the launcher's managed inventory.

### Preserve the collision-safe managed filename and fail closed

Chosen. It preserves user data and allows a managed release already installed at a safe alternative name to remain usable. If an actual setup operation fails, no executable is started.

## Consequences and follow-up

Retries still perform pak preparation, but the known `(1).pak` collision no longer causes a rename attempt while the preferred filename remains unmanaged. Pak preparation now gates both normal and retry launches. Users must resolve genuine pak setup failures before launch; there is no continue-anyway path.

The changes are not committed, pushed, or included in a published GUI build. The observed `-67` Wine exit remains unresolved, and the corrected executable has not yet been tested against the live Steam installation. Before delivery, normalize the pre-existing mixed line endings/trailing-whitespace diff, rerun the relevant tests in a Windows-targeted-capable environment or the established portable harness, commit the implementation and test, and retest the retry with both pak filenames present.

## Reconsideration triggers and historical gaps

Reconsider filename preservation if the inventory format gains explicit ownership or collision metadata that permits a safer deterministic repair. Reconsider the fail-closed policy only with evidence that a particular setup failure cannot affect launch correctness.

The source checkpoint does not include the responsible implementation agent, exact portable-harness commands, test output, or source response identifier. The log proves the original failure sequence, but does not prove behavior of the uncommitted correction.
