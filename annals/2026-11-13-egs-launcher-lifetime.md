# EGS launcher follows game lifetime

- Date: 2026-11-13
- Status: accepted; implementation uncommitted
- Scope: GUI client and server launches on Epic Games Store (EGS)
- Repository: [UnchainedLauncher](https://github.com/Chiv2-Community/UnchainedLauncher)
- Branch: `fix/egs-exit-after-game`; recording HEAD and hotfix base: `52f769704bc3bf872acc66142b7a2e49a9f50eed`; exact and nearest tag: `v1.0.1`.
- Source: user and build assistant, session `ses_e295a6ddbffe8btLL4jkqo4AvT`, through response `msg_1d6ab1504001ZqkX4ArrmWWRSy`. This review uses the supplied transcript; any unavailable or compacted history is unknown.

## Decision and rationale

The user requested that an EGS launcher remain alive until its launched Chivalry 2 client or hosted server exits, then exit, because EGS launcher sessions cannot be reused. The build assistant chose to close without retrying after any exit and to preserve Steam behavior. The prior client path closed immediately after a successful non-reusable launch; the server path could remain open after exit. Keeping either behavior was not consistent with the requested lifetime. No other alternatives or detailed rationale were recorded.

## Implementation and evidence

The uncommitted changes in `UnchainedLauncher.GUI/ViewModels/HomeVM.cs` hide the client window, attach the existing actual-game-process watcher, and dispatch shutdown on non-reusable exit before retry handling. `ServersTab/ServersTabVM.cs` removes and disposes the server and dispatches shutdown on non-reusable exit. Reusability is currently defined as Steam versus all other installation types, not an explicit EGS-only predicate. A client watcher attachment failure still shuts down immediately; a server attachment failure only logs a warning, so waiting and eventual shutdown are not guaranteed in that case.

The working tree also contains uncommitted client and server lifetime tests. The source build succeeded and the Wine test logs report 24 focused tests and 316 full unit tests passing, with no skips. These tests use mocked watcher callbacks; they do not establish actual EGS or native Windows game-process behavior. The build and tests preceded a line-ending-only rewrite of the two GUI files in the supplied transcript. No implementation commit, PR, or release is established.

## Consequences and follow-up

Non-reusable sessions no longer attempt client or server retry after a watched exit; Steam retains its existing retry paths. Validate real EGS client and headless/non-headless server lifetimes, watcher resolution and attachment failures, and shutdown behavior on native Windows before release. Reconsider the policy if EGS becomes reusable or actual process monitoring cannot reliably identify the launched instance. The desired server failure fallback and behavior for other non-Steam installation types remain unspecified.
