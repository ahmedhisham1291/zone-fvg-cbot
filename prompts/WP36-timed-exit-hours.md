# WP36: Timed exit does not wait for reported trading hours

## Read first

`AGENTS.md`; `docs/spec.md` 3.8 (paragraphs "Timed exit" and "Reported trading hours"), 5.5; EXE-025 and EXE-026 in `docs/requirements.yml`; `docs/architecture.md` section 10 (row "Owner's first backtest of SessionRange, and the reported trading hours of gold"); `src/ZoneFvg.Core/App/BotCore.cs` (the range-exit duty on the bar path and on the timer path, and its `deferred` journal line), `Execution/TradeManager.cs` (`RangeExitDue`, `RangeExit`), `Recovery/SafetyActions.cs` (`MarketOpen`); the EXE-025 scenarios of WP34.

## Requirements

EXE-026.

## Allowed paths

`src/ZoneFvg.Core/App/BotCore.cs` and, only if the change cannot be made there, `src/ZoneFvg.Core/Execution/TradeManager.cs`; new test files under `tests/ZoneFvg.Core.Tests/`; a helper under `tests/ZoneFvg.Core.Tests/TestSupport/` only by adding to it; `docs/requirements.yml` (the status line of EXE-026). No existing test changes its expectations: the EXE-025 scenarios for a closed market use the timer path or a market that is really closed and must still pass as they are. If one of them fails, stop and ask.

## Build

1. On the bar path (a closed M15 bar whose close time is at or after `RangeExitNy` on the date in the position's id) the duty sends the close without calling `MarketOpen()`. It writes no `deferred` line there.
2. On the timer path nothing changes: the reported market state is used and the duty waits while it says closed.
3. A close that the broker refuses is retried as today; from the bar path at most one close request is sent per closed M15 bar.
4. Nothing changes in the Friday close, in the entries, in recovery or in the other two modes.

## Tests (tagged)

EXE-026, scenarios through `FakeBroker`: a winter day on which the broker reports the market closed from 20:50 UTC while M15 bars keep coming: the position is closed at the 21:30 UTC bar (16:30 New York), with no `deferred` line and no second request; the same day in summer (20:30 UTC) is unchanged; the exit due on the timer with the market reported closed and no bar: deferred and closed on reopening, as before; the broker refuses the close at the bar: one request per bar, closed when it is accepted; a position of another kind is not touched; a ZoneFvg and a Breakout instance send no request and make no extra broker read.

## Done

Tests first, CI green, changed lines covered, Python checks, status to `implemented` only if no decision is open, a plain summary for the owner in the pull request.
