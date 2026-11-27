# Runbook: operating the Zone + FVG cBot

What to do in normal operation and when something happens. For the server
set-up, updates and roll-back see [`deploy/README.md`](../deploy/README.md);
every command below is explained there with its documentation link
(standard tools such as `grep`, `ls`, `df` and `nano`: see its "Documentation used" table; `grep`: https://man7.org/linux/man-pages/man1/grep.1.html). All
`docker compose` commands run in `/opt/zfvg` on the VPS.

Rules that never change:

- The broker holds the truth (spec 8.1). When the bot and the cTrader app
  disagree, believe the cTrader app.
- Every bot trade has its stop loss and take profit at the broker, so a
  stopped bot does not mean an unprotected trade (spec 8.2). The exception is a
  position whose stop is missing: that is always urgent (see `StopLossMissing`).
- Never trade by hand on the bot's account (spec 15.7, open question 2). If you
  must close something by hand, stop the bot for that symbol first.
- The bot never closes trades for risk reasons except through the FTMO
  emergency guard (spec 4.2).

## 1. Where to look

| Need | Where | How |
|---|---|---|
| Alerts and the daily summary | Telegram chat of your bot | Phone |
| Is each instance alive? | Heartbeat-monitoring service (one check per symbol) | Phone; it alerts when calls stop for more than its grace time |
| Positions, orders, history | cTrader app (mobile, web or desktop) on the same account | Phone |
| Full bot log | `docker compose logs --since 24h --timestamps eurusd` | Laptop, SSH |
| Container state | `docker compose ps` | Laptop, SSH |

Alert format: `ZFVG <SYMBOL> <Kind>: <text>`. Log lines from the bot's core
look like `<time UTC> <category> key=value ...`; adapter lines start with
`ZFVG adapter`. Secrets are masked in both.

## 2. What each alert means and what to do

Bot states (spec 8.5): **Recovering** (starting or reconnecting, no new
orders), **Running**, **Blocked** (no new orders until the cause clears; open
trades still managed), **Halted** (no new orders, pending orders cancelled,
Friday close still runs).

| Alert kind | Meaning | What to do |
|---|---|---|
| `Started` | The instance started and finished recovery. The text is the recovery report: positions adopted, orders kept or cancelled, strangers, zones, downtime (spec 8.3 step 10) | Expected after a restart, update or reboot. If you did not cause it, check `docker compose ps` and the log for the reason (crash, reconnect). Several per hour: see "Restart loop" below. Any `strangers` greater than 0: see "Stranger" |
| `Recovered` | Recovery after a reconnect, same report | Nothing if it happens now and then. Many per day: check the server's internet (provider status page) |
| `StateChanged` | State moved, for example Running to Blocked; the text gives the reason | Read the reason. Blocked by calendar, data or daily limit clears by itself. Halted: see the halt rows below |
| `TradeOpened` / `TradeClosed` | A position opened or closed | Nothing. Compare now and then with the cTrader app |
| `OrderRejected` | The broker clearly refused an entry order (bad volume, market closed, no money). Not retried (spec 5.3) | One now and then: nothing. Repeated: send the log lines to the developer |
| `OrderProblem` | An order did not match after placement, was not found, or a cancel or close failed | Open the cTrader app. Check that every bot position has a stop and target and that no pending order is duplicated. Cancel a duplicate by hand after stopping that symbol (`docker compose stop <symbol>`). Send the log to the developer |
| `SanityCap` | A trade was refused: volume above `MaxVolumeUnits` (automatic by default: 3,000,000 units; gold 1,000 ounces; spec 7.1), stop or target on the wrong side, or, only if `SanityCapEnabled` is on (off by default), risk above `SanityCapPercent` (0.55%) of balance (spec 4.3). The open-risk cap and the currency cap are off by default too (`OpenRiskCapEnabled`, `CurrencyCapEnabled`) | Nothing to do now; no order was sent. Send the log to the developer: it means a calculation looked wrong |
| `StopLossMissing` | A bot position has no stop loss, or it was closed because the stop could not be set | **Urgent.** Open the cTrader app now. If the position is still open without a stop, set the stop yourself from the planned stop in the alert or close the position. No new entries are placed while any bot position lacks a stop |
| `StopLossRepaired` | A missing stop was set again from the trade's comment | Check in the app that the stop is where planned. Find out who or what removed it |
| `DailyLimit` | The daily risk budget (2% of initial capital, 2,000 USD on the 100,000 FTMO Instant account, measured from 00:00 Prague balance, all symbols together) is used up. Pending orders cancelled; no entries until the next Prague day | Nothing. Read the daily summary next morning |
| `KillSwitch` | Drawdown from the highest closing balance since `RiskEpochDate` reached 8%. The bot is **Halted** until you set a new `RiskEpochDate` (spec 4.2, 8.5) | Do not resume the same day. Review the trades with the developer. Resume only by section 5, after a decision |
| `EmergencyGuard` | Equity came within 500 USD (0.5% of the 100,000 initial capital) of an FTMO floor: 2,500 USD below the 00:00 Prague balance, or 9,500 USD below the highest closed balance (section 9). The bot closed all positions, cancelled orders and **Halted** (spec 4.2, 15.8) | Check in the app that everything is closed. Stop all containers (`docker compose stop`) until reviewed with the developer. Resume by section 5 |
| `CircuitBreaker` | Too many entries or order requests (spec 4.3). The request limit halts the instance until you restart it | Send the log to the developer before restarting. Restart one symbol with `docker compose restart <symbol>` only after the cause is understood |
| `ErrorStorm` | 5 or more unhandled errors in an hour; Halted until the next Prague day or a restart (spec 8.5) | Send the log of the last hour (`docker compose logs --since 1h <symbol>`) to the developer. Do not restart in a loop |
| `NewsFeedDown` | A calendar download failed or was rejected; the earlier good data is kept | Nothing at first. If it lasts more than a day, check that the Forex Factory export address still works in a browser |
| `CalendarUnavailable` | No trusted news data covers the current time: new entries blocked (spec 6.3) | Same as above. Entries resume by themselves when a download succeeds. Do not switch `NewsEnabled` off on a live or Challenge account (architecture section 10) |
| `Inactivity` | 21 days without a trade, also while Halted. FTMO is reported to close accounts after 30 days without a trade (spec 11, 15.2) | Decide with the developer before day 30. Check FTMO's current rule in the client area; do not place a manual trade without that decision |
| `DailySummary` | Once per Prague day, for the day that ended | Read it (section 3) |

**Heartbeat alert from the monitoring service** (no Telegram message from the
bot itself): the instance, the container, the server or its internet is down.

1. `docker compose ps` over SSH. If you cannot connect, the server is down:
   check the provider's status page and console, and reboot it there.
2. If a container is not `Up`, start it: `docker compose up -d`.
3. If everything is up but heartbeats still fail, read the log for
   `heartbeat` lines with a failure.
4. On a Friday afternoon, if the bot cannot be brought back before 20:00 UTC,
   close the bot's positions and cancel its orders in the cTrader app (spec 8.6).

**Restart loop** (many `Started` messages, `docker compose ps` shows
`Restarting`): usually an invalid parameter (the log says `refused to start:
invalid parameter: ...`) or a wrong password or account. Stop that symbol
(`docker compose stop <symbol>`), fix the file, start it again.

**Stranger** (recovery report shows strangers): a bot-labelled position whose
zone is unknown, for example after a parameter change. It keeps its stop and
target, counts in the risk totals and is closed on Friday (spec 8.3 step 6).
Nothing to do unless you want it closed early.

**Monthly loss limit** (spec 4.2, row BF; `MonthlyLossLimitPercent`, default
2). Once the account's closed result for the calendar month (Prague time)
reaches -2% of the initial capital (-2,000 USD at 100,000), the bot opens
nothing new until 00:00 Prague time on the first day of the next month and
cancels its pending entry orders. It counts every closed trade of the account,
commission and swap included, so both gold instances stop together. It never
closes or changes a trade: open positions run to their stop, target or timed
exit. There is no alert and the state stays Running; signals are journaled as
skipped with `MonthlyLossLimit`, cancelled orders with `duty=monthly-limit`.
Nothing is stored, so a restart cannot reset it. `0` switches it off.

## 3. Reading the daily summary

```
Daily summary 2026-11-16 (CE(S)T day)
Trades 2, net 412.50, net R 0.83 (R known for 2)
Balance 100412.50, equity 100412.50
Drawdown 0.00 (0.00%)
Room to FTMO daily floor 5000.00, overall floor 10412.50
Target progress 4.1% (412.50 of 10000.00)
Skips NewsWindow 3, SpreadTooWide 1
Calendar Available
Uptime 1.00:00:00, restarts 0
```

(Example values.)

| Line | Meaning | Look for |
|---|---|---|
| Trades, net, net R | Trades closed that Prague day, money after commission and swap, result in R. "R known for" counts trades whose plan was found | Net R far below -3 in one day should not happen (daily budget); tell the developer |
| Balance, equity | At summary time | Equity much lower than balance means open trades are losing |
| Drawdown | From the highest closing balance since `RiskEpochDate` | Kill switch at 8% |
| Room to FTMO floors | Distance from equity to the FTMO daily floor (00:00 Prague balance minus `FtmoDailyLossPercent`, 3% on FTMO Instant) and to the overall floor: 90% of initial capital on the 2-Step (`FtmoMaxLossMode=Static`); on FTMO Instant (`TrailingBalance`) P minus 10% of initial capital, P the highest closed balance (section 9), which the `Started` report and every guard line name as `(trailing, peak ...)` | Emergency guard acts at 500 USD room (0.5% of the 100,000 initial capital) |
| Target progress | Profit against `ProfitTargetPercent` | |
| Skips | Signals not taken, by reason | `SpreadTooWide` or `NewsCalendarUnavailable` every day means a setting or the feed needs a look |
| Calendar | News data status: `Available` or `Unavailable` | `Unavailable` for more than a day: see `CalendarUnavailable` |
| Uptime, restarts | Since the summary before | Restarts you did not cause: check the log |

`NA` means the value could not be read; one `NA` is not a problem, a week of
`NA` is.

A missing daily summary for one symbol means that instance was not running at
the Prague day change, or Telegram failed. Check `docker compose ps`.

## 4. Halt and resume by hand

The bot has no "pause" switch. These are the safe ways:

| Goal | Steps |
|---|---|
| Stop new trades on one symbol, keep open trades protected | `docker compose stop <symbol>`. Stops and targets stay at the broker. **Friday close does not run** and its heartbeat stops (pause that check in the monitoring service so it does not alarm) |
| Stop everything | `docker compose stop` |
| Flatten everything (emergency) | 1. `docker compose stop`. 2. In the cTrader app close all bot positions and cancel all bot pending orders (label starts with `ZFVG`). 3. Check the app shows nothing open |
| Resume | `docker compose start` (or `start <symbol>`). Each instance runs the recovery procedure, sends `Started`, then trades. Resume monitoring checks |
| Resume after `KillSwitch` or `EmergencyGuard` | Only after review, by section 5 |
| Resume after `ErrorStorm` or the request-limit `CircuitBreaker` | After the developer has looked at the log: `docker compose restart <symbol>` |

## 5. Changing `RiskEpochDate`

`RiskEpochDate` is the day from which the 8% drawdown is measured (spec 4.2).
It is a date, `yyyy-MM-dd`, meaning 00:00 Prague time on that day
(architecture section 10). The bot refuses to start without a valid one.

When to change it:

- at the start of each new account: the Free Trial, the Challenge, the
  Verification and the FTMO Account each start with their own first day.
  On the **FTMO Instant** account it stays the account's first day: it is not
  moved after a reward (section 9);
- to resume after `KillSwitch` or `EmergencyGuard`, once you and the developer
  have decided the bot may trade again. Setting a later date forgets the old
  peak, so it is a deliberate risk decision, never a routine fix.

How:

1. `docker compose stop` (all four).
2. In each of `/opt/zfvg/params/{eurusd,gbpusd,usdjpy,audusd}/bot.cbotset`
   set the same `RiskEpochDate=yyyy-MM-dd` with `nano`. Not a future date.
3. `docker compose start`, then check the four `start` lines and `Started`
   messages.
4. Write the date, the reason and who decided in your operations notes.

On the laptop stage: stop each instance, change the parameter in the instance's
parameter panel, start it.

`EntryMode` is a parameter too: change it, like any other parameter, only
when the bot is flat on that symbol (`deploy/README.md`, section "Entry
mode").

**Defaults from spec v0.16 (owner request of 2026-11-27).** A backtest needs no
parameter typed by hand except `LadderEnabled` (`false`, the default: a fixed
0.75% of balance per trade; `true`: the ladder). The defaults are SessionRange
with `EntryModel` Fvg on gold (XAUUSD) only, all day, with the D1 trend
filter, and the drawdown brake: while the balance is 3% or more below its peak
since `RiskEpochDate` (the kill switch's peak), a new trade risks 0.375%
(`DrawdownBrakePercent` 3, `DrawdownBrakeFactor` 0.5; fixed sizing only,
the ladder and Recovery ignore it); the full 0.75% comes back as soon as the
balance is less than 3% below the peak. From spec v0.17 (row AY) SessionRange
takes its side from `RangeTrendFastEma` 20: a buy when the D1 EMA(20) is above
the D1 EMA(50), a sell when it is below, no order when they are equal
(`RangeTrendFastEma` 0 is the close against the EMA(50) of before). The `start` line ends with
`brake=3/0.5` (until v0.17; from v0.18 the brake is off by default and the
line ends with `brake=off`, see "Gold as two instances" below). Every account limit is unchanged (daily budget 2%, kill
switch 8%). Only the XAUUSD instance is started with these files; no
currency-pair instance runs SessionRange. Before the Challenge, on a demo or
FTMO Free Trial account: the stop-order record (PLT-011), the live gold
spread check and the SessionRange record (`deploy/README.md`, "Before
SessionRange trades on the Challenge account"); proposed, for you to accept
or waive: two to four weeks on the Free Trial with these files. The ZoneFvg
defaults of v0.14 (Choch model) stay selectable with `EntryMode=ZoneFvg`
and `EntryModel=Choch`. `MaxSpreadPips`, `MinStopPips`,
`ChochMinStopPips` and `MaxVolumeUnits` at `0`, automatic from the symbol
(spec 7.1; the `start` line ends with `profile=... pipSize=...` and the
values). If one of the four is typed by hand and does not fit the symbol, the
bot refuses to start with a Log line `refused to start: invalid parameter:
...` that names the parameter, the pip size and the automatic value: set it
back to 0. Live, `RiskEpochDate`, the Telegram values, the heartbeat address
and the news feed address are still set in the parameter files.

Do not change `EntryMode` while the instance has an order or a position.
This matters most for SessionRange (spec 3.8): its position is closed at
`RangeExitNy` (16:30 New York) only by an instance that runs in
SessionRange mode. If the instance starts in another mode while a
SessionRange position (label with `RH-` or `RL-`) is open, the position
keeps its stop loss, its take profit and the Friday close, but nothing
closes it at 16:30 New York.

**Gold as two instances (spec v0.18, rows AZ to BE).** From spec v0.18 gold
runs as two cBot instances on the same XAUUSD account, each with its own file:
`deploy/params/xauusd.cbotset.example` (`EntryMode=SessionRange`,
`RiskPercent=0.75`) and `deploy/params/xauusd-overnight.cbotset.example`
(`EntryMode=Overnight`, `RiskPercent=1.0`). The Overnight instance places a
buy stop just above the price at 18:15 New York on Monday to Thursday
evenings (the close of the 18:15 bar plus the spread plus 0.03 x ATR(M15)),
for 60 minutes; its stop is one daily ATR below the entry, its safety target
5R above (the `MaxStopAtr` limit of the other modes does not apply to it;
the 8 USD smallest stop does); an open Overnight position is closed at 02:45
New York. Its labels
carry `ON-yyyyMMdd`. Neither instance cancels, closes, modifies or
time-exits the other's order or position, also after a restart; the
emergency guard is the one exception: it closes every bot trade of the
account. Each instance still refuses an entry while any bot order or
position is open on XAUUSD, so the two can never hold a position at the
same time. **The account limits are shared:** the 2% daily budget, the 8%
kill switch, the monthly loss limit and the FTMO guard count the trades of
both instances, so a loss of one instance can stop the other's next entry.
Give each instance its own heartbeat address. Do not stop one of the two
while it has an order or a position: the other does not close it at its
timed exit or on Friday evening. The drawdown brake is off by default from
v0.18 (`DrawdownBrakePercent` 0, row BC; the `start` line ends with
`brake=off`); `DrawdownBrakePercent=3` switches it on again. Before the
Overnight instance trades on the Challenge account, its cost gate (row BD)
must be passed on the Free Trial: `docs/test-records/WP43-overnight-spread.md`.

## 6. Weekly checks (Saturday)

| # | Check | How | Action if wrong |
|---|---|---|---|
| 1 | All four running, few restarts | `docker compose ps`; `docker inspect --format '{{.RestartCount}}' zfvg-eurusd-1` (and the other three). Source: https://docs.docker.com/reference/cli/docker/inspect/ | Restarts you did not cause: send the logs to the developer |
| 2 | Seven daily summaries per symbol arrived | Telegram | See section 3 |
| 3 | Calendar feed against the website | `docker compose logs --since 168h eurusd \| grep ' news '` lists the downloads; compare the week's high-impact events for USD, EUR, GBP, JPY and AUD on the Forex Factory calendar with the news windows in the `skip` lines (`reason=NewsWindow`). Spec 6.2: differences are reviewed weekly | A missed or mistimed event: add it to `ExtraBlackouts` if it repeats, and tell the developer |
| 4 | Slippage | `docker compose logs --since 168h \| grep ' trade '` and read `slippagePips` and `exit` | Average slippage on stop exits above 0.5 pip (the stress level in spec 12.1 step 6): tell the developer |
| 5 | Disk space | `df -h /` and `docker system df` | Above 80% used: tell the developer before it fills |
| 6 | Security updates and reboot | `ls /var/log/unattended-upgrades/` (recent files) and `pro system reboot-required`. Sources: https://ubuntu.com/server/docs/how-to/software/automatic-updates/ , https://ubuntu.com/blog/ubuntu-updates-best-practices-for-updating-your-instance | Reboot needed and the bot flat: `sudo reboot` now (Saturday), then `docker compose ps` |
| 7 | Clock synchronised | `timedatectl status` shows `System clock synchronized: yes` | See `deploy/README.md` step 6 |
| 8 | Heartbeat monitor | All four checks green in the monitoring service | Fix the failing one |
| 9 | Image still pinned | `grep image: /opt/zfvg/docker-compose.yml` shows `5.9.11` (or the version you deliberately moved to), never `latest` | Put the pinned tag back |
| 10 | Account deadlines | Days left on the Free Trial (reported 14 days, ends early with no trade in the first 7, spec 15.6); days since last trade (30-day rule) | Plan the next trial or talk to the developer |

## 7. Before an FTMO rule change

FTMO changes its terms from time to time (spec 15). When FTMO announces a
change, or once a month, before it takes effect:

1. Read the change in the FTMO client area.
2. Compare it with spec 15.2 and with these parameters: `InitialCapital`,
   `FtmoDailyLossPercent`, `FtmoMaxLossPercent`, `FtmoMaxLossMode`,
   `FtmoGuardBufferPercent`, `ConsistencyMaxPercent`, `DayResetZone` (FTMO's day boundary), `ProfitTargetPercent`,
   `MaxRequestsPerDay`, `MaxVolumeUnits` (FTMO's lot cap), and the rules on
   news, weekends, inactivity and VPS location.
3. If anything differs, or you are not sure: stop the bot when it is flat
   (`docker compose stop`) and tell the developer **before** the change takes
   effect. Do not edit FTMO-related parameters yourself: the spec and the
   parameters are changed together.
4. If nothing differs, note the date of the check.

## 8. Contacts and records

- Developer: through the repository's pull requests and issues. Never paste a
  password, token, account number or heartbeat address there; logs are masked,
  but check before you paste.
- Fault-injection and first-start results:
  `docs/test-records/WP11-fault-injection.md`.

## 9. FTMO Instant account (100,000 USD, spec 15.8)

The bot is set for an FTMO Instant account of 100,000 USD (spec v0.20, rows
BG to BM). FTMO's rules and the bot's limits inside them:

| | FTMO Instant rule | Bot limit | Room left |
|---|---|---|---|
| Daily | Equity above the 00:00 Prague balance minus 3,000 | Daily risk budget 2,000 (2%); the emergency guard closes everything at minus 2,500 | 1,000 after the budget |
| Overall | Equity above P minus 10,000; P is the highest closed-trade balance (at least 100,000), it rises in real time, never falls and is not reset by a reward | Kill switch 8% below P; the emergency guard closes everything at P minus 9,500 | 2,000 at the start |
| Monthly | none | Monthly loss limit 2,000 (2%) | n/a |
| Consistency | Best day at most 30% of the total profit since the last reward; a condition for a reward, not a breach | The daily `consistency` journal line | n/a |

The parameter files carry `InitialCapital=100000.0`,
`FtmoDailyLossPercent=3.0`, `FtmoMaxLossPercent=10.0`,
`FtmoMaxLossMode=TrailingBalance`, `FtmoGuardBufferPercent=0.5`,
`ConsistencyMaxPercent=30.0` and `RewardPeriodStartDate=` (empty). Do not
change them yourself (section 7).

- **`RiskEpochDate` stays the account's first day.** FTMO's floor never
  resets, so the bot's peak must not either. Do not move it after a reward.
  If the bot cannot read the history it needs for P, the guard falls back to
  the static floor (90,500 at 100,000), which is never looser.
- **Withdrawals (rewards).** A withdrawal lowers the balance but not P, so it
  uses room under both the kill switch and FTMO's floor. Withdraw only so much
  that the balance stays at least about 5% above P x 0.92 (the kill switch
  line). Example: with P at 112,000 the kill switch line is 103,040; keep the
  balance at or above about 108,200 after the withdrawal.
- **Consistency line.** Once a Prague day and at every start each instance
  writes, for example:
  `consistency since=2026-12-01 total=4200 bestDay=2026-12-09 bestDayProfit=1500 ratio=35.71 limit=30 met=no`.
  `total` is the net closed profit of the whole account (commission and swap
  included) since 00:00 Prague time of `RewardPeriodStartDate` (empty: of
  `RiskEpochDate`); `bestDay` is the Prague day with the largest profit;
  `ratio` is its share of `total` in percent (`NA` while `total` is not above
  0); `met=yes` means the 30% condition for a reward is met. It is
  information only: it never changes an order. `consistency status=unavailable`
  means the history could not be read that day; nothing else happens.
- **After each reward**, set `RewardPeriodStartDate` to the date of that
  reward (`yyyy-MM-dd`) in every parameter file, the same way as section 5
  (stop, edit, start), and only while the bot is flat: no open position and no
  pending order of the bot. The next `consistency` lines then count from that
  day.

## 10. FTMO 2-Step Challenge (200,000 USD, spec row BN)

For the 2-Step Challenge and Verification of 200,000 USD use the two gold
files `deploy/params/xauusd-challenge2step.cbotset.example` (SessionRange)
and `deploy/params/xauusd-overnight-challenge2step.cbotset.example`
(Overnight) in place of `xauusd` and `xauusd-overnight`. They equal the
funded (FTMO Instant) files except six lines: both risk **1.25%** per trade
(`RiskPercent=1.25`) with the monthly loss limit off
(`MonthlyLossLimitPercent=0.0`), and the account limits are the 2-Step's:
`InitialCapital=200000.0`, `FtmoDailyLossPercent=5.0` (5% daily) and
`FtmoMaxLossMode=Static` (a fixed 10% floor at 180,000), with
`FtmoGuardBufferPercent=1.0` (the emergency guard closes everything 2,000
USD before either floor). The daily budget (2%) and the kill switch (8%)
are unchanged. After passing, switch to the funded files (`xauusd`,
`xauusd-overnight`) and set `RiskEpochDate` to the funded account's first
day, only while the bot is flat (section 5).
