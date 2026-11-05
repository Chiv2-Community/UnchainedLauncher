# Issue #161: server actor selection reconciliation

Base: main `71609dc`, draft `469e74e`. PR #153 already made equal-record checkbox enables idempotent; it did not cover changed actor metadata or stale scans.

## Behavior

- Actor identity is the case-insensitive Unreal class path, falling back to asset path when class path is absent. JSON reads/writes, checkbox state, enable/disable, and launch use that identity rather than full DTO equality.
- Launch emits one selected, currently available actor per nonblank class path and refreshes surviving saved DTO metadata. The same array feeds plugin arguments and the live server information frame.
- Mod enable/disable events remove selections whose known release providers are no longer active, retaining actors supplied by another enabled release/dependency. Launch also reconciles configurations saved while their providers were disabled or replaced.
- Missing ownership metadata is not proof of disablement. Unknown selections and selections whose owning mod has an enabled but unresolved release remain saved, but are not emitted without an available provider. Unresolved legacy coordinate entries remain lossless.
- `IPakDir.ManagedPaks` exposes the inventory already implemented by `PakDir`. Scans cannot supply actors from tracked managed files: launcher release/dependency state is authoritative, including offline registry startup. Known inactive actor identities are also excluded from scan contributions.
- Unmanaged scan contributions require an existing pak whose SHA-512 matches the scan. Catalog refresh at launch rejects deleted, replaced, unreadable, and hashless scan results without requiring another scan notification. Unknown preferences survive this rejection.
- Automatic restart refreshes and filters its captured actor selections again; it does not replay actors removed since the previous launch. Process reattachment deduplicates saved actor identities, but does not reconstruct historical process arguments.

## Validation

- Passed: `dotnet build UnchainedLauncher.Core.Tests.Unit/UnchainedLauncher.Core.Tests.Unit.csproj --no-restore -p:EnableWindowsTargeting=true -clp:ErrorsOnly` (0 errors).
- Added focused regressions for changed-metadata/class-case identity, JSON round trips, checkbox state, launch metadata refresh, shared providers, disabled saved selections, missing enabled versions, deleted/replaced scan files, and managed scans without registry metadata.
- Passed 15 portable linked-source checks against the production catalog, identity, JSON converter, DTOs, and relevance extensions; WPF/registry/scanner boundaries were stubbed. Local harness: `UnchainedLauncher.Core.Tests.Unit/obj/issue161-portable/Portable.csproj` (ignored build artifact, not a replacement for Windows tests).
- Focused `dotnet test --no-build --no-restore -p:EnableWindowsTargeting=true --filter 'FullyQualifiedName~ServerSelectionMigrationTests|FullyQualifiedName~AvailableCatalogRegressionTests'` aborted before execution: Linux lacks `Microsoft.WindowsDesktop.App` 10.0.0. Windows/WPF runtime assertions remain unverified.
- A build initially hit the shared tmpfs quota; the subsequent no-restore build passed. No other worktree was changed.

## Limits

- Scan validation hashes unmanaged paks synchronously during catalog reconciliation, including launch; large scanned inventories can add latency.
- External pak mutation after validation is not locked out through process startup. This is not an atomic filesystem/launch transaction.
- No game/plugin runtime test was performed. Existing-process reattachment still infers actor state from saved preferences, not historical arguments.
- No push, PR, issue comment, or issue closure performed.
