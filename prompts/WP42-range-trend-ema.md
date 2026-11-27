# WP42: SessionRange direction from D1 EMA(20) against EMA(50)

## Read first

`AGENTS.md`; `docs/spec.md` 0 (row AY of v0.17, and rows AS to AX of v0.16), 3.1 (the bias, and the Breakout trend filter, which compares two D1 EMAs in the same way), 3.8 (Evaluation), 7 (rows BiasEmaPeriod, TrendSlowEmaPeriod, RangeTrendFastEma); SIG-042 in `docs/requirements.yml`, and for what it refers to SIG-028 (the Breakout trend filter), SIG-030 to SIG-034 (SessionRange), PLT-015, PLT-020.

Code to reuse, not to copy: `Signals/SignalEngine.cs` (how the bias and the Breakout trend filter's slow EMA are computed and passed: `_trendSlowEmaPeriod`), `Signals/SessionRangeTracker.cs` (`OnM15Closed` takes a `Bias`; `Evaluate` refuses `Bias.Neutral`), `Signals/SessionRangeParameters.cs`, `Execution/ParameterHash.cs` (how `TrendSlowEmaPeriod` is appended only when not off), `App/BotParameters.cs` (`Validate`), `src/ZoneFvg.Bot/ZoneFvgBot.cs` (the SessionRange parameters and how they are built); tests: the SIG-028 tests, the SessionRange tests and scenarios (`Sig034SessionRangeScenarios`), the hash pins of PLT-020 (`469b1f35`, `f949f461`) and of SessionRange (`3f0f30a3`).

## Requirements

SIG-042. One pull request, branch `wp/42-range-trend-ema`.

## Allowed paths

- `src/ZoneFvg.Core/Signals/**` (`SessionRangeParameters.cs`, `SignalEngine.cs`; the tracker only if the direction cannot be passed to it as the `Bias` it already takes).
- `src/ZoneFvg.Core/Execution/ParameterHash.cs`, `src/ZoneFvg.Core/App/BotParameters.cs` (validation of the range against `BiasEmaPeriod`), and the place where the core builds or validates the SessionRange parameters for the start line, if any.
- `src/ZoneFvg.Bot/ZoneFvgBot.cs` (the new parameter and where the SessionRange parameters are built).
- `deploy/**`, `docs/runbook.md`, `docs/backtest-howto.md`, `deploy/README.md`.
- New test files under `tests/ZoneFvg.Core.Tests/`; helpers under `TestSupport/` only by adding; existing tests only as listed below.
- `docs/requirements.yml`: the status line of SIG-042.

Not allowed: `tools/`, `docs/spec.md`, `.github/`, `prompts/`, any other trading rule, sizing, risk, the brake, the timed exit, any core record default other than the new field (core default 0).

## Owner authorisation to change existing tests

The parameter files gain `RangeTrendFastEma=20`, so their hashes change. The tests that pin the hash of the five parameter files (`469b1f35` for gold and `f949f461` for the forex files, added by WP41) may change to the new values, computed independently (for example with Python's `hashlib`, shown in the pull request, after reproducing at least three existing pins with the same script). Tests that assert the exact list or order of lines of the parameter files, or the cBot's exact SessionRange parameter declarations, may gain the new line. List each changed test with its old and new expectation. Every other hash pin, in particular `3f0f30a3`, stays. No assertion may be removed or loosened. If any other existing test fails, stop and ask.

## Build

1. **Parameter (SIG-042).** `SessionRangeParameters.RangeTrendFastEma` (int, default 0), XML documentation naming SIG-042 and row AY; its own `Validate` refuses a negative value or 1; `BotParameters.Validate` refuses, naming `RangeTrendFastEma`, a value above 0 that is not below `BiasEmaPeriod`. Use the existing message style.
2. **Direction (SIG-042).** In `SignalEngine`, when the instance runs SessionRange and `RangeTrendFastEma` is above 0, compute D1 EMA(`RangeTrendFastEma`) with exactly the method, seed and history of the bias EMA, and pass to the tracker the direction of build rule: `Bias.Long` when EMA(fast) > EMA(`BiasEmaPeriod`) by more than `PriceMath.DefaultTolerance`, `Bias.Short` when below by more than it, otherwise `Bias.Neutral` (no setup); `Bias.Neutral` too while either EMA has too few D1 bars. The last closed D1 bar at the evaluation decides, as for the bias. With 0: the bias of 3.1 as today, and nothing else changes (no extra EMA is computed).
3. **Hash (SIG-042).** The SessionRange part of the canonical text appends `RangeTrendFastEma=<n>;` only when n is not 0, in the place and style used for `TrendSlowEmaPeriod`. With 0 every hash is unchanged. Update the XML documentation of `Canonical`.
4. **Warm-up.** If the warm-up count of D1 bars is computed from `BiasEmaPeriod`, check that it also covers `RangeTrendFastEma` (it is smaller by the validation rule, so nothing should change); say what you found.
5. **cBot.** `[Parameter("RangeTrendFastEma", DefaultValue = 20, Group = "<the group of the other Range parameters>")]`, passed to the core's SessionRange parameters; its XML summary names spec 3.8 and row AY. A refusal at start names `RangeTrendFastEma` as the existing range refusals name theirs.
6. **Files and guides.** All five files under `deploy/params/` carry `RangeTrendFastEma=20` after the other range lines, in the cBot's order; they stay equal apart from `HeartbeatUrl`. The guides name the new default in one sentence each where they list the SessionRange settings.

## Tests required (tagged SIG-042)

- Validation: -1, 1 refused naming `RangeTrendFastEma`; 0 and 2 accepted; with `BiasEmaPeriod` 50: 49 accepted, 50 and 51 refused.
- Direction on hand-built D1 series (`BarBuilder`): EMA(20) above EMA(50) while the last close is below EMA(50) → a buy-stop setup at the range high (the old rule would have given a sell); the mirror case → a sell-stop at the range low; EMA(20) equal to EMA(50) within tolerance → no setup that trading day; one tolerance either side of equal → long / short.
- The decision uses the last D1 bar closed at the evaluation, not one that closes later in the trading day (a D1 bar that flips the EMAs after the evaluation does not change that day's setup).
- Too few D1 bars for EMA(20) or EMA(50) → no setup (fail closed).
- `RangeTrendFastEma` 0: a full SessionRange scenario run gives the same signals, orders and journal lines, byte for byte, as before this work package (pin one existing scenario's output); hash `3f0f30a3` unchanged.
- Other modes: ZoneFvg (Fvg and Choch) and Breakout with `RangeTrendFastEma` 20 set in the parameters: identical signals and hashes to 0 (the field is not read and not hashed outside SessionRange; if the parameters of another mode cannot carry it, prove that instead).
- Hash: SessionRange with 20 gives a canonical text ending in the range fields plus `RangeTrendFastEma=20;` (pin text and hash, computed independently); the five parameter files' new hashes pinned.
- cBot source: the declaration once, with default 20, and the assignment into the SessionRange parameters; the five files carry the line once, in order, equal apart from `HeartbeatUrl`.
- A full scenario through `FakeBroker` where the two rules disagree: with 0 the bot places a sell stop, with 20 a buy stop, for the same bars.

## Owner decisions already taken (do not reopen them in code)

- Row AY: EMA(20) against EMA(`BiasEmaPeriod`) for SessionRange only, default 20 in the cBot, 0 = the old rule, equal = no setup, hash field only when not 0.
- Anything the spec does not settle: write it under "Decisions for the human" and leave SIG-042 `planned`.

## Done

Tests first, CI green, changed lines covered, all commands of `AGENTS.md` run and pasted (the dotnet commands from CI), the Python checks, the status line to `implemented` only if no decision is open, and a plain summary for the owner: a default run on gold now buys the Asian range break when the D1 EMA(20) is above the D1 EMA(50) and sells it when below, instead of using the close against the EMA(50); nothing else in the trading rules, sizing or limits changes; the parameter files' hashes change for that reason only.
