# WP38: Recovery sizing plan per pair, 200,000 USD account, 2% daily budget, switchable sanity, open-risk and currency caps, larger size cap

## Read first

`AGENTS.md`; `docs/spec.md` 0 (rows P to Y of v0.12 and row Z of v0.13), 4.1 and 4.1.1, 4.2, 4.3 (the table and the paragraph "What bounds a single trade"), 7 (rows SizingPlan, RecoveryRunLossPercent, RecoveryMaxRiskPercent, DailyLossLimitPercent, InitialCapital, MaxOpenRiskPercent, OpenRiskCapEnabled, MaxPositionsPerCurrency, CurrencyCapEnabled, MaxVolumeUnits, SanityCapEnabled, SanityCapPercent), 8.5 (backtests, decision C), 12.1 step 9, 15.2, 15.4, 15.5; RSK-002, RSK-004, RSK-005, RSK-006, RSK-009, RSK-016, RSK-017, RSK-018, SIM-006, SIM-007, PLT-017 and, for what they refer to, RSK-001, RSK-011, RSK-015, RSK-013, RSK-014, REC-014, SIM-004 in `docs/requirements.yml`; `docs/architecture.md` section 10 (rows "200,000 USD account, Recovery sizing plan, 2% daily budget, switchable sanity cap" and "Owner's answers to plan pull request #89"; the second replaces the first where they differ; the row "Recovery with step columns" of the same day replaces the Recovery amount rule of both); `prompts/WP18-ladder-win-reset.md` (how the ladder was specified and tested).

Code to reuse, not to copy: `Risk/LadderSizer.cs` (`EntriesFromBroker`, the ordering in `Ordered`, the event order and the fail-closed `Require`), `Risk/LadderEntry.cs`, `Risk/RiskParameters.cs` (`Validate`, `ValidateSizing`), `Risk/PositionSizer.cs` (the order of the checks, the sanity cap), `Risk/RiskEngine.cs` (`Check`, `CheckOpenRisk`, `CheckCurrencyExposure`, `CheckDailyBudget`, `DailyLimit`), `Execution/PreTradeFilters.cs` (`CheckSizing`, `CheckAccountRisk`, `HistoryStart`), `Execution/SkipReasons.cs`, `App/BotCore.cs` (the start line), `App/ChochText.cs` (how a cBot text is parsed in the core), `Simulation/FtmoChallengeSimulator.cs` (`PathRun.Open`, `Close`, the ladder path), `Simulation/SimCommandLine.cs` (`FtmoParameters`, `Sizing`, the help texts), `Simulation/PortfolioCombiner.cs`, `Simulation/AcceptanceReport.cs`, `src/ZoneFvg.Bot/ZoneFvgBot.cs` (`BuildRisk`); tests: `Risk/RiskData.cs`, `Execution/ExecutionData.cs` (`ExecutionHarness`), `Scenarios/Rsk013LadderScenarios.cs`, `Scenarios/Rsk002SanityCapScenarios.cs`, `Simulation/LadderSimulationTests.cs`, `Simulation/SimData.cs`, `Fakes/FakeBroker`.

Naming: the word "Recovery" already names the restart procedure (`ZoneFvg.Core.Recovery`, REC-*, state Recovering). Put the new code in `ZoneFvg.Core.Risk` and name it after sizing (suggested: `SizingPlan` with values `Ladder` and `Recovery`, `RecoverySizer`, `RecoveryRun`), never in the `Recovery` namespace.

## Requirements

RSK-002, RSK-004, RSK-005, RSK-006, RSK-009, RSK-016, RSK-017, RSK-018, SIM-006, SIM-007, PLT-017.

## Split (size limit)

Run it as two pull requests, in this order, each from an up-to-date `main`, each with only its ids:

| Part | Branch | Ids | Content |
|---|---|---|---|
| WP38a | `wp/38a-recovery-sizing-core` | RSK-002, RSK-004, RSK-005, RSK-006, RSK-009, RSK-016, RSK-017, RSK-018 | Risk parameters and defaults, the per-pair Recovery amount from broker data, the switchable sanity, open-risk and currency caps, the wiring in `PreTradeFilters`, the start line |
| WP38b | `wp/38b-recovery-sizing-sim-cbot` | SIM-006, SIM-007, PLT-017 | Simulator option and defaults, cBot parameters, parameter files, runbook and deploy guide |

