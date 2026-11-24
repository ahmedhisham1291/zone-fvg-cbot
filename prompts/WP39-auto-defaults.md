# WP39: Defaults of 24 November 2026 and the automatic symbol profile

## Read first

`AGENTS.md`; `docs/spec.md` 0 (rows AA to AG of v0.14), 2, 3.6, 3.9 ("Bias", "Skipped when", "Trading window"), 4.1, 4.3 (the table), 5.1, 7 (rows EntryMode, EntryModel, ChochUseBias, ChochMinStopPips, MinStopPips, RiskPercent, LadderEnabled, MaxVolumeUnits, SessionStartUtc / SessionEndUtc, MaxSpreadPips) and **7.1**, 13.1 ("What goes live"); EXE-030, EXE-031, EXE-032, PLT-018, and the changed texts of PLT-010, PLT-012, PLT-015, PLT-016, PLT-017 in `docs/requirements.yml`, and for what they refer to EXE-002, EXE-003, EXE-008, EXE-028, EXE-029, RSK-001, RSK-002, RSK-013, RSK-018, SIG-035, REC-020; `docs/architecture.md` section 10, row "Defaults and automatic symbol profile" of 2026-11-24.

Code to reuse, not to copy: `App/ChochText.cs` and `App/SizingPlanText.cs` (how a cBot value is read in the core and refused with the cTrader name), `App/BotParameters.cs` (`Validate`), `App/BotCore.cs` (the start line), `Execution/ParameterHash.cs`, `Execution/PreTradeFilters.cs` (the spread and stop checks, unchanged), `Core/PriceMath`, `src/ZoneFvg.Bot/ZoneFvgBot.cs` (`OnStart`, `BuildParameters`, `BuildExecution`, `BuildRisk`, `ParameterMessage`); tests: `App/Plt017CbotSizingTests.cs` (how the cBot source and the parameter files are read), `TestSupport/BotHarness`, `Execution/ExecutionData.cs`, the existing hash tests.

## Requirements

EXE-030, EXE-031, EXE-032, PLT-018, PLT-010, PLT-012, PLT-015, PLT-016, PLT-017.

PLT-010 and PLT-012 are `verify: manual`: the owner proves them; leave their status `planned`. One pull request, branch `wp/39-auto-defaults`. If it goes over about 1,000 changed lines (tests included), say so in the pull request; do not split it.

## Allowed paths

- `src/ZoneFvg.Core/App/**` (a new `SymbolProfile.cs` or similar; `BotParameters.cs`; `BotCore.cs`, the start line only).
- `src/ZoneFvg.Core/Execution/ParameterHash.cs` (and the call site in the core that computes the hash).
- `src/ZoneFvg.Bot/ZoneFvgBot.cs` (defaults, `BuildParameters`, the spread line at start), and a new file in `src/ZoneFvg.Bot/` only if the reading of the symbol needs one.
- `deploy/**`, `docs/runbook.md`, `docs/backtest-howto.md` (only to correct a name or a value that differs from the build: the guide was written in the plan), `README.md` (only a sentence that names an old default).
- New test files under `tests/ZoneFvg.Core.Tests/`; a helper under `tests/ZoneFvg.Core.Tests/TestSupport/` only by adding to it; `tests/ZoneFvg.Core.Tests/App/Plt017CbotSizingTests.cs` for the changes listed under "Owner authorisation".
- `docs/requirements.yml`: the status lines of EXE-030, EXE-031, EXE-032, PLT-015, PLT-016, PLT-017 and PLT-018.

Not allowed: `tools/`, `docs/spec.md`, `.github/`, any core record default (`ExecutionParameters`, `ChochParameters`, `RiskParameters`, `SignalParameters`, `BreakoutParameters`, `SessionRangeParameters`), any filter, sizer or signal rule, any change to `PreTradeFilters` or `PositionSizer`.

## Owner authorisation to change existing tests

The owner asked on 2026-11-24 for new cBot defaults and for automatic symbol values. The core's record defaults do **not** change, so the core tests keep their fixtures. Existing tests that assert an old **cBot** default or an old line of a parameter file may be changed, only as follows, and each changed test is listed in the pull request with its old and new expectation:

