# WP34: Session range in the engine and in the execution layer

## Read first

`AGENTS.md`; `docs/spec.md` 3.3, 3.6, 3.8, 5.1 to 5.5, 8.3, 9.3; SIG-034, EXE-024, EXE-025 and, for what they refer to, SIG-004, SIG-026, EXE-018, EXE-021 to EXE-023 in `docs/requirements.yml`; `docs/architecture.md` section 10 (rows of 22 and 23 November on Breakout and on SessionRange); the code of WP33; then `Signals/SignalEngine.cs`, `Domain/SignalKind.cs`, `Domain/Signal.cs`, `App/EntryMode.cs`, `App/BotParameters.cs`, `App/BotCore.cs`, `Execution/OrderManager.cs`, `Execution/TradeManager.cs` (the Friday close is the pattern for the timed exit), `Execution/PreTradeFilters.cs`, `Execution/ParameterHash.cs`, `Recovery/RecoveryService.cs`, `Recovery/SafetyActions.cs`, the warm-up calculator in `Signals/SignalParameters.cs`; `prompts/WP27-breakout-wiring.md` and `prompts/WP29-order-guards.md` (how Breakout was wired).

## Requirements

SIG-034, EXE-024, EXE-025.

## Allowed paths

`src/ZoneFvg.Core/**`; new test files under `tests/ZoneFvg.Core.Tests/`; an existing test file only where a test lists every value of `SignalKind` or `EntryMode` or every property of `BotParameters` (name each such edit and its reason in the pull request; no assertion is weakened); `docs/requirements.yml` (the status lines of the three requirements).

## Build

1. `EntryMode.SessionRange`, `SignalKind.SessionRange`, `BotParameters.SessionRange` (the `SessionRangeParameters` of WP33, validated at start in every mode).
2. Engine (SIG-034): the tracker of WP33 is fed in SessionRange mode only; its setup becomes a signal with the setup's expiry; replay as for Breakout.
3. Orders (EXE-024): stop order, the signal's expiry, the void notice cancels the pending order; the engine tells the tracker when the order is no longer pending. EXE-022 allows a stop order for the new kind and still refuses every other combination.
4. Timed exit (EXE-025), beside the Friday close and with its rules for a closed market and for the bot states.
5. `ParameterHash`: in SessionRange mode append `RangeStartNy=…;RangeEndNy=…;RangeLastEntryNy=…;RangeExitNy=…;RangeMinWidthAtr=…;RangeStopAtr=…;` after the existing fields, with the formats the hash already uses for times and numbers. In the other modes nothing is appended: the existing hash tests pass unchanged.
6. Warm-up, journal (`kind=range`, level = the range edge that is traded), trade log and daily summary: as for Breakout.
7. Recovery: replay of the current trading day, adoption by label, the timed exit also while Recovering.

## Tests (tagged)

Scenario tests through `FakeBroker`: placement at 03:00 New York in summer and in winter; fill then target; fill then stop; expiry at 12:00 New York; the Friday cap; void then cancel, and no request when nothing is pending; timed exit at 16:30 New York with the market open, and on reopening when it was closed; the Friday close before the timed exit; a news window that shortens the order; a restart between placement and fill (the order is adopted, void and timed exit still work); a restart with an open position (the timed exit still happens); a replayed setup is not executable; price already at the entry; `SymbolBusy`. The other modes bit for bit: their scenario suites and hash values are unchanged.

## Done

Tests first, CI green, new and changed core files covered as the gate asks, Python checks, statuses to `implemented` only if no decision is open, a plain summary for the owner in the pull request.