If a part goes over about 800 changed lines (tests included), split it once more by requirement id and say so in the pull request.

## Allowed paths

- WP38a: `src/ZoneFvg.Core/Risk/**` (new files allowed); `src/ZoneFvg.Core/Execution/PreTradeFilters.cs`; `src/ZoneFvg.Core/App/BotCore.cs` (the start line only), `src/ZoneFvg.Core/App/BotParameters.cs` (only if validation needs it), a new `src/ZoneFvg.Core/App/SizingPlanText.cs` (or a similar name) for the cBot's text; new test files under `tests/ZoneFvg.Core.Tests/`; a helper under `tests/ZoneFvg.Core.Tests/TestSupport/` only by adding to it; the existing test files listed under "Owner authorisation" for the changes listed there; `docs/requirements.yml` (status lines of the eight ids).
- WP38b: `src/ZoneFvg.Core/Simulation/**`; `src/ZoneFvg.Bot/**`; `deploy/**`; `docs/runbook.md`; `docs/backtest-howto.md` (only to correct a name or value that differs from the build; Part 2 and Part 7 were written in the plan); new test files under `tests/ZoneFvg.Core.Tests/`; the existing simulator test files listed under "Owner authorisation"; `docs/requirements.yml` (status lines of the three ids).

Not allowed in either part: `tools/` (the `sim` program only calls `SimCommandLine`; nothing there changes), `docs/spec.md`, `.github/`, any change of a rule outside the nine ids.

## Owner authorisation to change existing tests

The owner decided on 2026-11-24 to change these defaults: `InitialCapital` 100,000 to 200,000, `DailyLossLimitPercent` (and the simulator's daily budget) 1.5 to 2.0, the per-trade sanity cap from always on to `SanityCapEnabled` = false, the open-risk cap and the currency cap from always on to `OpenRiskCapEnabled` = false and `CurrencyCapEnabled` = false (also in the simulator's combiner), and `MaxVolumeUnits` 500,000 to 3,000,000. Existing tests whose expected values follow from these old defaults, or that force the old open-risk cap, currency cap or volume cap, may be changed, **only** in one of these two ways, and each changed test is listed in the pull request with its old and new value:

1. **Keep the old account in the shared test data (preferred).** The tests were written for a 100,000 account with a 1.5% budget and the cap on; they keep proving the same rules there.
   - `tests/ZoneFvg.Core.Tests/Risk/RiskData.cs`: `Parameters` sets `InitialCapital = 100000.0`, `DailyLossLimitPercent = 1.5`, `SanityCapEnabled = true`, `OpenRiskCapEnabled = true`, `CurrencyCapEnabled = true` and `MaxVolumeUnits = 500000.0` explicitly (it is used by `ExecutionData.RiskDefaults`, `RecoveryData.Parameters()` and about 70 tests). No expected value then changes in the risk, execution, recovery, scenario and ops tests.
   - Simulator tests whose parameter objects rely on the default capital or daily budget (`Simulation/FtmoChallengeSimulatorTests.cs`, `Simulation/LadderSimulationTests.cs`, `Simulation/PortfolioCombinerTests.cs`, `Simulation/AcceptanceReportTests.cs`, `Simulation/SimCommandLineTests.cs`, `Simulation/AcceptCommandSymbolsTests.cs`, `Simulation/AcceptanceProfitableSymbolsTests.cs`, `Simulation/SimExtractCommandTests.cs`, `Simulation/MonteCarloTests.cs` where they apply): set `InitialCapital = 100000.0` and `DailyBudgetPercent = 1.5` / `DailyLossLimitPercent = 1.5`, and switch the combiner's open-risk and currency caps on, in the test's own parameter object; or add `--initial-capital 100000 --daily-budget 1.5` (and `--max-open-risk 1.5 --max-per-currency 2` where the test relies on the caps without naming them) to a command's arguments.
