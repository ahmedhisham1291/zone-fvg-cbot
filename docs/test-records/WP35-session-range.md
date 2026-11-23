# WP35 test record: SessionRange in the cBot

Manual checks of the SessionRange mode (spec 3.8) in the cBot: the six new
parameters, the start line and its hash, the refusals, and one backtest on
gold that is compared with the strategy search of 23 November 2026
(`research/search/REPORT-2026-11-23.md`). The owner runs the steps on
cTrader desktop (Mac), in a backtest or with an FTMO Free Trial or demo
account where a step says so, and fills in the "Observed" and
"Pass / fail" lines.

PLT-015 itself is proven by unit tests (`verify: unit`). This record is the
part that needs cTrader. **Passing it does not accept the mode.** The mode
is not accepted until you have decided which marks a mode on one symbol
must pass (spec 13.1, "A mode on one symbol", and spec 14, question 4), and
the mode has passed them. Until then no instance runs with
`EntryMode=SessionRange` on the Challenge account.

| Field | Value |
|---|---|
| Date | (fill in) |
| Run by | (fill in) |
| cTrader version | (Help > About) |
| Account | FTMO server (backtest), or FTMO Free Trial or demo |
| Bot version | `ZoneFvgBot 0.1.0`, commit (fill in) |
| `.algo` | artifact `ZoneFvgBot-algo` of the CI run of that commit |

## Automatic evidence (no owner action)

| Item | Requirement | Evidence |
|---|---|---|
| The cBot with the six range parameters builds and the `.algo` is uploaded as artifact `ZoneFvgBot-algo` | PLT-015 | `build-test` job, step "Build cBot (.algo)", of the pull request |
| The six texts and numbers become the core's `SessionRangeParameters`; each refusal names the parameter as cTrader shows it; the hash of the mode is `3f0f30a3` (forex values) and `377b6162` (gold's values); the hashes of the other modes do not change | PLT-015 | `build-test` job, tests tagged PLT-015 (`tests/ZoneFvg.Core.Tests/App/SessionRangeTextTests.cs`) |
| The range, the order, the void, the timed exit, recovery and the journal lines of the mode | SIG-030 to SIG-034, EXE-024, EXE-025 | `build-test` job, tests with these tags |

## Preparation

1. Download the artifact `ZoneFvgBot-algo` from the CI run and unzip it. It
   holds `src.algo`: rename it to `ZoneFvgBot.algo` (as in
   `deploy/README.md`, Part A), then double-click it (or drag it into
   cTrader). It appears under Algo > cBots.
2. Open an **XAUUSD** chart on **M15**. Add an instance of ZoneFvgBot to it.
3. Open the Log tab of the instance. Every adapter line starts with
   `ZFVG adapter`; core journal lines start with a time stamp and a
   category.

Steps 1 to 3 need only a short start: a backtest of a few days (settings as
in `docs/backtest-howto.md`, step 17, but any short period after
mid-February 2021) is enough, because the start lines come first.

## Steps

### 1. Parameter panel (PLT-015)

Open the instance's parameter panel.

Expected: the group **Entry mode** has eleven parameters. After
`EntryMode`, `BreakoutEntryBufferAtr`, `BreakoutStopAtr`,
`TrendSlowEmaPeriod` and `BreakoutLastConfirmUtc` come six new ones with
these defaults: `RangeStartNy` 19:00, `RangeEndNy` 03:00, `RangeLastEntryNy`
12:00, `RangeExitNy` 16:30, `RangeMinWidthAtr` 0.5, `RangeStopAtr` 1.0.
`EntryMode` is still `Breakout` by default. The other groups are unchanged.

Observed: (fill in)

Pass / fail: (fill in)

### 2. Start line and hash (PLT-015)

Set exactly three values: `EntryMode` = `SessionRange`, `MinStopPips` = 8,
`MaxSpreadPips` = 0.4. Leave everything else at its default. Start a short
backtest.

Expected:
- `ZFVG adapter version 0.1.0, symbol XAUUSD, backtest yes, mode SessionRange`.
- The core `start` journal line ends with `mode=SessionRange`, and its
  `hash=` is `377b6162`.

Then set `EntryMode` back to `Breakout` (keep 8 and 0.4) and start again.
Expected: `mode Breakout`, the `start` line ends with `mode=Breakout`, and
`hash=fbdef461`. With `EntryMode` = `ZoneFvg`: `mode ZoneFvg`, no `mode=` in
the `start` line, `hash=05ee8872`. These two are the hashes of gold's
parameter file from WP35 on; the range values are not part of them.

How the hashes were found: the core's own rule
(`src/ZoneFvg.Core/Execution/ParameterHash.cs`): SHA-256 of the canonical
text, UTF-8, first eight hex characters, computed with Python's hashlib
from the values above. The text of the SessionRange start ends with
`RangeStartNy=19:00:00;RangeEndNy=03:00:00;RangeLastEntryNy=12:00:00;RangeExitNy=16:30:00;RangeMinWidthAtr=0.5;RangeStopAtr=1;`.
The core's tests assert the same three values, and `3f0f30a3` for the
forex values (`MinStopPips` 12, `MaxSpreadPips` 1.5).

