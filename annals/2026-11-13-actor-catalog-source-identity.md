# Actor catalog identity and scan folder default

- Date: 2026-11-13
- Status: implemented; manual validation pending
- Scope: v1 server actor catalog and scanner folder picker
- Repository: [UnchainedLauncher](https://github.com/Chiv2-Community/UnchainedLauncher)
- Branch: `fix/v1-scanned-mod-actors`; recording HEAD `ce087c0934c9b42db35e2808b25a286f2524117d`; no exact tag; nearest reachable tag `v1.0.1`.
- Implementation: `80af474f6f8f7d39bbe01377b529eebb7f0f9e79`, merged with formatter commit `41f1b3892ed757b4715bbe5d233852f5cd0d682a` at recording HEAD. [PR #187](https://github.com/Chiv2-Community/UnchainedLauncher/pull/187) targets `hotfix/v1.0.x`.
- Source: user and build assistant, session `ses_e295ba4f6ffevv32G6mD2BmCkk`, through response `msg_1d6bd6d1c00140lMEEAfxyloSV`; private reasoning unavailable. Working tree was clean at recording.

## Decision and context

After the [scanned pak identity fix](2026-11-13-scanned-pak-identity.md) and its [PR submission](2026-11-13-scanned-pak-pr.md), the user reported duplicate actor rows and requested deduplication by actor path and pak path. The user also requested that the scan directory dialog always default to the installation's `TBL/Content/Paks`. Previously the dialog opened a folder picker without that default. The assistant implemented both requests; no other alternatives or rationale were recorded.

The catalog now compares normalized physical pak path and actor asset path case-insensitively, ignoring differing blueprint metadata. Same actor paths in different paks remain separate rows. Managed releases use their managed path when available, otherwise a path constructed from the pak directory and release filename. The picker sets both initial and default directory from the installation path. This changes catalog row identity, not the separate class-path-based selection and launch identity; rows for the same class may therefore share selection behavior. The physical-path fallback and selection interaction warrant review.

## Evidence and consequences

Commit `80af474` contains the implementation and regression tests. The supplied transcript and retained test logs show 306 Core/GUI tests passing under Wine and 19 scanner tests passing on Linux before the formatter merge; they do not establish a post-merge run, native Windows behavior, or an end-to-end checkbox test. The transcript reports a successful Windows Release publish before the merge. The previously launched Proton instance was the older build, not the updated one; manual confirmation of the new picker and rows remains pending. An earlier full Wine run failed one test before a fixture correction; the later run passed.

This advances the earlier records' pending Windows-targeted test execution: Wine tests ran, but native Windows and manual GUI validation remain open. It does not reverse the path/hash design. Reconsider on duplicate or missing rows, linked checkbox behavior, path fallback mismatch, or a picker that does not reliably start in the requested directory. No release or merged PR is established here; test after the merge and verify the real UI. The source conversation has no private reasoning and any unavailable earlier history cannot be reconstructed.
