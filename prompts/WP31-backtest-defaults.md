# WP31: A backtest runs with the default parameters

## Read first

`AGENTS.md`; `docs/spec.md` section 0 (row L) and section 7 (rows RiskEpochDate and NewsEnabled); PLT-014 in `docs/requirements.yml`; `docs/architecture.md` section 10, row "Backtest defaults"; then `src/ZoneFvg.Bot/ZoneFvgBot.cs` (OnStart, BuildParameters, ReadNewsHistory), `src/ZoneFvg.Bot/ParameterText.cs` (Date), `src/ZoneFvg.Core/News/EmbeddedHistoryProvider.cs`, `src/ZoneFvg.Core/App/StartChecks.cs`, `src/ZoneFvg.Core/Ops/CestClock.cs` and the start line in `App/BotCore.cs`.

## Why

The owner's first backtest (EURUSD, Breakout, defaults) took no trade in three years: the embedded news history is empty, so the calendar was unavailable and every entry was blocked. It also needed RiskEpochDate typed by hand. The owner asked that the defaults work without manual changes. Live behaviour must not change.

## Requirement

PLT-014.

## Allowed paths

- new `src/ZoneFvg.Core/App/BacktestDefaults.cs`
- `src/ZoneFvg.Core/News/EmbeddedHistoryProvider.cs`, only if you need a public way to count the events of the embedded history (no change of what it accepts or rejects)
- `src/ZoneFvg.Bot/ZoneFvgBot.cs`, `src/ZoneFvg.Bot/ParameterText.cs`
- new test files under `tests/ZoneFvg.Core.Tests/`
- `docs/backtest-howto.md` (step 6) and `deploy/README.md` (only where they tell the owner to set `NewsEnabled=false` or `RiskEpochDate` for a backtest)
- the `status:` line of PLT-014

No existing test changes.

## What to build

1. `BacktestDefaults` (static, pure, XML docs naming PLT-014):
   - `DateTime RiskEpoch(DateTime? configured, bool isBacktest, DateTime serverTimeUtc)`: the configured date when given; otherwise, in a backtest, the CE(S)T date of `serverTimeUtc` as a UTC-kind date (use `CestClock`, as the rest of the core does); otherwise (live, empty) throw `ArgumentException` with parameter name `RiskEpochDate` and the message the adapter gives today for an empty date.
   - `bool NewsFilterOn(bool newsEnabled, bool isBacktest, int embeddedEventCount)`: false when `newsEnabled` is false; false in a backtest with 0 events; true otherwise.
2. Adapter: `RiskEpochDate` text empty or whitespace means "not configured"; any other text is parsed as today (an invalid text is still refused). The epoch comes from `BacktestDefaults.RiskEpoch` with `Server.Time` at start. `NewsEnabled` passed to the core is `BacktestDefaults.NewsFilterOn(...)` with the number of events of the embedded history; when that turns the filter off although `NewsEnabled` is true, log one adapter line at start: `ZFVG adapter news filter off for this backtest: the embedded news history has no events`. No other rule in the adapter.
3. Guides: step 6 of `docs/backtest-howto.md` no longer asks to set `NewsEnabled=false` or `RiskEpochDate`; it says the defaults do it in a backtest, and that the start line shows `news=disabled` and the backtest's first day as `riskEpoch`. Keep "set them by hand" as an option for a run with a different epoch.

## Tests (unit, all tagged PLT-014)

- RiskEpoch: configured date returned in both modes; backtest empty at 2023-10-22 21:05 UTC → 2023-10-22 (CEST date, kind Utc); backtest empty at 2023-10-22 22:30 UTC → 2023-10-23 (CEST is UTC+2 that day); a winter case (2023-12-04 23:30 UTC → 2023-12-05); live empty → throws with ParamName `RiskEpochDate`; the result passes `StartChecks` for that server time (epoch start not after it).
- NewsFilterOn: the full truth table (2 × 2 × event count 0 / 1 / many).
- If you add an event count to the embedded history provider: 0 for empty text, the right count for a small valid history, and nothing it accepted or rejected before changes.

## Definition of done

Tests first; CI green; `BacktestDefaults.cs` fully covered in the per-file coverage listing; three Python checks pass; PLT-014 `implemented` only if "Decisions for the human" is empty; the pull request says in plain words what changes for a backtest and that live is unchanged.
