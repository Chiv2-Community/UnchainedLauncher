# EGS lifetime implementation and PR

- Date: 2026-11-13
- Status: implemented; pull request open at source response
- Scope: EGS GUI client and server lifetime
- Repository: [UnchainedLauncher](https://github.com/Chiv2-Community/UnchainedLauncher); remote `https://github.com/Chiv2-Community/UnchainedLauncher.git`
- Branch: `fix/egs-exit-after-game`; recording HEAD: `4facdde8816349f2aa523646f5afbe61c5bf20d9`; no exact tag; nearest reachable tag `v1.0.1`.
- Source: user and build assistant, session `ses_e295a6ddbffe8btLL4jkqo4AvT`, through response `msg_1d6b07ebe001QDzYzUu4sQ28rS`. Unavailable or compacted history is unknown.

## Development

The user requested a PR for the previously [accepted EGS lifetime decision](2026-11-13-egs-launcher-lifetime.md). The assistant committed the implementation and tests as `4facdde8816349f2aa523646f5afbe61c5bf20d9` and opened [PR #188](https://github.com/Chiv2-Community/UnchainedLauncher/pull/188) against `hotfix/v1.0.x`. Git confirms the four implementation and test files in that commit; the PR was open at the source response and verification. This changes the prior record's time-specific uncommitted and no-PR status, not the decision or its rationale. No merge or release is established.

The source logs report 24 focused and 316 full unit tests passed under Wine, with no skips. The existing record describes the mocked watcher coverage, the build preceding a line-ending-only rewrite, and watcher-attachment limitations. Native Windows and actual EGS client and server lifetimes remain unverified. Complete those checks before release, including failure paths. Reconsider if EGS sessions become reusable or process resolution proves unreliable; the server watcher failure fallback and non-Steam types other than EGS remain unspecified. No new alternatives or rationale were recorded.
