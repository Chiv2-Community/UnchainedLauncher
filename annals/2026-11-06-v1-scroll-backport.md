# Separate v1 scroll-fix backport

- Date: 2026-11-06
- Status: implemented locally; validation and pull request pending at source response
- Scope: v1 WPF mouse-wheel forwarding, issues #158 and #162
- Repository: [UnchainedLauncher](https://github.com/Chiv2-Community/UnchainedLauncher)
- Branch: `backport/v1.0.x/scroll-fixes`
- Recording HEAD: `783d0b887bd7bb54af68f622728ce9f3c208873e`; no exact tag; nearest reachable tag `v1.0.0`.
- Base: `d0da9709fc33893b711b86d8aafd46e3e96ca56c`, merging [PR #178](https://github.com/Chiv2-Community/UnchainedLauncher/pull/178).
- Source: user and build assistant, session `ses_e5658faf6ffewA56PWUxX9QcPT`, through response `msg_1b2feedd4001nkdGCjM9c54b8c`. Earlier conversation was compacted; its full history is unavailable.

## Decision and context

The user requested the missing scrolling fixes in a separate PR, rather than adding them to the server-actor backport. The assistant chose the updated `hotfix/v1.0.x` as its base and cherry-picked both behavior fixes and their routed-wheel regression tests. This separation follows the user's request; no other rejected alternatives or rationale are documented.

Issue #158 addresses parent traversal for inline content such as hyperlinks, where visual-only traversal can fail. Issue #162 forwards wheel events through a list's own template scroll viewer while preserving input for independently nested scrollable controls. The source fixes and tests were `8acb6e45d9b81d78290d73267f5553e42092662b`, `979f07e0f3fbc61336e7ae0e16d518545138ba17`, `dea1e4b4edc4f71b5e974088f45b76cdb992c532`, and `979a86a9e52a1895901ae033f3e1ee1c0ba1d548`.

## Implementation and consequences

The local backport commits are `37a119ac9f6a3aad1bb1681710731d56a99104bf`, `d5ef2637af726f1563f305a302e4862f9454b101`, `fa49413397886036ce2366ed41f387b298b6bab4`, and `783d0b887bd7bb54af68f622728ce9f3c208873e`. They change `UnchainedLauncher.GUI/Behaviors/MouseWheelScrollForwardingBehavior.cs` and add `MouseWheelContentSourceTests.cs` and `MouseWheelListTemplateTests.cs` under `UnchainedLauncher.Core.Tests.Unit/Services/Server/`.

At the source response, the build and formatting were running; the transcript does not establish results, a published PR, or a merge for the scroll backport. The three implementation files have uncommitted formatting changes at recording time; they are outside this record. Complete validation, review, and the requested separate PR remain follow-up work, not verified outcomes. Reconsider if routed-wheel tests or native WPF use reveal broken nested scrolling or unsafe parent traversal.

The updated base already includes PR #178's actor-selection changes. The assistant stated that selections and restart requests remain per server while the catalog determines availability; the configuration's own `EnabledServerModList` and the launch-time snapshot support that design, but no new multi-server integration test is shown in the supplied conversation. Physical mouse and native game behavior remain unverified.
