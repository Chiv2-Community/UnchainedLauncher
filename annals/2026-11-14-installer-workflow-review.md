# Installer workflow review on the 1.0 hotfix

- Date: 2026-11-14
- Status: proposed (remedies only; no fix accepted or implemented in the reviewed conversation)
- Scope: installer navigation, selection, failure reporting, and startup scan
- Repository: [UnchainedLauncher](https://github.com/Chiv2-Community/UnchainedLauncher.git)
- Branch at review and recording: `review/installer-workflows`
- Reviewed and recording HEAD: `57b6c5582ea3f5ca39ef77b5980af9f4c0135822`; no exact tag; nearest tag `v1.0.1`.
- Source: user request and build agent review, session `ses_e23bab0b0ffe23is16dxUKhyAq`, response `msg_1dc48a704001E26L9aWryymi6U`. Working tree was clean at review; this record is the only subsequent change at recording.

## Context and outcome

The user requested a bug review on the latest `hotfix/v1.0.x`, especially backward navigation. The agent created a separate worktree at the fetched hotfix head and reviewed without changing installer implementation. Five problems were reported; the review suggested remedies, but neither the user nor an implementation accepted a particular design. No alternatives or original design rationale are documented.

## Evidence and findings

The source-linked .NET harness used actual view-model code and Fody weaving, with Windows dialogs and services stubbed. The supplied transcript reports a successful run. Source inspection at the pinned commit supports the following:

- The first page exposes Back, and the window decrements its page index without a boundary check; the harness observed an exception and index -1. See [selection](../UnchainedLauncher.GUI/ViewModels/Installer/InstallationSelectionPageViewModel.cs) and [window](../UnchainedLauncher.GUI/ViewModels/Installer/InstallerWindowViewModel.cs).
- [InstallerLogPageViewModel](../UnchainedLauncher.GUI/ViewModels/Installer/InstallerLogPageViewModel.cs) ignores the installer's boolean result and reports success even for failed or empty target sets. [The installer](../UnchainedLauncher.Core/Services/Installer/UnchainedLauncherInstaller.cs) can return false.
- Browsing adds an already-selected target without recomputing `CanContinue`; rescanning clears selections without recomputing it. The harness observed both disabled Continue after Browse and enabled Continue after rescan with no selected targets.
- Initial loading runs a scan on a worker thread and mutates the bound collection; the window discards the load task. The harness observed off-thread events and simulated a thread-affinity rejection. An actual WPF failure was not tested.

The temporary review report and harness were outside the repository and may not persist. No Windows UI, real installation, download, or game files were exercised. The source transcript is the available provenance; earlier conversations or design history were not established.

## Proposed follow-up and consequences

The reviewer suggested bounding navigation, recomputing selection validity on collection changes, checking nonempty targets and per-target installation results, and dispatching collection updates to the UI thread while observing load failures. These are proposals, not accepted decisions. Until addressed, users may see a navigation exception or a false success message; actual WPF timing and user impact remain unverified. Reconsider the findings against a later implementation and Windows UI regression tests. No issue or pull request was identified in the supplied review.
