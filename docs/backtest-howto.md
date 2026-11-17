# How to backtest the bot and judge the result (Mac)

This guide takes you from the cBot file to the pass or fail numbers of spec
section 13.1. You need about one hour the first time, plus the time the
backtests take. Nothing here touches your FTMO account: a backtest only
replays old prices.

What happens, in short: you run a backtest in your cTrader app, copy its
**Log** tab into a text file, and the `sim` tool on your Mac turns that file
into a trade list and computes the numbers.

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

## Part 2. Run one backtest per symbol

Spec 12.1 asks for each symbol separately: the four forex pairs (EURUSD,
GBPUSD, USDJPY, AUDUSD) and gold (XAUUSD).

6. Open an **M15** chart of the symbol and add an instance of `ZoneFvgBot` to
   it, with the default parameters, except these (in the instance's parameter
   panel):
   - **InitialCapital:** 100000, the same number as the Starting capital in
     step 7. With a different starting capital the FTMO emergency guard
     fires at once (10,000 against 100,000 is already 90% below), and from
     WP22 on the bot refuses to start and says why in the Log.
   - **RiskEpochDate:** the start date of the backtest (step 7). A later
     date silently switches off the ladder steps and the kill switch; from
     WP22 on the bot refuses to start with a RiskEpochDate after the
     backtest's start.
   - **NewsEnabled:** false, until the developer says the embedded news
     history is in the bot. Without it, a backtest with the news filter on
     has no events to block.
   - **DrawObjects:** true only if you want to see the zones and gaps on the
     chart; it needs Visual mode on (step 7).
   - **Gold (XAUUSD) only:** MaxSpreadPips 40, MinStopPips 800,
     MaxVolumeUnits 200. Everything else as for the forex pairs.
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
     bot needs about 9.5 months of daily bars before it can find zones, and
     cTrader cannot load older history during a backtest (Spotware staff on
     the cTrader forum: loading more history "is not currently supported in
     backtesting ... start backtesting from a previous date",
     [forum 24262](https://communityuat.ctrader.com/forum/cbot-support/24262)).
     Until then the bot waits in Recovering and trades nothing. Example: to
     judge 2020 to October 2024, start on 1 March 2019. In the Log, the
     first `state ... from=Recovering to=Running` line shows when trading
     could start; it must be before the period you judge.
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
   `eurusd-dev-log.txt` (for EURUSD) in the `zone-fvg-cbot-main` folder.
   If Cmd+A does not select the lines, right-click the Log and look for a
   **Copy** or **Copy all** entry. If neither works, tell the developer: this
   step could not be tried on a Mac before this guide was written.
10. **Turn the log into a trade log.** In Terminal, in the
    `zone-fvg-cbot-main` folder:

    ```
    dotnet run --project tools/sim -- extract --in eurusd-dev-log.txt --out eurusd-dev.csv
    ```

    It prints `Extracted N trades ...`. **Check N** against the number of
    trades of the backtest (History tab, or "Total trades" in Trade
    statistics). They should be equal. If N is smaller, the Log was cut off
    (users report a limit of 50,000 Log lines per backtest:
    [cTrader forum, 2023](https://communityuat.ctrader.com/forum/ctrader-algo/41642)).
    Then split the period, for example one backtest per two years, save and
    extract each run into its own file (`eurusd-dev-1.csv`,
    `eurusd-dev-2.csv` ...), and give all of them to the commands below.
    A position still open when the backtest ends has no line; that can make
    N one smaller than the platform's count.

    If `extract` stops with an error, it says which trade and why. Two runs
    pasted into one file, or a missing trade, are refused on purpose.
11. Repeat steps 6 to 10 for GBPUSD, USDJPY and AUDUSD, and for XAUUSD
    (with its own values from step 6). Add `--log xauusd-dev.csv`
    (or `--dev xauusd-dev.csv` for `accept`) to the commands below.

## Part 3. Compute the numbers

All commands run in Terminal in the `zone-fvg-cbot-main` folder. Each
command first merges the files it gets by time and removes the trades the
account limits would have refused (spec 12.1 step 7). `--seed 1` makes a
result repeatable; keep it the same.

12. **Portfolio, drawdown and caps** (spec 12.1 step 7):

    ```
    dotnet run --project tools/sim -- combine --log eurusd-dev.csv --log gbpusd-dev.csv --log usdjpy-dev.csv --log audusd-dev.csv
    ```

13. **Monte Carlo** (step 8):

    ```
    dotnet run --project tools/sim -- montecarlo --log eurusd-dev.csv --log gbpusd-dev.csv --log usdjpy-dev.csv --log audusd-dev.csv --seed 1
    ```

    Write down the 1st and 5th percentile of the 30-trade average R: live
    edge monitoring uses them (spec 13.3).
14. **FTMO challenge simulation** at 0.5%, 0.75% and 1% risk (step 9):

    ```
    dotnet run --project tools/sim -- ftmo --log eurusd-dev.csv --log gbpusd-dev.csv --log usdjpy-dev.csv --log audusd-dev.csv --seed 1
    ```

15. **Acceptance report** (spec 13.1):

    ```
    dotnet run --project tools/sim -- accept --dev eurusd-dev.csv --dev gbpusd-dev.csv --dev usdjpy-dev.csv --dev audusd-dev.csv --seed 1
    ```

    Optional extra inputs, each made like the development files: `--stress`
    for the cost-stress backtests (spread and commission +50%, step 6),
    `--benchmark` for the benchmark bot (step 10), and at the very end
    `--holdout` and `--benchmark-holdout`. Without them the report marks those
    rows as missing (INCOMPLETE), not as passed.

To save any report to a file, add `> report.md` at the end of the command.

## Part 4. Which numbers decide

The `accept` report states pass or fail for each line of spec 13.1:

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

How to act on it (spec 12.1 and 13.4):

- **Baseline unprofitable across symbols:** stop. The rules are revisited with
  the developer; a losing baseline is not optimised into a winner.
- **Development passes:** robustness, variants, then the holdout, once.
- **Holdout fails:** the strategy is rejected or redesigned; the holdout is
  not used again for tuning.

Send the developer the four `-dev.csv` files and the reports, together with
the settings you used (period, commission, spread, data source).
