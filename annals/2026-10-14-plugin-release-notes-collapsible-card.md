# Use the shared CollapsibleCard for Plugin Manager release notes

- Date: 2026-10-14
- Status: implemented
- Scope: Plugin Manager release-notes presentation in the main GUI
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/dll-validation`
- Git provenance: `f93230686031d6d0f6c718a0b3d96f22482d8034` (`v1.0.0-RC4-31-gf932306`); no tag points at this commit
- Pull request: [#152](https://github.com/Chiv2-Community/UnchainedLauncher/pull/152), reported open in the supplied checkpoint
- Authoring agent: Annalist (GPT-5.6 Terra)
- Source: supplied conversation checkpoint; committed XAML and regression test inspected. No source session or response ID was supplied.
- Related records: [Plugin Manager workflow and update boundaries](2026-10-12-plugin-manager-workflow-and-update-boundaries.md); [Preserve registry approval across unchanged Plugin Manager refreshes](2026-10-14-plugin-card-refresh-approval.md)

## Decision summary

Present each Plugin Manager release-notes section with the shared `CollapsibleCard`, initially collapsed, rather than a standalone WPF `Expander`.

## Context and evidence

The supplied checkpoint states the requirement that Plugin Manager release notes use the shared `CollapsibleCard`, matching Mod Manager Contents. Commit `f932306` adds the `controls` namespace to `MainWindow.xaml` and replaces the `Expander` at the Plugin Manager release-notes location with `controls:CollapsibleCard Header="Release notes" IsExpanded="False"`.

The existing release-notes text binding and the command that opens the GitHub release remain inside the card body. `PluginReleaseNotesCardTests` parses the packaged XAML and verifies one matching shared card, its collapsed default, and both bindings. The checkpoint reports focused GUI tests passed in Debug and Release and that a local Wine GUI build was opened and later closed; the exact commands, raw output, and user interaction result are unavailable.

## Options considered

### Keep the standalone `Expander`

Rejected by the supplied requirement. It left Plugin Manager release notes outside the shared card presentation used for the comparable Mod Manager content.

### Use the shared `CollapsibleCard`

Chosen. This directly satisfies the stated shared-control requirement while retaining the release-notes and original-release-link behavior.

No evidence identifies other evaluated presentation controls or a broader redesign.

## Consequences and follow-up

The Plugin Manager now uses the common collapsible-card visual and interaction pattern, and release notes remain hidden until expanded. The structural regression protects the specific header, collapsed default, text binding, and GitHub command binding, but it does not prove runtime appearance or accessibility behavior.

Reconsider only if the shared control changes its required content contract or the Plugin Manager needs release notes to have a materially different interaction model. Confirm the presentation in a Windows GUI environment before treating the visual result as fully verified.

## Historical gaps

The source checkpoint does not identify the responsible implementation agent, source response ID, exact test commands and output, or a completed user assessment of the Wine GUI build.
