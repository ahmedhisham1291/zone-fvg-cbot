# WP11 test record: first-start checks and fault-injection tests

Manual verification of PLT-008 and of spec section 12.3. The owner runs every
test on an **FTMO Free Trial or other demo account**, never on the Challenge
account (spec 15.7), fills in the blank fields and signs at the end. PLT-008 is
marked `implemented` only after this record is signed
(`docs/test-records/README.md`).

Two variants:

- **Laptop** (spec 10.5): cTrader desktop on Windows, four instances. Tests 1
  and 3 to 14. Test 2 cannot be proven on a laptop; test 10 needs a Friday
  evening.
- **VPS** (spec 10.2): the four containers of `deploy/docker-compose.yml`. All
  14 tests, repeated in full even if they passed on the laptop.

The record is run in the mode that goes live (spec 12.3 and 13.1), with the
parameter files of `deploy/README.md`, section "Entry mode". The pending
entry order of tests 2 and 3 is a limit order in ZoneFvg mode and a stop
order in Breakout mode.

Commands are explained, with documentation links, in `deploy/README.md`. Run
them in `/opt/zfvg`. Container names are `zfvg-eurusd-1`, `zfvg-gbpusd-1`,
`zfvg-usdjpy-1`, `zfvg-audusd-1`; the network is `zfvg_default`. The examples
use EURUSD; use whichever symbol has the position or order the test needs.

Extra documentation for the commands in this record:

| Command | Source |
|---|---|
| `docker inspect --format '{{.State.Pid}}' ...`, `{{.RestartCount}}` | https://docs.docker.com/reference/cli/docker/inspect/ |
| `docker network disconnect` / `connect` | https://docs.docker.com/reference/cli/docker/network/disconnect/ , https://docs.docker.com/reference/cli/docker/network/connect/ |
| `docker compose run -d --no-deps --name ...` | https://docs.docker.com/reference/cli/docker/compose/run/ |
| `docker rm -f` | https://docs.docker.com/reference/cli/docker/container/rm/ |
| `kill -9`, `sleep`, `date`, `seq`, `grep -c` | https://man7.org/linux/man-pages/ (`kill(1)`, `sleep(1)`, `date(1)`, `seq(1)`, `grep(1)`) |
| `restart: unless-stopped` | https://docs.docker.com/reference/compose-file/services/ ("restart") |

## Run details

| Field | Laptop | VPS |
|---|---|---|
| Date(s) | | |
| Run by | | |
| Account type (Free Trial / demo; hedging or netting) | | |
| Bot version and commit (`start` line) | | |
| cTrader desktop version / CLI image tag and digest | | |
| VPS provider, country, Ubuntu version | n/a | |

## First-start checks (VPS; `deploy/README.md` step 12)

Things the documentation does not say. Each must have an answer before the
fault tests.

| # | Check | Steps | Expected | Observed | Pass / fail |
|---|---|---|---|---|---|
| F1 | Image digest | README 12.1 | A `ghcr.io/spotware/ctrader-console@sha256:...` value. Write it here | | |
| F2 | Docker starts at boot | `systemctl is-enabled docker` | `enabled` | | |
| F3 | The CLI accepts our `.algo` (built for net6.0; CLI pages ask for .NET 8) | README 12.2 | The parameter list is printed. Laptop: an instance started with "Start in external process" runs | | |
| F4 | Account and symbol names | README 12.3 | Account listed; `EURUSD`, `GBPUSD`, `USDJPY`, `AUDUSD` present exactly | | |
| F5 | The `.cbotset` file is read | README 12.4 | No "Invalid .cbotset file format"; the bot does not refuse with `RiskEpochDate`; Telegram `Started` arrives (token and chat id came from the file) | | |
| F6 | `--full-access` not needed | README 12.4 log | At most a warning about access rights and a short delay; the bot starts; news download and Telegram work | | |
| F7 | Local storage location | README 12.5 | A `cAlgo/LocalStorage/cBots/ZoneFvgBot/<id>` folder inside volume `zfvg_eurusd-data`. Write the path | | |
| F8 | Same instance id after a restart | README 12.5 | The same `<id>` after `docker compose restart eurusd`; no news download on the second start | | |
| F9 | Does the CLI write log files of its own? | `sudo find "$(docker volume inspect --format '{{ .Mountpoint }}' zfvg_eurusd-data)" -name '*.log'` | Write what is found (paths) or "none". Only standard output is documented | | |
| F10 | Memory and CPU per container | README 12.6 | Write the numbers; total memory below 70% of the server's | | |

If F3, F5, F7 or F8 fails: stop and report to the developer. Do not run the
fault tests.

