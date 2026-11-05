# Issue #161: server actor selection reconciliation

Base: main `71609dc`, draft `469e74e`. PR #153 already made equal-record checkbox enables idempotent; it did not cover changed actor metadata or stale scans.

## Behavior

- Actor identity is the case-insensitive Unreal class path, falling back to asset path when class path is absent. JSON reads/writes, checkbox state, enable/disable, and launch use that identity rather than full DTO equality.
- Launch emits one selected, currently available actor per nonblank class path and refreshes surviving saved DTO metadata. The same array feeds plugin arguments and the live server information frame.
- Mod enable/disable events remove selections whose known release providers are no longer active, retaining actors supplied by another enabled release/dependency. Launch also reconciles configurations saved while their providers were disabled or replaced.
- Missing ownership metadata is not proof of disablement. Unknown selections and selections whose owning mod has an enabled but unresolved release remain saved, but are not emitted without an available provider. Unresolved legacy coordinate entries remain lossless.
- `IPakDir.ManagedPaks` exposes the inventory already implemented by `PakDir`. Scans cannot supply actors from tracked managed files: launcher release/dependency state is authoritative, including offline registry startup. Known inactive actor identities are also excluded from scan contributions.
- Unmanaged scan contributions require an existing pak whose SHA-512 matches the scan. Ordinary metadata/toggle reconciliation reuses successful validation while the expected hash, file length, and modification time remain unchanged; a new scan clears that cache. Launch/restart always rehashes, including same-size/same-timestamp replacements. Failed validation invalidates the cache. Unknown preferences survive rejection.
- Without a scan, reconciliation does not read `PakDir` at all; registry-only catalogs and existing lightweight server configuration fixtures remain supported.
- Automatic restart refreshes and filters its captured actor selections again; it does not replay actors removed since the previous launch. Process reattachment deduplicates saved actor identities, but does not reconstruct historical process arguments.

## Validation

- Passed: `dotnet build UnchainedLauncher.Core.Tests.Unit/UnchainedLauncher.Core.Tests.Unit.csproj --no-restore -p:EnableWindowsTargeting=true -clp:ErrorsOnly` (0 errors).
- Added focused regressions for changed-metadata/class-case identity, JSON round trips, checkbox state, launch metadata refresh, shared providers, disabled saved selections, missing enabled versions, deleted/replaced scan files, and managed scans without registry metadata.
- Passed 15 portable linked-source checks against the production catalog, identity, JSON converter, DTOs, and relevance extensions; WPF/registry/scanner boundaries were stubbed. Local harness: `UnchainedLauncher.Core.Tests.Unit/obj/issue161-portable/Portable.csproj` (ignored build artifact, not a replacement for Windows tests).
- Initial native `dotnet test` aborted before execution because Linux lacks `Microsoft.WindowsDesktop.App` 10.0.0. Follow-up used the installed Windows SDK's VSTest runner through Wine instead.
- Follow-up native build passed with `--artifacts-path /home/jbarber/.cache/opencode/issue161-wpf-artifacts -p:EnableWindowsTargeting=true` (0 errors).
- Full Windows core suite through Wine passed: **245 passed, 0 failed, 0 skipped** (this branch, including WPF and all five existing auto-balance profile cases). Command: `WINEDEBUG=-all wine 'C:\Program Files\dotnet\dotnet.exe' exec 'C:\Program Files\dotnet\sdk\10.0.102\vstest.console.dll' 'Z:\home\jbarber\.cache\opencode\issue161-wpf-artifacts\bin\UnchainedLauncher.Core.Tests.Unit\debug_win-x64\UnchainedLauncher.Core.Tests.Unit.dll' '/Logger:console;verbosity=minimal'` with `TMPDIR=/home/jbarber/.cache/opencode/launcher-recent-issues-tmp`.
- Across four full Wine runs, two passed completely (including the final build), and two had 244 passes plus the same unrelated intermittent `LocalReleaseFormRegressionTests.FormSubmissionWritesAndReadsRequiredManifest(editing: False)` cleanup failure (`MyFancyMod.json` locked). No fixture or registry code was changed to hide that failure.
- Follow-up regressions cover no-scan pak-inventory independence, hash-cache reuse on metadata events, forced launch revalidation, and same-size/same-timestamp pak replacement.
- A build initially hit the shared tmpfs quota; the subsequent no-restore build passed. No other worktree was changed.

## Limits

- Initial scan publication and launch/restart still hash unmanaged paks synchronously: work scales with their total bytes, so large inventories can delay the UI. Unchanged metadata/toggle events now perform file-stat checks rather than rereading pak contents. No large-inventory latency benchmark was performed.
- External pak mutation after validation is not locked out through process startup. This is not an atomic filesystem/launch transaction.
- No game/plugin runtime test was performed. Existing-process reattachment still infers actor state from saved preferences, not historical arguments.
- No push, PR, issue comment, or issue closure performed.
