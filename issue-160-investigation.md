# Issue #160: shared pak inventory

Status: investigated; no production fix yet. Do not close.

## Findings on main (71609dc)

- `App.InitializeModManager` loads each launcher's managed-pak inventory independently and registers persistence only on application exit.
- A second launcher started while the first remains open can load an empty or stale inventory, despite the first having installed or renamed paks.
- `PakDir.InstallModSet` treats files absent from its inventory as unmanaged. It downloads another copy under a collision-safe name rather than adopting unknown files. This is correct for genuinely unmanaged content, but cannot reconcile stale inventories.
- PR #152 fixes repeat preparation and load-order collision handling within one inventory. It does not provide cross-process inventory synchronization.
- `v1.0.0` was cut from RC4, not current main, so the release also lacks those within-inventory fixes.

## Next implementation

1. Reproduce with two launcher processes using one installation; capture the on-disk mod-manager JSON before/after server preparation and before client preparation.
2. Add a directory-scoped shared inventory and an interprocess lock covering read, install/rename/remove, and atomic inventory publication. Reload under the lock before planning operations.
3. Persist successful partial changes even if a later operation fails. Prevent exit-time snapshots from overwriting newer inventory state; keep enabled-mod preferences separate from shared file ownership.
4. Test two stale readers, alternating server/client sets, interrupted publication, installation failure, and unmanaged filename collisions.

Do not infer ownership from filenames or delete the reported extra files automatically. Already running games may also be using these paks; safe preparation while a server runs needs Windows validation.
