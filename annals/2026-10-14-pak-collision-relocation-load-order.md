# Preserve pak load-order prefixes during collision-safe relocation

- Date: 2026-10-14
- Status: implemented
- Scope: Chivalry 2 managed-pak relocation and collision-safe filename assignment
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/dll-validation`
- Git provenance: decision and implementation `89aed4c6ce77762dad5fd8b55fa09b236d960bb4` (`v1.0.0-RC4-35-g89aed4c`); no tag points at this commit; working tree was clean when recorded
- Pull request: [#152](https://github.com/Chiv2-Community/UnchainedLauncher/pull/152), reported open in the supplied conversation checkpoint
- Authoring agent: Annalist (GPT-5.6 Terra)
- Source: supplied conversation checkpoint; commit `89aed4c6ce77762dad5fd8b55fa09b236d960bb4` and its regression tests inspected. No source session or response ID was supplied.
- Related record: [Fail closed on pak launch preparation](2026-10-14-pak-launch-preparation-fail-closed.md)

## Decision summary

When a managed pak must move to accommodate a changed dependency order but its preferred path is user-owned, assign the next collision-safe name that retains the newly required forced-sort prefix. Do not reuse a prior collision-safe name whose prefix encodes the old order.

## Context and evidence

The earlier fail-closed preparation work preserved an existing collision-safe managed filename whenever the preferred filename was occupied by an unmanaged pak. Commit `89aed4c` establishes that filename prefixes before `__-__` determine pak load order. Preserving a name with an obsolete prefix would therefore preserve an obsolete dependency order after the managed set grows or is reordered.

`PakDir.AvailablePakName` now finds names case-insensitively against unmanaged pak names. During planning, an existing collision-safe name can be skipped only if its prefix matches the new target prefix; otherwise `ProcessMove` chooses a collision-safe target based on the new target name and updates the managed inventory only after the move succeeds.

`PakOrderCollisionTests` covers managed-set growth, reordering, removal, both casing forms of collisions, fresh-install name determinism, byte preservation for unmanaged pak and signature files, and a failed relocation that must not publish a new inventory entry. The supplied checkpoint reports CI run `38586208196` passed 210 Core and 17 scanner tests in each configuration, plus portable Core and GUI tests; that result was not independently queried during this record.

## Options considered

### Always preserve an existing collision-safe filename

Rejected. It protects unmanaged files but can retain an incorrect forced-sort prefix and thus an incorrect load order.

### Move directly to the preferred ordered filename

Rejected. The preferred path can be unmanaged. Direct relocation would conflict with the ownership boundary established in the related record.

### Relocate using a collision-safe name with the new prefix

Chosen. It preserves unmanaged bytes while making the filename's ordering portion agree with the requested dependency order. Deterministic availability selection also makes restored inventories stable across retries.

## Consequences and follow-up

Managed paks may acquire a new suffix such as ` (2)` when their desired ordered name collides, but their prefixes remain correct for load ordering. A relocation failure returns an error and leaves the prior managed inventory entry intact, so the existing fail-closed launch policy prevents execution after failed preparation.

This does not prove runtime game behavior under native Windows or Wine. Reconsider the filename-based ordering contract if the game or registry gains an explicit ordering mechanism that makes forced filename prefixes unnecessary.

## Historical gaps

The supplied checkpoint does not include the original review discussion that identified the prefix defect, raw CI logs, the implementation agent identity, or a source response ID. The commit and tests establish the implemented behavior and intended regression coverage.
