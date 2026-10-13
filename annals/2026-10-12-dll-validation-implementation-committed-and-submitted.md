# DLL-validation implementation committed and submitted

- Date: 2026-10-12
- Status: implemented
- Scope: delivery status for registry-validated DLL loading and Plugin Manager
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/dll-validation`
- Git provenance: `c8bd89504e6f4a789456882b4ca6c97b1e8c577e`
- Tag at commit: none
- Implementation commit: `c8bd89504e6f4a789456882b4ca6c97b1e8c577e` (Add registry-validated DLL loading and Plugin Manager)
- Pull request: [#152](https://github.com/Chiv2-Community/UnchainedLauncher/pull/152), open against `main`
- Authoring agent: Annalist (GPT-5.6 Terra)
- Source: completion review of session `ses_ed7be2d91ffeonK4Di8UKbHXLI`, response `msg_132db27620017FbGLw2FCSnl8A`; local Git and GitHub PR metadata verified
- Related records: [DLL registry package metadata and target filtering](2026-10-12-dll-registry-package-metadata-and-target-filtering.md); [Plugin Manager workflow and update boundaries](2026-10-12-plugin-manager-workflow-and-update-boundaries.md); [Plugin Manager links and prerelease startup default](2026-10-12-plugin-manager-links-and-prerelease-startup-default.md)

## Decision summary

The accepted DLL-validation and Plugin Manager decisions were consolidated into
one implementation commit and submitted for review as PR #152. The branch is
now published; it is not merged or released.

## Context and evidence

The three related records were written while their implementation existed only
as uncommitted work pinned to `aa8642601f9ed490ce0462e8a492ab989c0b0140`.
The source session subsequently committed the implementation as
`c8bd89504e6f4a789456882b4ca6c97b1e8c577e`, pushed
`feature/dll-validation`, and opened PR #152 against `main`.

At this review, local `HEAD` and `origin/feature/dll-validation` both resolve
to that full SHA. GitHub metadata reports PR #152 open with the same head SHA,
base `main`, and no merge timestamp. The working tree is clean.

The source session reran and reported passing 55 focused Core DLL/dependency
tests, 27 portable GUI tests, and a Windows-targeted GUI Release build before
the commit. Those command results were recorded in the source conversation;
this review verified the resulting commit and PR metadata, not a fresh test run.

## Options considered

### Leave the implementation only as local work

Rejected by the explicit request to create a pull request. Publishing a reviewable
branch makes the implementation and its decision records durable and inspectable.

### Treat PR creation as merge or release

Rejected. PR #152 is open and has no recorded merge timestamp. Deployment,
release, and live game-injection validation remain outside this delivery event.

## Consequences and follow-up

The implementation is now commit-pinned and reviewable, replacing the earlier
"uncommitted" implementation-state claims. The earlier decisions remain
accepted and implemented; this record changes their delivery provenance, not
their technical rationale.

Review and merge PR #152 as appropriate. Before relying on the feature in a
release, complete live Windows game launch and DLL injection validation. The
source session explicitly reported that those scenarios remain untested.

## Reconsideration triggers and open questions

Update this record if PR #152 is merged, closed, materially revised, or replaced.
The source conversation was compacted, so it is not a complete original design
transcript; this record relies on the supplied response plus independently
verified Git and PR state.

## Subsequent developments (2026-10-12)

The claim at lines 31-33 that both the local branch and PR #152 resolved to
`c8bd89504e6f4a789456882b4ca6c97b1e8c577e` is superseded. The published PR
head is now `3f19f94b06bb93326a98e19931c73dd7843195b5` (`[GHA] Format Source`),
while this local branch is at annals-only commit
`e57be08e77214d941f738ef711359083ebd6f143`. The PR remains open and unmerged.
The user also replaced its summary with direct, terse change-and-reason bullets.
See [PR #152 description and formatting update](2026-10-12-pr-152-description-and-formatting-update.md).

## Subsequent developments (2026-10-12)

The implementation was materially revised after independent review identified two
defects: a registry-revoked enabled selection could not be cleared, and local
release editing dropped plugin dependencies. Commit
`6d6e181167eb6e51b374494b927749c61c7d2fb5` fixes both. The current PR head is
`9e5a1118221f0b74ff75033e43b79c179156b661`; it remains open and unmerged. See
[PR #152 review fixes and clean review](2026-10-12-pr-152-review-fixes-and-clean-review.md).
