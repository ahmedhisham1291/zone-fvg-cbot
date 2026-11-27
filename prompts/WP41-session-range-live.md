# WP41: SessionRange on gold as the default, fixed 0.75% risk and the drawdown brake

## Read first

`AGENTS.md`; `docs/spec.md` 0 (rows AS to AX of v0.16, and rows AA, AB of v0.14 for what they replace), 3.8, 3.9 (the refusal of Choch with another mode), 4.1 (the paragraph "Drawdown brake" and the formula), 4.2 (the kill switch), 7 (rows EntryMode, EntryModel, RiskPercent, DrawdownBrakePercent, DrawdownBrakeFactor, LadderEnabled, MaxDrawdownPercent); RSK-019 and PLT-020 in `docs/requirements.yml`, and for what they refer to RSK-001, RSK-008, RSK-013, RSK-018, PLT-017, PLT-018, SIG-035.

Code to reuse, not to copy: `Risk/RiskEngine.cs` (`KillSwitch`, `BalanceAt`: the peak and its epoch), `Risk/RiskParameters.cs` (`Validate`, `RequirePercent`), `Execution/PreTradeFilters.cs` (`SizeLadder`: the one place where the fixed percent is chosen; it already reads the history from the broker when the ladder is on), `Risk/PositionSizer.cs`, `App/BotCore.cs` (the start line and how `sizing=Recovery` and the profile part are appended), `src/ZoneFvg.Bot/ZoneFvgBot.cs` (`BuildRisk`, the parameter declarations); tests: `App/Plt017CbotSizingTests.cs` and the WP39 tests of PLT-018 (how the cBot source and the parameter files are read, and "the defaults run"), `TestSupport/BotHarness`, `Execution/ExecutionData.cs`, `Risk` tests of RSK-008 and RSK-013.

## Requirements

RSK-019, PLT-020. One pull request, branch `wp/41-session-range-live`.

## Allowed paths

- `src/ZoneFvg.Core/Risk/**` (`RiskParameters.cs`; a small pure helper for the peak if `RiskEngine` cannot share its own without a change of behaviour).
- `src/ZoneFvg.Core/Execution/PreTradeFilters.cs` (`SizeLadder` only, and what it needs to journal the brake).
- `src/ZoneFvg.Core/App/BotCore.cs` (the start line only), `src/ZoneFvg.Core/App/BotParameters.cs` only if validation of the new values needs it.
- The journal text of a taken trade, only to add the brake note of build step 3.
- `src/ZoneFvg.Bot/ZoneFvgBot.cs` (defaults, the two new parameters, `BuildRisk`).
- `deploy/**`, `docs/runbook.md`, `docs/backtest-howto.md`, `deploy/README.md`, `README.md` (only a sentence that names an old default).
- New test files under `tests/ZoneFvg.Core.Tests/`; helpers under `tests/ZoneFvg.Core.Tests/TestSupport/` only by adding; existing tests only as listed under "Owner authorisation".
- `docs/requirements.yml`: the status lines of RSK-019 and PLT-020.

