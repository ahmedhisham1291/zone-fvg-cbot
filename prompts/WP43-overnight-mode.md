# WP43: Overnight mode (second gold instance) and the drawdown brake off by default

## Read first

`AGENTS.md`; `docs/spec.md` 0 (rows AZ to BE of v0.18; rows AS to AY for what they build on), **3.10**, 3.8 (trading day, New York time, timed exit, reported trading hours: the new mode reuses them), 3.6 and 3.7 (spread), 5.2, 5.3, 5.5, 8.3, 4.1 (the brake), 7 (the Night* rows, DrawdownBrakePercent, EntryMode); SIG-043, EXE-033, PLT-021 in `docs/requirements.yml`, and for what they refer to SIG-030 to SIG-034, SIG-042, EXE-018, EXE-020 to EXE-026, REC-020, RSK-019, PLT-015, PLT-020; `docs/architecture.md` section 10, the rows of 2026-11-23 on the SessionRange tracker, engine and execution (WP33, WP34) and the timed exit (WP36).

Code to reuse, not to copy: the whole SessionRange path is the model: `Signals/SessionRangeTracker.cs`, `SessionRangeParameters.cs`, `SessionRangeId.cs`, `SessionRangeSetup.cs`, `SessionRangeEvent*.cs`, `SignalEngine.cs`; the New York clock helper the tracker uses; the timed exit duty and its handling of a closed market (EXE-024 to EXE-026); `Execution/PreTradeFilters.cs`, `Execution/ParameterHash.cs`, `App/BotParameters.cs`, `App/ChochText.cs` (mode refusals), `App/BotCore.cs`, the recovery and replay code that knows the SessionRange id; `src/ZoneFvg.Bot/ZoneFvgBot.cs` (the Range parameters and how they are built); tests: the SessionRange unit tests, `Sig034SessionRangeScenarios`, the EXE-024 to EXE-026 scenarios, PLT-015 and PLT-020 tests, `TestSupport/BotHarness`, `FakeBroker`, `BarBuilder`.

## Requirements

SIG-043, EXE-033, PLT-021. One pull request, branch `wp/43-overnight-mode`. If it goes over about 1,800 changed lines (tests included), say so in the pull request; do not split it.

## Allowed paths