## Fault-injection tests (spec 12.3)

General expected results for every test: no position without a stop loss for
longer than one minute; no duplicate order (two bot orders or positions with the
same label); no manual help needed for recovery.

How to check "no duplicate": in the cTrader app, every bot order and position
label is `ZFVG|v1|<symbol>|<zoneId>`; no label may appear twice.

---

### Test 1. Kill the container while a position is open

Precondition: a bot position is open on the symbol.

VPS:

1. `docker compose ps` and note the status.
2. Kill the CLI process from the host (a crash, not a manual stop; use this and
   not `docker kill`, which Docker may treat as a manual stop):
   `sudo kill -9 "$(docker inspect --format '{{.State.Pid}}' zfvg-eurusd-1)"`
3. Wait 2 minutes. `docker compose ps`;
   `docker inspect --format '{{.RestartCount}}' zfvg-eurusd-1`;
   `docker compose logs --since 5m eurusd`.

Laptop: end the cTrader process (or the external CLI process) in Task Manager,
then start cTrader and the instance again (WP08 record, step 8).

Expected: the container is `Up` again on its own and the restart count went up
by one (laptop: after your manual start); recovery report with `adopted=1`; the
position keeps its stop and target; no new order for that zone; Telegram
`Started`.

| | Laptop | VPS |
|---|---|---|
| Date | | |
| Observed | | |
| Pass / fail | | |

### Test 2. Reboot the server while an entry order is pending

Precondition: a bot pending entry order exists (a limit order in ZoneFvg
mode, a stop order in Breakout mode). VPS only.

1. Note the order's price, stop, target and expiry in the cTrader app.
2. `sudo reboot`
3. Do not log in. Wait for Telegram `Started` from all four symbols (or 10
   minutes). Then log in: `docker compose ps`.

Expected: the four containers are `Up` without any login or command; the order
is still at the broker with the same price, stop, target and expiry; the
recovery report shows it kept (`kept=1`) if its zone is still valid, or
cancelled with a reason otherwise.

| | Laptop | VPS |
|---|---|---|
| Date | n/a (spec 10.5) | |
| Observed | n/a | |
| Pass / fail | n/a | |

### Test 3. Order fills while the bot is stopped

Precondition: a bot pending entry order exists (limit or stop order, as in
test 2).

VPS: `docker compose stop eurusd`. Watch the order in the cTrader app until it
fills (this needs the market; it may take several attempts). Then
`docker compose start eurusd` and read `docker compose logs --since 10m eurusd`.

Laptop: stop the instance; start it after the fill.

Expected: recovery report `adopted=1`; stop and target present and equal to the
plan in the order's comment; no `StopLossMissing` alert; no new order for that
zone.

| | Laptop | VPS |
|---|---|---|
| Date | | |
| Observed | | |
| Pass / fail | | |

### Test 4. Trade hits its target while the bot is stopped

Precondition: a bot position is open.

VPS: `docker compose stop eurusd`. Wait until the History tab shows the trade
closed at take profit. `docker compose start eurusd`. Then
`docker compose logs --since 30m eurusd | grep ' trade '`.

Laptop: stop the instance; start it after the close.

Expected: a `trade` line for the position with `exit=` take profit (journal
backfilled, spec 8.3 step 7); `TradeClosed` alert; for the rest of the zone's
life no `signal` line with `result=taken` for the same `zone=` id.

| | Laptop | VPS |
|---|---|---|
| Date | | |
| Observed | | |
| Pass / fail | | |

### Test 5. Network cut for 1, 10 and 60 minutes

Run three times: 1, 10 and 60 minutes, during the trading session.

VPS:

1. `docker network disconnect zfvg_default zfvg-eurusd-1`
2. Wait the time. Watch `docker compose ps` once in the middle and note whether
   the container stays `Up` or restarts (both are acceptable; write which).
3. `docker network connect zfvg_default zfvg-eurusd-1`
4. `docker compose logs --since 70m eurusd | grep -E ' connection | state | recovery | signal '`
   (if the container restarted: `docker compose ps` must show it `Up` again).

Laptop: switch Wi-Fi off for the time, then on.

Expected: a `connection` line `event=disconnected` and state Recovering (or a
restart of the cBot, spec 8.4); after the reconnect a recovery report and
Running; `signal` lines for bars inside the gap are not taken (spec 8.1
principle 4); no order sent while disconnected; for 10 and 60 minutes a
heartbeat alert from the monitoring service.

