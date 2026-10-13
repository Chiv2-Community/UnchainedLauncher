# Plugin Manager workflow and update boundaries

- Date: 2026-10-12
- Status: accepted
- Scope: native-plugin user interface, persisted update settings, and manual update behavior
- Repository: UnchainedLauncher
- Canonical remote: https://github.com/Chiv2-Community/UnchainedLauncher.git
- Branch: `feature/dll-validation`
- Git provenance: `aa8642601f9ed490ce0462e8a492ab989c0b0140` (`v1.0.0-RC4`)
- Implementation state: uncommitted working-tree changes pinned to the Git provenance above
- Authoring agent: Annalist (GPT-5.6 Terra)
- Source: supplied conversation checkpoint; local uncommitted view-model, XAML, test, and README artifacts inspected. No source session or response ID was available in the supplied checkpoint.
- Related record: [DLL registry package metadata and target filtering](2026-10-12-dll-registry-package-metadata-and-target-filtering.md)

## Decision summary

Rename the native DLL management experience to **Plugin Manager** and give it the
same discovery workflow as Mod Manager: search, shared sorting modes, explicit
refresh, per-plugin version selection, and a manual update action. Keep the two
existing persisted plugin settings at the bottom of that page, in separate
columns below a themed divider.

Manual updates refresh metadata and consider only enabled plugins that are still
visible in the current filter. They require confirmation, select a newer
registered version when approved, and do not download DLLs or enable disabled
plugins. Unstable releases participate only when the existing unstable-release
setting permits them. Download and validation remain launch-time responsibilities.

## Context and evidence

The earlier registry decision established package-backed discovery, launch-side
target filtering, and the existing automatic-download and unstable-release
preferences. The supplied checkpoint requested a Plugin Manager rather than a
DLL Manager, with Mod Manager-like controls, while preserving those preferences
and avoiding automatic enablement or immediate downloads.

`DllManagerVM` groups releases into one card per repository, searches names,
repository IDs, descriptions, and authors, and uses the shared `ModSortMode`
values. Its `UpdatePlugins` command refreshes first, derives update candidates
from the displayed enabled cards, asks through `IUserDialogueSpawner`, and calls
`SetEnabled` only for approved candidates. It does not invoke download code.
`MainWindow.xaml` labels the tab Plugin Manager and binds search, sort, refresh,
and update controls. It binds the existing settings directly to the same
settings interface below the card list. The divider is a one-pixel top border
using `Brush.Border`; footer status text was removed from the view, while errors
are reported through dialogs.

Focused GUI tests verify prerelease visibility without losing a saved selection,
search and sort behavior, the enabled/visible update boundary, confirmation,
the absence of DLL-download requests during manual update, and the exact footer
layout. The checkpoint reports 15 GUI tests passing in Debug and Release and
successful Windows GUI builds, but those results were not independently rerun by
this review.

## Options considered

### Retain a separate DLL Manager UI

Rejected. Native registry packages now represent user-manageable plugins, and a
separate reduced workflow would make selection and discovery inconsistent with
the established Mod Manager interaction model.

### Let manual update affect all enabled plugins or download immediately

Rejected. Restricting the action to visible enabled cards gives search a useful
scoping role, does not surprise users by enabling unrelated plugins, and keeps
the managed-download integrity checks and automatic-download preference at the
launch boundary.

### Add a new prerelease-display preference or discard hidden selections

Rejected. The existing unstable-release preference remains the single saved
control for prerelease eligibility. An enabled prerelease can be hidden when it
is turned off without being silently disabled.

### Keep settings in the general Settings page or use a status footer

Rejected. The controls concern plugin behavior and belong with plugin actions.
The requested bottom placement uses a themed visual division; actionable errors
use dialogs rather than a persistent footer message.

## Consequences and follow-up

The page gives users an explicit way to inspect and select registry-approved
native plugins without broadening the injection trust boundary recorded in the
related annal. Manual updates still alter persisted selected versions after
confirmation, so users must understand that download and validation occur at
the next eligible launch.

The implementation remains uncommitted. Live GUI verification of the final
footer edit was not performed: publishing a fresh Wine GUI output failed with
`Disk quota exceeded`, and the existing local published output predates that
edit. Live Windows game injection is also unverified. Reconsider the workflow
if users need an update scope independent of search, if persisted settings prove
incompatible with the relocated controls, or if visual testing finds the footer
layout unusable.

## Historical gaps

No issue, pull request, original design transcript, source session ID, response
ID, or independently retained GUI-test logs were available. The rejected-option
rationale is taken from the supplied checkpoint and corroborated where possible
by the current implementation and tests.

## Subsequent developments (2026-10-12)

The statement in Decision summary that unstable releases participate only when
the existing setting permits them is superseded for initial startup of a
prerelease launcher build: prerelease builds now enable the setting regardless of
a saved false preference. Stable builds retain the same-version saved-preference
behavior. See [Plugin Manager links and prerelease startup default](2026-10-12-plugin-manager-links-and-prerelease-startup-default.md). This does not change
the manual-update boundary or the decision to use the existing setting rather
than a separate prerelease preference.

## Subsequent developments (2026-10-12)

The implementation-state claim at lines 90-93 is superseded: the Plugin Manager
work was included in commit `c8bd89504e6f4a789456882b4ca6c97b1e8c577e`, pushed
on `feature/dll-validation`, and submitted as [PR #152](https://github.com/Chiv2-Community/UnchainedLauncher/pull/152).
The PR remains open; its creation does not establish live game-injection
validation. See [DLL-validation implementation committed and submitted](2026-10-12-dll-validation-implementation-committed-and-submitted.md).
