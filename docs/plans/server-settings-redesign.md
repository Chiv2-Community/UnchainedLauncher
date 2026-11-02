# Server settings redesign

## Contract

- Put mods and plugins under global-settings or individual servers, not the root.
- Both scopes define always-on ini/mod-actors, ordered map-settings, and match-presets.
- Rotation entries accept a preset string or a mapping with optional preset, map, name, ini, and mod-actors.
- Resolve the final map before matching wildcard rules.
- Precedence: global always-on → server always-on → specificity-ordered map rules → specificity-ordered presets → inline rotation settings; equal specificity preserves global/server and declaration order.
- INI merges by category/section/key; later arrays replace earlier arrays.
- Serialization requires an explicit root-config boolean: WindowsNoEditor standalone files resolve commands into repeated plain keys with no array operators; native map/profile/index layers retain operators. Empty root arrays omit the key.
- Actors form an ordered deduplicated union; later layers cannot remove always-on actors.
- Server rules replace same-named global definitions case-insensitively, matching native folder names.
- Rule keys support exact names or one trailing ~ prefix wildcard; ?, middle/multiple ~, and * are rejected. Map and preset matching are case-insensitive. Longer prefixes beat shorter prefixes; exact rules win. This supersedes the launcher's earlier general-glob and case-sensitive-preset contract.
- Every rotation slot must resolve to a concrete map after matching presets and inline settings; missing maps report the slot number and preset reference before generation.
- StructuredINI emits replacement arrays as !Key=ClearArray followed by +Key entries, using .Key for duplicates; imported operations, including -Key removals, remain unchanged.
- Quoted YAML field prefixes +, -, ., and ! select explicit INI array operations. Delta-only fields extend earlier command streams in order; ordinary fields and clears replace them. Known arrays retain their declared type; unknown primitive arrays infer from YAML. Empty deltas are no-ops.
- Unknown INI fields infer primitive scalar/list types from YAML, including numeric-list promotion; explicit type/value wrappers remain supported for conversions and structured records. Quoted strings retain their text. Nulls, heterogeneous inferred lists, nested arrays, and nonfinite numbers are rejected.
- Packages combine across used slots and always-on settings; conflicting versions are rejected.
- Names remain readable and case-insensitively unique within each server rotation.
- !load substitutes one local YAML document, relative to its containing file, without adding precedence.
- INI accepts a directory, category-to-file mappings, inline mappings, or mixed categories.
- Unchanged INI files may be hard-linked; merged files are generated separately; cross-filesystem copies are allowed.
- Plugin output uses ordered match-rotation entries plus native maps/<rule>/, profiles/<rule>/, and indices/<slot>/ files. Never flatten map/preset/inline settings or copy baseline INI into conditional files; runtime selection and merging belong to the plugin. The map-config-hierarchy branch is authoritative.
- Per-map actor activation requires a companion plugin change; do not union all slots at launch.
- Preserve explicit INI restoration safeguards until runtime restoration is verified.
- Preserve unmixed legacy server configurations; reject mixed legacy/layered schemas with migration guidance; preserve client behavior.
- Wildcard map rules require an explicit rotation; always-on settings alone preserve the saved rotation.

## Checklist

- [x] YAML composition: local !load, nested relative paths, bounded expansion, cycle checks, source-aware errors.
- [x] Shared input settings models and immutable per-server/per-slot resolved models.
- [x] Precedence resolver, presets, wildcard matching, actor union, server isolation.
- [x] INI directories/files and lossless representation of repeated keys/array directives.
- [x] Source-relative paths for resources referenced by included YAML.
- [x] Native layered file generation, safe hard-link/copy behavior, content-addressed generations.
- [x] Global/server package resolution and conflict validation before installation.
- [x] Plugin contract and pre-consumption mod-loader hook for per-map actor selection.
- [x] Migration diagnostics, examples, and reference documentation.
- [x] CLI Debug/Release, portable GUI, actual-plugin contract, and Linux publication validation.
- [ ] Separately authorized runtime tests for map transitions, actor isolation, and INI restoration.

## Delivery notes

- Existing launcher PR: #157; companion plugin schema PR: Chiv2-Community/UnchainedPlugin#20.
- Preserve unrelated worktrees, processes, Wine prefixes, and local annals.
- Do not push local annals commits as part of implementation.
- YAML composition preserves inactive-schema skipping and document-local anchors.
- Runtime map/preset folders retain rule names and trailing ~ wildcards. Repeated preset references receive distinct aliases and copies of only their exact preset layer; wildcard layers remain shared. Inline settings go only in zero-based index folders.
- INI snapshots are cached before linking; source edits cannot change an existing generation.
- Arrays changed per map require an explicit always-on replacement baseline; per-map deltas extend that baseline so other maps can restore it.
- INI publication is atomic and never writes through an existing hard link.
- Concurrent codec lookup exposed an existing dictionary race; CodecRegistry now uses ConcurrentDictionary with a regression test.

## Validation and remaining limits