| | Laptop 1 / 10 / 60 min | VPS 1 / 10 / 60 min |
|---|---|---|
| Date | | |
| Container stayed Up or restarted | n/a | |
| Observed | | |
| Pass / fail | | |

### Test 6. Network cut at the moment an order is sent

The exact moment cannot be chosen by hand. The procedure cuts the network
briefly just after every M15 close on one symbol during a session and waits for
a cut to coincide with an order. A closed bar reaches the core with the first
tick after the close, or at the latest from the one-minute timer
(`src/ZoneFvg.Bot/CTraderBars.cs`), so the cut starts 1 second after each close
and lasts 10 seconds.

VPS (one symbol; stop with Ctrl+C, then run the `connect` command once in case
the loop stopped during a cut; an "already connected" error is harmless):

```bash
while true; do sleep $(( 900 - $(date +%s) % 900 + 1 )); docker network disconnect zfvg_default zfvg-eurusd-1; sleep 10; docker network connect zfvg_default zfvg-eurusd-1; done
```

```bash
docker network connect zfvg_default zfvg-eurusd-1
```

Find a hit: `docker compose logs --since 24h eurusd | grep -E ' signal .*result=taken'`
and compare its time with the cuts.

Laptop: switch Wi-Fi off right at an M15 close, on again 10 seconds later,
whenever a signal is expected.

Expected: for every zone, at most one bot order or position at the broker,
also after the reconnect (spec 5.3). If no cut coincided with an order during
the test period, write "not reached" and the number of cuts; the owner then
decides whether the scenario tests of WP06 are enough.

| | Laptop | VPS |
|---|---|---|
| Date | | |
| Number of cuts / hits | | |
| Observed | | |
| Pass / fail / not reached | | |

### Test 7. Remove the stop loss by hand

Precondition: a bot position is open.

Both variants: in the cTrader app, modify the position and remove its stop
loss. Note the time. Watch Telegram and the app.

Expected: a `StopLossMissing` alert, then within one event (the one-minute
timer or the next M15 close) the stop is set again at the planned level and a
`StopLossRepaired` alert arrives; no new entry on any symbol while the stop is
missing.

| | Laptop | VPS |
|---|---|---|
| Date | | |
| Time removed / time restored | | |
| Observed | | |
| Pass / fail | | |

### Test 8. Start a second instance on the same symbol

VPS:

1. `docker compose run -d --no-deps --name zfvg-test-second eurusd`
2. Leave it running until at least one signal is taken on EURUSD, or a full
   session.
3. `docker logs zfvg-test-second` and `docker compose logs eurusd`.
4. `docker rm -f zfvg-test-second`

Laptop: add a second instance of ZoneFvgBot on another EURUSD M15 chart with
the same parameters; remove it after the session.

Expected: at most one bot order or position per zone label; the second instance
finds the first one's order at the broker and does nothing (spec 5.3 step 1);
both recover and run without errors.

| | Laptop | VPS |
|---|---|---|
| Date | | |
| Observed | | |
| Pass / fail | | |

### Test 9. Calendar feed returns an error or garbage

The bot downloads at start if the last attempt was more than an hour ago,
otherwise at the next refresh (at most `NewsRefreshHours` = 4 hours) (spec 6.2).

VPS, part A (error):

1. Write down the current `NewsFeedUrl` of `params/eurusd/bot.cbotset`.
2. Set `NewsFeedUrl=https://example.invalid/calendar.json` with `nano`, then
   `docker compose restart eurusd`.
3. Wait for the next download attempt; `docker compose logs --since 5h eurusd | grep -E ' news | state '`.

Part B (garbage): the same with `NewsFeedUrl=https://example.com/` (an HTML
page, not a calendar).

Part C (stale): keep a bad address from Friday through the week roll-over to
Monday 08:00 UTC (the cached data no longer covers the new week), or for 48
hours.

Finally put the original address back and `docker compose restart eurusd`.

Laptop: the same changes in the instance's parameter panel.

Expected: A and B: a `news` line with a failed or rejected outcome, a
`NewsFeedDown` alert, earlier data kept, entries still allowed. C: a
`CalendarUnavailable` alert, state Blocked, skips with
`reason=NewsCalendarUnavailable`; open trades unaffected. After the original
address is back: a successful download and Running.

| | Laptop A / B / C | VPS A / B / C |
|---|---|---|
| Date | | |
| Observed | | |
| Pass / fail | | |

### Test 10. Bot stopped across Friday close with a position open

Precondition: Friday, a bot position open at about 19:30 UTC.

VPS: at about 19:30 UTC `docker compose stop eurusd`. After 20:00 UTC and
while the market is still open (before about 21:00 UTC),
`docker compose start eurusd`.