Observed (the three adapter lines and the three hashes): (fill in)

Pass / fail: (fill in)

### 3. Refusals name the cTrader parameter (PLT-015)

Start once for each line below, with the values of step 2 and one change.
Each start must be refused and the instance must stop, with no order.

a. `RangeExitNy` = `12:00`. Expected: `ZFVG adapter refused to start:
   invalid parameter: RangeExitNy must be after RangeLastEntryNy (12:00:00)
   in the order of the trading day but was 12:00:00. (Parameter
   'RangeExitNy')`
b. `RangeStopAtr` = 0. Expected: `ZFVG adapter refused to start: invalid
   parameter: RangeStopAtr must be a finite number greater than zero but
   was 0. (Parameter 'RangeStopAtr')`
c. `RangeStartNy` = `7pm`. Expected: `ZFVG adapter refused to start:
   invalid parameter: RangeStartNy must be a New York time of day written
   as HH:mm, for example 19:00. (Parameter 'RangeStartNy')`
d. `EntryMode` = `Breakout` and `RangeStopAtr` = 0. Expected: the same
   refusal as in b, word for word: the range values are checked in every
   mode.
e. `EntryMode` = `Abc`. Expected: `ZFVG adapter refused to start: invalid
   parameter: EntryMode must be one of: ZoneFvg, Breakout, SessionRange.
   (Parameter 'EntryMode')`

Each expected text is one line in the Log; it is broken here only for
reading.

Observed: (fill in)

Pass / fail: (fill in)

### 4. Backtest on gold from 6 May 2020, compared with the search

Run `docs/backtest-howto.md`, Part 5, steps 16 to 23: XAUUSD M15, from
6 May 2020 to 22 November 2026, the three values of step 2 above
(`EntryMode` SessionRange, `MinStopPips` 8, `MaxSpreadPips` 0.4), spread
`0.3` in cTrader's backtest settings (the platform's pip is 1 USD on gold,
so 0.3 is 0.30 USD; above 0.4 every entry is blocked).

Write down:

1. The date of the first `from=Recovering to=Running` line. Expected: about
   mid-February 2021. The bot needs 201 closed D1 bars first, and your
   server has no history before 6 May 2020.
2. The date of the first trade. Expected: after the date of item 1, so not
   in 2020.
3. The number of trades ("Total trades") and N of the `extract` command.
4. The net profit in USD and the largest drawdown, from cTrader's report.
5. Optional: trades, profit factor and average result per trade from the
   `accept` command (step 21 of the guide).

What to compare (the search, with the rules of spec 3.8, gold's smallest
stop of 8 USD, spread 0.30 USD, no news filter): 202 trades, 55% winners,
+56R over the whole period; with cTrader's way of filling orders about
+44R. By year, with the search's own fills: 2020 -8.3R, 2021 +6.8R, 2022
+9.9R, 2023 -2.8R, 2024 +5.8R, 2025 +25.5R, 2026 +19.4R
(`research/search/REPORT-2026-11-23.md` has the same rule with a 3 USD stop;
spec 3.8 has the totals with 8 USD). Because of the warm-up this run has
no trade in 2020 and none before about mid-February 2021, so expect fewer
than 202 trades. Compare 2022 to 2026 year by year; the developer does this
from your `.csv` file. cTrader's own fills make the total lower than the
search's own figures. Your backtest sizes trades with the ladder, so the
money result does not follow R one to one.

Pass if: the run has trades from 2021 on; every `signal` line has
`kind=range`, and its `zone=` field holds the signal id, `RH-yyyyMMdd` (buy)
or `RL-yyyyMMdd` (sell); and the
developer finds the years 2022 to 2026 close to the search (the developer
writes the comparison into this record).

Observed (items 1 to 5): (fill in)

Developer's comparison by year: (fill in)

Pass / fail: (fill in)

### 5. The void and the timed exit in the Log

In the Log of step 4 (search with Cmd+F):

a. **Void.** Find a line `range event=void id=RH-...` (or `RL-...`). On the
   same time stamp, or one bar later, a line `action duty=range-void ...`
   shows that the pending order of that id was cancelled. Write down one
   such pair. After it, no new order with that id is placed that day.
b. **Timed exit.** Find a line `action duty=range-exit ...`. Its time is
   16:30 New York: 20:30 UTC while New York is on summer time, 21:30 UTC
   otherwise. Check in cTrader's History that the position of that id was
   closed at that time. Write down one such line. If the market was closed
   at 16:30 New York, a line `range-exit event=deferred
   reason=market-closed` comes first, and the close follows when the
   market opens again.

Observed (one void pair and one timed exit, with times): (fill in)

Pass / fail: (fill in)

## Sign-off

| Item | Steps | Owner signature and date |
|---|---|---|
| PLT-015 in cTrader | 1, 2, 3 | |
| SessionRange backtest on gold compared with the search | 4, 5 | |
| SessionRange accepted (spec 14, question 4 decided and passed) | not part of this record | |