2. **New expected values only where a test asserts a default itself:**
   - `Risk/RiskParametersTests.cs` `Defaults_MatchSpecSections4And7`: `DailyLossLimitPercent` 1.5 to 2.0, `InitialCapital` 100000 to 200000, `MaxVolumeUnits` 500000 to 3000000, the constant `RiskParameters.MaxComputedRiskPercent` 0.55 to the parameters `SanityCapEnabled` false and `SanityCapPercent` 0.55; add the new defaults of RSK-018. Every other default stays asserted as it is.
   - `Simulation/PortfolioCombinerTests.cs` (the assertions of `Defaults.InitialCapital` and `Defaults.DailyLossLimitPercent`, now 200000 and 2.0, and of the two caps, now off) and `Simulation/FtmoChallengeSimulatorTests.cs` (the defaults test: `InitialCapital` 200000, `DailyBudgetPercent` 2.0). If the `Defaults` object of `PortfolioCombinerTests` also drives scenario numbers, pin it as in way 1 and assert the new defaults on a fresh `new PortfolioParameters()`.
   - Help-text expectations in `Simulation/SimCommandLineTests.cs` that show "default 100000" or "default 1.5": the new defaults.

No other existing test may be changed, and no assertion may be removed or loosened. Every Ladder test (`LadderSizerTests`, `LadderStepColumnTests`, `Rsk013LadderScenarios`, `LadderSimulationTests` apart from the pin) and every parameter-hash test must pass with its expectations unchanged. If any other existing test fails, stop and ask.

## Build

### WP38a (core)

1. **Parameters (RSK-018).** `RiskParameters` gains `SizingPlan` (default `Ladder`), `RecoveryRunLossPercent` 1.0 (owner decision Z; there is no `RecoveryFirstRiskUsd`), `RecoveryMaxRiskPercent` 1.28, `SanityCapEnabled` false, `SanityCapPercent` 0.55, `OpenRiskCapEnabled` false, `CurrencyCapEnabled` false; `InitialCapital` defaults to 200000.0, `DailyLossLimitPercent` to 2.0 and `MaxVolumeUnits` to 3000000.0. Validation of RSK-018 in `Validate` (and the sizing values in `ValidateSizing`), messages in the existing style naming the parameter. The constant `MaxComputedRiskPercent` is replaced by the parameter; keep no second source of the 0.55.
2. **Sanity cap (RSK-002).** In `PositionSizer.Size` the computed-risk check runs only with `SanityCapEnabled`, against `SanityCapPercent`; the detail text names the percent used (for example `risk 1100.01 above 0.55% of balance 1100`). The other checks and their order do not change.
3. **Recovery amount (RSK-016, RSK-017; owner decision Z).** A pure sizer (suggested `RecoverySizer`) that takes the entries of the instance's symbol only (reuse `LadderSizer.EntriesFromBroker` and `LadderEntry` on the broker records filtered by symbol, ordinal; do not write a second reader of broker data, and do not change the ladder's call, which stays account-wide), the placement time T, InitialCapital, the balance and RewardRisk, replays the events in the ladder's order (reuse the ladder's event ordering; do not write a second one) and returns R with the position, the raw amount, the eight columns C to J, whether the cap applied and whether the steps are frozen (so the tests and a later journal can see them). Base multipliers m1 = 1, mk = (m1 + ... + m(k-1) + RR) / RR, summed in that fixed order; F0 = InitialCapital x RecoveryRunLossPercent / 100 / M; raw = mp x F0 x LadderGrowth ^ step by repeated multiplication. Raising, the eight-loss reset and the freeze as RSK-016. Fail closed with `ArgumentException` on the cases of RSK-017, as the ladder does. Suggested shape: a small immutable state per symbol (columns, position count, consecutive non-wins, frozen) with `Place` and `Close`, like `LadderState`.
4. **Wiring (RSK-016).** In `PreTradeFilters.CheckSizing`: with `SizingPlan` = `Recovery`, read the history as the ladder does, compute R with the instance's `SignalParameters.RewardRisk` and the account balance read for this check, and size with R as the risk amount (add a `PositionSizer` overload that takes money, or pass R / balance x 100; the amount sized must equal R within 1e-9 relative). An `ArgumentException` gives `SkipReason.BrokerDataInvalid`. With `Ladder` the code path is the existing one, unchanged.
5. **Open-risk and currency caps (RSK-004, RSK-005).** In `RiskEngine.Check` the two checks run only when their switch is on; the order of the remaining checks is unchanged. `OpenRiskAtStop` and every report of open risk stay as they are. Do not delete the checks or their values.
6. **Daily budget and guard (RSK-006, RSK-009).** They stay account-wide over all symbols; no code change is expected beyond the defaults; the tests below prove the new numbers.
7. **Start line (RSK-018, PLT-017).** With `Recovery` the core's start line gets one more field at the very end, `sizing=Recovery` (after `mode` and `model` when present). With `Ladder` the line is unchanged, byte for byte.
8. **Hash.** `ParameterHash` is not touched: it holds no risk value, so the plan changes no hash.
9. **Fixed sizing.** A position or order without a stop is still counted at `RiskPercent` of balance in the open-risk and worst-case sums (`SizingRuleRisk`), in both plans. Do not change it; it is an error path.