Laptop: stop the instance before 20:00 UTC and start it after 20:00 UTC while
the market is open.

Expected: during recovery the position is closed at once (spec 8.3 step 9;
if the market has already closed, at the market's reopen, architecture
section 10 "Weekend restart");
`TradeClosed` alert and the recovery report; no new order before Monday.

| | Laptop | VPS |
|---|---|---|
| Date | | |
| Observed | | |
| Pass / fail | | |

### Test 11. Change a parameter while a trade is open

Precondition: a bot position is open.

VPS: in `params/eurusd/bot.cbotset` change `ImpulseAtrMult=2.0` to
`ImpulseAtrMult=2.5`, then `docker compose restart eurusd`. Read the recovery
report. Afterwards put `2.0` back and restart again.

Laptop: the same in the parameter panel.

Expected: the position is kept with its stop and target (adopted, or listed as
a stranger if its zone is not found under the new parameters, spec 8.3 step 6);
it is included in the risk totals and in Friday close; no second order for its
zone.

**Adoption test for a version pair** (`deploy/README.md` section 15): the same
procedure, but instead of the parameter change replace `algo/current/ZoneFvgBot.algo`
by the new version and run `docker compose up -d --force-recreate eurusd`.
Same expected result. Record old and new version below; each pair needs its own
pass.

| | Laptop | VPS |
|---|---|---|
| Date | | |
| Version pair (adoption test only) | | |
| Observed | | |
| Pass / fail | | |

### Test 12. Stop all heartbeats

VPS: `docker compose stop`. Wait longer than the monitoring service's grace
time. Then `docker compose start`.

Laptop: stop all four instances (or switch Wi-Fi off) for longer than the grace
time.

Expected: the monitoring service alerts the phone (and the Telegram chat, if
configured, spec 11) for each of the four checks; after the start the checks
return to normal.

| | Laptop | VPS |
|---|---|---|
| Date | | |
| Time stopped / time of phone alert | | |
| Pass / fail | | |

### Test 13. Force repeated exceptions

No owner-side way is known to force unhandled exceptions in the released bot:
it has no test switch, and the bad inputs an owner can give (parameters,
calendar address) are rejected or handled without an unhandled error. See the
WP11 pull request, "Decisions for the human". Until that is decided, write
"not testable with the released bot" and note that the error-storm behaviour
(RSK-010, REC-011) is covered by the core's automated tests.

Expected (spec 12.3): the bot halts after 5 errors in an hour, sends
`ErrorStorm`, keeps running in a non-trading state, does not crash or loop.

| | Laptop | VPS |
|---|---|---|
| Date | | |
| Method used | | |
| Observed | | |
| Pass / fail / not testable | | |

### Test 14. Restart ten times in ten minutes

VPS:

```bash
for i in $(seq 1 10); do docker compose restart eurusd; sleep 60; done
docker compose logs --since 15m eurusd | grep -c ' news '
```

Also check the app for orders and read `docker compose logs --since 15m eurusd`.

Laptop: stop and start the instance ten times, one minute apart.

Expected: at most one calendar download in the ten minutes (spec 6.2: never
more than once an hour, across restarts); ten recovery reports; no duplicate
order; the bot ends in Running (or Blocked with a reason). This test depends on
first-start check F8.

| | Laptop | VPS |
|---|---|---|
| Date | | |
| Downloads counted | | |
| Observed | | |
| Pass / fail | | |

## Soak test (spec 12.3)

Four weeks of continuous running on the VPS with at least ten forced restarts
at random times (use test 1's kill command or `docker compose restart`). The
Free Trial is reported to last 14 days (spec 15.6), so the four weeks span
several trials: change `ACCOUNT` in `/opt/zfvg/.env` and `RiskEpochDate` in the
parameter files (runbook section 5), then `docker compose up -d --force-recreate`.

| Week | Account | Restarts forced | Positions without stop | Duplicate orders | Manual help needed | Notes |
|---|---|---|---|---|---|---|
| 1 | | | | | | |
| 2 | | | | | | |
| 3 | | | | | | |
| 4 | | | | | | |

## Sign-off

PLT-008 text: "Deployment files run one cTrader CLI container per symbol with an
automatic restart policy, a mounted volume for storage and logs, a pinned image
version, and no secret inside the image or the repository."

| Item | Evidence | Owner signature and date |
|---|---|---|
| First-start checks F1 to F10 | Table above | |
| Laptop tests 1, 3 to 14 | Tables above | |
| VPS tests 1 to 14 | Tables above | |
| Soak test | Table above | |
| PLT-008 | All of the above | |
