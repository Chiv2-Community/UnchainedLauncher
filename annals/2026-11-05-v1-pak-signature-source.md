# V1 pak signature source and signing-error logging

- Date: 2026-11-05
- Status: implemented; end-to-end validation outstanding
- Scope: V1 mod installation, pak signing, and signing-error logging
- Repository: [Chiv2-Community/UnchainedLauncher](https://github.com/Chiv2-Community/UnchainedLauncher.git)
- Branch at recording: `backport/v1.0.x/pak-sig`
- HEAD and implementation: `08926d29a807f7a209c1a9c8e17e5a3fe5e17088`; no exact tag
- Release base and nearest tag: `fdae4444c13729e9997e7901b7747376dada35c5`, `v1.0.0`
- Source: session `ses_e53041386ffeTir8aDWZaEMHv5`, response `msg_1adce7ff1001C1KEA5dDQnL4sf`; only a compacted conversation summary was supplied. Commit author: Jacob Barber. Record author: Annalist.
- Related: [PR #174](https://github.com/Chiv2-Community/UnchainedLauncher/pull/174), described in the source summary as targeting `hotfix/v1.0.x` from `backport/v1.0.x/pak-sig`; PR state not independently verified.
- Working tree: clean before this record.

## Decision and context

Use the game installation's `TBL/Content/Paks/pakchunk0-WindowsNoEditor.sig` as the source for signing mod paks, independently of the mod destination. Log a signing failure's message as text rather than passing the LanguageExt error as an exception to log4net. The user specified the installation directory as the signature source; this is not a request to copy the base signature into the mod directory.

The V1 installation investigation found that a download completed but signing looked in the separate mod directory, where the base signature was absent. The investigation attributed the apparent freeze to recursive log4net rendering of the resulting error, exhausting the UI thread's stack. Its Proton diagnostic run with only the logging change kept the UI responsive, but later signing still failed and prevented launch. Thus logging alone was not a complete fix.

## Evidence and choices

- The implementation in [PakDir.cs](../UnchainedLauncher.Core/Services/Mods/PakDir.cs) separates `signatureSourceDir` from `DirPath`, defaults it to `FilePaths.PakDir`, and logs `e.Message`. [FilePaths.cs](../UnchainedLauncher.Core/Utilities/FilePaths.cs) defines that default as `TBL\\Content\\Paks`.
- [PakDirTest.cs](../UnchainedLauncher.Core.Tests.Unit/Services/PakDir/PakDirTest.cs) covers a separate source and destination, rejection of a conflicting mod-directory base signature, re-signing, and missing-source text logging without an exception object.
- The local investigation artifact `/tmp/opencode/cwd-log4rs-launch-v1/INVESTIGATION.md` reports stack traces, a native logging reproduction, and a Proton diagnostic run. These artifacts are outside the repository and may not persist; their underlying traces were not independently reanalyzed in this review.
- The retained local harness log reports 30 passing tests on Linux; the Windows-targeted build log reports success with warnings, including a log4net advisory and bad-image warnings. These logs are not durable repository artifacts and their results do not prove a Windows test run.
- Keeping the mod directory as source was the previous behavior and failed in the observed separate-directory layout. Copying the base signature into that directory was explicitly rejected in the source investigation. Logging only was tested as a diagnostic alternative and left the launch blocker. No other alternatives or tradeoff discussion are recoverable from the compacted history.

## Consequences and follow-up

The implementation preserves the mod destination while sourcing the signature from the installation path; a missing installation signature remains an error. Text-only logging avoids the observed recursive exception rendering but does not preserve an exception object in that log event. The optional source parameter permits isolated tests.

Run the complete patch through Windows tests and an end-to-end installation and game launch, including a separate mod directory and a missing base signature. Reconsider the source or logging approach if those tests reveal incorrect installation resolution, signing failures, or recurring rendering failures. The supplied history does not establish a full Windows test run, successful complete-patch launch, PR merge, or release. The original conversation and full decision discussion are unavailable; no prior annals were present to reconcile.
