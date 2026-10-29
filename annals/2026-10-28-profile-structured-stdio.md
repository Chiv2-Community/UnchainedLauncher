# Profile-specific structured stdio and Proton helper

- Date: 2026-10-28 (local; review completed 2026-10-29 UTC)
- Status: implemented; PR validation incomplete
- Scope: server log capture, Proton launch boundary and test safety
- Repository: [UnchainedLauncher](https://github.com/Chiv2-Community/UnchainedLauncher); branch `review/pr156-loop-20261029`; recording HEAD `04520b83211fab93e08144571ff3e4926d98580f`.
- Exact tag: none; nearest tag: `v1.0.0-RC4` (`v1.0.0-RC4-122-g04520b8`). Working tree clean before this record.
- Implementation: [0bc4825](https://github.com/Chiv2-Community/UnchainedLauncher/commit/0bc4825826f7c56393033488ad5151b2f8a866a1); preceding stdio transition [49c0ea8](https://github.com/Chiv2-Community/UnchainedLauncher/commit/49c0ea8c553eeb64119d668def887028d38449e7); review fix [04520b8](https://github.com/Chiv2-Community/UnchainedLauncher/commit/04520b83211fab93e08144571ff3e4926d98580f); [launcher PR #156](https://github.com/Chiv2-Community/UnchainedLauncher/pull/156), base `8be3d51efeb037d9da83949b41143b3763b4905e`.
- Sources: user and agents, session `ses_e9a73bf7affeZOgu4IFxMPFwmQ` through response `msg_185f15e6d001T73TP8m0AgnwsQ`; review ledger and independent pass-2 report supplied in the checkpoint. Earlier conversation was compacted. A separate, unpushed annals-only commit `b0af7373067b1934187f3d5422c7835f67b809fc` records the structured-stdio decision at `0bc4825` on another worktree; it is not an ancestor of this record and was left untouched.

## Decision and context

Use native log4rs JSON over ordinary captured stdout for server plugin logs, rather than a dedicated logging transport or custom envelopes. Generate `<saveddir>/unchained/logging.yaml` only when absent and pass it via `--log4rs-yaml`. Existing configuration and explicit overrides win; Proton converts the generated path to a Windows path. Clients do not opt into this server configuration. See [PluginLogging.cs](../UnchainedLauncher.CLI/PluginLogging.cs) and [CLI documentation](../UnchainedLauncher.CLI/README.md).

Recognized records are rendered as time, level, source and message; malformed or non-JSON diagnostics remain text. Structured attributes are not displayed in the formatted line. Bounded capture and sanitization remain. The old raw display of recognized JSON and earlier dedicated transport are superseded, not shown to be defective. A separate JSON file appender can retain attributes.

The existing ProtonHelper is an implementation choice, not an inherent requirement. Review considered a smaller MinGW helper, proxy loader and native Wine-aware injection, but established no replacement preserving suspended startup, injection validation, stdio and supervision with a verified net simplification. No helper rewrite was authorized. Normal tests must not open console windows; desktop-console tests remain opt-in.

## Evidence, rationale and consequences

The source conversation attributes the structured logging request to the user; the prior record and review report identify native log4rs and inherited streams as the selected approach. Complete rationale for every formatting and ownership detail is unavailable. The independent review compared generated YAML with plugin defaults and reported that text file appenders and filters remain; existing files are preserved by non-overwriting publication. Ordinary stream writes can block on slow readers and initialization output can be non-JSON.

The review ledger reports two independent full reviews and one fix leg. The final fix corrected Epic authentication guidance and removed unused environment-override machinery, without replacing the helper. It reports 697 CLI tests in each Debug and Release configuration, 79 portable GUI tests and Linux single-file publication; these results were not rerun for this record. The earlier structured-logging record reports a plugin/log4rs YAML smoke test, not an end-to-end game launch. Windows runtime and real game validation remain outstanding. Product CI did not run against the `server-cli` base according to the review; Mergify checks are not product-build validation. The review report states companion [plugin PR #17](https://github.com/Chiv2-Community/UnchainedPlugin/pull/17) merged as `6c2eacbc9e5bb7784b0713f3fbf0cf17348571d9`; this was not independently verified here.

## Reconsideration and gaps

Reconsider if plugin JSON fields or path semantics change, full structured attributes are needed in the dashboard, configuration drifts from plugin defaults, or real Windows/Proton game validation exposes incompatibilities. Any helper replacement needs evidence that it preserves the boundary contracts. Verify mixed output, overrides and path mapping in actual deployments. Earlier discussion and exhaustive rationale are unavailable; test and remote PR claims above are attributed to supplied artifacts rather than independently rerun or queried.
