# How to backtest the bot and judge the result (Mac)

This guide takes you from the cBot file to the pass or fail numbers of spec
section 13.1. You need about one hour the first time, plus the time the
backtests take. Nothing here touches your FTMO account: a backtest only
replays old prices.

What happens, in short: you run a backtest in your cTrader app, copy its
**Log** tab into a text file, and the `sim` tool on your Mac turns that file
into a trade list and computes the numbers.

The bot has three entry modes (spec v0.10 and 3.8, parameter `EntryMode`):
**Breakout** (the default), **ZoneFvg** and **SessionRange**. Breakout and
ZoneFvg are backtested on all five symbols, each as its own series, and each
is judged by itself (spec 12.1 and 13.1). So you do Parts 2 and 3 twice, once
per mode. SessionRange is meant for gold alone; its backtest is Part 5.

Inside ZoneFvg mode there are two entry models (spec 3.9, parameter
`EntryModel`, from WP37 on): **Fvg** (the default, the gap entry) and
**Choch** (your manual method: the change of character at the zone and the
retest). Part 6 says how to backtest both side by side.

Why the Log tab: the cBot runs without file access (`AccessRights.None`), so it
cannot write files itself. During a backtest it prints one line per closed
trade into the Log, starting with `tradelog seq=`. cTrader's own backtest
exports (the HTML report, the History tab) do not contain the planned stop,
target and risk that the simulation needs. Sources: cTrader help,
["Backtest a cBot"](https://help.ctrader.com/ctrader-algo/how-tos/cbots/backtest-a-cbot/)
(the Log "displays your cBot output throughout backtesting"; the only export
described is the HTML report).

## Part 1. One-time preparation

1. **Get the cBot file.** On GitHub, open the repository, then **Actions**,
   the latest green **ci** run on `main`, and download the artifact
   **ZoneFvgBot-algo** at the bottom of the page. Unzip it. Rename `src.algo`
   to `ZoneFvgBot.algo` if it has that name.
2. **Import it into cTrader.** Open your **FTMO Platform cTrader** app.
   Double-click `ZoneFvgBot.algo` (or drag it onto the cTrader window, or use
   **Algo > cBots > import**). If cTrader asks to install a .NET component to
   build algos, allow it. The bot appears in the cBots list as `ZoneFvgBot`.
   (Same steps as the laptop stage in [`deploy/README.md`](../deploy/README.md), Part A.)
3. **Get the project files onto the Mac**, for the `sim` tool. On GitHub, open
   the repository's main page, click **Code > Download ZIP**, and unzip it, for
   example into your **Documents** folder. The folder is called
   `zone-fvg-cbot-main`.
4. **Install the .NET 10 SDK** (needed to run `sim`). Source: Microsoft,
   ["Install .NET on macOS"](https://learn.microsoft.com/dotnet/core/install/macos).
   1. Find your processor: Apple menu > **About This Mac**. "Chip: Apple M..."
      means Apple silicon; "Processor: Intel" means Intel.
   2. Open https://dotnet.microsoft.com/download/dotnet and choose **.NET 10.0**.
   3. In the **SDK** section, row **macOS**, column **Installers**: choose
      **Arm64** for Apple silicon (M1, M2, M3 ...) or **x64** for Intel.
   4. Open the downloaded `.pkg` file and follow the installer.
   5. Check: open **Terminal** (Applications > Utilities), type
      `dotnet --info` and press Return. It must list an SDK starting with `10.`.
      Microsoft lists .NET 10 as supported on macOS 14 Sonoma, 15 Sequoia and
      26 Tahoe.
5. **Check the tool.** In Terminal:

   ```
   cd ~/Documents/zone-fvg-cbot-main
   dotnet run --project tools/sim -- --help
   ```

   The first run takes a minute (it builds the tool). It prints a list of
   commands starting with `extract`. Everything after `--` goes to the tool.

## Part 2. Run one backtest per symbol and per mode

Spec 12.1 asks for each symbol separately: the four forex pairs (EURUSD,
GBPUSD, USDJPY, AUDUSD) and gold (XAUUSD). From v0.10 it also asks for one
series per entry mode, with the same period, data and costs (spec 12.1,
step 1).

6. Open an **M15** chart of the symbol and add an instance of `ZoneFvgBot` to
   it, with the default parameters, except these (in the instance's parameter
   panel):
   - **EntryMode:** `Breakout` or `ZoneFvg` (exactly so, with this spelling).
     `SessionRange` is backtested on gold only, as Part 5 describes.
     Run the whole of Part 2 once with `Breakout` and once with `ZoneFvg`,
     with the same period, data, commission and spread in both series. Keep
     the files of the two modes apart: put the mode into every file name,
     for example `eurusd-breakout-dev.csv` and `eurusd-zonefvg-dev.csv`.
     Leave `BreakoutEntryBufferAtr` (0.1) and `BreakoutStopAtr` (1.0) at
     their defaults for the baseline; in ZoneFvg mode they are not used.
     (`BreakoutStopAtr` is one of the values searched later in Breakout
     mode, spec 7 and 12.1 step 3.)
   - **TrendSlowEmaPeriod** (default 200) and **BreakoutLastConfirmUtc**
     (default 08:00): the two Breakout filters of 23 November 2026 (spec
     3.1 and 3.7). Spec 7 asks to test each both ways: `TrendSlowEmaPeriod`
     200 and 0 (0 = off), `BreakoutLastConfirmUtc` 08:00 and 17:00 (17:00,
     equal to SessionEndUtc, = off). Put the values into the file names, for
     example `eurusd-breakout-t200-c0800-dev.csv`. In ZoneFvg mode they are
     not used.
   - **InitialCapital:** 100000, the same number as the Starting capital in
     step 7. With a different starting capital the FTMO emergency guard
     fires at once (10,000 against 100,000 is already 90% below), and from
     WP22 on the bot refuses to start and says why in the Log.
   - **RiskEpochDate:** leave it empty. In a backtest an empty
     RiskEpochDate means the backtest's first day (its Prague date at the
     start), so nothing has to be typed (from WP31 on). The `start` line in
     the Log shows that day as `riskEpoch=yyyy-MM-dd`. You can still set it
     by hand for a run with a different epoch: it must not be after the
     backtest's start (step 7), or the bot refuses to start; a later date
     would silently switch off the ladder steps and the kill switch.
   - **NewsEnabled:** leave it at the default (true). While the news history
     embedded in the bot has no events, the bot switches the news filter off
     for that backtest by itself (from WP31 on): the `start` line shows
     `news=disabled`, and one line before it says
     `ZFVG adapter news filter off for this backtest: the embedded news history has no events`.
     Once the history has events, the filter works as set. You can still
     set NewsEnabled to false by hand for a run without the news filter.
   - **DrawObjects:** true only if you want to see the zones and gaps on the
     chart; it needs Visual mode on (step 7). The drawings exist in ZoneFvg
     mode only: in Breakout mode the bot keeps no zones and no gaps, so
     nothing is drawn.
   - **Gold (XAUUSD) only:** MaxSpreadPips 0.4, MinStopPips 8,
     MaxVolumeUnits 200. Everything else as for the forex pairs. FTMO's
     cTrader reports a gold pip of 1 USD (spec 7), so 0.4 means a spread of
     0.40 USD and 8 means a stop of 8 USD. (Until 23 November 2026 this
     guide said 40 and 800, values written for a pip of 0.01 USD. The gold
     backtests of 22 and 23 November ran with the forex values 12 and 1.5.)
7. Open the **Backtesting** tab of that instance (to the right of the chart
   tab) and click the cog icon. Settings (spec 12.1):
   - **Data:** tick data from server.
   - **Starting capital:** 100000 (the planned capital), equal to the
     InitialCapital parameter of step 6.
   - **Commission:** the commission of your FTMO account per million units.
     If you do not know it, write down what you used.
   - **Spread:** leave the default unless the developer said otherwise.
   - **Period:** development data is everything **before November 2024**:
     end the backtest on 31 October 2024 at the latest. Do **not** include
     November 2024 to November 2026 yet: that is the holdout, used once at
     the very end (spec 12.1 and 13.1).
   - **Start at least 10 months before the period you want judged.** The
     bot needs about 9.5 months of daily bars before it can find zones (in
     Breakout mode too: it loads the same history in both modes, spec 7), and
     cTrader cannot load older history during a backtest (Spotware staff on
     the cTrader forum: loading more history "is not currently supported in
     backtesting ... start backtesting from a previous date",
     [forum 24262](https://communityuat.ctrader.com/forum/cbot-support/24262)).
     Until then the bot waits in Recovering and trades nothing. Example: to
     judge 2020 to October 2024, start on 1 March 2019. In the Log, the
     first `state ... from=Recovering to=Running` line shows when trading
     could start; it must be before the period you judge.
   - **With the trend filter on (`TrendSlowEmaPeriod` 200) start about 3.1
     years earlier instead.** The D1 EMA(200) reads 800 daily closes, so the
     bot needs 801 closed D1 bars before its first breakout (with the filter
     off: 201). Since cTrader does not load older history during a backtest
     (see above), the warm-up happens inside the run: set the backtest start
     about 3 years before the period you want judged. Example: to judge
     22 October 2023 onward, start in about August 2020. Check in the Log
     that the first `state ... from=Recovering to=Running` line is before
     the period you judge; until then recovery reports the D1 series as too
     short. Not verified: one cTrader forum thread
     ([forum 35644](https://communityuat.ctrader.com/forum/suggestions/35644))
     says loading more history in backtests was announced for v5.0 and then
     for v5.2; a user on v5.0 reported that it did not change the bar count.
     If your cTrader does load it, trading starts earlier; the Log line shows
     it either way.
   - **Visual mode:** off (faster). Turn it **on** only to look at the
     drawings (DrawObjects true in step 6). Drawings can disappear when a
     visual backtest ends, so pause the backtest while it runs and look at
     the chart then.
   Source for these settings: cTrader help,
   ["Backtest a trading bot"](https://help.ctrader.com/ctrader-algo/how-tos/cbots/backtest-a-trading-bot/).
8. Click **Play** and wait until the progress bar is full.

   **Halts in backtests (from WP23 on).** A backtest never halts: when the
   kill switch, the FTMO emergency guard, the error limit, the request limit
   or the daily risk budget would stop the bot, it writes a line with
   `would-halt` or `would-block` to the Log and keeps trading. That keeps
   the trade list complete; the `sim` commands of Part 3 apply the account
   limits and FTMO's rules afterwards. Before WP23, any `KillSwitch` or
   `EmergencyGuard` line in the Log means the bot stopped trading there and
   the run is too short for spec 13.1.
9. **Save the Log.** Click the **Log** tab under the chart. Click one line in
   it, press **Cmd+A** (select all) and **Cmd+C** (copy). Open **TextEdit**,
   choose **Format > Make Plain Text**, press **Cmd+V**, and save the file as
   `eurusd-breakout-dev-log.txt` (for EURUSD in Breakout mode; in ZoneFvg
   mode `eurusd-zonefvg-dev-log.txt`) in the `zone-fvg-cbot-main` folder.
   If Cmd+A does not select the lines, right-click the Log and look for a
   **Copy** or **Copy all** entry. If neither works, tell the developer: this
   step could not be tried on a Mac before this guide was written.
10. **Turn the log into a trade log.** In Terminal, in the
    `zone-fvg-cbot-main` folder:

    ```
    dotnet run --project tools/sim -- extract --in eurusd-breakout-dev-log.txt --out eurusd-breakout-dev.csv
    ```

    It prints `Extracted N trades ...`. **Check N** against the number of
    trades of the backtest (History tab, or "Total trades" in Trade
    statistics). They should be equal. If N is smaller, the Log was cut off
    (users report a limit of 50,000 Log lines per backtest:
    [cTrader forum, 2023](https://communityuat.ctrader.com/forum/ctrader-algo/41642)).
    Then split the period, for example one backtest per two years, save and
    extract each run into its own file (`eurusd-breakout-dev-1.csv`,
    `eurusd-breakout-dev-2.csv` ...), and give all of them to the commands
    below.
    A position still open when the backtest ends has no line; that can make
    N one smaller than the platform's count.

    If `extract` stops with an error, it says which trade and why. Two runs
    pasted into one file, or a missing trade, are refused on purpose.
11. Repeat steps 6 to 10 for GBPUSD, USDJPY and AUDUSD, and for XAUUSD
    (with its own values from step 6). With gold, add
    `--log xauusd-breakout-dev.csv` (or `--dev xauusd-breakout-dev.csv` for
    `accept`) to the commands below.
    Then do steps 6 to 10 again for every symbol with `EntryMode` =
    `ZoneFvg`, which gives the files `eurusd-zonefvg-dev.csv` and so on
    (with gold, `xauusd-zonefvg-dev.csv`; see step 15 for its benchmark).

## Part 3. Compute the numbers

All commands run in Terminal in the `zone-fvg-cbot-main` folder. Each
command first merges the files it gets by time and removes the trades the
account limits would have refused (spec 12.1 step 7). `--seed 1` makes a
result repeatable; keep it the same.

Run each command once per mode: once with the `...-breakout-dev.csv` files
(as shown) and once with the `...-zonefvg-dev.csv` files. Never mix the files
of the two modes in one command, except as `--benchmark` in step 15.

12. **Portfolio, drawdown and caps** (spec 12.1 step 7):

    ```
    dotnet run --project tools/sim -- combine --log eurusd-breakout-dev.csv --log gbpusd-breakout-dev.csv --log usdjpy-breakout-dev.csv --log audusd-breakout-dev.csv
    ```

13. **Monte Carlo** (step 8):

    ```
    dotnet run --project tools/sim -- montecarlo --log eurusd-breakout-dev.csv --log gbpusd-breakout-dev.csv --log usdjpy-breakout-dev.csv --log audusd-breakout-dev.csv --seed 1
    ```

    Write down the 1st and 5th percentile of the 30-trade average R: live
    edge monitoring uses them (spec 13.3).
14. **FTMO challenge simulation** at 0.5%, 0.75% and 1% risk (step 9):

    ```
    dotnet run --project tools/sim -- ftmo --log eurusd-breakout-dev.csv --log gbpusd-breakout-dev.csv --log usdjpy-breakout-dev.csv --log audusd-breakout-dev.csv --seed 1
    ```

15. **Acceptance report** (spec 13.1):

    Breakout mode:

    ```
    dotnet run --project tools/sim -- accept --dev eurusd-breakout-dev.csv --dev gbpusd-breakout-dev.csv --dev usdjpy-breakout-dev.csv --dev audusd-breakout-dev.csv --seed 1
    ```

    ZoneFvg mode, with the Breakout development files as the benchmark
    (spec 12.1 step 10: the Breakout mode is the benchmark bot of the
    ZoneFvg mode):

    ```
    dotnet run --project tools/sim -- accept --dev eurusd-zonefvg-dev.csv --dev gbpusd-zonefvg-dev.csv --dev usdjpy-zonefvg-dev.csv --dev audusd-zonefvg-dev.csv --benchmark eurusd-breakout-dev.csv --benchmark gbpusd-breakout-dev.csv --benchmark usdjpy-breakout-dev.csv --benchmark audusd-breakout-dev.csv --seed 1
    ```

    With gold, the ZoneFvg command also takes
    `--dev xauusd-zonefvg-dev.csv --benchmark xauusd-breakout-dev.csv`, so
    that both sides of the benchmark row cover the same five symbols (spec
    12.1 step 10: the same data).

    Optional extra inputs, each made like the development files: `--stress`
    for the cost-stress backtests (spread and commission +50%, step 6),
    `--benchmark` for the benchmark bot (step 10), and at the very end
    `--holdout` and `--benchmark-holdout`. Without them the report marks those
    rows as missing (INCOMPLETE), not as passed.

    **Read this for the Breakout report.** The Breakout mode has no
    benchmark yet (spec 12.1 step 10). Its report has two rows "Versus
    benchmark (return-to-drawdown)", both with the result NOT EVALUATED:
    the Development row with the value "no benchmark log", and the Holdout
    row with the value "no holdout log" (after a holdout run, "no benchmark
    log"). The line `Overall: INCOMPLETE` under the table follows from that
    unless another row fails; after it the report goes on with the section
    "Challenge simulation used for the two challenge rows". That is
    expected and is not a failure of the other rows. Spec 13.1 says the row "Versus
    benchmark" is not applied to the Breakout mode; every other row is. So
    judge the Breakout mode by all the other rows.

To save any report to a file, add `> report.md` at the end of the command.

## Part 4. Which numbers decide

The `accept` report states pass or fail for each line of spec 13.1. The
table is applied to each mode separately (spec 13.1, "Two entry modes"):

| Measure | Development | Holdout |
|---|---|---|
| Trades, all symbols | 300 or more | 60 or more |
| Profit factor after costs | 1.30 or higher | 1.15 or higher |
| Average result per trade | +0.15R or higher | At least half the development figure |
| Maximum drawdown at 0.5% risk | 6% or less | 6% or less |
| Chance of passing the Challenge phase (simulation) | 70% or higher | n/a |
| Chance of breaching an FTMO floor (simulation) | 10% or lower | n/a |
| Profitable symbols | 3 of 4 (4 of 5 with gold) | 3 of 4 (4 of 5 with gold) |
| Share of profit from one symbol | 60% or less | n/a |
| Profit factor under cost stress | Above 1.10 | n/a |
| Versus benchmark | Better return-to-drawdown ratio | Same |

For the Breakout mode the row "Versus benchmark" is not applied (see
step 15).

How to act on it (spec 12.1, 13.1 and 13.4):

- **Baseline unprofitable across symbols:** stop. The rules are revisited with
  the developer; a losing baseline is not optimised into a winner.
- **Two modes:** a mode may go live only if it passes by itself. If only one
  passes, that mode is the candidate. If both pass, the mode with the better
  return-to-drawdown ratio on the development data is chosen.
- **Development passes:** robustness, variants, then the holdout, once, and
  for the chosen mode only. Running both modes on the holdout and keeping
  the better one would turn the holdout into a tuning step (spec 13.1).
- **Holdout benchmark, when ZoneFvg is the chosen mode** (spec 13.1). The
  ZoneFvg holdout row "Versus benchmark" needs Breakout trade logs of the
  holdout period. So, after the choice, run the Breakout mode once on the
  holdout, with the same data and costs and its accepted settings, and
  give its files as `--benchmark-holdout` (for example
  `--benchmark-holdout eurusd-breakout-holdout.csv`, one per symbol, beside
  `--holdout eurusd-zonefvg-holdout.csv`). This run is the benchmark input
  of that row only: its result changes nothing about the choice and is
  never used for tuning. When Breakout is the chosen mode, no
  `--benchmark-holdout` is given and its benchmark rows stay NOT
  EVALUATED, as in step 15.
- **Holdout fails:** the strategy is rejected or redesigned; the holdout is
  not used again for tuning.

Send the developer the `-dev.csv` files of both modes and the reports,
together with the settings you used (period, commission, spread, data
source).

## Part 5. SessionRange on gold

SessionRange (spec 3.8) trades a break of the range that gold made from
19:00 to 03:00 New York time, on the side of the D1 trend. It comes from the
strategy search of 23 November 2026 (`research/search/REPORT-2026-11-23.md`).
This backtest checks that the bot trades the rule as the search did. It is
**not** the acceptance of the mode: which marks a one-symbol mode must pass
is your open decision (spec 13.1, "A mode on one symbol", and spec 14,
question 4). Until you decide it, the mode is not accepted. The period below
includes the holdout years. The search has already looked at them once, so
this run uses nothing new (spec 14, question 4).

Write what you see into the record
[`docs/test-records/WP35-session-range.md`](test-records/WP35-session-range.md),
step 4.

16. Open an **M15** chart of **XAUUSD** and add an instance of `ZoneFvgBot`
    with the default parameters, except exactly these three:
    - **EntryMode:** `SessionRange`
    - **MinStopPips:** `8`
    - **MaxSpreadPips:** `0.4`

    Leave the six range values at their defaults: `RangeStartNy` 19:00,
    `RangeEndNy` 03:00, `RangeLastEntryNy` 12:00, `RangeExitNy` 16:30,
    `RangeMinWidthAtr` 0.5, `RangeStopAtr` 1.0. Leave `InitialCapital`
    (100000), `RiskEpochDate` (empty) and `NewsEnabled` (true) as in step 6.
    The news filter is then off for this run, as in the search (the `start`
    line shows `news=disabled`). You may also set `MaxVolumeUnits` to 200,
    as in the gold file. It changes nothing here: with a stop of at least
    8 USD, a trade reaches 200 ounces only above about 320,000 USD of
    equity.
17. Backtest settings (cog icon, as in step 7):
    - **Data:** **m1 bars from server**, as in your earlier gold backtests.
      This setting should give results near the second line of figures in
      step 22 ("cTrader's fills"). Tick data from server comes nearer the
      first line (the search's own fills).
    - **Starting capital:** 100000.
    - **Commission:** the commission of your FTMO account for XAUUSD.
      Write down what you used.
    - **Spread:** `0.3`. cTrader takes this number in the platform's pip,
      and the gold pip is 1 USD, so 0.3 means 0.30 USD, the spread the
      search assumed. **Do not enter more than 0.4.** With a spread above
      0.4, `MaxSpreadPips` blocks every gold entry and the run has no trade.
    - **Period:** from **6 May 2020** (the first day of intraday history on
      your FTMO server) to **22 November 2026**.
    - **Visual mode:** off.
18. Click **Play** and wait until the progress bar is full.
19. **The warm-up.** Before it trades, the bot needs closed bars of each
    timeframe (with the default values): 201 D1 bars (`BiasEmaPeriod` 50
    reads 4 x 50 daily closes, spec 7), 248 H4 bars and 1968 M15 bars.
    - **D1:** on your FTMO server the daily history of XAUUSD is older than
      6 May 2020. Your ExportBars run of 23 November 2026, started on
      6 May 2020, got daily bars from 28 March 2019, about 290 before the
      start. So the 201 D1 bars are there on the first day.
    - **H4 and M15:** the intraday history starts on 6 May 2020 (H4 at
      13:00 UTC, M15 at 15:15 UTC), and cTrader loads no older intraday
      history in a backtest. Gold has about 6 H4 bars and about 92 M15
      bars per trading day. 1968 M15 bars take about 21 trading days (to
      about 5 June 2020). 248 H4 bars take about 41 trading days: the
      248th H4 bar closes on **2 July 2020** at 21:00 UTC at the earliest.
    So the first line with `from=Recovering to=Running` in the Log should
    come on **about 3 July 2020**. Holidays with missing bars can make it
    a few days later. Write down its date. If it comes much later (for
    example in February 2021), the D1 history did not load: write down the
    date and tell the developer.
20. Save the Log as in step 9, as `xauusd-sessionrange-all-log.txt`, and
    extract it as in step 10:

    ```
    dotnet run --project tools/sim -- extract --in xauusd-sessionrange-all-log.txt --out xauusd-sessionrange-all.csv
    ```

    Check N against "Total trades", as in step 10.
21. Optional, for the numbers: run

    ```
    dotnet run --project tools/sim -- accept --dev xauusd-sessionrange-all.csv --seed 1
    ```

    Read only three rows of the Development column: "Trades, all symbols",
    "Profit factor after costs" and "Average result per trade". The total
    in R is the number of trades times the average result per trade. Do
    **not** read its pass or fail lines: the run covers the holdout years
    and one symbol, and the marks for such a mode are your open decision.
22. **What to compare.** The figures below are for XAUUSD with the rules of
    spec 3.8, the smallest stop 8 USD, spread 0.30 USD and no news filter,
    by year of entry, as trades / result in R:

    | Year | 2020 | 2021 | 2022 | 2023 | 2024 | 2025 | 2026 | Total |
    |---|---|---|---|---|---|---|---|---|
    | Search's own fills | 17 / -8.3 | 18 / +6.8 | 17 / +9.9 | 16 / -2.8 | 28 / +5.8 | 61 / +25.5 | 45 / +19.4 | 202 / +56.3 |
    | cTrader's fills, "m1 bars from server" | 17 / -10.1 | 18 / +6.5 | 17 / +9.1 | 16 / -4.5 | 28 / +3.9 | 61 / +22.4 | 45 / +17.0 | 202 / +44.2 |

    Your run starts trading only after the warm-up (step 19, about 3 July
    2020). The search's 2020 trades by month were: May 1, June 1, July 3,
    August 5, September 5, November 1, December 1. So compare with the
    figures **from 15 July 2020**: 200 trades; search's own fills +57.0R
    with 55% winners; cTrader's fills +44.9R with 54% winners. 2020 then
    has 15 trades (-7.6R with the search's fills, -9.4R with cTrader's).
    With "m1 bars from server" (step 17) expect results near the cTrader
    line; with tick data nearer the search line. Compare the trade counts
    and the R results year by year; the developer does this from your
    `.csv` file. Your backtest sizes trades with the ladder, so the money
    result does not follow the R result one to one.
23. Send the developer `xauusd-sessionrange-all-log.txt`,
    `xauusd-sessionrange-all.csv` and the settings of step 17.

## Part 6. The two ZoneFvg entry models side by side: Fvg and Choch

Spec 3.9 (v0.11) writes your manual supply and demand method as rules: at an
H4 zone an M15 bar closes beyond the most recent M15 swing (the change of
character, CHoCH), a limit order waits at that broken level for the retest,
the stop goes just beyond the lowest low (highest high for a sell) since the
zone was touched, and the target sits just before the next opposing H4 zone
or swing level, with the trade skipped when that gives less than 1:2. The
cBot runs it only from WP37c on: check that the parameter group **Entry
model** exists in your cBot before you start.

Both models are backtested with the same build, the same period, data,
commission and spread, on the **development** period only (before November
2024, step 7). The holdout is run once, later, for the model that is chosen
(spec 13.1, "Two entry models").

24. **Series 1: Fvg.** Do Part 2 with `EntryMode` = `ZoneFvg` and
    `EntryModel` = `Fvg` (the default). Leave `SessionStartUtc` 07:00 and
    `SessionEndUtc` 17:00. File names as before, for example
    `eurusd-zonefvg-dev.csv`. If you already have these files from a build
    before WP37, run them again with the new build anyway: the parameter
    hash in the `start` line must be the same as before for the Fvg model;
    if it is not, tell the developer.
25. **Series 2: Choch.** Do Part 2 again with exactly these values
    different from the defaults (besides the gold values of step 6):
    - **EntryMode:** `ZoneFvg` (with `Breakout`, the default, the bot
      refuses to start when `EntryModel` is `Choch`, and says why).
    - **EntryModel:** `Choch`
    - **SessionStartUtc:** `00:00` and **SessionEndUtc:** `24:00` (you trade
      all day; the Friday rules and the news filter stay as they are).
    - Gold only: **ChochMinStopPips** `3` (the gold pip is 1 USD, so 3 USD).
    Leave the other Choch values at their defaults: `RequireSweep` false,
    `ChochWindowBars` 32, `ChochUseBias` false, `ChochStopAtDistal` false,
    `ChochTargetBufferAtr` 0.1, `ChochMinStopPips` 5, `MinRewardRisk` 2.0.
    Put the model into the file names, for example
    `eurusd-choch-dev-log.txt` and `eurusd-choch-dev.csv`. The `start` line
    of the Log ends with `mode=ZoneFvg model=Choch`; check it.
26. **Warm-up.** As in step 7: start at least 10 months before the period
    you judge, and check the first `from=Recovering to=Running` line.
27. **What to look at in the Log.** Every Choch signal line has
    `kind=choch`, the broken level (`level=`), `sweep=yes` or `sweep=no`,
    where the target came from (`target=zone:...`, `target=swing:...` or
    `target=default`) and the planned reward (`rr=`). Skips with
    `RewardRiskTooLow` are setups whose next opposing level was nearer than
    2R. Many skips with `CostTooHigh` or `VolumeAboveCap` mean the stops are
    small: spec 3.9 explains both limits, which stay as they are.
28. **Compare.** Run `accept` (Part 3) on each series separately, with the
    Breakout files of the same period as the benchmark, as for ZoneFvg. Put
    the two results side by side: trades, profit factor after costs,
    average R, largest drawdown, by symbol. Each model is judged by itself
    (spec 13.1).
29. **Optional, one at a time** (spec 7, "Test" column): `RequireSweep`
    true, `ChochUseBias` true, `ChochStopAtDistal` true. Change one value
    per run and put it into the file name (for example
    `eurusd-choch-sweep-dev.csv`). With `RequireSweep` false you can also
    compare the `sweep=yes` and `sweep=no` trades of one run without a new
    backtest: send the developer the log. Every extra run is counted as a
    variant tried (spec 12.4).
30. Send the developer the logs, the `.csv` files and the settings of step 7
    for both series. The values marked "proposal" in spec 3.9 are yours to
    confirm or change (spec 14, question 5); the developer turns your
    answer into parameter defaults.
