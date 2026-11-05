# Issue #160: duplicate server/client paks

Status: implemented and tested on Linux; Windows/game validation remains. Do not close based on these tests alone.

## Findings on main (71609dc)

- `App.InitializeModManager` loads each launcher's managed-pak inventory independently and registers persistence only on application exit.
- A second launcher started while the first remains open can load an empty or stale inventory, despite the first having installed or renamed paks.
- `PakDir.InstallModSet` treats files absent from its inventory as unmanaged. It downloads another copy under a collision-safe name rather than adopting unknown files. This is correct for genuinely unmanaged content, but cannot reconcile stale inventories.
- PR #152 fixes repeat preparation and load-order collision handling within one inventory. It does not provide cross-process inventory synchronization.
- `v1.0.0` was cut from RC4, not current main, so the release also lacks those within-inventory fixes.

## Implementation

- `PakInventory` stores ownership in `<PakDir>/.unchained/inventory.json`, separately from enabled-mod preferences and metadata caches.
- A persistent lock file, opened with `FileShare.None`, serializes inventory reload, preparation, and publication across launcher instances. Lock contention waits up to 30 seconds before failing.
- Each preparation reloads ownership under the lock. The legacy ModManager snapshot seeds ownership only when no shared inventory exists; later exit-time snapshots cannot overwrite the shared inventory.
- Successful changes are flushed and atomically published before yielding results, including partial preparations. Uninstall and explicit reset publish their changes too. Hash-path lookup reloads current names.
- Downloads reserve new filenames and remove failed partial output. Failed signature moves roll the pak rename back. Unknown paks are neither adopted nor removed during preparation.
- A durable `pending` marker makes interrupted operations/publication failures fail closed, rather than treating newly untracked files as unmanaged and downloading duplicates. Corrupt inventory also fails closed.
- Synchronization does not reload or save enabled-mod choices. Existing launcher preference persistence remains separate and unchanged.

## Validation

- `dotnet test /tmp/opencode/issue-160-tests/PortableTests.csproj -clp:ErrorsOnly`: **46 passed**, including the existing pak tests and 15 shared-inventory cases. The temporary net10 harness links the repository test sources and references the production Core project.
- Coverage: stale instances/restarts, alternating orders and subsets, overlapping preparations, unmanaged collision preservation, legacy migration, independent enabled choices, partial enumeration, download failure/cleanup, stale uninstall, signature-move rollback, invalid inventory, interrupted preparation/publication, and reset.
- `dotnet build UnchainedLauncher.Core.Tests.Unit/UnchainedLauncher.Core.Tests.Unit.csproj -p:EnableWindowsTargeting=true -p:CopyLocalLockFileAssemblies=false -clp:ErrorsOnly`: **passed**, 0 errors. A normal cross-build initially hit the environment's disk quota copying dependencies; disabling copy-local allowed compilation to complete.
- `dotnet build /tmp/opencode/issue-160-worker/Worker.csproj -clp:ErrorsOnly` and `python3 /tmp/opencode/issue-160-worker/check-processes.py`: **passed**. Two separate overlapping .NET processes prepared one directory; only one writer ran, leaving one pak and one inventory entry.

## Remaining limitations

- No Windows UI/game reproduction or validation with an already-running server. This serializes preparation, not game lifetimes; different requested sets may still remove/reorder managed paks used by a running game, or fail on Windows sharing restrictions.
- Interrupted transactions require manual reconciliation. Inspect `inventory.json` and actual pak paths, repair ownership or restore the interrupted file operation, then remove `.unchained/pending`. Do not simply delete the marker or inventory and relaunch: that can lose ownership and recreate duplicates. Recovery is deliberately not automatic.
- Already-created duplicate/unmanaged paks are not cleaned up. Mixed old/new launcher versions are not coordinated because older versions ignore the new lock/inventory.
- Existing exit-time preference saves are unchanged; this fix does not solve general concurrent editing of launcher configuration. It does not merge another instance's enabled selections into the current instance.

Do not infer ownership from filenames or delete the reported extra files automatically.
