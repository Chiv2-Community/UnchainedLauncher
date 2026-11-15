# Searchable dropdowns for the 1.0.x launcher

- Date: 2026-11-14
- Status: accepted; implementation uncommitted and WPF interaction unverified
- Scope: launcher GUI dropdowns on the 1.0.x line
- Repository: UnchainedLauncher, https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/v1-searchable-dropdowns`
- HEAD at recording: `be6b4b3cd76b52258cec7a78d1f12635794f9403`; no exact tag; nearest tag `v1.0.2-rc1` (one commit behind).
- Source: user and build agent, session `ses_e2309679dffejOP7aLU7PYo1Oe`, through response `msg_1dcfdc0a3001uSXNqpwnSsZidt`. The supplied conversation may omit earlier or compacted history.

## Decision and context

The user requested all launcher dropdowns combine search with the existing dropdown: show all options normally and fuzzy-filter as text is entered. The build agent chose the shared WPF ComboBox template to provide consistent behavior. Its stated intention was to preserve the current selection until a result is chosen and restore the full list on clearing or reopening.

The agent created the worktree from the fetched `origin/hotfix/v1.0.x` at the HEAD above. The existing GUI contains dropdowns with diverse item labels, including map converters and version display properties; the shared template and these views are relevant evidence. No other design alternatives are documented as considered.

## Implementation and rationale

The uncommitted changes use an independent results list rather than filtering the ComboBox's source view. The implementation comment attributes this choice to avoiding changes to the bound selection and shared views. `FuzzySearch` matches an ordered subsequence case-insensitively, ignoring non-alphanumeric query characters; this is not a typo-distance or ranked search. Per-view converters or text paths provide labels where necessary. The code includes keyboard and mouse selection, and clears the search when closing. These describe code and intent, not verified end-to-end behavior.

Evidence: `UnchainedLauncher.GUI/Resources/Themes/Inputs.xaml`, `UnchainedLauncher.GUI/Behaviors/SearchableComboBoxBehavior.cs`, `UnchainedLauncher.GUI/Services/FuzzySearch.cs`, and `UnchainedLauncher.GUI.Tests/FuzzySearchTests.cs`. The working tree contains uncommitted implementation and tests; no implementation commit exists in the supplied history. The agent's blanket claim that all dropdowns work is not established by the portable tests or compilation alone.

## Validation, consequences and follow-up

The supplied tool output reports 29 portable GUI tests passed and Windows-targeted GUI and regression projects compiled. It also reports dependency vulnerability warnings. WPF interaction tests were written and compiled, but not run on the Linux host; interactive Windows behavior remains unverified. Verify filtering, selections, label rendering, mouse and keyboard navigation, accessibility, shared data sources, and every actual dropdown on Windows before treating the feature as complete. The separate results list trades native ComboBox item handling for isolation; compatibility with existing item templates and styles needs validation.

Reconsider the approach if Windows testing exposes selection, accessibility, styling, or performance regressions, or if users need a different definition of fuzzy matching. The source does not establish rejected alternatives, a release target, or subsequent acceptance, merge, or deployment.

## Subsequent developments (2026-11-14)

The implementation-uncommitted status and the statement in Implementation and rationale that no implementation commit exists are historical, now outdated: the implementation was committed and submitted for review. The decision remains accepted; interaction validation remains open. See [implementation and PR](2026-11-14-searchable-dropdowns-pr.md).

Further review fixed Tab traversal and ran WPF regressions in Windows CI before merge. The blanket interaction-validation-open statement above is partially outdated; manual visual coverage remains open. See [alphabetical map dropdown results](2026-11-14-alphabetical-map-dropdowns.md).