- `src/ZoneFvg.Core/Signals/**` (new `Overnight*` files; `SignalEngine.cs`; the `EntryMode` enum).
- `src/ZoneFvg.Core/Execution/**` (where the SessionRange kind is handled: placement, the timed exit, the journal line, the hash), `src/ZoneFvg.Core/App/**` (parameters, mode refusals, start line, recovery hand-over), `src/ZoneFvg.Core/Recovery/**` (or wherever the replay and recovery recognise the SessionRange id), `src/ZoneFvg.Core/Domain/**` only to add the new signal kind.
- `src/ZoneFvg.Bot/ZoneFvgBot.cs`.
- `deploy/**` (a new file `deploy/params/XAUUSD-overnight` in the same format as the others), `docs/runbook.md`, `docs/backtest-howto.md`, `deploy/README.md`, `docs/test-records/` (a new record for the owner's Free Trial check of row BD).
- New test files under `tests/ZoneFvg.Core.Tests/`; helpers under `TestSupport/` only by adding; existing tests only as listed below.
- `docs/requirements.yml`: the status lines of SIG-043, EXE-033, PLT-021.

Not allowed: `tools/`, `docs/spec.md`, `.github/`, `prompts/`; any rule of the other modes; sizing plans, the brake's logic (only its cBot default changes), account limits; any core record default of an existing record.

## Owner authorisation to change existing tests

- Tests that assert the cBot default of `DrawdownBrakePercent` (3.0) or the line `DrawdownBrakePercent=3.0` of a parameter file: the expectation becomes 0.0. Tests that assert the start-line ending ` brake=3/0.5` for the **cBot defaults** become ` brake=off`; core tests that build their own parameters keep theirs.
- Tests that pin the hashes of the parameter files, or the exact list or order of their lines, or the exact list of cBot parameter declarations, may change for the new lines and the new file (hashes computed independently with Python `hashlib` after reproducing at least three existing pins with the same script; show it in the pull request). The hash pins of the modes themselves (`3f0f30a3` and the others) stay.
- Tests that enumerate every `EntryMode` value (for example a refusal message listing the modes) may gain `Overnight`.
- Each changed test is listed in the pull request with its old and new expectation. No assertion is removed or loosened. If any other existing test fails, stop and ask.

## Build

1. **Parameters and tracker (SIG-043).** `OvernightParameters` record (defaults of spec 7: 18:15, 19:00, 02:45, 0.03, 1.0, 5.0, 60; XML documentation naming SIG-043) with `Validate`. `OvernightTracker` modelled on `SessionRangeTracker`: same trading day and New York clock (no time zone database), same rule for out-of-order bars, one setup per trading day, days by the New York date of the 17:00 start (Monday to Thursday), the evaluation bar rule of 3.10, levels and expiry as SIG-043. The spread enters the entry at placement (in the execution layer, as SessionRange does with its spread), not in the tracker, unless the existing design puts it elsewhere; say which.
2. **Engine and mode (SIG-043, PLT-021).** `EntryMode.Overnight`; `BotParameters.Overnight` selects the mode and is refused together with Breakout or SessionRange; `SignalEngine` feeds the tracker in this mode only. Signal kind and id `ON-yyyyMMdd`.
3. **Execution (EXE-033).** Placement through the existing path as a buy stop; skipped when price already reached the entry; spread check as 3.7; the take and skip journal lines of this kind carry `spread=<price>` (exact format: as the existing spread field if one exists, else ` spread=<R format>` at the end). Timed exit at `NightExitNy`, reusing the SessionRange duty (generalise it to the two kinds rather than copying it), with retries, market closed and reported-trading-hours rules unchanged for SessionRange.
4. **Two instances (EXE-033, row BA).** Read how the one-position-per-symbol check, the stranger handling of recovery and the timed exit select orders and positions today. Make every action that cancels, closes, modifies or time-exits select only ids of the instance's own mode. Report in the pull request what the one-position check counts and whether that needed a change. The two-instance scenario of EXE-033 is required.
5. **Hash (SIG-043).** The canonical text of this mode: the existing common fields plus the six Night fields (the mode-specific part in the style of SessionRange). Pin text and hash, computed independently.
6. **cBot (PLT-021).** `EntryMode` accepts "Overnight"; the seven Night parameters in a new group "Overnight" with the defaults; times parsed with the existing `HH:mm` reader; checked at start in every mode (core `Validate`, refusal names the cTrader parameter); passed to the core only in Overnight mode. `DrawdownBrakePercent` default 0.0. The start line names `mode=Overnight` as the other modes do.
7. **Files and guides (PLT-021).** The five files gain the seven Night lines (after the Range lines, in the cBot's order) and `DrawdownBrakePercent=0.0`; new `deploy/params/XAUUSD-overnight` equal to the XAUUSD file except `EntryMode=Overnight`, `RiskPercent=1.0` and its heartbeat line if those must differ. `deploy/README.md`, `docs/runbook.md`, `docs/backtest-howto.md`: gold runs as two instances (SessionRange 0.75%, Overnight 1.0%) on one account; limits are shared; how to backtest each (cTrader backtests one instance at a time: run each and send both logs); the cost gate of row BD. A new record `docs/test-records/WP43-overnight-spread.md` for the owner: on the Free Trial, for at least ten Overnight evaluations, write the spread of the journal line and whether the entry was taken or skipped; pass when most were taken and the taken spreads average at most 0.40 USD.

## Tests required (tagged)

**SIG-043**

- Validation of each parameter: in range accepted, out of range refused naming it (times outside 00:00–23:59, entry not before latest, latest not before exit in trading-day order, buffer and stop and target not positive finite, minutes 0 and 181 refused, 1 and 180 accepted).
- Days: a trading day that starts on Sunday or Friday evening gives no setup; Monday to Thursday give one; across the US daylight-saving changes (March and November) the evaluation bar is the one closing at 18:15 New York.
- Evaluation bar: the bar closing exactly at 18:15 is used; if it is missing, the bar closing at 18:30 is used; if no bar closes before 19:00, no setup; a bar closing exactly at 19:00 is not used.
- Levels with exact numbers: close 2,650.00, spread 0.30, ATR(M15) 3.00, ATR(D1) 40.00 → entry 2,650.39, stop 2,610.39, target 2,850.39; expiry 60 minutes after 18:15.
- ATR(D1) or ATR(M15) missing, zero, NaN → no setup. One setup per trading day.
- Id `ON-yyyyMMdd` from the date that names the trading day.
- Hash: pinned text and hash; every other mode's pinned hash unchanged.

**EXE-033**

- Through `FakeBroker`: the buy stop is placed with the levels, label and expiry; already reached → skipped; spread above the limit → skipped as SpreadTooWide; both journal lines carry the spread.
- Timed exit: a filled position is closed at the first M15 bar closing at or after 02:45 New York of its trading day; with the market reported closed at that bar the close is still sent (rule of 3.8); a failed close is retried as for SessionRange; a SessionRange position is not closed by the Overnight exit and vice versa.
- Idempotency and unclear outcomes for this kind: the cases of EXE-020, EXE-021 and EXE-023 as the SessionRange tests have them.
- Two instances: a SessionRange core and an Overnight core on one XAUUSD `FakeBroker` account over at least five trading days: each places and closes only its own; a restart of each (recovery and replay) keeps the other's order and position untouched; the daily budget counts both instances' losses; and, whatever the one-position check counts, there is no moment with two open positions on XAUUSD in the scenario.
- Recovery of an Overnight position after a restart between entry and 02:45: kept with its stop and target, closed at the timed exit.

**PLT-021**

- The cBot source declares the seven Night parameters once each with their defaults, `DrawdownBrakePercent` default 0.0, and `EntryMode` accepts "Overnight"; the Night values are assigned into the core's Overnight parameters.
- Defaults run with `EntryMode` Overnight (and otherwise the defaults) for XAUUSD at pip 1 and 0.01: accepted; with `EntryModel` Choch refused naming EntryModel.
- Files: the five files and the new one carry each new line once in order; the new file differs from the XAUUSD file only in `EntryMode`, `RiskPercent` (and the heartbeat line if needed); pinned hashes.

## Owner decisions already taken (do not reopen them in code)

- Rows AZ to BE of v0.18: buys only, Monday to Thursday evenings, 18:15 to 02:45 New York, buy stop at close + spread + 0.03 ATR(M15) for 60 minutes, stop 1 ATR(D1), safety target 5R, own-mode actions only, RiskPercent 1.0 in its file, brake off by default, the cost gate.
- Anything the spec does not settle: write it under "Decisions for the human" and leave the related id `planned`.

## Done

Tests first, CI green, changed lines covered, all commands of `AGENTS.md` run and pasted (dotnet from CI), the Python checks, status lines to `implemented` only if no decision is open, and a plain summary for the owner: what a second gold instance with `EntryMode` Overnight does (a buy stop just above the price at 18:15 New York on Monday to Thursday evenings, stop one daily ATR below, closed at 02:45 New York), that it risks 1.0% from its own file, that the account limits are shared with the SessionRange instance, that neither instance touches the other's trades, and that the brake is now off by default.
