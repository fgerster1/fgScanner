# Architecture Decision Records

One file per decision, newest number last. Format: Context / Decision / Status /
Consequences (MADR-lite). A decision belongs here when a future reader would
otherwise re-litigate it — not for every implementation choice.

Write the ADR in the same commit as the code that implements the decision.

| # | Decision |
|---|---|
| [0001](0001-english-only-ui.md) | UI strings stay in English, no .resx |
| [0002](0002-auto-orient-every-angle.md) | Auto-orientation rotates to the detected angle, not 180 only |
| [0003](0003-preserve-originals.md) | The untouched capture is kept beside the edited page |
| [0004](0004-field-scope.md) | A field can be scoped to the group instead of the row |
| [0005](0005-captured-by-is-null-for-adopted-files.md) | `capturedBy` is null for retro-processed pages |
| [0009](0009-field-length-and-memo.md) | Field length and memo are settings on Text, not a new field type |
| [0010](0010-settings-change-notification.md) | One announced settings change, and a capture in hand wins |
| [0011](0011-duplex-ordering.md) | A two-pass stack is ordered before adoption, and keeps every back |
