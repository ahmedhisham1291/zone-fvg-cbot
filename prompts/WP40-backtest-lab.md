# WP40: Offline backtest lab (six years of data, sweeps, money management)

## Read first

`AGENTS.md`; `docs/spec.md` 0 (rows AH to AR of v0.15, and rows C, L, R, U, AA, AC of earlier versions), 3.9, 4.1, 4.1.1, 4.2, 7, 7.1, 12.1, 12.4, **12.5**, 13.1, 14 (question 5), 15.2, 15.4, 15.5; PLT-019, LAB-001 to LAB-014, SIM-008, SIM-009 in `docs/requirements.yml`, and for what they refer to SIM-001 to SIM-007, RSK-001, RSK-006, RSK-008, RSK-010, RSK-013, RSK-016, REC-003, REC-014, REC-016, EXE-030, PLT-014, PLT-018; `docs/architecture.md` sections 3, 4, 5 and section 10, row "Offline backtest lab" of 2026-11-26.

Code to reuse, not to copy: `App/BotCore.cs` (constructor, `OnStart`, `OnBarClosed`, `OnTimer`, `OnTradeEvent`, the start line), `App/StartChecks.cs`, `App/BotParameters.cs`, `App/SymbolProfile.cs`, `App/ChochText.cs`, `App/SessionRangeText.cs`, `App/SizingPlanText.cs`, `App/BacktestDefaults.cs`, `Domain/NewYorkClock.cs`, `Domain/PriceMath.cs`, `Execution/Ports/*` (`IBroker` and its records, `SymbolCostMath`, `TradingSession`, `TradeExitReason`), `Recovery/IBarHistory.cs`, `Simulation/*` (`BacktestLogExtract`, `TradeLogCsv`, `TradeRecord`, `PortfolioCombiner`, `AcceptanceReport`, `FtmoChallengeSimulator`, `SimCommandLine`, `Xoshiro256StarStar`, `NearestRank`); `src/ZoneFvg.Bot/ZoneFvgBot.cs` (`OnStart`, `OnTimer`, the event wiring, `BuildParameters` and the `Build*` methods: the lab must build the same `BotParameters`), `src/ZoneFvg.Bot/CTraderBars.cs` (when the cBot treats a bar as closed), `src/ZoneFvg.Bot/CTraderBroker.cs` (how broker records are filled); `deploy/params/*.cbotset.example`; `tools/sim/` (how a console project only does IO); tests: `Fakes/FakeBroker.cs` (fill semantics to mirror, not to reference from `src/`), `TestSupport/BarBuilder.cs`, `App/Plt017CbotSizingTests.cs` (how the cBot source and parameter files are read in a test), `Simulation/SimData.cs`; `research/search/lib/data.py` and `costs.py` (the earlier harness's bar and cost conventions); `.github/workflows/research.yml` (a workflow that is not a merge gate).

## Requirements

PLT-019, LAB-001, LAB-002, LAB-003, LAB-004, LAB-005, LAB-006, LAB-007, LAB-008, LAB-009, LAB-010, LAB-011, LAB-012, LAB-013, LAB-014, SIM-008, SIM-009.

PLT-019 and LAB-001 are `verify: manual`: they are proven by the first recorded run of the workflow on `main`, which the orchestrating session starts after the part is merged; leave their status `planned`.

## The goal, in one paragraph

The owner wants to stop backtesting by hand in cTrader for exploration. CI must run the **real** `ZoneFvg.Core`, the code the cBot runs, over about six years of one-minute history of five symbols, try a limited grid of entry settings, choose one on the development years by a rule written down before the sweep, run it once on the holdout, and compare sizing plans against a target of about 1% a month, honestly. Nothing in the bot, its rules, its defaults or its money decisions changes.

## Split (size limit)

Four pull requests, in this order, each from an up-to-date `main`, each with only its ids. Each part needs the label `human-approved-infra` because it creates or changes `.github/workflows/lab.yml`; the orchestrating session adds it after reading the workflow diff. The owner approved this protected path through the plan (spec row AQ).

| Part | Branch | Ids | Content |
|---|---|---|---|
| WP40a | `wp/40a-lab-data` | PLT-019 (data and Python-test jobs), LAB-001 | Python downloader and converter, its unit tests, the workflow with the `data` stage and the `lab-tests` job, `lab/data/README.md` |
| WP40b | `wp/40b-lab-engine` | LAB-002, LAB-003, LAB-004, LAB-005, LAB-006, LAB-007, LAB-008 | `src/ZoneFvg.Lab`: minute reader, bar builder, simulated broker, costs, replay through `BotCore`, parameter reader, run record; workflow `single` stage and the .NET part of `lab-tests` |
| WP40c | `wp/40c-lab-sweeps` | LAB-009, LAB-010, LAB-011, LAB-012 | Grid file and the first grid, sharded sweep, measures, selection rule LAB-SEL-1, holdout gate; workflow `sweep` and `holdout` stages |
| WP40d | `wp/40d-lab-money` | SIM-008, SIM-009, LAB-013, LAB-014 | Drawdown brake and account replay in `Simulation/`, the money-management table, the report and its delivery; workflow `report` job |

If a part goes over about 800 changed lines (tests included, test data files not counted), split it once more by requirement id (for example WP40b1 LAB-002 to LAB-005 and WP40b2 LAB-006 to LAB-008) and say so in the pull request. Never put two parts' ids in one pull request.

## Allowed paths

- **WP40a:** `lab/data/**` (new), `lab/README.md` (new), `lab/.gitignore` (new; ignores the local data and output folders), `.github/workflows/lab.yml` (new file; the only protected path), `docs/test-records/WP40-lab.md` (new; the template the orchestrating session fills after the first run), `docs/requirements.yml` (no status line changes: both ids are manual).
- **WP40b:** `src/ZoneFvg.Lab/**` (new project), `lab/symbols.json`, `lab/costs.json` (new), `tests/ZoneFvg.Core.Tests/Lab/**` (new), `tests/ZoneFvg.Core.Tests/ZoneFvg.Core.Tests.csproj` (one added `ProjectReference` to `src/ZoneFvg.Lab`, nothing else), `.github/workflows/lab.yml`, `lab/README.md`, `docs/requirements.yml` (status lines of the part's ids).
- **WP40c:** `src/ZoneFvg.Lab/**`, `lab/grids/**` (new; the first grid file), `lab/selection/README.md` (new; the format, no selection file), `tests/ZoneFvg.Core.Tests/Lab/**`, `.github/workflows/lab.yml`, `lab/README.md`, `docs/requirements.yml` (status lines).
- **WP40d:** `src/ZoneFvg.Core/Simulation/**` (additive: SIM-008 and SIM-009 only), `src/ZoneFvg.Lab/**`, new test files under `tests/ZoneFvg.Core.Tests/Simulation/` and `tests/ZoneFvg.Core.Tests/Lab/**`, `tests/ZoneFvg.Core.Tests/Simulation/SimCommandLineTests.cs` (only as "Owner authorisation" says), `.github/workflows/lab.yml`, `lab/README.md`, `docs/backtest-howto.md` (one new part "The lab": what it is, how the orchestrating session starts it, what the owner still does in cTrader), `docs/requirements.yml` (status lines).

Not allowed in any part: any other file in `src/ZoneFvg.Core` (no trading rule, filter, sizer, default, record or port changes), `src/ZoneFvg.Bot/**`, `deploy/**` (read only: the lab reads the example files), `tools/**` (including `tools/sim`: the new `sim replay` command lives in `SimCommandLine`), `research/**`, `docs/spec.md`, `docs/architecture.md`, every other workflow, `.gitignore`, `Directory.Build.props`, `global.json`. If the lab needs anything from the core that is not public, or a core seam, **stop and ask**: the plan expects none.

## Owner authorisation to change existing tests

None, except in WP40d: help-text expectations in `Simulation/SimCommandLineTests.cs` that pin the full usage text may get the new lines for `sim replay`, `--brake-at` and `--brake-factor`; every existing line of the expected text stays. Each changed test is listed in the pull request with its old and new expectation. No other existing test may change; every existing simulator result must stay bit for bit (SIM-008: "without it every result is the same as before").

## Constraints that apply to every part

- **No new dependency.** Python: standard library only (`urllib`, `lzma`, `struct`, `gzip`, `hashlib`, `csv`, `concurrent.futures`, `unittest`). .NET: no new package; `ZoneFvg.Lab` uses only the BCL and `ZoneFvg.Core`.
- **No secret.** The workflow uses no repository secret. `GITHUB_TOKEN` only, with `contents: read`, plus `issues: write` in the job that posts a report. No URL with a key anywhere.
- **No data in the repository or in artifacts.** Raw `.bi5` files and the converted monthly files live only in the Actions cache (spec row AI: the repository is public and Dukascopy's terms were not confirmed to allow redistribution). Artifacts hold manifests, trade logs, run records and reports. Test fixtures are synthetic and small (built in the test, or a few hand-written rows).
- **Determinism** as in `AGENTS.md` rule 7, also in `ZoneFvg.Lab` and the Python code: sorted outputs whatever the thread order, invariant culture, fixed gzip header (mtime 0, no file name), JSON keys in a fixed order, no clock or machine data in any output file. Elapsed time may be printed to the console only.
- **Performance.** The engine handles about 4 million clock minutes and 2.9 million candle minutes per symbol and run. No operation may grow with the history inside the minute loop: `GetHistory(since)` uses a binary search on a list sorted by close time, positions and orders are small lists, bars are delivered from precomputed arrays.
- **Closed bars only** (rule 8): a bar is delivered only once the clock has reached its nominal close time and only if its period is complete in the input.
- **The lab is not the bot.** `ZoneFvg.Lab` never references `ZoneFvg.Bot` or `cAlgo`. Its simulated broker mirrors cTrader's behaviour as far as the bot can see it; where it cannot know, it is conservative (spec row AK).

## Build

### WP40a: data (PLT-019 data part, LAB-001)

1. `lab/data/dukascopy.py` (download, decode, check, convert, manifest) and `lab/data/test_dukascopy.py` (`python3 -m unittest` from `lab/data`, run with `python3 -I`).
   - URL (**proposal, to confirm on the first real files**): `https://datafeed.dukascopy.com/datafeed/{SYMBOL}/{YYYY}/{MM}/{DD}/{BID|ASK}_candles_min_1.bi5`, with `MM` the month **minus one**, two digits (January is `00`), `DD` the day, two digits. Write the confirmed layout into `lab/data/README.md` in the pull request of the first run if it differs.
   - Decode: `lzma.decompress(data, format=lzma.FORMAT_ALONE)`; an empty body is a day without data; the length must be a multiple of 24; record `>I4if` (seconds, four prices, volume). The price order is **proposal** time, open, close, low, high (Dukascopy's candle order); the record checks of LAB-001 fail on most records if the order is wrong, so a wrong order cannot pass silently. Prices times the point of LAB-001.
   - Requests: at most 4 at a time, User-Agent `zone-fvg-lab`, timeout 30 s, retries after 2, 4, 8, 16 and 32 s on a timeout, a connection error, 429 or 5xx; 404 is a day without data; any other status fails the run. Inject the opener so the tests need no network.
   - CLI: `python3 -I lab/data/dukascopy.py --symbols EURUSD,GBPUSD,USDJPY,AUDUSD,XAUUSD --from 2019-03-01 --to-month <yyyy-mm> --out <dir>`; `--to-month` defaults to the month before the current UTC month (the program may read the clock only for this default; the workflow passes the value explicitly). Output layout `<dir>/<SYMBOL>/<yyyy-mm>.csv.gz`, `<dir>/manifest.csv`, `<dir>/raw-manifest.csv`, the data id printed on the last line as `data id <sha256>`.
2. `.github/workflows/lab.yml` with the inputs of PLT-019 (stages not yet built are refused with a clear message), the `data` job (matrix over the five symbols; `actions/cache` restore and save with key `lab-m1-v1-<SYMBOL>-<last month>` and restore prefix `lab-m1-v1-<SYMBOL>-`; then the downloader; then a job that merges the five manifests and uploads only the manifests as artifact `lab-data-manifest`), and the `lab-tests` job on `pull_request` for the paths of PLT-019. Pin actions by the same major versions as `ci.yml`. Timeouts as PLT-019. `concurrency: lab-${{ github.ref }}` without cancelling a running data job.
3. `lab/README.md`: what the lab is, the stages, what is cached and why nothing is uploaded, the costs being modelled, and that Dukascopy prices differ slightly from FTMO's feed. `docs/test-records/WP40-lab.md`: a template with one section per stage (run link, data id, counts, runtime, problems), to be filled by the orchestrating session.

### WP40b: engine (LAB-002 to LAB-008)

1. Project `src/ZoneFvg.Lab/ZoneFvg.Lab.csproj`: `net10.0`, `OutputType` Exe, nullable, `TreatWarningsAsErrors`, `GenerateDocumentationFile`, `ImplicitUsings` disabled, `ProjectReference` to Core. `Program.cs` only parses arguments and does IO; the logic is in testable classes (suggested folders `Data/`, `Bars/`, `Broker/`, `Replay/`, `Params/`).
2. **Minute reader (LAB-002)** and **bar builder (LAB-003)** as the requirements say; use `NewYorkClock.TradingDate` and `TradingDayStartUtc` for D1 and H4, never `TimeZoneInfo`.
3. **Symbol facts** `lab/symbols.json` (**proposal**, every value marked in the file's own `note` field as "to be replaced by the values of the owner's FTMO cTrader start line"): EURUSD, GBPUSD, AUDUSD pip 0.0001, digits 5, tick 0.00001; USDJPY pip 0.01, digits 3, tick 0.001; volumes min 1,000, step 1,000, max 10,000,000 units; XAUUSD pip 0.01, digits 2, tick 0.01, volume min 1, step 1, max 10,000 oz; account currency USD; sessions forex `Sunday 21:00 - Friday 21:00` UTC, gold the same with a daily break `21:00 - 22:00` UTC Monday to Thursday; leverage 30 (pairs) and 9 (gold) for the margin estimate. Pip value per unit in USD from `SymbolCostMath.PipValuePerUnit` with the current price (all five symbols convert through their own price).
4. **Costs (LAB-005)** `lab/costs.json` with the values of LAB-005 and a `stress` section.
5. **Simulated broker (LAB-004)**, `IBroker`, with the fill rules. Commission is charged per side at that side's fill price: EURUSD 100,000 units filled at 1.10000 is 110,000 USD of notional and 3.30 USD.
6. **Replay (LAB-006)** with the minute clock of spec 12.5. A journal sink keeps the lines in memory (or streams them to a file when `--journal` is given) and the trade log is made with `BacktestLogExtract.Extract`. `IBarHistory` returns, for each timeframe, the bars closed at the broker's current time.
7. **Parameters (LAB-007).** Read `deploy/params/<symbol>.cbotset.example` (the lab passes `RiskEpochDate` as empty and ignores `HeartbeatUrl`, `TelegramBotToken`, `TelegramChatId` and `NewsFeedUrl`, which the cBot does not use in a backtest either); `--set Key=Value` overrides. Build `BotParameters` in the order of `ZoneFvgBot.BuildParameters`: the symbol profile first, then signal, execution, session range, Choch, risk (with `BacktestDefaults.RiskEpoch`), news (`BacktestDefaults.NewsFilterOn` with an empty embedded history), ops, `WriteTradeLog` and `BacktestHaltsLogOnly` true. Time-of-day and enum texts are parsed with the same rules as `ParameterText` (a copy in the lab is allowed because `ParameterText` is in the cBot; the test of LAB-007 pins the behaviour on the five files).
8. **CLI** `lab run --data <dir> --symbol <S> --params <file> [--set K=V ...] --from <yyyy-mm-dd> --to <yyyy-mm-dd> --count-from <yyyy-mm-dd> --symbols lab/symbols.json --costs lab/costs.json [--stress] [--journal <file>] --out <dir>`: writes `trades.csv` (trades opened on or after `--count-from`) and `run.json` (LAB-008).
9. Workflow: the `single` stage (one symbol, one parameter file with overrides given as an input, the development range), and in `lab-tests` the .NET build of `src/ZoneFvg.Lab`, the Lab tests, `dotnet format src/ZoneFvg.Lab --verify-no-changes` and `python3 tools/check_coverage.py TestResults --package ZoneFvg.Lab --min-line 80 --min-branch 70`.

### WP40c: sweeps (LAB-009 to LAB-012)

1. **Grid file format** (LAB-009), for example:

   ```
   rule=LAB-SEL-1
   symbols=EURUSD,GBPUSD,USDJPY,AUDUSD,XAUUSD
   base=deploy/params/{symbol_lower}.cbotset.example
   [block choch]
   EntryModel=Choch
   ChochUseBias=true|false
   SessionStartUtc,SessionEndUtc=00:00,24:00|07:00,17:00
   ChochWindowBars=16|32
   MinRewardRisk=2.0|3.0
   EntryExpiryBars=8|16
   [block choch-stop]
   EntryModel=Choch
   StopBufferAtr=0.05|0.2
   [block fvg]
   EntryModel=Fvg
   SessionStartUtc,SessionEndUtc=00:00,24:00|07:00,17:00
   EntryExpiryBars=8|16
   StopBufferAtr=0.1|0.2
   ```

   A joined key (`A,B=x,y|...`) sets several keys together and counts as one setting for neighbours. Keys not named keep the base file's value. Commit this grid as `lab/grids/2026-11-26-choch-fvg.txt` (**proposal**: 32 + 2 + 8 = 42 combinations, 210 runs).
2. **Sweep** stage: a `plan` job lists the runs, a matrix of `shards` jobs (default 20) restores the five data caches, builds the lab once per job and runs its share; each shard uploads `lab-runs-<k>` (trade logs and run records). Shard timeout 330 minutes. If WP40b's recorded runtime shows that 210 runs do not fit 20 shards of 300 minutes, say so in the pull request: the grid gets smaller, never the cap larger.
3. **Measures (LAB-010)** and **selection (LAB-011)**: a `select` job downloads every shard's artifact, checks that every (symbol, combination) has a trade log and a run record with the same data id (otherwise the result is INCOMPLETE and nothing is chosen), and writes `measures.csv` (one row per combination and symbol, plus pooled), `selection.json` and a selection section of the report.
4. **Holdout gate (LAB-012)**: the `holdout` stage takes `selection=<path under lab/selection/>`. The orchestrating session commits the selection file from the sweep's `selection.json` in a small pull request before it starts this stage, and after the run adds `holdout_run=<run id and date>` to the file in a second one. Document both steps in `lab/selection/README.md`.

### WP40d: money management and report (SIM-008, SIM-009, LAB-013, LAB-014)

1. **SIM-008** in `FtmoChallengeSimulator` (a nullable brake record in `FtmoChallengeParameters`, default null) and in `SimCommandLine` (`--brake-at`, `--brake-factor`).
2. **SIM-009** as a new public function in `Simulation/` (suggested `AccountReplay`) and the command `sim replay --log <file> [--log ...] --from <yyyy-mm-dd> --to <yyyy-mm-dd> [the ftmo sizing and limit options]`. Reuse the path logic of the challenge simulator; do not write a second set of FTMO rules.
3. **LAB-013** in the lab, calling the simulator functions in-process.
4. **LAB-014**: `lab report` writes `report.md`; the workflow's `report` job (after `select`, or after `holdout`) uploads `lab-report-<run id>`, appends the report to `$GITHUB_STEP_SUMMARY`, prints it between the marker lines, and with `report_issue` posts it with `gh api repos/${{ github.repository }}/issues/<n>/comments -F body=@report.md` (the only job with `issues: write`).
5. `docs/backtest-howto.md`: the new part "The lab".

## Tests required (tagged)

Prices compare with `PriceMath` or a 1e-9 relative tolerance; money to 1e-6 USD unless "exactly" is said. Every dataset is built in the test (a minute builder in `tests/ZoneFvg.Core.Tests/Lab/` that makes bid and ask minutes from numbers, like `BarBuilder`). Python tests use `unittest` and need no network.

**LAB-001 (Python, run by `lab-tests`; the id stays `planned`)**

- URL of EURUSD BID for 2020-01-05 is `https://datafeed.dukascopy.com/datafeed/EURUSD/2020/00/05/BID_candles_min_1.bi5`; XAUUSD ASK for 2024-12-31 is `.../XAUUSD/2024/11/31/ASK_candles_min_1.bi5`.
- A synthetic file made with `lzma.compress(..., format=lzma.FORMAT_ALONE)` of one record (60, 110000, 110020, 109990, 110030, 1.5) decodes for EURUSD to minute 00:01 UTC, open 1.10000, close 1.10020, low 1.09990, high 1.10030; for USDJPY the record (0, 110500, 110520, 110490, 110530, 2.0) gives 110.500, 110.520, 110.490, 110.530.
- Refused, naming the file and record: a length of 23 bytes; low above open; high below close; time 86,400; times not increasing; EURUSD at 1.5000 (above 1.4); XAUUSD at 900.
- An empty body and a 404 are a day without data; a 500 then 200 (fake opener) succeeds after one retry; six 500s fail the run; a 403 fails at once.
- Join: a minute with bid volume 0 is dropped and counted; a minute with only a bid candle is dropped and counted; Friday 2021-03-05 22:00 UTC (17:00 New York) is dropped as weekend, 21:59 is kept; Sunday 2021-03-14 21:00 UTC (17:00 New York, summer time) is kept, 20:59 dropped.
- The monthly file of a fixed three-minute month, written twice, has the same bytes and sha256; the manifest lists the counts and the checksum; a month with 3 weekdays without data (not 25 December or 1 January) fails, 2 passes; a month already present with a matching checksum is not requested again (the fake opener sees no call).

**LAB-002**

- A well-formed two-month input of 4 minutes reads back as 4 records in order, bid and ask values exact.
- Refused with file, row and column: a wrong header; a time not on a whole minute; a time not after the previous one (also across the two files); bid low above bid open; ask close below bid close; a non-number; a file whose sha256 differs from the manifest.

**LAB-003**

- EURUSD minutes 2021-03-01 10:00 to 10:14 with bid opens 1.2000 to 1.2014 (step 0.0001), highs open + 0.0002, lows open − 0.0003, closes open + 0.0001: one M15 bar opening 10:00, open 1.2000, high 1.2016, low 1.1997, close 1.2015.
- No minute from 10:15 to 10:29: no bar for that period. Input ending at 10:40 with minutes 10:30 to 10:39: no 10:30 bar.
- Winter (2021-03-01): D1 of trading date 2021-03-01 opens 2021-02-28 22:00 UTC; H4 bars open 22:00, 02:00, 06:00, 10:00, 14:00, 18:00 UTC. Summer (2021-03-15): D1 opens 2021-03-14 21:00 UTC; H4 at 21:00, 01:00, 05:00, 09:00, 13:00, 17:00 UTC.
- A minute at 2021-03-01 21:59 UTC belongs to D1 2021-03-01; 22:00 to D1 2021-03-02.

**LAB-004** (EURUSD, costs of LAB-005 without stress, volume 100,000 units unless said)

- Buy limit 1.10000, stop 1.09800, target 1.10400. Minute with bid 1.10050 / 1.10060 / 1.09990 / 1.10000 (O/H/L/C) and data ask = bid + 0.00001: the effective ask low is 1.09993 (floor 0.3 pip) and the order fills at exactly 1.10000, commission 3.30. The next minute's bid low 1.09800: closed by the stop at 1.09799 (slippage 0.1 pip), gross −201.00, close commission 3.29397 (on 109,799 USD of notional), net −207.59397, balance after close 199,792.40603 from 200,000, reason `StopLoss`.
- Same order; a minute whose ask low is 1.10001: no fill (the effective ask never reaches 1.10000).
- Open buy, a minute with bid low 1.09790 and bid high 1.10410: closed by the stop, not the target.
- The fill minute also has bid low 1.09795: closed by the stop in that minute. The fill minute has bid high 1.10450 and bid low above the stop: still open after it; the next minute with bid high 1.10400: closed at 1.10400, reason `TakeProfit`.
- Buy stop entry 1.10100, a minute opening with ask 1.10120: filled at 1.10121. Sell stop entry 1.10000, bid open 1.09980: filled at 1.09979.
- Open buy with stop 1.09800, a minute whose bid opens at 1.09750: closed at 1.09749.
- Order with expiry 10:15 and a 10:15 minute that reaches its price: removed before the minute, not filled; a 10:14 minute that reaches it: filled.
- Sell limit 1.10000: fills when the bid high reaches 1.10000; its stop is checked on the effective ask.
- USDJPY 100,000 units: commission 3.00 per side whatever the price. XAUUSD 100 oz filled at 2,000.00: 1.40 per side.
- Volume 1,500 (not on the 1,000 step) and a price off the tick grid are refused; with no candle for the current minute `IsMarketOpen` is false and a close request is refused.
- `GetHistory(since)` returns exactly the trades closed at or after `since`.

**LAB-005**

- EURUSD at 2021-03-01 22:00 UTC (17:00 New York, winter): floor 0.00009; at 23:11 UTC (18:11 New York): 0.00003. Data spread 0.00012 at noon: kept (larger than the floor).
- Stress: EURUSD commission 4.95 per side on 110,000 notional; floor 0.000045; stop-loss slippage 0.00006; XAUUSD stop slippage 0.20.
- `RoundTripCommissionPerUnit` given to the core for EURUSD at 1.10000 is 0.000066.
- Run record says `swap: not modelled` with the default file.

**LAB-006 (scenario)**

- A built dataset (about 12 months of synthetic EURUSD minutes from a seeded random walk, seed printed on failure, plus one hand-placed Choch setup near the end) runs through `BotCore` with the default example file: the trade log reads back with `TradeLogCsv`, every trade has a matching history record (time, prices, net), and the hand-placed setup produces exactly one trade with its expected entry, stop and exit.
- The same dataset with `EntryModel=Fvg` and the window 07:00 to 17:00 runs without a core exception.
- An open position at the end is closed with `EndOfRun` and appears in the trade log.
- Bars reach the core in (close time, D1, H4, M15) order; no bar is delivered before its nominal close (assert with a recording wrapper around the core's inputs or with the journal).
- Friday 20:00 UTC (`FridayCloseUtc`) with an open position: the core's close request is executed at the minute's price and the reason is `ClosedByBot`.

**LAB-007**

- The set of `[Parameter("...")]` names in `src/ZoneFvg.Bot/ZoneFvgBot.cs` equals the lab's key set; for every key the cBot's `DefaultValue` text equals the lab's default text.
- Each of the five example files with no override gives `BotParameters` that pass `Validate(isBacktest: true)`, and the parameter hash the core computes equals the hash the core computes for the same values built by the test directly through the core functions.
- Refused, naming the key: an unknown key `Foo=1`; a file without `RiskPercent`; `EntryExpiryBars=eight`; `SessionEndUtc=25:00`; `EntryModel=Choch` with `EntryMode=Breakout` (the core's refusal, unchanged).

**LAB-008 (scenario)**

- Two runs of the LAB-006 dataset give byte-identical `trades.csv` and `run.json`; a run with one override (`EntryExpiryBars=16`) gives a different parameter hash in `run.json`.
- `run.json` keys appear in the fixed order of LAB-008; numbers use the invariant culture (run the test under `de-DE` as the current culture).

**LAB-009**

- The example grid gives exactly 42 combinations, numbered 1 to 32 (block choch, in file order: last key fastest), 33 and 34, 35 to 42.
- A grid of 48 is accepted; 49 is refused with the count in the message. A duplicate combination, an unknown key, `ChochWindowBars=0` (refused by the core's validation) are refused.
- 210 runs over 20 shards: shards 0 to 9 get 11 runs, shards 10 to 19 get 10; run k is in shard k mod 20; over 1 shard all 210.

**LAB-010**

- Trades (result_r, close month): +2 and −1 in January 2021, −1 and +2 in February, −1 and +0.5 in March; segment January to March 2021: 6 trades, 2.0 trades a month, win rate 50%, average +0.25R, profit factor 1.5, maximum drawdown 2.0R, worst losing run 2, monthly R +1, +1, −0.5, share of positive months 2/3.
- The same trades with an empty April added to the segment: 1.5 trades a month and a share of 2/4. Without a losing trade the profit factor is reported as infinite. A trade of exactly 0R counts as not a win and continues a losing run.

**LAB-011**

- One block with key A = 1|2|3; c1 eligible with score 0.30, c2 eligible with 0.50, c3 eligible with 0.05: the neighbours of c2 are c1 and c3, their median 0.175 is below 0.25, so c2 is not on a plateau; c1's neighbour c2 has 0.50 ≥ 0.15: c1 is chosen.
- Boundaries: 150 pooled trades eligible, 149 not; pooled profit factor 1.20 eligible, 1.1999 not; average +0.10R eligible, +0.0999R not; positive in 3 of 5 years eligible, 2 not; a symbol with 30 trades and profit factor 1.10 counts, 29 trades or 1.0999 does not; one counting symbol makes the combination ineligible.
- Score with a maximum drawdown of 0.4R uses 1 as divisor.
- Ties: two chosen candidates with equal score: the lower number.
- No eligible combination: the exact text `No combination passes on the development data`, and no selection file is written.
- Only development trades are read: a trade opened 2024-11-01 00:00 UTC in the input is ignored; one opened 2024-10-31 23:59 is used.

**LAB-012**

- A selection file with `holdout_run=` is refused; one with another grid checksum is refused; one whose data manifest differs for 2023-05 is refused; one that differs only by months after 2024-10 is accepted.
- Counting: a holdout trade log holds only trades opened on or after 2024-11-01; a trade opened 2024-10-31 and closed 2024-11-01 is in neither segment's count and is listed as a boundary trade.
- The comparison of the trades before 2024-11-01 with the sweep's: equal inputs report "equal"; one changed exit price reports that trade.

**SIM-008**

- InitialCapital 200,000, fixed 0.5%, brake 3.0 / 0.5, trades one per day, each closed before the next opens: seven −1R trades leave 193,104.1294 (each risk 0.5% of the balance: 1,000, 995, 990.025, 985.0749, 980.1495, 975.2488, 970.3725); the eighth trade opens 3.448% below the peak and risks 0.25%: 482.7603. If that trade is +2R the balance is 194,069.6500, 2.965% below the peak, and the ninth risks 0.5%: 970.3483.
- Boundary: balance exactly 194,000 with peak 200,000 (3.000%): braked; 194,000.01: not braked.
- `--brake-at 3` with `--sizing ladder` and with `--sizing recovery`: refused naming SIM-008. Without `--brake-at` the challenge results for the existing test data are identical to the current pinned values.

**SIM-009**

- Fixed 0.5% from 200,000: +2R and −1R closing in January 2021, −1R in February: January ends at 200,990.00 (+0.495%), February at 199,985.05 (−0.500%); mean monthly −0.0025%, share of positive months 1/2, worst month −0.500%.
- A sequence that breaches the static floor in a month ends the replay there, and the report names the date and "static floor"; one that reaches the 8% kill switch stops new trades and names the date and "kill switch"; later months show 0 trades.
- `sim replay --help` exists and the command rejects `--to` before `--from`.

**LAB-013**

- On a fixed trade list the table has the rows fixed 0.25, 0.5, 0.75, 1.0, Ladder, Recovery, brake 0.5, brake 1.0, for development and holdout, each with the columns of LAB-013; the challenge cells use seed 20261126 and 5,000 runs and give the same numbers as calling `FtmoChallengeSimulator.Simulate` with those parameters directly; the daily budget is 2.0 in every cell.
- Development R per month 0.8: the risk for 1% a month is 1.25%; with a development maximum drawdown of 9.2R the drawdown at that risk is 11.5%.

**LAB-014**

- With one plan at a holdout mean of 1.05%, maximum drawdown 6.1% and breach rate 4.0%: the first line is `On the holdout, fixed 1.0% reached 1.05% a month with a maximum drawdown of 6.1% and a Challenge breach rate of 4.0%.`; with none: exactly `No configuration and sizing plan reached 1% a month on the holdout.`; before a holdout run the report says `Holdout not run`.
- The report contains the data id, the commit, the line that the holdout is not unused, the count of combinations of every grid file under `lab/grids/`, and the sections in the order of LAB-014.
- A report over 60,000 characters is cut for the comment with the closing line `Report cut at 60,000 characters; the full report is in the artifact.`

## Decisions already taken (do not reopen them in code)

- Spec rows AH to AR and section 12.5, including every value marked **proposal** there and in LAB-001 to LAB-014; the owner can reverse them, the implementer does not.
- No trading rule, default or money decision changes. The cBot and the parameter files are not touched.
- Anything the spec does not settle: write it under "Decisions for the human" and leave the related id `planned`.

## Done

Per part: tests first, CI green (including `lab-tests`), all commands of `AGENTS.md` run and pasted, plus the part's lab commands (`python3 -I -m unittest` in `lab/data`, `dotnet build src/ZoneFvg.Lab -c Release`, `dotnet format src/ZoneFvg.Lab --verify-no-changes`, the Lab coverage line), status lines to `implemented` only for the part's ids with `verify: unit` or `scenario` and no open decision. A plain summary for the owner: what the part adds, that nothing in the bot, its orders, its risk or its money settings changes, and, from WP40c on, what the orchestrating session has to do to run the next stage.