### WP38b (simulator, cBot, files)

1. **SIM-006.** `--sizing recovery` with `--recovery-run-loss`, `--recovery-max`, `--reward-risk` and `--growth`, valid for `ftmo` and `accept`, listed in their help texts; refused for other commands like any unknown option. The challenge simulator keeps the positions, columns, freeze and reset of each symbol on each path from its own opens and closes, with the same per-symbol state as the bot (preferred: the same `Place` / `Close` state type). Only the two phases are run, as for the ladder; the report's sizing column says `recovery`.
2. **SIM-007.** Defaults 200,000 and 2.0 in `PortfolioParameters`, `FtmoChallengeParameters`, `AcceptanceInputs` and every `--initial-capital` / `--daily-budget` default and help line. The combiner's open-risk and currency caps are off by default (suggested `OpenRiskCapEnabled` and `CurrencyCapEnabled` in `PortfolioParameters`, both false); `--max-open-risk` or `--max-per-currency` given on the command line switches that cap on with the value given. The Recovery state of `--sizing recovery` is kept per symbol (`TradeRecord.Symbol`).
3. **PLT-017.** The seven cBot parameters in the group "Risk" after `MaxVolumeUnits` and the ladder values; `InitialCapital` default 200000.0, `DailyLossLimitPercent` default 2.0, `MaxVolumeUnits` default 3000000.0; `SizingPlan` is a text parsed in the core (refusal "SizingPlan must be one of: Ladder, Recovery." naming the parameter as cTrader shows it); `BuildRisk` passes all of them. All five files under `deploy/params/` get `InitialCapital=200000.0`, `DailyLossLimitPercent=2.0`, `SizingPlan=Ladder`, `RecoveryRunLossPercent=1.0`, `RecoveryMaxRiskPercent=1.28`, `SanityCapEnabled=False`, `SanityCapPercent=0.55`, `OpenRiskCapEnabled=False` and `CurrencyCapEnabled=False` (in the files' existing spelling for booleans), and `MaxVolumeUnits=3000000.0` in the four forex files and `MaxVolumeUnits=1000.0` in the gold file. `deploy/README.md` (the backtest-equality note that names 100000) and `docs/runbook.md` (rows `SanityCap` and `DailyLimit`, and any row naming the open-risk or currency cap: the sanity cap is off by default and its percent is a parameter, the open-risk and currency caps are off by default, `MaxVolumeUnits` is 3,000,000 (gold 1,000), the budget is 2%) name the new values.
4. Check `docs/backtest-howto.md` Part 2 (InitialCapital and Starting capital 200000) and Part 7 (backtest of the Recovery plan) against the build, and correct the guide if a name or value differs.

## Tests required (tagged)

Money in account currency, compare with 1e-4 for the four-decimal values below (they are rounded) and 1e-9 between two computations, unless "bit for bit" is said. Defaults unless said: RecoveryRunLossPercent 1.0, RR = 2.0, LadderGrowth 1.5924, cap 1.28%, InitialCapital 200,000, balance 200,000 at every placement in the pure-function tests, daily budget 2%. "L" = a closed losing entry, "W" = a win, all of one symbol and each closed before the next is placed, unless said. Positions are written p1 to p8 and columns as (C, D, E, F, G, H, I, J), trailing zeros left out.

**RSK-016 (amount, step columns, reset, cap and freeze)**

- **Base sequence ("1% start"):** with all steps 0, positions p1 to p8 risk 40.6027, 60.9040, 91.3561, 137.0341, 205.5511, 308.3267, 462.4901, 693.7351; their sum is 2,000 (within 1e-9), and M = 49.2578125 exactly. With InitialCapital 100,000 every amount is half.
- **Owner-style example, a first win:** W at p1 (risk 40.6027) raises C: columns (1). The next entry is p1 and risks 40.6027 x 1.5924 = 64.6557.
- **L then W at position 2:** from (1): L at p1 (64.6557), W at p2 (60.9040, D = 0 and C = 1, so D is allowed): columns (1, 1). The next p1 risks 64.6557; after an L there, p2 risks 60.9040 x 1.5924 = 96.9836.
- **No skipping a column:** from (1): L (p1 64.6557), L (p2 60.9040), W at p3 (91.3561): D is 0, so the raise moves back to D: columns (1, 1), E stays 0. From all 0: L, L, W at p3: C is raised: columns (1). From (2, 1): W at p2 raises D: (2, 2).
- **A win ends the run:** after any W the next entry is p1 with its column's step.
- **Eight-loss reset:** from (1, 1): eight L risk 64.6557, 96.9836, 91.3561, 137.0341, 205.5511, 308.3267, 462.4901, 693.7351 (together 2,060.1326); at the close of the eighth all columns are 0 and the next entry is p1 at 40.6027. From all 0: eight L lose 2,000 and the ninth risks 40.6027. Seven L and a break-even (net 0) also reset. Seven L and a W at p8 do not reset: the W raises a column by the rule above (from all 0: C).
- **Cap:** with balance 200,000 the cap is 2,560: an entry whose raw amount is 2,800 risks 2,560. A cap below the base amount (balance 2,000, cap 25.60) gives 25.60 at p1.
- **Freeze (proposal):** `RecoveryMaxRiskPercent` 0.05 (cap 100 at 200,000): W at p1 (40.6027): (1); W at p1 (64.6557): (2); the next p1 has raw 102.9578 > 100, risks 100 and freezes the steps; it wins and C stays 2; the next p1 again risks 100 (raw 102.9578). After eight L (from p1) the columns are 0 and the freeze is lifted: the next p1 risks 40.6027 and a W raises C to 1. A raw amount equal to the cap (within the 1e-9 tolerance of `PriceMath`) does not freeze. A new RiskEpochDate also starts from all 0 and unfrozen.
- **RR other than 2 (proposal):** RR = 3: m = 1, 1.333333, 1.777778, ...; the eight base amounts still sum to 2,000.
- **Sizing:** R is the risk amount of RSK-001: on the reference EURUSD buy (stop 20 pips, pip value 0.0001, step 1,000) R = 64.6557 gives 32,000 units (32,327.8 rounded down) and risk money 64.00. With the default cap of 3,000,000 units (12-pip stop, 0.0012 USD per unit): R = 2,560 gives 2,133,000 units and is allowed by the sizer; R = 3,600 gives exactly 3,000,000 units, allowed; R = 3,601.19 gives 3,000,991 units, rounded down to 3,000,000, allowed; R = 3,601.20 gives 3,001,000 units and is skipped as `VolumeAboveCap`, not reduced.
- **Plan off:** with SizingPlan Ladder the same broker data gives the ladder percent (one existing `Rsk013LadderScenarios` case re-run with `SizingPlan = Ladder` set explicitly: same order, same volume, same journal lines).

**RSK-017 (positions and columns from broker data)**

- **Each pair on its own:** EURUSD has W at p1 (C = 1 for EURUSD); GBPUSD has no entry: the next GBPUSD entry risks 40.6027 and the next EURUSD entry 64.6557. Eight GBPUSD losses reset GBPUSD only: EURUSD stays (1). Adding or removing entries of other symbols, open or closed, changes none of these amounts (bit for bit). The same broker data under `Ladder` gives the ladder percent computed from all symbols together, as today.
- **Win threshold:** net +0.01 is a win; net +1e-10 is a non-win and counts towards the eight. Net results include commission and swap: an entry with gross +3.00 and commission -4.20 is a non-win.
- **Ties (one symbol):** a win and a loss close at the same instant: they are taken in placement order (then label), so the column raised and the count of non-wins follow that order; give both orders. A close at the same instant as the new entry's placement counts before it.
- **Positions count entries, not closes:** an entry placed and still open counts towards the position of an entry placed after it (a ninth uses position 8, proposal); a pending order that is cancelled or expires is not in the data and never counts.
- **Partial entries:** a label with one closed part (+50) and an open remainder is not closed and raises nothing; when the remainder closes at -70 the entry is a non-win (net -20). Two partial closes of one position are one entry.
- **Epoch:** entries placed before RiskEpochDate are not counted, also when they close after it: the first entry from the epoch is p1 at 40.6027 with all columns 0.
- **Restart determinism:** the same broker data in three different record orders gives the same R, position and columns bit for bit; a scenario through `FakeBroker` stopped and restarted after a W and two L places the next order with the same volume as without the restart.
- **Fail closed:** a closed trade with net NaN, a close time that is not UTC, and a close after T: no order, skip `BrokerDataInvalid`, in `Recovery` only (the same data in `Ladder` behaves as today).

**RSK-006 (2% budget)**

- Budget 4,000 at InitialCapital 200,000. Day start M = 200,000. A first trade of 2,560 at its stop (no commission) is allowed (used 2,560). It loses: balance 197,440. The next Recovery amount is 2,527.232 (1.28% of 197,440); used = 2,560 + 2,527.232 = 5,087.232 > 4,000: refused with `DailyBudgetExceeded` (the volume, 1,263,000 units at a 20-pip stop, is within the default `MaxVolumeUnits`).
- Boundary after that loss: a new trade risking exactly 1,440.00 at its stop is allowed (used 4,000.00); 1,440.01 is refused; with a commission of 0.01 on the 1,440.00 trade it is refused.
- The same data with `DailyLossLimitPercent` 1.5 explicitly: the limit is 3,000 (the old rule at the new capital).
- In a backtest (REC-014 flag on) the refusal above is written as "would block" and the trade is placed, as today.
- **Account-wide over symbols:** the first loss of 2,560 is on EURUSD and the next trade is on GBPUSD at p1, asking 40.6027: the trade is allowed (used 2,600.6027); with an open AUDUSD position risking 1,400 at its stop it is refused (used 4,000.6027 > 4,000).

**RSK-004 and RSK-005 (caps off by default)**

- Defaults: three open bot positions risking 1,000 each at their stops on 200,000 and a new trade risking 1,000 (4,000 total, above 1.5% = 3,000): the open-risk check refuses nothing (the daily budget then decides: used 4,000, allowed; one more cent refused). With `OpenRiskCapEnabled` true the same trade is refused with `OpenRiskCapExceeded`, measured 4,000, limit 3,000; exactly 3,000 allowed.
- Defaults: long EURUSD, long GBPUSD and a new long AUDUSD (USD short 3 > 2): allowed. With `CurrencyCapEnabled` true: refused with `CurrencyExposureCapExceeded` as today; gold counted with the forex pairs (RSK-015) when on.
- The existing tests of RSK-004, RSK-005 and RSK-015 pass unchanged with the pinned `RiskData.Parameters` (both caps on).

**RSK-002 (sanity cap)**

- Default (off): balance 200,000, risk percent 0.56 (1,120 at 20 pips = 560,000 units, within the default `MaxVolumeUnits` 3,000,000): allowed. A Recovery amount of 2,560 at 200,000 (1.28%): allowed by the sizer.
- On (`SanityCapEnabled` true, 0.55): 0.56 refused with `RiskAboveSanityCap`, measured 1,120, limit 1,100; exactly 0.55 (1,100, 550,000 units) allowed. On with `SanityCapPercent` 1.28: 2,560 at 200,000 allowed, 2,560.01 refused.
- Off: `VolumeAboveCap` and both wrong-side checks still refuse, in the same order as before.
- The existing `Rsk002SanityCapScenarios` pass unchanged with the pinned `RiskData.Parameters` (cap on).

**RSK-009 (guard at the new default)**

- With the defaults on 200,000: daily floor M - 8,000, overall floor 182,000. Equity 182,000 gives CloseAllAndHalt; 182,000.01 does not (with no daily breach). M = 200,000 and equity 192,000 gives the daily condition; 192,000.01 does not.

**RSK-018 (defaults, validation, Ladder unchanged)**

- Every default of RSK-018 asserted on `new RiskParameters()`, including `MaxVolumeUnits` 3,000,000 and both caps off.
- Validation: `RecoveryRunLossPercent` 0, -1, NaN, infinity and 100.0001 refused, 0.01 and 100 accepted; `RecoveryMaxRiskPercent` and `SanityCapPercent` 0 and 100.0001 refused, 100 accepted; an undefined `SizingPlan` value refused; `SizingPlanText` accepts "Ladder" and "Recovery" and refuses "recovery", "" and null with the message of PLT-017.
- **Ladder byte for byte:** the reference Breakout, ZoneFvg (Fvg and Choch) and SessionRange scenarios run once with `SizingPlan` left at its default and once with `Ladder` set explicitly: the same orders (volume, risk money, comment), the same journal lines and the same hash. The parameter hash of each mode equals the value its existing hash test pins, and with `SizingPlan` = `Recovery` it is the same value. The start line ends with `sizing=Recovery` in Recovery and is unchanged in Ladder.

**SIM-006 (simulator)**

- Ten trades of one symbol, each -1R, one a day (so the daily budget does not decide), no commission, from 200,000 with `--sizing recovery`: simulated risks 40.6027, 60.9040, 91.3561, 137.0341, 205.5511, 308.3267, 462.4901, 693.7351, then (reset) 40.6027, 60.9040, and the balance after the tenth 197,898.4933.
- W (+2R) at p1 then L then W at p2: risks 40.6027 (the win adds 81.2054), 64.6557, 60.9040; columns (1, 1); the next risks 64.6557.
- Two symbols interleaved, EURUSD W, GBPUSD L, EURUSD next, GBPUSD next: risks 40.6027, 40.6027, then EURUSD 64.6557 and GBPUSD 60.9040.
- Same seed and trades: same report bit for bit; `--sizing Recovery` (capital R) and `--sizing martingale` refused naming fixed, ladder, recovery; `--recovery-run-loss 0` refused naming the option.

**SIM-007 (simulator defaults)**

- `new PortfolioParameters()`, `new FtmoChallengeParameters()` and `new AcceptanceInputs(...)` default to 200,000 and 2.0 where they have the value, and the combiner's two caps to off; the help texts show them; one existing combine case with `--initial-capital 100000 --daily-budget 1.5 --max-open-risk 1.5 --max-per-currency 2` gives the same output as before, byte for byte; the same log without the two cap options keeps the trades the caps refused before and the report says the caps are off.

**PLT-017 (cBot)**

- What can be tested without cTrader lives in the core and is tested there: the text of `SizingPlan`, the refusal messages, the start line, and that `BuildRisk`'s values reach `RiskParameters` (as PLT-016 did). The parameter files are checked by a test that reads them, if such a test exists for PLT-016; otherwise list the lines in the pull request.

## Owner decisions already taken (do not reopen them in code)

- `MaxVolumeUnits` defaults to 3,000,000 (gold 1,000 in its file); the margin check is unchanged (owner decision X).
- The divisor is the instance's `RewardRisk`, also in the Choch model; no separate parameter (owner decision Y). In the step-column rule it sets the base multipliers (owner decision Z; proposal for RR other than 2).
- Recovery uses step columns C to J per symbol, raised by wins without skipping a column, reset after eight losses in a row; `RecoveryMaxRiskPercent` caps a trade and freezes the steps (owner decision Z; the freeze is a proposal kept from the earlier plan).
- Recovery is per symbol; the Ladder stays account-wide; the daily budget, kill switch and emergency guard stay account-wide (owner decisions V and W).
- Anything else the spec does not settle: write it under "Decisions for the human" and leave the related id `planned`.

## Done

Tests first, CI green, changed lines covered, all commands of `AGENTS.md` run and pasted, the Python checks, status lines to `implemented` only if no decision is open, and a plain summary for the owner of what changes for orders, risk and money: a default build trades as before except for the 200,000 capital, the 2% budget, the sanity, open-risk and currency caps being off and the size cap of 3,000,000 units (gold 1,000); the Recovery plan changes sizing only when `SizingPlan` is set to `Recovery`.