- CLI: 816 tests pass in Debug and Release for separate native layers and operator-free root output, including six-slot repeated-map order; the earlier flattened-profile run is superseded.
- CLI YAML inputs, including legacy, invalid-schema, include, and argument-boundary cases, live under test-resources; runtime substitutions use resource placeholders instead of inline YAML documents.
- StructuredINI: 48 tests pass in Release, including explicit root/layer serialization modes, ordered root array materialization, empty arrays, and all four layer array operations.
- Portable GUI: 79 tests pass.
- Actual plugin module harness: 15 tests with map_profiles, 9 without; Debug/Release generated-file and mod-loader hook checks pass for three slots.
- Native inode check confirms an unchanged imported file links to its cached snapshot, not the user source.
- Linux single-file publication/extraction/help passes (47,504,610 bytes).
- Windows Rust compile-check, including tests and map_profiles, passes; the local winres toolkit override was removed afterward.
- Full Windows DLL linking was attempted but hit disk quota; only this task's generated Cargo artifacts were cleaned.
- User authorized a Steam server launch with the map-config-hierarchy DLL; its native format supersedes the earlier plugin harness results above.
- Native plugin mod-actors entries are display metadata only; per-slot actor activation from the earlier companion branch must not be claimed for this branch.
- Live transition and restoration results must be recorded separately from generated-file tests.
- Steam live startup succeeded in an isolated copy of the prefix with the map-config-hierarchy DLL (SHA-256 `1d36d09c17b1101b0299a84113a3cc05d7590215cf560aacc694f74224c742ea`); process mappings confirmed that DLL and A2S returned `CLI Map Hierarchy Live` on `FFA_Courtyard` at `192.168.1.31:7097` (game port `7797`).
- Live INI values, travel/restoration, and per-slot actor behavior remain unverified; the plugin registered with its backend despite parsing `register: false`, and the launcher status still reported A2S unavailable despite a successful direct query.

### Live layering follow-up

- Fixture: `test-resources/settings-resolution/inline-map-preset/server.yaml`; expectations are documented beside it.
- Live config and evidence: `/tmp/opencode/steam-map-hierarchy-live/` (`server.yaml`, `layering-results.json`, `layering-slot-0.log`). The test backend now points at unused loopback port 1 to prevent public registration.
- Startup failure confirmed by RCON `getall` on the live GameMode/GameSession and direct A2S: slot 0 loads Courtyard, but MaxPlayers remains 24 instead of 16, low/high bots remain 2/2 instead of 8/6, and match wait remains 15 instead of 35. Generated profile files contain the expected values.
- `tbsendgame 1` did not advance the map within 100 seconds; natural rotation is inconclusive. Attempts to select the live rotation index with `set` (class and explicit object targets) left it at 0, so the controlled script stopped before issuing `servertravel`; no travel/restoration success is claimed. See `controlled-results.json`.
- Source inspection points to startup context selection: the plugin obtains profile indices from pending `ProcessServerTravel` context, while initial `LoadMap` starts without an index. This is a suspected cause, not a validated fix; the plugin worktree was not changed.
- The user rejected flattened per-slot generation after these tests. The new generator preserves all native layers; the preceding live results describe the old flattened output and must not be attributed to the new files.
- New native output was launched from generation `69bd6ff631a439afe55bd1502145ab4eab49cd3491a29bd08ebd50ef6865073d`. Startup RCON reads confirm Courtyard's exact map layer: live MaxPlayers=16 and high bots=6; baseline low bots=2 and match wait=15 remain because no profile/index is selected at initial load. The saved baseline still contains MaxPlayers=24 and high bots=2. Evidence: `/tmp/opencode/steam-map-hierarchy-live/native-startup-map-layers.log`.
- Native-layer travel evidence is in `native-results.json` and `native-*.log` under the same test directory. Only final `/Game/...` GameMode objects are inspected; temporary seamless-transition worlds must not be counted as destination results.
- Courtyard → Wardenglade successfully changed MaxPlayers 16→12, high bots 6→9, and spectate timer 180→99. On TDM Wardenglade, map fields restored (MaxPlayers=24, high bots=2, spectate=180), but low bots=8 and match wait=35 remained instead of baseline 2/15. Profile/index restoration is not validated; this is a runtime failure, not a passing layering test.
- Returning from TDM Wardenglade to Courtyard ended the server with code 3 after an access violation at `0x1436D6E0E` (read `0x1E31A0940`). No root cause is established. The test server was left stopped, the plugin source was not changed, and saved baseline INI still contains MaxPlayers=24, low/high bots=2/2, and match wait=15.
- Those live runs predate the root-config serialization fix: standalone WindowsNoEditor INIs incorrectly contained array operators. They cannot establish behavior with valid root files; see the post-fix rerun below.
- Post-fix CLI rerun: root INI and live GameMode Maplist both retain all six entries in YAML order, without root operators. Startup still misses preset/index values (low bots=2, wait=15 rather than 8/35). Natural end-match after InProgress reaches Wardenglade with all expected fields (MaxPlayers=12, low/high bots=8/9, spectate=99, wait=25), but its new GameMode reports MapListIndex=0. Ending that match reloads Wardenglade rather than the next Courtyard slot; the 180-second destination wait expires. Cause is not established. Evidence: `/tmp/opencode/steam-map-hierarchy-live/root-fixed-launcher.log` and `root-fixed-rotation-results.json`.
- Post-fix controlled travel passes all five destination checks: Courtyard preset+index=16/8/6/180/35; unmatched Wardenglade=12/2/9/99/15; baseline TDM Wardenglade=24/2/2/180/15; Courtyard return=16/8/6/180/35; wildcard-only Bazaar=20/2/4/180/15 (MaxPlayers/low bots/high bots/spectate/wait). Previous profile/index leakage and return-travel access violation were not reproduced. This does not validate progression through all six slots. Evidence: `root-fixed-manual-results.json`; shutdown requested only for owned launcher PID 403650 after testing.
- Folder-based YAML-to-INI golden cases live in `test-resources/server-configuration/`; optional input profile trees seed existing INI before generation.
- Rotation writing reuses StructuredINI serialization through typed buffer merging, including shared Unreal array operations.
- Removed the CLI's separate INI import parser and array formatter. Imports use StructuredINI's untyped reader, shared with model deserialization; inline fields use the same entry serializer as model serialization. Existing imported directives remain intact.
- Shared-reader and model/dynamic serialization parity tests cover replacing inherited arrays, empty arrays, and duplicates.
- Nested includes, imported-array overrides, startup defaults, composed patches, and typed values use golden fixtures rather than duplicate procedural output tests. All fixtures check two generation passes. Focused validation, path/argument selection, immutable-source, changed-generation, and failure checks remain.
- Preset wildcard fixtures cover overlapping patterns, server replacement, inline map overrides, and missing-map diagnostics.
- The mixed-map-preset-rules fixture combines wildcard/exact map and preset rules across global/server scopes, preset rotation references, and an inline map/INI override; each complete output tree is checked twice.
- Array-operation fixtures cover YAML/imported deltas, typed record removal, inherited engine values without an implicit clear, explicit clears, and no-op lists. The legacy profile regression also exercises removal. Generation replaces each configured field's command stream rather than accumulating commands from previous output files.
- The existing INI reader restores valid saved MapListIndex values before launch-option creation; explicit starting-map overrides them, and invalid saved indices require an explicit selection.

