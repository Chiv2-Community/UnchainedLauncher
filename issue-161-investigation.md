# Issue #161: partial fix

- PR #153 already makes checkbox enable idempotent and removes all equal selections on disable.
- This branch deduplicates saved DTO entries and emits one currently available actor per class path at launch, refreshing surviving actor metadata.
- Unavailable actors are not emitted. Their saved preferences are retained because missing registry metadata or an incomplete local scan is not proof that the user disabled them.
- Follow-up: reconcile permanently disabled selections with confirmed mod ownership, and invalidate stale scan inventories after pak changes. Do not close the whole issue yet.
- Validation: Windows-targeted unit-test project cross-compiles on Linux. WPF runtime tests still require Windows.
