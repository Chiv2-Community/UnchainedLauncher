# DLL cache race hardening and review loop

- Date: 2026-10-13
- Status: implemented; PR #152 open
- Scope: concurrent managed-DLL archival and active-file publication
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/dll-validation`
- Git provenance: `afb17473d6e3ee8500d8c9c3bea43f6d27029d57` (`[GHA] Format Source`)
- Tag at commit: none; nearest tag: `v1.0.0-RC4-28-gafb1747`
- Relevant commits: `9bcfd9ae6df07e90557041c2014093c3c453edfb` (handle-bound archival deletion); `ab80b43131c62d40588726d8d09024b3b7442f9f` (non-overwriting active publication); `afb17473d6e3ee8500d8c9c3bea43f6d27029d57` (formatter follow-up)
- Pull request: [#152](https://github.com/Chiv2-Community/UnchainedLauncher/pull/152), open against `main`
- Review evidence: reviews `5510970039`, `5511118568`; comments `4264873766`, `4265010905`; fix replies `4264971894`, `4265092924`
- Authoring agent: Annalist (GPT-5.6 Terra)
- Source: automatic completion review of session `ses_ed7be2d91ffeonK4Di8UKbHXLI`, response `msg_137e5c11a001Z7ddjDRAq6YG8i`; supplied compacted transcript, current Git history, working tree, and prior annals inspected
- Related records: [Managed DLL cache boundary](2026-10-13-managed-dll-cache-boundary.md); [PR #152 review fixes and clean review](2026-10-12-pr-152-review-fixes-and-clean-review.md)

## Decision summary

Preserve the managed-DLL ownership boundary under external filesystem races. Archive a validated active DLL only through its still-open validated Windows handle; on non-Windows hosts, archival fails closed. Publish a cached DLL to the active managed path with an atomic non-overwriting move, so a file that appears during preparation is preserved and blocks launch rather than being replaced.

## Context and evidence

The earlier cache decision correctly limited ordinary movement to validated registered files, but independent review found two time-of-check/time-of-use gaps. After copying a validated active DLL to cache, the old code closed its handle and deleted by pathname. Another process could replace that pathname with unknown bytes before deletion. Separately, restoring cache content used an overwriting move after an existence check, allowing an unknown file created during the copy to be overwritten.

Review `5510970039` reproduced the archival replacement case and recorded comment `4264873766`. Commit `9bcfd9a` retains a validated Windows handle through cache publication and deletes through that handle. It deliberately refuses archival on non-Windows rather than claiming pathname deletion is safe there. Review `5511118568` then reproduced the active-publication collision and recorded comment `4265010905`. Commit `ab80b43` makes active publication non-overwriting and fails preparation when the destination appears; cache replacement remains permitted as a distinct operation.

The final independent review covered all 28 PR commits, the complete 44-file diff, merge resolutions, runtime/configuration integration, tests, and reuse/complexity. It reported no further actionable findings. Its optional observation was that some typed-object JSON round trips could save allocations if replaced by direct validation; it was not accepted as a correctness or maintenance change.

## Options considered

### Rehash immediately before pathname deletion or overwrite

Rejected. A second check followed by a separate pathname operation leaves another replacement window and does not bind the operation to the validated identity.

### Continue archival by pathname on non-Windows

Rejected. The required handle semantics are platform-specific. Failing closed preserves user files and the invariant that unregistered content is never moved or deleted.

### Bind archival to the validated Windows handle and publish without overwrite

Chosen. These operations preserve the identity of the archived content and make destination collisions visible failures, while retaining validated cache replacement where it is intentionally needed.

## Consequences and follow-up

On Windows, a registered managed DLL can still be archived after validation without deleting a replacement supplied by another process. On non-Windows, a launch needing archival is blocked; this is a deliberate availability tradeoff for data safety. A competing active-path file now remains untouched and prevents injection until the user resolves it.

Regression coverage includes native Windows archival interleavings and active-publication collision behavior. The supplied evidence reports exact-head Windows CI run `38483097711` green, with 200 Core and 17 scanner tests in each Debug and Release configuration; portable Core (76) and GUI (36) tests passed in Debug and Release. This review did not rerun those commands. Live Windows game injection and interactive GUI use remain unverified.

Reconsider only if a portable API can provide equivalent identity-bound archive/delete semantics on non-Windows, or if product requirements explicitly accept a different conflict policy. Any replacement must continue to leave unknown or unregistered DLL bytes untouched.

## Subsequent developments

This record refines, rather than reverses, the cache boundary recorded in [Managed DLL cache boundary](2026-10-13-managed-dll-cache-boundary.md). Its broad claim that unknown and unregistered files remain untouched required this race hardening to hold under concurrent writers. See that record's correction note.

## Historical gaps

The supplied source is compacted and does not include complete reviewer reproduction code or raw CI logs. Review/comment IDs, commit history, test counts, final PR SHA, and clean working-tree state were available in the supplied response and local repository evidence. The temporary review-loop ledger mentioned in the source was outside the repository and is not durable repository evidence.
