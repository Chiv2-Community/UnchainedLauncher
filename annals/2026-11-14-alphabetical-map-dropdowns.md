# Alphabetical map dropdown results

- Date: 2026-11-14
- Status: accepted; implementation uncommitted and WPF execution unverified
- Scope: 1.0.x map rotation and live server map selectors
- Repository: UnchainedLauncher, https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `fix/v1-alphabetical-map-dropdowns`; HEAD `52bdb9fb9db33a93da2ea6d732db4a7e97c385b2`; no exact tag; nearest tag `v1.0.2-rc1`.
- Source: user and build agent, session `ses_e2309679dffejOP7aLU7PYo1Oe`, through response `msg_1dd28d086001EhOATVk6OAMhTY`. Supplied history may be incomplete or compacted.

## Decision and context

The user requested merging [PR #196](https://github.com/Chiv2-Community/UnchainedLauncher/pull/196), then alphabetizing map dropdown entries on a new branch from the latest bugfix line. The agent chose to sort both map selectors by the displayed travel string, without changing configured rotation order. PR #196 merged into `hotfix/v1.0.x` as `52bdb9fb9db33a93da2ea6d732db4a7e97c385b2`; this branch starts there.

The [searchable dropdown design](2026-11-14-searchable-dropdowns.md) isolates results from the source to protect selections and shared views. The uncommitted change extends that design: only the two map selectors opt in to sorting the filtered result snapshot with `StringComparer.OrdinalIgnoreCase`; other dropdowns retain their order. This avoids reordering the shared `AvailableMaps` collection, which also feeds rotation and default-map behavior. Sorting the underlying collection was not documented as a considered alternative. The exact choice of ordinal case-insensitive comparison is the agent's implementation choice, not a separately stated user requirement.

## Evidence and consequences

- The working-tree changes in `SearchableComboBoxBehavior.cs`, `Views/Servers/Server.xaml`, and `Views/Servers/Sections/BaseConfigurationSectionView.xaml` implement the opt-in. They are uncommitted at the recorded HEAD. The map label converter derives the travel string from the asset path, not map metadata.
- A new uncommitted WPF test covers ordering, filtering, live additions, reopening, selection and source order. The supplied build output shows the Windows-targeted test project compiled and 29 portable GUI tests passed. Those portable tests do not execute this new WPF test. The attempted Wine harness failed before completing because of runtime globalization/font errors; sorting and rotation preservation were not independently demonstrated at runtime.
- Prior claims of an unmerged PR and unexecuted WPF regressions in [implementation and PR](2026-11-14-searchable-dropdowns-pr.md) and [selection theme](2026-11-14-dropdown-selection-theme.md) are historical. Review fixes in `46af697aa045882507c721012de05055c627ca00` addressed Tab trapping and WPF test initialization. Supplied CI output reports all four jobs passed at reviewed head `3da9a69d022f6160a73913c69d5da0e7dd9298c5`; it does not validate the later uncommitted sorting change. Manual visual inspection remains unverified.

## Follow-up and reconsideration

Run the new WPF regression on Windows and check both actual selectors, filtering, dynamic catalog changes, selected item and rotation order. Reconsider the comparison or snapshot approach if users require locale-aware sorting, duplicate-label handling, accessibility, or performance changes. No release or PR for the alphabetical change is established; no additional alternatives or user rationale are recorded.
