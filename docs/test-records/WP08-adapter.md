# WP08 test record: cTrader adapter

Manual verification of PLT-002, PLT-005, PLT-006 and PLT-009, plus the platform
facts the adapter relies on. The owner runs the steps on cTrader desktop with an
FTMO Free Trial (or demo) account and fills in the "Observed" and "Pass / fail"
lines. A requirement is marked `implemented` only after the owner has signed
this record (docs/test-records/README.md).

| Field | Value |
|---|---|
| Date | (fill in) |
| Run by | (fill in) |
| cTrader version | (Help > About) |
| Account | FTMO Free Trial, account type (hedging / netting) |
| Bot version | `ZoneFvgBot 0.1.0`, commit (fill in) |
| `.algo` | artifact `ZoneFvgBot-algo` of the CI run of that commit |

## Automatic evidence (no owner action)

| Item | Requirement | Evidence |
|---|---|---|
| CI builds the `.algo` and uploads it as artifact `ZoneFvgBot-algo` | PLT-007 | CI run linked in the pull request |
| `[Robot(AccessRights = AccessRights.None, TimeZone = TimeZones.UTC)]`, checked by `tools/check_architecture.py` | PLT-003 | `repo-checks` job |

## Preparation

1. Download the artifact `ZoneFvgBot-algo` from the CI run, unzip it, and
   double-click `ZoneFvgBot.algo` (or drag it into cTrader). It appears under
   Algo > cBots.
2. Open an EURUSD chart on **M15**. Add an instance of ZoneFvgBot to it.
3. Parameters for the laptop test: `RiskEpochDate` = today (yyyy-MM-dd),
   `NewsFeedUrl` = the Forex Factory weekly JSON export address,
   `TelegramBotToken` and `TelegramChatId` from your Telegram bot (or both
   empty), `HeartbeatUrl` (or empty). Leave everything else at its default.
4. Open the Log tab of the instance. Every adapter line starts with
   `ZFVG adapter`; core journal lines start with a time stamp and a category.

## Steps

### 1. Start, version and recovery (spec 8.3; prompt item 1)

Start the instance.

Expected:
- `ZFVG adapter version 0.1.0, symbol EURUSD, backtest no, mode Breakout`
  (with the defaults; `mode ZoneFvg` when `EntryMode` is ZoneFvg).
- `ZFVG adapter symbol EURUSD account USD pipSize 0.0001 digits ... platformPipValue ... pipValuePerUnit 0.0001 ... roundTripCommissionPerLot ...`.
- A core `start` journal line with `version=0.1.0` and `hash=` (parameter
  hash; `119a3864` with the defaults of the preparation). In Breakout mode
  the line ends with `mode=Breakout`; in ZoneFvg mode it has no `mode=`
  field.
- A recovery report, then the state Running (or Blocked, with the reason, if
  for example the calendar is not yet loaded).
- No Telegram token, chat id or heartbeat address appears anywhere in the log.

Observed: (fill in)

Pass / fail: (fill in)

### 2. First bar after start is closed (SIG-001; prompt item 5)

Wait for the next M15 close after the start.

Expected: one line `ZFVG adapter first live bar after start: M15 open
yyyy-MM-dd HH:mm:ss close yyyy-MM-dd HH:mm:ss passed for time yyyy-MM-dd
HH:mm:ss` (all UTC). The open time is the open of the bar that has just
finished on the chart (not of the new bar), the close time is 15 minutes
later, and the close time is at or before the time after "passed for time".
If an H4 or D1 bar closes at the same moment, the line names that bar
(`H4` or `D1`) instead, because it is passed first; the same checks apply.

Observed: (fill in)

Pass / fail: (fill in)

### 3. Bar times and bar order (architecture section 10, "Close time" and "Live bar order")

Look at the chart's D1 and H4 bars (switch the chart timeframe temporarily, or
read the journal).