### Lifecycle instrumentation follow-up (2026-11-01)

- Evidence: `/home/jbarber/.cache/opencode/map-index-trace-live/experiment.md`, instrumented logs, and JSON results. Diagnostic snapshots leave the authoritative plugin worktree unchanged; these are not published fixes.
- Actual rotation tracing establishes index loss: GetNextMapName changes live index 0→1; SaveConfig writes 1 into the private NoSave overlay while baseline remains 0; destination creation and PostLoadMap initialize at 0 despite selected slot 1. This supersedes the unknown-cause qualification above.
- Controlled comparison uses the same DLL with a marker-gated baseline update for MapListIndex only. Natural Courtyard 0→Wardenglade 1→Courtyard 2 passes index, layer and restoration checks; overlays remain NoSave. No conditional settings are copied into the baseline.
- Separate fresh-process startup capture reads configured baseline index 1 before selecting gameplay layers. Wardenglade starts and remains at index 1 with MaxPlayers=12, bots=8/9, spectate=99, wait=25; all checks pass. Startup slot selection does not infer a slot from repeated map names.
- Full six-slot natural progression and wraparound pass with both diagnostic changes: 0→1→2→3→4→5→0, all 42 live field/index checks passing. Repeated Courtyard slots correctly distinguish preset+inline (wait 35), no preset (15), and preset-only (25); TDM restores baseline and Bazaar applies wildcard-only values. PostLoadMap retains each selected index. Evidence: `matrix-results.json`, `matrix-step-*.log`, `matrix-launcher.log`.
- Process mappings verify diagnostic DLL `19555317f094dc6990b5d9a83bf47ea4928eb54a27c407aedf07a74d77b6fa74`. Final disk baseline retains scalar defaults without conditional leakage; MapListIndex is absent on disk, so index progression is established by in-memory config and live GameMode reads. Owned launcher 526083 was sent SIGINT after testing. Registration opt-out and launcher A2S status remain separate issues. No crash reproduced; the suspected world-lifetime race is not ruled out.

Logs and external contract harness:

- `/tmp/opencode/settings-redesign-{debug,release,gui,publish}.log`
- `/tmp/opencode/settings-structured-ini-{debug,release}.log`
- `/tmp/opencode/settings-plugin-{contract,contract-release,contract-tests,default-tests,windows-check-final,windows-build}.log`
- `/tmp/opencode/map-profile-contract/`
- `/tmp/opencode/ini-wildcard-config-cli-{debug,release}.log`
- `/tmp/opencode/ini-array-operations-library-{debug,release}.log`
- `/tmp/opencode/ini-array-operations-gui-debug.log`
- `/tmp/opencode/ini-explicit-array-operations-{cli,library}-{debug,release}.log`
- `/tmp/opencode/ini-explicit-array-operations-gui-debug.log`
