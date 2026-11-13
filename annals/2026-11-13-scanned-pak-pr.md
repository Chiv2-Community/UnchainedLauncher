# Scanned pak identity fix submitted for review

- Date: 2026-11-13
- Status: implemented; pull request open; Windows runtime validation pending
- Scope: v1 scanner and server actor catalog
- Repository: [UnchainedLauncher](https://github.com/Chiv2-Community/UnchainedLauncher)
- Branch: `fix/v1-scanned-mod-actors`; recording HEAD `1e1a9e68a49d0f992820081b4d12ca77f5b1e073`; no exact tag; nearest reachable tag `v1.0.1`.
- Base: `52f769704bc3bf872acc66142b7a2e49a9f50eed`, tagged `v1.0.1`; earlier annal commit `22fe722e5915401b407eb6223393bf058e829c49`.
- Source: user and build assistant, session `ses_e295ba4f6ffevv32G6mD2BmCkk`, through response `msg_1d6aa880e001o75bdTwZRQJISt`; private reasoning unavailable.

## Development

The user requested a PR after the [scanned pak identity decision](2026-11-13-scanned-pak-identity.md). The assistant committed its previously uncommitted implementation and tests as `1e1a9e68a49d0f992820081b4d12ca77f5b1e073`, pushed the branch, and opened [PR #187](https://github.com/Chiv2-Community/UnchainedLauncher/pull/187) against `hotfix/v1.0.x`. Its range includes the earlier annal commit. Local Git and the PR metadata confirm the implementation commit and open PR head. This changes the earlier record's uncommitted and PR-pending implementation status, not its design rationale.

The prior record describes the path and hash approach, risks, and reported tests. This review did not rerun those tests. Windows GUI tests and end-to-end checkbox validation remain pending; an open PR is not a merge or release. Reconsider the design if those checks fail or if manifest consumers, duplicate filenames, or second-pass references expose the risks identified in the prior record. No alternative submission plan or further rationale is recorded.

## Subsequent developments - 2026-11-13

The PR was extended with the catalog identity and folder picker changes in [Actor catalog identity and scan folder default](2026-11-13-actor-catalog-source-identity.md). The claim that Windows-targeted GUI tests were pending is superseded for the Wine run only; native Windows and manual end-to-end checkbox checks remain pending.