Not allowed: `tools/`, `docs/spec.md`, `.github/`, `prompts/`, any signal rule, any change to the SessionRange tracker or engine, `PositionSizer`'s checks, the ladder, Recovery, the kill switch's own decision, any core record default other than the two new fields (the core's `RiskPercent` stays 0.5, `DrawdownBrakePercent` defaults to 0 in the core).

## Owner authorisation to change existing tests

The owner asked on 2026-11-27 for new cBot defaults (rows AS, AT, AW). Existing tests that assert the old **cBot** default of `EntryMode` ("ZoneFvg"), `EntryModel` ("Choch") or `RiskPercent` (0.5), or the old line of a parameter file for these three, may be changed only in that expectation. "The defaults run" test of PLT-018 may be changed so that it runs the new defaults (SessionRange, Fvg); keep a copy of its old cases with `EntryMode` ZoneFvg and `EntryModel` Choch passed explicitly, so that the Choch path stays proved. Each changed test is listed in the pull request with its old and new expectation. No assertion may be removed or loosened; every parameter-hash test passes with its pinned value unchanged; every core test passes unchanged. If any other existing test fails, stop and ask.

## Build

1. **Parameters (RSK-019).** `RiskParameters` gains `DrawdownBrakePercent` (default 0) and `DrawdownBrakeFactor` (default 0.5), with XML documentation naming RSK-019. `Validate` refuses, naming the parameter: `DrawdownBrakePercent` not finite, below 0, or not below `MaxDrawdownPercent`; `DrawdownBrakeFactor` not finite, not above 0, or above 1. Use the existing message style.
2. **Brake (RSK-019).** In `SizeLadder`, when `LadderEnabled` is false and `DrawdownBrakePercent` is above 0: read the history from the broker as the ladder path does, compute the peak exactly as `KillSwitch` does (same epoch, same `BalanceAt`, same history), and braked = peak > 0 and peak − balance ≥ peak × `DrawdownBrakePercent` / 100 within `PriceMath.DefaultTolerance` (equal counts as braked). Braked: risk percent `RiskPercent` × `DrawdownBrakeFactor`; else `RiskPercent`. Share the peak computation with `KillSwitch` (one function) rather than writing it twice; the kill switch's decisions and journal must not change. A broker read or snapshot error takes the existing `BrokerDataInvalid` path. A peak not above 0: no brake (the kill switch halts on its own check). With `DrawdownBrakePercent` 0, or with the ladder on, or with Recovery: not a single extra broker read, and every decision and line exactly as before.
3. **Journal (RSK-019).** When the brake set the percent of a taken trade, its existing take line gets, at the very end, ` brake=on peak=<peak> balance=<balance> riskPercent=<braked percent>` (invariant "R" format after rounding to 2 decimals for money, 9 for percent). If the take line has no clean place for it, write it as one separate info line `drawdown-brake signal=<id> peak=... balance=... riskPercent=...` immediately before the take line, and say which you chose. Not braked: every line unchanged byte for byte.
4. **Start line (PLT-020).** With `SizingPlan` Ladder and `LadderEnabled` false the core's start line ends with ` brake=<DrawdownBrakePercent>/<DrawdownBrakeFactor>` ("R" format, for example `brake=3/0.5`) or ` brake=off` when the percent is 0, after every part it has today (after the profile part). Otherwise unchanged byte for byte. Existing start-line tests that build parameters with the ladder on or with Recovery pass unchanged; tests with fixed sizing get the new ending only where they assert the whole line (that change is authorised; list it).
5. **cBot (PLT-020).** Defaults: `[Parameter("EntryMode", DefaultValue = "SessionRange", Group = "Entry mode")]`, `[Parameter("EntryModel", DefaultValue = "Fvg", Group = "Entry model")]`, `[Parameter("RiskPercent", DefaultValue = 0.75, Group = "Risk")]`, new `[Parameter("DrawdownBrakePercent", DefaultValue = 3.0, Group = "Risk")]` and `[Parameter("DrawdownBrakeFactor", DefaultValue = 0.5, Group = "Risk")]`, passed in `BuildRisk`. Update the XML summaries (spec v0.16). Nothing else changes in the adapter.
6. **Files and guides (PLT-020).** All five files under `deploy/params/` carry `EntryMode=SessionRange`, `EntryModel=Fvg`, `RiskPercent=0.75`, and new lines `DrawdownBrakePercent=3.0` and `DrawdownBrakeFactor=0.5` in the cBot's order; they stay equal apart from `HeartbeatUrl`. `deploy/README.md`, `docs/runbook.md` and `docs/backtest-howto.md`: the default setup is SessionRange on XAUUSD with a fixed 0.75% and the brake; only the XAUUSD instance is started with these files; a backtest still needs no value set by hand except `LadderEnabled` if wanted; the gates of spec row AX (stop-order record, live gold spread, Free Trial weeks proposed) come before the Challenge.

## Tests required (tagged)

**RSK-019**

- Validation: `DrawdownBrakePercent` −0.01, NaN, +∞, 8 (equal to `MaxDrawdownPercent` 8) and 9 refused naming it; 0, 3 and 7.99 accepted. `DrawdownBrakeFactor` 0, −0.5, NaN, 1.01 refused naming it; 0.01, 0.5 and 1 accepted.
- Brake boundary through `PreTradeFilters` with `FakeBroker` (fixed sizing, `RiskPercent` 0.75, brake 3 / 0.5, epoch balance 200,000): closed trades bring the peak to 210,000; balance 203,700 (exactly 3% below) → risk 0.375% of 203,700; balance 203,700.01 → 0.75%; balance 203,699.99 → 0.375%; balance back to 205,000 → 0.75%. Assert the planned risk money and volume.
- The peak is the kill switch's: a scenario where the highest balance was reached by a trade closed before `RiskEpochDate` does not count; the same history gives the same peak in `KillSwitch` and in the brake.
- Factor 1: braked and not braked give the same risk (and the take line still carries the note when braked).
- `DrawdownBrakePercent` 0: identical sizing and identical journal lines to a run without the new fields, and no extra `GetHistory` call (count the calls on `FakeBroker`).
- Ladder on (`LadderEnabled` true) and `SizingPlan` Recovery, both 5% below the peak with the brake at 3: the ladder's and Recovery's own percents and amounts, unchanged, no brake note.
- The braked amount still meets every later check: with `SanityCapEnabled` true and `SanityCapPercent` 0.5, an unbraked 0.75% trade is skipped as `RiskAboveSanityCap` and a braked 0.375% trade is not; daily budget and kill switch decisions unchanged by the brake (one case each).
- Broker history that throws or is invalid while the brake is on: skipped as `BrokerDataInvalid`, no order.
- The journal note: exact text for one braked trade; a not-braked take line equal byte for byte to the line before this work package (pin it).

**PLT-020**

- The cBot source declares exactly the five parameter lines of build step 5, each once, and unchanged `LadderEnabled` false, `SizingPlan` "Ladder", `SessionStartUtc` "00:00", `SessionEndUtc` "24:00", `InitialCapital` 200000.0, `MaxSpreadPips` 0.0, `MinStopPips` 0.0, `ChochMinStopPips` 0.0, `MaxVolumeUnits` 0.0; `BuildRisk` assigns `DrawdownBrakePercent` and `DrawdownBrakeFactor` from the parameters.
- **The defaults run:** the cBot's new default values through the same core path as the PLT-018 test (texts and numbers as declared, profile function, `ChochText` checks, `BotParameters.Validate`) for XAUUSD at pip 1 and pip 0.01: accepted; `SessionRange` is set, `Choch` is null; risk has `RiskPercent` 0.75, `LadderEnabled` false, `SizingPlan` Ladder, brake 3 / 0.5. The same with `EntryModel` Choch and otherwise the defaults: refused naming `EntryModel` (SIG-035, unchanged).
- Start line: fixed sizing ends with exactly ` brake=3/0.5`; with `DrawdownBrakePercent` 0, ` brake=off`; with the ladder on and with Recovery, the line is the line before this work package, byte for byte.
- The SessionRange hash with the new defaults is `3f0f30a3`'s parameters' hash as pinned today for the same signal values (risk values are not in the hash); assert it unchanged.
- Parameter files: each of the five carries each line of build step 6 once, in the cBot's order; the five files are equal apart from `HeartbeatUrl`.

## Owner decisions already taken (do not reopen them in code)

- Rows AS to AX of spec v0.16: SessionRange and Fvg by default, gold only, fixed 0.75%, the brake at 3% and 0.5 for fixed sizing only, peak as the kill switch's, open-risk sum unchanged, hash unchanged.
- Anything the spec does not settle: write it under "Decisions for the human" and leave the related id `planned`.

## Done

Tests first, CI green, changed lines covered, all commands of `AGENTS.md` run and pasted, the Python checks, status lines to `implemented` only if no decision is open, and a plain summary for the owner of what changes for orders, risk and money: a default run now trades the Asian range break on gold, risks a fixed 0.75% of balance per trade, and 0.375% while the balance is 3% or more below its peak since `RiskEpochDate`; the ladder and Recovery are unchanged and opt-in; every account limit is unchanged.
