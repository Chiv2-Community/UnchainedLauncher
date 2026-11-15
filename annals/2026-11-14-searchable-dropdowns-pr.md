# Searchable dropdown implementation and PR

- Date: 2026-11-14
- Status: implemented on feature branch; WPF interaction unverified
- Scope: 1.0.x launcher GUI
- Repository: UnchainedLauncher, https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/v1-searchable-dropdowns`
- HEAD at recording and implementation commit: `22782111d530fe250e3f16bedbd2282135f52e74`; no exact tag; nearest tag `v1.0.2-rc1`.
- Prior decision record commit: `32c37828862b0a2d5c6114bce87b853b9614e6d8`; base: `be6b4b3cd76b52258cec7a78d1f12635794f9403`.
- Source: user and build agent, session `ses_e2309679dffejOP7aLU7PYo1Oe`, through response `msg_1dd01eb83001GIldn1QebX0zqx`. Supplied history may omit earlier or compacted context.

## Development

The [earlier decision](2026-11-14-searchable-dropdowns.md) recorded an uncommitted shared ComboBox search implementation. The user then requested a pull request and a launcher run. The implementation and tests were committed in `2278211` and submitted as [PR #196](https://github.com/Chiv2-Community/UnchainedLauncher/pull/196), targeting `hotfix/v1.0.x`. The PR was confirmed open at that commit when this record was written. The earlier claim that the implementation was uncommitted was accurate at its recording time but is now outdated; the design itself was not superseded.

The existing choice remains: an independent result list avoids modifying the bound selection and shared views while offering ordered, case-insensitive subsequence matching. The earlier record explains requirements, tradeoffs and rationale. No further alternatives or rationale are established by the supplied conversation.

## Evidence and remaining work

- The implementation commit contains the WPF behavior, shared template, label configuration and tests. This verifies committed code, not that every dropdown works.
- The supplied tool output reports 29 portable GUI tests passed, Windows-targeted projects compiled, and a self-contained Release publish succeeded. The WPF interaction tests were not executed on Windows; builds and portable tests do not establish interactive correctness.
- The supplied process listing shows the published executable and WebView process running under Proton. It does not establish that the window rendered correctly or that dropdown interactions were tested.
- Validate selection, display labels, mouse and keyboard behavior, accessibility and shared sources on Windows. Reconsider the design if those checks expose regressions or users need a different fuzzy-matching definition. Merge and release are not established.
