# Preserve scanned pak identity for server actors

- Date: 2026-11-13
- Status: implemented locally; Windows runtime validation pending
- Scope: v1 mod scanner manifests and server actor catalog
- Repository: [UnchainedLauncher](https://github.com/Chiv2-Community/UnchainedLauncher)
- Branch: `fix/v1-scanned-mod-actors`; HEAD and hotfix base `52f769704bc3bf872acc66142b7a2e49a9f50eed`, exact tag `v1.0.1`.
- Implementation and tests are uncommitted working-tree changes, not part of that commit.
- Source: user and build assistant, session `ses_e295ba4f6ffevv32G6mD2BmCkk`, through response `msg_1d6a9015d001HcPO22z7xVbOh6`. Private reasoning is unavailable.

## Decision and evidence

The user reported that scanned mod actors no longer appeared as server configuration checkboxes and requested a branch from the latest v1 hotfix. The assistant chose to preserve each container's absolute physical path and SHA-512 hash in scan results, rather than weakening the server catalog's existing file validation. This choice is attributed to the assistant's diagnosis; no other alternatives or user approval of a specific design are recorded.

At the base, the first scanner pass used the reader's filename as `PakPath` while hashing its physical path. The catalog checks existence and SHA-512 using `PakPath`, so a filename resolved outside the actual pak directory can be rejected. The second pass could also create a filename-only bucket with no hash. These code paths support the diagnosis, but do not independently establish that it explains every reported missing checkbox.

## Implementation and consequences

The uncommitted `ModScanResult.GetOrAddPak` retains filename keys but stores an absolute reader path and hash. Both scan passes use it; the second pass falls back to an existing source bucket or skips the reference when neither identity is available. This retains catalog validation and changes exported `pak_path` values to absolute paths. Filename-key collisions and the skipped-reference case merit further review; no compatibility assessment for manifest consumers is recorded.

The source transcript reports 19 scanner tests passing and a Windows-targeted test project cross-build succeeding; the new GUI catalog tests were not executed on Windows. A real `Unchained-Mods.pak` scan produced four blueprint entries, an existing absolute path and matching SHA-512. An earlier scan processed zero paks and is not evidence of success. The working tree contains scanner and catalog regression tests as well as the implementation; this review verified the diff but did not rerun tests or exercise the GUI.

Reconsider if real Windows scans or checkbox tests fail, if physical paths are unsuitable for manifest consumers, or if same-name containers or second-pass references lose assets. Windows GUI execution, end-to-end checkbox verification, review and any eventual commit or release remain open. The prior [scroll backport record](2026-11-06-v1-scroll-backport.md) describes the existing per-server selection design; this fix does not supersede it.

## Subsequent developments - 2026-11-13

The statements that implementation was uncommitted and a commit or PR remained open describe the earlier recording point. They are superseded by the committed implementation and open PR documented in [Scanned pak identity fix submitted for review](2026-11-13-scanned-pak-pr.md). Windows runtime validation remains pending.
