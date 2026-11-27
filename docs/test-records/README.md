# Test records

Evidence for requirements with `verify: manual` or `verify: backtest`, and for
the owner's gates in `prompts/00-orchestrator.md`.

One Markdown file per record, named `WPNN-topic.md` or `GATE-topic.md`, with:
date, who ran it, software versions, account type, exact steps, observed
result, pass or fail. A requirement of this kind is marked `implemented` only
after the owner has signed its record.

## Records in this folder

| File | What it checks |
|---|---|
| `WP08-adapter.md` | The cTrader adapter |
| `WP11-fault-injection.md` | First-start checks and fault-injection tests |
| `WP22-drawing-cleanup.md` | Chart drawing clean-up (PLT-005) |
| `WP26-stop-orders.md` | Stop-entry orders (PLT-011) |
| `WP28-entry-mode.md` | Entry mode in the cBot (PLT-012, PLT-013) |
| `WP35-session-range.md` | SessionRange in the cBot and the gold backtest compared with the strategy search (PLT-015 in cTrader). It does not accept the mode (spec 14, question 4) |
| `WP43-overnight-spread.md` | The Overnight cost gate (spec v0.18 row BD): gold's spread at ten or more Overnight entries on the Free Trial, before the Overnight instance trades on the Challenge |
