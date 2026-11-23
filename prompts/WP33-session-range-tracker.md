# WP33: Session range tracker (core, signals only)

## Read first

`AGENTS.md`; `docs/spec.md` 0 (row N), 3 (the two mode paragraphs), 3.1, 3.8; SIG-030 to SIG-033 in `docs/requirements.yml`; `docs/architecture.md` section 10 (row "SessionRange mode"); then, as the pattern to follow, `Signals/BreakoutParameters.cs`, `Signals/BreakoutSetup.cs`, `Signals/BreakoutTracker.cs` and their tests; `Indicators/Atr.cs`, `Indicators/BiasCalculator.cs`, `Domain/PriceMath.cs`, `Risk/CestClock.cs` (an existing clock that is pure).

## Requirements

SIG-030, SIG-031, SIG-032, SIG-033.

## Allowed paths

New files under `src/ZoneFvg.Core/Signals/` and one new file `src/ZoneFvg.Core/Domain/NewYorkClock.cs`; new test files under `tests/ZoneFvg.Core.Tests/`; `docs/requirements.yml` (the status lines of the four requirements). No existing source or test file changes. If you find that one must, stop and ask.

## Build

1. `NewYorkClock` (static, pure): New York wall-clock time of a UTC instant by the rule of SIG-030; the trading day of a UTC instant (the date that names it and its start as a UTC instant); the UTC instant of a New York time of day inside a given trading day. No `TimeZoneInfo`, no clock, no culture.
2. `SessionRangeParameters` (record): `RangeStartNy` 19:00, `RangeEndNy` 03:00, `LastEntryNy` 12:00, `ExitNy` 16:30, `MinWidthAtr` 0.5, `StopAtrMult` 1.0, and the constant `MinRangeBars` 16. `Validate()` as SIG-032 says, with messages in the style of `BreakoutParameters`.
3. `SessionRangeSetup` (record): side, entry, stop, target, expiry (UTC), id, range high, range low, the date of the trading day.
4. `SessionRangeTracker`: it is given closed M15 bars one at a time, each with what the rules need from the caller (the bias, ATR(D1) and ATR(H4) of the last closed bars, `RewardRisk`, `FridayNoEntryUtc`). For each bar it answers: nothing, a setup (SIG-031, SIG-032) or a void notice (SIG-033). It keeps only the state of the current trading day and reaches the same state when the same bars are fed again. It knows nothing of orders: the caller tells it when the day's order is no longer pending, and after that it reports no void.
5. Not in this package: the signal engine, `EntryMode`, `SignalKind`, orders, the timed exit, the parameter hash and the cBot (WP34, WP35). Nothing that exists changes its behaviour.

## Tests (tagged)

- SIG-030: both switch instants to the second for 2020 to 2030 (the last second before and the first second of each); the offset in summer and in winter; the trading day's name and start at 16:59:59 and 17:00:00 New York, on a Friday evening, on a Sunday evening and across both switch weekends; the UTC instant of 19:00, 03:00, 12:00 and 16:30 in a summer and in a winter trading day.
- SIG-031: the range comes from exactly the bars inside the window (the bar that opens at 19:00 is in, 18:45 is out, 02:45 is in, 03:00 is out); evaluation at the bar that closes at 03:00 and, when that bar is missing, at the next closed bar; no setup when that bar closes at or after `LastEntryNy`; 15 range bars give no setup and 16 give one; trading days that end on a Saturday or a Sunday; neutral bias; a width one tick below, equal to and one tick above the threshold; a missing ATR(D1) and a missing ATR(H4); a summer and a winter day (the window is 23:00 to 07:00 UTC and 00:00 to 08:00 UTC).
- SIG-032: buy and sell levels; stop and target arithmetic; the expiry instant in summer and in winter; the Friday cap with `FridayNoEntryUtc` later than, equal to and earlier than `LastEntryNy`; both ids; at most one setup per trading day; every `Validate` rule at its boundary, with the message.
- SIG-033: a low equal to the range low voids a buy setup, one tick above does not; the mirror for a sell setup; a bar that opened before `RangeEndNy` never voids; the void is reported once; nothing is reported after the caller said the order is no longer pending; no second setup that day.
- Property test (seeded, prints the seed on failure): for random M15 histories the tracker that was fed bar by bar and a tracker rebuilt from the same bars give the same answers.

## Done

Tests first, CI green, new files fully covered, Python checks, statuses to `implemented` only if no decision is open, a plain summary for the owner in the pull request.
