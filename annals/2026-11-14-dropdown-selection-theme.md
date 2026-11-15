# Theme-aware searchable dropdown selection

- Date: 2026-11-14
- Status: implemented on feature branch; visual fix unverified
- Scope: searchable dropdown result rows in the 1.0.x launcher GUI
- Repository: UnchainedLauncher, https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/v1-searchable-dropdowns`
- HEAD at recording: `aa925b2636537e205295224b5f14223849ae76ba`; no exact tag; nearest tag `v1.0.2-rc1` at `10eefb06b9d2c84d282b388e3c92cee6235b814d`.
- Implementation: `83e2a992f17378e622e8566c845cfcf4e896fead`; merged with remote formatting in `aa925b2636537e205295224b5f14223849ae76ba`.
- Source: user and build agent, session `ses_e2309679dffejOP7aLU7PYo1Oe`, through response `msg_1dd04afa5001jRNxChU69RL1BW`. Supplied history may omit earlier or compacted context.

## Decision and context

After the [searchable dropdown PR](2026-11-14-searchable-dropdowns-pr.md), the user reported that the selected item was bright white in dark mode and requested appropriate styling. The build agent attributed the appearance to WPF default selection styling and chose an explicit theme-aware result-item template. That attribution is the agent's diagnosis, not independently established by a visual reproduction in this review.

The new `SearchResultItemBase` in `UnchainedLauncher.GUI/Resources/Themes/Inputs.xaml` uses dynamic theme brushes for text, hover, selected background and border; the version selector inherits it while retaining its latest-stable emphasis. This changes the earlier result-row styling, not the search or selection decision. No other alternatives or rationale are recorded as considered.

## Evidence, consequences and follow-up

- Commit `83e2a99` contains the template, version-style change and a WPF regression test for inactive selection and dark/light theme changes. It is included in the recorded HEAD and [PR #196](https://github.com/Chiv2-Community/UnchainedLauncher/pull/196).
- The supplied tool output reports 29 portable GUI tests passed, Windows-targeted unit projects built, and a Release publish succeeded before merging the remote formatter commit. The WPF style regression was compiled but not shown running. These checks do not verify the visual fix.
- A Proton launch was initiated after publishing; the supplied response does not establish that the relaunched window rendered or that the updated selection was inspected. The publish preceded the formatting merge, so the launched binary is not proven to be the exact merged HEAD.
- Validate selected, inactive, hovered and disabled rows in dark and light modes, including the version selector and theme switching, on Windows. Reconsider the template if visibility, accessibility, or existing item styling regresses. PR merge and release remain unverified.

## Subsequent developments (2026-11-14)

The WPF inactive-selection regression subsequently ran in passing Windows CI, and PR #196 merged. The earlier unexecuted-test and unverified-merge statements are historical; manual visual verification and release remain unestablished. See [alphabetical map dropdown results](2026-11-14-alphabetical-map-dropdowns.md).
