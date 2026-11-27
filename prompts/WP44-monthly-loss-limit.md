# WP44: Monthly loss limit

## Read first

`AGENTS.md`; `docs/spec.md` 0 (row BF of v0.19), 4.2 (the table, the daily budget and its paragraph on restarts), 7 (row MonthlyLossLimitPercent), 15.4; RSK-020 in `docs/requirements.yml`, and for what it refers to RSK-006 (daily budget, the cancellation of pending entries), RSK-007 (stateless day start), RSK-008, RSK-011 (the snapshot), RSK-019.

Code to reuse, not to copy: `Risk/RiskEngine.cs` (`CheckNewTrade`, `CheckDailyBudget`, `DailyBudgetExhausted`, `DayStart`, `BalanceAt`), `Risk/CestClock.cs` (add a start-of-month helper next to the day helpers if none exists), `Risk/RiskParameters.cs`, `Risk/RiskReason.cs`, `Execution/PreTradeFilters.cs` (`HistoryStart`: the history must reach back to the month start when the limit is on), the place where the daily budget cancels pending entry orders (find it; reuse it), `Execution/SkipReason*`, `src/ZoneFvg.Bot/ZoneFvgBot.cs` (`BuildRisk`); tests: the RSK-006 and RSK-007 tests and scenarios, `FakeBroker`, `BotHarness`.

## Requirements

RSK-020. One pull request, branch `wp/44-monthly-loss-limit`.

## Allowed paths

- `src/ZoneFvg.Core/Risk/**`, `src/ZoneFvg.Core/Execution/**` (the pre-trade check, the history window, the skip reason, the cancellation of pending entries), `src/ZoneFvg.Core/App/**` only where the daily budget's cancellation is driven from there.
- `src/ZoneFvg.Bot/ZoneFvgBot.cs` (the new parameter and `BuildRisk`).
- `deploy/**`, `docs/runbook.md`, `docs/backtest-howto.md`, `deploy/README.md`.
- New test files under `tests/ZoneFvg.Core.Tests/`; helpers under `TestSupport/` only by adding; existing tests only as listed below.
- `docs/requirements.yml`: the status line of RSK-020.

Not allowed: `tools/`, `docs/spec.md`, `.github/`, `prompts/`; any signal rule; the daily budget's own rule, the kill switch, the emergency guard, the brake, sizing plans; any core record default other than the new field (core default 0).

## Owner authorisation to change existing tests

- Tests that pin the exact list or order of lines of the parameter files, or the exact list of the cBot's Risk parameter declarations, may gain the new line. Risk values are not in the parameter hash, so every hash pin stays.
- Tests that enumerate every skip reason (if any) may gain the new one.
- List each changed test with its old and new expectation. No assertion removed or loosened. If any other existing test fails, stop and ask.

## Build

1. **Parameter.** `RiskParameters.MonthlyLossLimitPercent` (default 0, XML documentation naming RSK-020), validated as RSK-020.
2. **Month start.** A pure helper for 00:00 CE(S)T on the first day of the CE(S)T calendar month that contains a UTC instant (the same CE(S)T rule as `CestClock` uses for the day: last Sunday of March / October at 01:00 UTC). Tests at the month boundary and across both daylight-saving changes.
3. **Check.** In the new-trade check, after the existing checks' order is kept, add the monthly check (with the limit above 0): S rebuilt from history as RSK-007 does for the day, refused with a new `RiskReason` and skip reason (suggested `MonthlyLossLimit`) when S − balance ≥ limit within tolerance. The measured value and the limit go in the skip record as for the daily budget. A not-positive S or broker history that cannot be read refuses the entry (fail closed, `BrokerDataInvalid` where the existing code uses it).
4. **History window.** `HistoryStart` reaches back to the earliest of the day start, the epoch start and, with the limit above 0, the month start. With the limit 0 the window and the broker calls are exactly as before.
5. **Pending entries.** When the limit is reached, pending bot entry orders are cancelled through the same path the daily budget uses when it is exhausted; the journal line names the monthly limit. Open positions are never closed or modified by it.
6. **cBot and files.** `[Parameter("MonthlyLossLimitPercent", DefaultValue = 2.0, Group = "Risk")]` directly after `DailyLossLimitPercent` (or where the account limits are declared), passed in `BuildRisk`; every file under `deploy/params/` gains `MonthlyLossLimitPercent=2.0` in the cBot's order. The guides describe the limit in one short paragraph (what it counts, that it is account-wide over both gold instances, that it never closes a trade, that 0 switches it off). The start line is unchanged.

## Tests required (tagged RSK-020)

- Validation: −0.01, NaN, +∞, 8 (equal to `MaxDrawdownPercent`) refused naming it; 0, 2 and 7.99 accepted.
- Boundary, InitialCapital 200,000, limit 2 (4,000 USD): month start balance 205,000; current balance 201,000 (exactly −4,000) → refused; 201,000.01 → allowed; 200,999.99 → refused.
- S is rebuilt from history: trades closed before the month start do not count; a trade closed at 23:59 CE(S)T on the last day of the previous month does not count, one at 00:00 of the first day counts; commission and swap count.
- Month change: refused on the 30th; allowed at 00:00 CE(S)T on the 1st of the next month; across the end of March and the end of October (daylight-saving) the boundary is 00:00 CE(S)T.
- Account-wide: losses of trades of another instance's label (another mode on the same symbol) and of trades without a bot label count.
- Pending entries are cancelled when the limit is reached; open positions keep their stop and target and are not closed; the timed exit of a SessionRange position still runs.
- Limit 0: identical decisions, journal lines and broker calls (count `GetHistory` calls and their `since` argument) to a run without the field.
- In both sizing plans (Ladder with the ladder on and off, Recovery) the limit refuses the same way.
- Broker history read error with the limit on → no order.
- cBot source: the declaration once with default 2.0, the assignment in `BuildRisk`; every parameter file carries the line once in order.

## Owner decisions already taken (do not reopen them in code)

- Row BF: 2% of initial capital, calendar month in CE(S)T, closed result only (balance, not equity), no new entries and pending entries cancelled, never closes a trade, account-wide, 0 = off, cBot default 2.
- Anything the spec does not settle: write it under "Decisions for the human" and leave RSK-020 `planned`.

## Done

Tests first, CI green, changed lines covered, all commands of `AGENTS.md` run and pasted (dotnet from CI), the Python checks, the status line to `implemented` only if no decision is open, and a plain summary for the owner: once the account's closed result for the calendar month reaches −2% of the initial capital (−4,000 USD at 200,000), the bot opens nothing new until the next month and cancels its pending entries; trades already open run to their stop, target or timed exit; the limit covers both gold instances together.