Expected (`docs/architecture.md` section 10, row "Daily and H4 bar boundaries
on cTrader"): D1 bars open at 21:00 UTC while New York is on daylight saving
time and at 22:00 UTC otherwise, five a week, the first of the week on Sunday
evening UTC and running 24 hours; there is no short daily bar for Sunday. H4
bars follow every four hours from the weekly open. A bar's close is its open
plus 24 hours or 4 hours. The bot prints no line for a processed bar. Around
the daily close the Log shows no `error` line with `Bar rejected (SIG-004)`
(that line would mean a bar came out of order). If the D1 bars
open at 00:00 UTC or a short Sunday bar exists, record it here: Breakout mode
then does not go live before a follow-up.

Observed D1 open hour: (fill in)  H4 open hours: (fill in)

Pass / fail: (fill in)

### 4. Invalid parameter (PLT-006; prompt item 3)

Stop the instance. Set `RiskPercent` = 0 (or `SessionStartUtc` = `7am`, or
clear `RiskEpochDate`). Start it.

Expected: `ZFVG adapter refused to start: invalid parameter: ...` naming the
parameter and the reason, then the instance stops. No order is placed.
Repeat once with the chart on H1: `refused to start: the cBot runs on an M15
chart only`.

Observed: (fill in)

Pass / fail: (fill in)

### 5. Parameters and defaults (PLT-006)

Open the instance's parameter panel.

Expected: groups Entry mode, Signal, Execution, Risk, News, Operations
(Entry mode: EntryMode Breakout, BreakoutEntryBufferAtr 0.1, BreakoutStopAtr
1.0); every parameter of
spec section 7 with its default (BiasEmaPeriod 50, AtrPeriod 14, ...,
MaxSpreadPips 1.5, NewsEnabled true, NewsBeforeMin/NewsAfterMin 30,
NewsMaxCacheHours 48, NewsRefreshHours 4, MaxRequestsPerDay 100,
InitialCapital 100000, DayResetZone Europe/Prague, secrets empty), plus
MaxRequestsPerHour 20, MaxErrorsPerHour 5, ProfitTargetPercent 10,
MarketHolidays 12-25,01-01, NewsFeedUrl, DrawObjects false.

Observed: (fill in)

Pass / fail: (fill in)

### 6. Restart in the middle of a trade (spec 8.3, 12.3)

While a bot pending order or position is open (wait for the bot to place one),
stop the instance and start it again.

Expected: the recovery report adopts the position or keeps the order; stop
loss, take profit and expiry are unchanged at the broker; no duplicate order.

Observed: (fill in)

Pass / fail: (fill in)

### 7. Disconnect (spec 8.4)

Turn Wi-Fi off for about two minutes, then on.

Expected: a `connection` journal line `disconnected`, the state Recovering,
then after reconnect a recovery report and Running again. No order is sent
while disconnected. (If the platform restarts the cBot instead of raising the
connected event, step 1's lines appear again; both are acceptable.)

Observed: (fill in)

Pass / fail: (fill in)

### 8. Stop handler not needed (PLT-009)

Stop the instance with the Stop button, and once by closing cTrader from the
task manager (no stop handler runs). Start it again.

Expected: the second start recovers exactly like the first; nothing is
missing because the stop handler did not run.

Observed: (fill in)

Pass / fail: (fill in)

### 9. Trade data mapping (decisions 1 and 2 of pull request WP08)

After the first closed demo trade, compare in the History tab and the journal:

- the trade's commission with `roundTripCommissionPerLot` from step 1 times the
  traded lots;
- `pipValuePerUnit` with `platformPipValue` (EURUSD: both 0.0001; USDJPY: the
  first follows the price, the second is fixed at start);
- the journal's net result with the History tab's net profit (commission and
  swap included).

Observed: (fill in)

Pass / fail: (fill in)

### 10. Backtest of one month, twice (prompt items 4 and 6)

Backtest tab, EURUSD M15, one month, starting balance 100000, `NewsEnabled` =
false (the embedded history holds no events yet), visual mode once and silent
mode once.

Expected: `backtest yes` in the start line; the run completes with no
exception; the two runs' trade lists are identical (export both and compare).

Observed: (fill in)

Pass / fail: (fill in)

### 11. Chart drawings (PLT-005; prompt item 2)

Automatic evidence: the core's snapshots are read-only copies, reading them
changes no signal, zone, order, journal line or alert, gaps older than 24 hours
of bar time drop out, and a bot rebuilt by recovery shows the same snapshots as
one that never stopped (tests tagged PLT-005 in
`tests/ZoneFvg.Core.Tests/Signals/SignalEngineSnapshotTests.cs` and
`tests/ZoneFvg.Core.Tests/Scenarios/Plt005ChartSnapshotScenarios.cs`). The
drawing itself can only be checked on the platform:

a. **Live chart.** Start an instance with `DrawObjects` = true on an M15 chart.
   Expected: green filled rectangles for demand zones and red ones for supply
   zones, from the zone's activation time to the current time, between its
   proximal and distal prices (names `ZFVG zone <id>`); blue filled rectangles
   for the fair value gaps confirmed in the last 24 hours, over their three M15
   bars (names `ZFVG fvg <yyyyMMddHHmm of c1>`). The ids match the zone ids in
   the recovery report and the journal. A zone that is used, invalidated or
   expired disappears at the next bar; a gap disappears 24 hours after its
   confirming bar closed.
b. **Ten examples.** Compare at least 10 drawn zones and gaps with the
   definitions (spec 3.3, SIG-010, SIG-016): edges, times and type. Write the
   ids checked below.
c. **No effect on trading.** Run the same one-month visual backtest twice, once
   with `DrawObjects` = false and once with `DrawObjects` = true. Expected: the
   trade lists are identical. Also with `DrawObjects` = true in a non-visual
   backtest the log says `DrawObjects is true, but this run shows no chart;
   nothing is drawn`.
d. **Failure is contained.** If any chart call fails, the log shows one line
   `ZFVG adapter error in chart drawing (drawing switched off for this run)`
   and the bot keeps trading normally. (Seen only if the platform refuses a
   drawing; note it if it happens.)
e. With `DrawObjects` = false (the default) nothing named `ZFVG ...` is drawn.

Observed: (fill in; list the 10 ids checked)

Pass / fail: (fill in)

### 12. Owner review of the handlers (PLT-002)

Read `src/ZoneFvg.Bot/ZoneFvgBot.cs`, methods `OnStart`, `OnTimer`,
`OnException`, `OnError`, `OnStop` and `Subscribe`: each handler converts and
forwards to `BotCore`, with no rule about prices, risk or time.

Observed: (fill in)

Pass / fail: (fill in)

## Sign-off

| Requirement | Steps | Owner signature and date |
|---|---|---|
| PLT-002 | 12 | |
| PLT-005 | 11 | |
| PLT-006 | 4, 5 | |
| PLT-009 | 8 | |
