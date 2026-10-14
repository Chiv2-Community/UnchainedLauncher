# Steam-install GUI startup smoke test

- Date: 2026-10-14
- Status: implemented; PR #152 open; startup smoke verified
- Scope: manual startup of the DLL-validation GUI against the local Steam installation
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/dll-validation`
- Git provenance: local `980af1ccb0f3f5a385a922d74e4f85198c84eeb6` (annals-only commit); functional PR head `afb17473d6e3ee8500d8c9c3bea43f6d27029d57`
- Tag at local commit: none; nearest tag: `v1.0.0-RC4-29-g980af1c`
- Pull request: [#152](https://github.com/Chiv2-Community/UnchainedLauncher/pull/152), open against `main`
- Authoring agent: Annalist (GPT-5.6 Terra)
- Source: automatic completion review of session `ses_ed7be2d91ffeonK4Di8UKbHXLI`, response `msg_13aa3d46a001P93YDPTzRr0syR`; supplied compacted transcript, launch log, current Git state, and prior annals inspected
- Related records: [DLL cache race hardening and review loop](2026-10-13-dll-cache-race-hardening-and-review-loop.md); [DLL-validation implementation committed and submitted](2026-10-12-dll-validation-implementation-committed-and-submitted.md)

## Decision summary

Use an externally published GUI build, not the installed launcher executable, for manual testing against the local Steam Chivalry 2 installation.

## Context and evidence

The session published the GUI to `/home/jbarber/.cache/opencode/pr152-launch-gui-afb1747` and started `UnchainedLauncher.exe --no-install` under Wine with the working directory set to `/home/jbarber/.local/share/Steam/steamapps/common/Chivalry 2`.

The retained log confirms that `TBL/Binaries/Win64/Chivalry2-Win64-Shipping.exe` existed at that installation, the installation's `.mod_cache` registry, mod-manager, settings, server-configuration, and server-tab state were loaded, and the configured GitHub registry refreshed 16 mods. The launcher process was still running when the response was made. The installed `Chivalry2Launcher.exe` was not replaced; the executable run was outside the game directory.

## Options considered

### Replace and run the installed launcher

Rejected for this test. It would alter the user's Steam installation and make recovery or comparison harder.

### Run an external published build with the Steam directory as working directory

Chosen. The application resolves its installation-relative state and game paths from the working directory, while the tested executable remains separate from the installed launcher.

## Consequences and follow-up

This is evidence of successful GUI initialization against a real Steam installation and persisted user state. It does not verify visible window interaction, game launch, DLL injection, or mod behavior. The Wine log also contained Mesa EGL warnings; it did not establish that they prevented startup.

Reconsider the external-build test setup only if launcher startup begins depending on its executable directory rather than the selected installation directory. Before release, still perform Windows game-launch and validated DLL-injection testing.

## Historical gaps

The source transcript is compacted and contains no screen capture, user interaction result, game-process result, or raw Wine exit result. The process remained running, so this record deliberately does not claim a completed GUI session or game launch.
