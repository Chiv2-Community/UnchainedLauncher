# Server configuration fixtures

Each case contains:

```text
<case>/input/server.yaml
<case>/input/...                 # Included YAML and referenced INI files
<case>/input/<server-id>/...     # Optional existing profile files, mirrored to output before generation
<case>/output/<server-id>/Config/WindowsNoEditor/*.ini
<case>/output/<server-id>/unchained/ini-overrides/unchained.yaml
<case>/output/<server-id>/unchained/ini-overrides/maps/<rule>/*.ini
<case>/output/<server-id>/unchained/ini-overrides/profiles/<rule>/*.ini
<case>/output/<server-id>/unchained/ini-overrides/indices/<slot>/*.ini
```

- Add a case folder to add a test; no test registration is needed.
- Single-server cases use `server` as the output server ID.
- Tests copy inputs to a temporary directory and use the production parser, resolver, and INI writers without launching games.
- Expected files are reviewed, checked-in outputs; tests never regenerate them.
- The complete output file set and contents must match, including startup defaults and plugin metadata.
- Each case generates twice against the same profile and must match on both passes, checking idempotence without separate relaunch tests.
- Only CRLF/LF differences are normalized; native folder names and every file are checked exactly.
- Profile references retain preset names; repeated references get distinct aliases containing only the exact preset layer. Baseline, map, and inline values must not appear in those files.
- The generated outer `map-config/<hash>` directory is omitted; caches are not consumable profile output and are not compared.
- Inputs must remain unchanged.

Cases cover always-on precedence, empty/repeated arrays, wildcard/preset/inline precedence,
rotation starting indices, repeated presets, nested YAML loads, INI directories/files,
inline array replacement over imported INI, and independent multi-server defaults and preset definitions.
The `saved-index` case seeds an existing `Game.ini` and verifies that its rotation index and unrelated fields survive.
`startup-defaults`, `profile-patches`, and `rotation-patches` cover existing profiles,
untouched files, pending startup/INI/rotation merges, and raw-value exclusion.
`typed-values` covers inferred fields, typed wrappers, named/positional records,
callbacks, and multiple INI categories.
`preset-wildcards` covers `~` preset/map rules, ordered overlapping presets,
server replacement of global definitions, and final-map matching after inline overrides.
`mixed-map-preset-rules` combines wildcard and exact map/preset rules across both scopes,
preset rotation references, and an inline map/INI override. Its slots resolve
`MapValue/PresetValue/SlotValue` to `2/4/0`, `5/6/0`, and `1/4/9` respectively.
Generated arrays clear inherited values, use `+` for first occurrences and `.` for duplicates;
imported `!`, `+`, `.`, and `-` directives remain unchanged.
`array-operations` covers YAML and imported deltas across always-on, map-rule,
preset, and inline layers; `inherited-array-operations` verifies that delta-only
fields retain Unreal's inherited values without an implicit clear.
`inferred-types` covers concise scalar/list forms, numeric promotion, precise and large
numbers, quoted/tagged strings, empty arrays, explicit conversions, and known game types.
The mixed map/preset and array-operation cases also use concise YAML values.
INI inputs use concise values wherever inference applies; explicit wrappers remain
only for wrapper/conversion coverage, raw overrides, and unknown structured types.

Keep focused tests for behavior not established by output snapshots: validation errors,
actor deduplication and launch selection, source isolation after edits, changed generations,
file-publication failures, and legacy configuration compatibility.

Run: `dotnet test UnchainedLauncher.CLI.Tests --filter FullyQualifiedName~ServerConfigurationFixtureTests`
