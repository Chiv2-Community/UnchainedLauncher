# Theme-aware settings divider

- Date: 2026-11-13
- Status: implemented locally; visual validation pending
- Scope: v1 settings WPF divider
- Repository: [UnchainedLauncher](https://github.com/Chiv2-Community/UnchainedLauncher); remote `https://github.com/Chiv2-Community/UnchainedLauncher.git`.
- Branch: `fix/settings-dark-mode-separator`; recording HEAD and hotfix base `ee9f68ce308efcb4d92083c3f6d7b72f34b6be3b`; no exact tag; nearest reachable tag `v1.0.1`.
- Source: user and build assistant, session `ses_e2932b02dffeuXwa2vpOK6PJ90`, through response `msg_1d6ce7ff4001d3JcGgZi6mOn8t`. Private reasoning and any unavailable earlier history are not included.

## Decision and evidence

The user reported a bright white horizontal separator in dark settings and requested the same color as other borders. The assistant replaced the settings `Separator` with a bottom-only, one-device-independent-unit `Border` using dynamic `Brush.Border`, preserving the margin and theme resource. Other dividers already use this brush and border pattern. The previous separator already set its background to the same brush; the assistant attributed the bright edge to the default WPF template, but the supplied evidence does not independently establish that cause.

The assistant chose the border pattern to avoid the suspected template edge while retaining theme switching. Keeping the existing separator is the observed prior approach; no other alternatives or rejected designs are recorded. Dark and light resource dictionaries define different border colors. The visual result and live theme switching remain unverified on Windows.

## Implementation and consequences

The change in `UnchainedLauncher.GUI/Views/Settings.xaml` is uncommitted at recording; HEAD does not contain it. The supplied transcript reports that the GUI Debug cross-build succeeded with warnings using `-p:EnableWindowsTargeting=true`; the first attempt without that property failed on a referenced Windows project. This review verified the working-tree diff but did not rerun the build or inspect the GUI. A successful cross-build does not prove visual correctness.

Verify the divider in both themes and during theme switching on Windows. Reconsider the approach if visual appearance, spacing, accessibility, or theme updates differ from other dividers. No issue, PR, implementation commit, release, or further design discussion is established by the source.
