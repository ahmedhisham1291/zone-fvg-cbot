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
| `SanityCap` | A trade was refused: risk above 0.55%, volume above `MaxVolumeUnits`, or stop or target on the wrong side (spec 4.3) | Nothing to do now; no order was sent. Send the log to the developer: it means a calculation looked wrong |
| `StopLossMissing` | A bot position has no stop loss, or it was closed because the stop could not be set | **Urgent.** Open the cTrader app now. If the position is still open without a stop, set the stop yourself from the planned stop in the alert or close the position. No new entries are placed while any bot position lacks a stop |
| `StopLossRepaired` | A missing stop was set again from the trade's comment | Check in the app that the stop is where planned. Find out who or what removed it |
| `DailyLimit` | The daily risk budget (1.5% of initial capital, measured from 00:00 Prague balance) is used up. Pending orders cancelled; no entries until the next Prague day | Nothing. Read the daily summary next morning |
| `KillSwitch` | Drawdown from the highest closing balance since `RiskEpochDate` reached 8%. The bot is **Halted** until you set a new `RiskEpochDate` (spec 4.2, 8.5) | Do not resume the same day. Review the trades with the developer. Resume only by section 5, after a decision |
| `EmergencyGuard` | Equity came within 1,000 USD of an FTMO floor. The bot closed all positions, cancelled orders and **Halted** (spec 4.2) | Check in the app that everything is closed. Stop all containers (`docker compose stop`) until reviewed with the developer. Resume by section 5 |
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
| Room to FTMO floors | Distance from equity to the FTMO daily floor (00:00 Prague balance minus 5%) and overall floor (90% of initial capital) | Emergency guard acts at 1,000 USD room |
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
  Verification and the FTMO Account each start with their own first day;
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
   `FtmoDailyLossPercent`, `FtmoMaxLossPercent`, `FtmoGuardBufferPercent`,
   `DayResetZone` (FTMO's day boundary), `ProfitTargetPercent`,
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
