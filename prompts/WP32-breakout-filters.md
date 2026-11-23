# WP32: Breakout filters (trend and first hour)

## Read first

`AGENTS.md`; `docs/spec.md` 0 (row M), 3.1, 3.7 ("Bars that are checked"), 7; SIG-022 to SIG-029 in `docs/requirements.yml`; `docs/architecture.md` section 10 (row "Breakout filters"); then `Signals/BreakoutParameters.cs`, `Signals/BreakoutTracker.cs`, `Signals/SignalEngine.cs` (how bias reaches the tracker), the bias and EMA code, `WarmupCalculator` in `Signals/SignalParameters.cs`, `Execution/ParameterHash.cs`, `src/ZoneFvg.Bot/ZoneFvgBot.cs` (the Entry mode group and `BuildBreakout`).

## Requirements

SIG-028, SIG-029.

## Allowed paths

`src/ZoneFvg.Core/Signals/**`, `src/ZoneFvg.Core/Execution/ParameterHash.cs`, `src/ZoneFvg.Bot/ZoneFvgBot.cs`, new test files under `tests/ZoneFvg.Core.Tests/`, `deploy/params/*.cbotset.example` (two new lines each), `deploy/README.md`, `docs/backtest-howto.md` and `docs/test-records/WP28-entry-mode.md` (only the parameter lists and the expected hashes of the cBot defaults), the `status:` lines of SIG-028 and SIG-029. No existing test changes: the record defaults switch both filters off, so every existing test keeps its meaning.

## Build

1. `BreakoutParameters`: `int TrendSlowEmaPeriod` (default 0) and `TimeSpan LastConfirmUtc` (default = `WindowEndUtc`'s default, 17:00). `Validate`: `TrendSlowEmaPeriod` 0 or greater than 1; `LastConfirmUtc` after `WindowStartUtc` and not after `WindowEndUtc`. Messages invariant.
2. Trend filter (SIG-028): the engine gives the tracker what it needs (the fast and slow D1 EMA values of the last closed D1 bar, or a trend flag) without changing what ZoneFvg mode computes. No slow EMA value (warm-up not reached): no confirmation (fail closed). The warm-up for D1 in Breakout mode uses the larger of the two periods.
3. First-hour rule (SIG-029) in the tracker, as the requirement says.
4. `ParameterHash`: append `TrendSlowEmaPeriod=<n>;` and `BreakoutLastConfirmUtc=<c-format>;` after the existing Breakout fields, only when they differ from 0 and the window end. Existing hash values stay.
5. cBot: two parameters in the group "Entry mode": `TrendSlowEmaPeriod` default 200, `BreakoutLastConfirmUtc` default "08:00", passed into `BreakoutParameters` (Breakout mode only, checked in both modes as the other two are). No rule in the adapter.
6. Parameter files: the two new lines after the three Entry mode lines. Guides and the WP28 record: new parameter lines and the new default hashes, computed with the core's rule (state how).
7. Check and say in the pull request how many D1 bars a backtest from 22 Oct 2023 needs before the first breakout can be traded with EMA 200, and whether the cBot loads them (`LoadMoreHistory`); if a backtest would lose months to warm-up, stop and report.

## Tests (tagged)

SIG-028: buy allowed/blocked with fast above/equal/below slow, inside and outside the tolerance; sell mirror; filter off = old results bit for bit (property test on the existing seeded histories); no slow EMA yet = no confirmation and reference stays Armed; ZoneFvg engine output unchanged with any value. SIG-029: close at 07:45 confirms, at exactly 08:00 and later confirms nothing and finishes the reference; off value (17:00) = old behaviour; refused values (before or at window start, after window end). Hash: off values give the old hashes; each non-off value appears in the canonical text; culture tests.

## Done

Tests first, CI green, new files fully covered, Python checks, statuses only if no open decision, plain summary for the owner.