- `App/Plt017CbotSizingTests.cs`, `Bot_AccountDefaults_AreThoseOfSpecV012`: the expected declaration `[Parameter("MaxVolumeUnits", DefaultValue = 3000000.0, Group = "Risk")]` becomes `DefaultValue = 0.0`; the InitialCapital and DailyLossLimitPercent cases stay.
- `App/Plt017CbotSizingTests.cs`, `ParameterFiles_CarryTheNewLinesWithTheirDefaults`: the expected volume line becomes `MaxVolumeUnits=0.0` in all five files (was 3000000.0, gold 1000.0). The check of the line after it becomes the line of the file's new order (if `LadderEnabled=false` is put right after the volume line, as the cBot orders its parameters, assert `LadderEnabled=false` there and `SizingPlan=Ladder` after the ladder lines). Every other expected line stays.

Keep the old behaviour proved where it is still a behaviour: a test that needs the forex values in pips passes them explicitly (as the core fixtures already do). No other existing test may be changed, and no assertion may be removed or loosened. Every parameter-hash test must pass with its pinned value unchanged. If any other existing test fails, stop and ask.

## Build

1. **Profile function (EXE-030, EXE-031).** A pure static function in `ZoneFvg.Core.App` (suggested `SymbolProfile.Resolve`) with inputs symbol name, base asset name, quote asset name, pip size, spread at start (price), and the four cBot values `MaxSpreadPips`, `MinStopPips`, `ChochMinStopPips`, `MaxVolumeUnits`. It returns a record (suggested `SymbolProfileResult`: `Profile` (enum `Fx`, `Gold`, `None`), `PipSize`, the four effective values, the four flags "automatic", and `SpreadNote` (string or null)) or throws `ArgumentException` whose `ParamName` is the cTrader name of the parameter (for the pip-size refusals use `"PipSize"`). Rules exactly as spec 7.1 and EXE-030/EXE-031:
   - Profile by asset names, ordinal ignoring case; the currency list of spec 7.1 as a fixed array; no culture, no dictionary order.
   - The pip-size checks first: not finite or not above 0 is refused for every profile; Fx needs 0.0001, or 0.01 for a JPY quote, within a relative 1e-9.
   - 0 = automatic. Gold: price / pip size, `Math.Round(x, 9, MidpointRounding.AwayFromZero)`; Fx: the pips as written; `MaxVolumeUnits` 3,000,000 or 1,000.
   - A value not 0: refused when negative or not finite; checked against the range of spec 7.1 (one tenth to ten times the profile value, both ends accepted with `PriceMath`'s tolerance), in price for gold (value x pip size) and in pips for Fx; units for `MaxVolumeUnits`. Profile None: no range check.
   - The parameters are checked in the order MaxSpreadPips, MinStopPips, ChochMinStopPips, MaxVolumeUnits; the first refusal is thrown.
   - `SpreadNote`: when spread at start > effective MaxSpreadPips x pip size (by more than `PriceMath`'s tolerance), the text below; otherwise null. A spread that is not finite or below 0 gives no note (the filters fail closed on it later).
   - Messages, exactly (numbers in the invariant "R" format after rounding to 9 decimals; `{name}` is the cTrader name):
     - Gold, outside the range: `{name} {value} is {price} USD of price for {symbol} at its pip size {pip}; accepted for gold: {lo} to {hi} USD; set 0 for the automatic value {auto} ({autoUsd} USD).` For `MaxVolumeUnits`: `MaxVolumeUnits {value} is outside the range for gold ({symbol}): {lo} to {hi} units; set 0 for the automatic value 1000.`
     - Fx, outside the range: `{name} {value} is outside the range for the currency pair {symbol}: {lo} to {hi} pips; set 0 for the automatic value {auto}.` (`units` for `MaxVolumeUnits`).
     - None with 0: `{name} is 0 (automatic), but {symbol} (base {base}, quote {quote}) has no automatic profile; set {name} by hand.`
     - Fx pip size: `{symbol} reports a pip size of {pip}; the currency-pair values are in pips of {std}; the bot does not start.`
     - Pip size not usable: `{symbol} reports a pip size of {pip}, which is not a finite number greater than 0; the bot does not start.`
     - Negative or not finite: `{name} must be 0 (automatic) or a finite number greater than 0; got {value}.`
     - Spread note: `spread at start {spreadPips} pips ({spread} in price) is above MaxSpreadPips {max}; entries are skipped while it lasts.`
2. **BotParameters (EXE-032).** An init-only `SymbolProfile` (the result record, default null). `Validate` refuses, naming `SymbolProfile`, a record whose `MaxSpreadPips` or `MinStopPips` differs from `Execution`, whose `ChochMinStopPips` differs from `Choch` when `Choch` is set, or whose `MaxVolumeUnits` differs from `Risk` (exact equality: the adapter passes the same doubles). Null: nothing changes.
3. **Start line (EXE-032).** With the record, the core's start line gets at the very end, after `sizing` when present: `profile`, `pipSize`, `maxSpreadPips`, `minStopPips`, `chochMinStopPips`, `maxVolumeUnits`, `auto` as EXE-032 says. Without it, the line is unchanged byte for byte.
4. **Hash (EXE-032).** `ParameterHash.Canonical` and `Compute` take the record as a further optional argument. With it, each of `MaxSpreadPips`, `MinStopPips` and (Choch only) `ChochMinStopPips` that is automatic is written in its usual place as `auto:<value><unit>`: Gold `auto:0.4USD`, `auto:8USD`, `auto:3USD`; Fx `auto:1.5pips`, `auto:12pips`, `auto:5pips` (the profile value in "R" format). Nothing is appended. Values set by hand, `MaxVolumeUnits`, profile None and a null record: the text is exactly as before. The core passes `BotParameters.SymbolProfile` wherever it computes the hash today. Update the XML documentation of `Canonical`.
5. **cBot (PLT-018 and the changed PLT texts).** Defaults: `EntryMode` "ZoneFvg", `EntryModel` "Choch", `ChochUseBias` true, `SessionStartUtc` "00:00", `SessionEndUtc` "24:00", `LadderEnabled` false, `MaxSpreadPips` 0.0, `MinStopPips` 0.0, `ChochMinStopPips` 0.0, `MaxVolumeUnits` 0.0; update their XML summaries (spec 7.1, v0.14). In `BuildParameters`, before `BuildSignal`, call the profile function with `Symbol.Name`, `Symbol.BaseAsset.Name`, `Symbol.QuoteAsset.Name`, `Symbol.PipSize` and `Symbol.Spread`; use its effective values in `BuildExecution`, in `ChochText.Build` and in `BuildRisk`, and set `BotParameters.SymbolProfile`. Its `ArgumentException` takes the existing `catch` path ("invalid parameter: ..."), so the cBot stops with the message. After the start checks, write `SpreadNote` with `_log.Info` when it is not null. Nothing else in the adapter changes.
6. **Files (PLT-010, PLT-016, PLT-017, PLT-018).** All five files under `deploy/params/` carry `EntryMode=ZoneFvg`, `EntryModel=Choch`, `ChochUseBias=true`, `SessionStartUtc=00:00`, `SessionEndUtc=24:00`, `MaxSpreadPips=0.0`, `MinStopPips=0.0`, `ChochMinStopPips=0.0`, `MaxVolumeUnits=0.0` and a new line `LadderEnabled=false` (after `MaxVolumeUnits`, in the cBot's order); the gold file is then equal to the forex files apart from the `HeartbeatUrl` line. `deploy/README.md` and `docs/runbook.md`: every place that names gold's own values, the old entry-mode default or the ladder as the default names the new defaults and says that the only value chosen by hand for a backtest is `LadderEnabled`; live, `RiskEpochDate`, the Telegram values, the heartbeat and the news feed address stay to be set (spec row AG).

## Tests required (tagged)

Tolerance 1e-9 relative unless "exactly" is said. Spread and stop in price unless "pips" is said.

**EXE-030 (profile and automatic values)**

- XAUUSD (base XAU, quote USD), all four 0: pip 1 gives exactly 0.4, 8, 3 and 1000; pip 0.01 gives exactly 40, 800, 300 and 1000; pip 0.1 gives 4, 80, 30. For pip 1 and pip 0.01 the spread limit is 0.40 USD (0.4 x 1 and 40 x 0.01) and the Choch stop floor 3.00 USD (3 x 1 and 300 x 0.01).
- Base `xau`, quote `usd` (lower case): Gold. Base XAU, quote EUR: None. XAGUSD: None. BTCUSD (base BTC): None. US30 (base US30): None.
- EURUSD pip 0.0001, all 0: Fx, exactly 1.5, 12, 5, 3,000,000. USDJPY pip 0.01: Fx, the same four. USDJPY pip 0.0001: refused (Fx pip-size message, `ParamName` "PipSize"). EURUSD pip 0.00001: refused. EURUSD pip 0.0001 x (1 + 1e-10): accepted; x (1 + 1e-8): refused.
- Pip size 0, -0.01, NaN, infinity: refused for Gold, Fx and None.
- None with MaxSpreadPips 0: refused naming MaxSpreadPips, with the exact message; None with all four set (2, 10, 4, 500): accepted, values as given, no range check.
- **Scenario, the owner's evidence:** with the effective values of XAUUSD at pip 0.01 (pip value per unit 0.01 USD) and at pip 1 (pip value 1 USD), the same gold signal through the existing filters (ExecutionHarness or `FakeBroker`): spread 0.08 USD and 0.30 USD pass the spread check, 0.41 USD is skipped as `SpreadTooWide`, at both pip sizes; and a Choch signal with a stop distance of 2.99 USD is skipped for its small stop and 3.00 USD is not, at both pip sizes. The planned volume and risk money are equal at both pip sizes.

**EXE-031 (values set by hand)**

- Gold pip 0.01: MaxSpreadPips 0.4 refused with exactly `MaxSpreadPips 0.4 is 0.004 USD of price for XAUUSD at its pip size 0.01; accepted for gold: 0.04 to 4 USD; set 0 for the automatic value 40 (0.4 USD).`; 4 accepted (0.04 USD), 3.99 refused, 400 accepted, 400.01 refused, 40 accepted. MaxSpreadPips 1.5 (the old forex default on gold) refused. ChochMinStopPips 3 refused (0.03 USD); 30 accepted (0.30 USD); 29.99 refused. MinStopPips 8 refused (0.08 USD), 80 accepted.
- Gold pip 1: MaxSpreadPips 0.4, MinStopPips 8, ChochMinStopPips 3 accepted (the old gold file); MinStopPips 0.79 refused, 0.8 accepted; ChochMinStopPips 0.29 refused, 0.3 accepted, 30 accepted, 30.01 refused.
- Gold MaxVolumeUnits: 3,000,000 refused with the exact volume message; 200 (the file value before v0.12) accepted; 100 accepted; 99 refused; 10,000 accepted; 10,001 refused.
- Fx EURUSD: MaxSpreadPips 1.5, 0.15 and 15 accepted, 0.14 and 15.01 refused with the exact Fx message; MinStopPips 8 (the value before v0.9) accepted, 1.19 refused; ChochMinStopPips 0.5 accepted, 0.49 refused; MaxVolumeUnits 500,000 accepted, 299,999 refused, 30,000,000 accepted.
- -1, NaN and positive infinity for each of the four: refused with the exact message, `ParamName` the parameter.
- Order: MaxSpreadPips and MinStopPips both out of range: the message names MaxSpreadPips.
- Spread note: Gold pip 0.01 all automatic, spread 0.50 USD: the exact note (`spread at start 50 pips (0.5 in price) is above MaxSpreadPips 40; entries are skipped while it lasts.`); spread 0.40: none; spread 0: none; NaN: none. Fx spread 0.0002 with 1.5: note; 0.00015: none.

**EXE-032 (start line, hash, validation)**

- Start line, XAUUSD pip 0.01, all automatic, Choch model, Ladder: ends with exactly ` mode=ZoneFvg model=Choch profile=Gold pipSize=0.01 maxSpreadPips=40 minStopPips=800 chochMinStopPips=300 maxVolumeUnits=1000 auto=MaxSpreadPips,MinStopPips,ChochMinStopPips,MaxVolumeUnits`. With Recovery: `sizing=Recovery` comes before `profile=`. EURUSD pip 0.0001 with MinStopPips 8 set by hand: `profile=Fx pipSize=0.0001 maxSpreadPips=1.5 minStopPips=8 chochMinStopPips=5 maxVolumeUnits=3000000 auto=MaxSpreadPips,ChochMinStopPips,MaxVolumeUnits`. All four set by hand: `auto=none`. Without the record: every existing start-line test passes unchanged.
- Hash: the Choch defaults with Gold all automatic at pip 1 and at pip 0.01 give the same canonical text and the same hash; the text contains `MaxSpreadPips=auto:0.4USD;MinStopPips=auto:8USD;` and `ChochMinStopPips=auto:3USD;`. Pin the whole canonical text and the hash, computed independently (for example with `hashlib.sha256` in Python, shown in the pull request). Fx all automatic: `MaxSpreadPips=auto:1.5pips;MinStopPips=auto:12pips;`, pinned likewise. Fvg model and Breakout mode with Gold automatic: no `ChochMinStopPips` in the text, the two pip-dependent fields as `auto:`. A record whose four values were all set by hand (1.5, 12, 5, 3,000,000 on EURUSD) gives exactly the existing pinned hash of the same parameters without a record; a record with only MaxVolumeUnits automatic, too. Every existing hash test passes unchanged.
- Validate: a record with MaxSpreadPips 40 beside `Execution.MaxSpreadPips` 1.5 refused naming `SymbolProfile`; the same for MinStopPips, ChochMinStopPips (Choch set) and MaxVolumeUnits; a record with ChochMinStopPips 300 and `Choch` null accepted.

**PLT-018 and the changed PLT texts (cBot, files)**

- The cBot source declares exactly `[Parameter("EntryMode", DefaultValue = "ZoneFvg", Group = "Entry mode")]`, `[Parameter("EntryModel", DefaultValue = "Choch", Group = "Entry model")]`, `[Parameter("ChochUseBias", DefaultValue = true, Group = "Entry model")]`, `[Parameter("ChochMinStopPips", DefaultValue = 0.0, Group = "Entry model")]`, `[Parameter("SessionStartUtc", DefaultValue = "00:00", Group = "Execution")]`, `[Parameter("SessionEndUtc", DefaultValue = "24:00", Group = "Execution")]`, `[Parameter("MinStopPips", DefaultValue = 0.0, Group = "Execution")]`, `[Parameter("MaxSpreadPips", DefaultValue = 0.0, Group = "Execution")]`, `[Parameter("MaxVolumeUnits", DefaultValue = 0.0, Group = "Risk")]`, `[Parameter("LadderEnabled", DefaultValue = false, Group = "Risk")]`, each once; and unchanged `RiskPercent` 0.5, `SizingPlan` "Ladder", `InitialCapital` 200000.0, `NewsEnabled` true.
- In the source, the call of the profile function is inside `BuildParameters` and before `BuildSignal()`; `BuildExecution`, `ChochText.Build` and `BuildRisk` take the effective values (assert the assignments, as PLT-017 does).
- **The defaults run:** in the core, the cBot's default values (texts and numbers as declared) through `ChochText.Build`, `ChochText.SessionStartUtc` / `SessionEndUtc`, `ChochText.ForEveryModeCheck`, the profile function and `BotParameters.Validate`, for EURUSD (pip 0.0001), USDJPY (0.01), XAUUSD (1) and XAUUSD (0.01): accepted; `Choch` is set with `ChochUseBias` true; the risk parameters have `LadderEnabled` false and `SizingPlan` Ladder. The same with EntryMode Breakout and otherwise the defaults: refused naming EntryModel (unchanged rule of SIG-035); with EntryModel Fvg also: refused naming SessionEndUtc (24:00 in Breakout, unchanged); with 07:00 and 17:00 also: accepted.
- Parameter files: each of the five carries each of the lines of build step 6 once; `LadderEnabled=false` is the line after `MaxVolumeUnits=0.0`; the five files are equal apart from their `HeartbeatUrl` line.

## Owner decisions already taken (do not reopen them in code)

- The defaults of rows AA and AB; `LadderEnabled` false; Recovery opt-in; `InitialCapital` 200,000; news unchanged.
- The profile values of 7.1: Fx 1.5 / 12 / 5 pips and 3,000,000 units; Gold 0.40 / 8.00 / 3.00 USD and 1,000 ounces.
- The plan's choices in `docs/architecture.md` section 10 (row of 2026-11-24): resolved in the adapter's `BuildParameters` through a core function, core record defaults unchanged, the range of one tenth to ten times, the spread at start only a Log line, the hash form `auto:<value><unit>`, Choch with another mode still refused.
- Anything else the spec does not settle: write it under "Decisions for the human" and leave the related id `planned`.

## Done

Tests first, CI green, changed lines covered, all commands of `AGENTS.md` run and pasted, the Python checks, status lines to `implemented` only if no decision is open (PLT-010 and PLT-012 stay `planned`: manual), and a plain summary for the owner of what changes for orders, risk and money: a default run now trades ZoneFvg with the Choch model all day with the D1 trend filter, risks a fixed 0.5% per trade unless `LadderEnabled` is true, and uses gold's spread, stop and size limits in price whatever pip size the broker reports; a value typed by hand that does not fit the symbol stops the bot at start with a Log line.
