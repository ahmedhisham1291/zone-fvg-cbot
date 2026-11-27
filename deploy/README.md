# Deployment guide: laptop stage and VPS

How to run the Zone + FVG cBot first on your laptop (spec 10.5) and then on a
rented Linux server (spec 10.2). Written for an owner who is not a server
administrator. Follow the steps in order and do not skip a check.

- What to do when an alert arrives, weekly checks, halting and resuming:
  [`docs/runbook.md`](../docs/runbook.md).
- The 14 fault-injection tests you must pass before the Challenge:
  [`docs/test-records/WP11-fault-injection.md`](../docs/test-records/WP11-fault-injection.md).
- The entry mode (Breakout, ZoneFvg, SessionRange or Overnight), which mode and values
  the live files carry, how to change them, the two records you sign before
  an instance trades in Breakout mode on the Challenge account, and what
  must happen before SessionRange trades there: section "Entry mode" below,
  before Part A.

## How to read this guide

- Every command has a link to the documentation it comes from, in the line
  `Source:` under it. If a command fails, read that page first.
- Text in `CAPITALS_LIKE_THIS` or with `your-` in front is a placeholder you
  replace. Never paste a real password, token or account number into a chat,
  an issue, a pull request or a file inside the repository.
- **To check on first start** marks something the documentation does not say.
  It is checked once, on a demo account, before any real use. Write the answer
  in the test record (`docs/test-records/WP11-fault-injection.md`, section
  "First-start checks").
- Use the laptop for setting up the server (copying files needs a keyboard).
  The phone is enough for monitoring: Telegram alerts, the heartbeat monitor and
  the cTrader mobile app.
- Commands that start with `sudo` run as administrator. Read them before you
  press Enter.

## Documentation used

| Topic | Page |
|---|---|
| cTrader CLI: run a cBot, Docker example, `.cbotset` file, `--full-access`, `--exit-on-stop` | https://help.ctrader.com/ctrader-cli/cbots/ |
| cTrader CLI: install, password file, .NET 8 requirement | https://help.ctrader.com/ctrader-cli/setup/ |
| cTrader CLI: FAQ (`.algo` must be built for .NET 8, `.cbotset` files) | https://help.ctrader.com/ctrader-cli/faq/ |
| cTrader CLI: troubleshooting | https://help.ctrader.com/ctrader-cli/troubleshooting/ |
| cTrader CLI: run a cBot around the clock | https://help.ctrader.com/ctrader-cli/use-cases/run-a-cbot-around-the-clock/ |
| cTrader CLI: full command and option reference | https://github.com/spotware/CLI-references (README, read 2026-11-16, commit `12c6042`) |
| cTrader CLI: official Docker image, tags, environment variables, Compose example | https://github.com/spotware/ctrader-console-docker (README, read 2026-11-16, commit `ea9a6f0`) |
| cTrader Algo: local storage (folders, saved every minute and on stop) | https://help.ctrader.com/ctrader-algo/guides/local-storage/ |
| cTrader Algo: network access works with `AccessRights.None` | https://help.ctrader.com/ctrader-algo/guides/network-access/ |
| Docker Engine on Ubuntu | https://docs.docker.com/engine/install/ubuntu/ |
| Docker post-installation (docker group) | https://docs.docker.com/engine/install/linux-postinstall/ |
| Compose file attributes | https://docs.docker.com/reference/compose-file/services/ (same text: https://github.com/compose-spec/compose-spec, `container_spec.md`, `05-services.md`, `09-secrets.md`) |
| Compose commands | https://docs.docker.com/reference/cli/docker/compose/ |
| json-file log rotation | https://docs.docker.com/engine/logging/drivers/json-file/ |
| Ubuntu users | https://ubuntu.com/server/docs/how-to/security/user-management/ |
| Ubuntu OpenSSH server | https://ubuntu.com/server/docs/how-to/security/openssh-server/ |
| sshd settings | https://man7.org/linux/man-pages/man5/sshd_config.5.html |
| Ubuntu firewall (ufw) | https://ubuntu.com/server/docs/how-to/security/firewalls/ and https://help.ubuntu.com/community/UFW |
| Ubuntu automatic updates | https://ubuntu.com/server/docs/how-to/software/automatic-updates/ |
| Ubuntu updates and reboots | https://ubuntu.com/blog/ubuntu-updates-best-practices-for-updating-your-instance |
| Ubuntu time synchronisation | https://ubuntu.com/server/docs/how-to/networking/timedatectl-and-timesyncd/ , https://ubuntu.com/server/docs/how-to/networking/chrony-client/ , https://ubuntu.com/server/docs/explanation/networking/about-time-synchronisation/ |
| OpenSSH client on Windows | https://learn.microsoft.com/windows-server/administration/openssh/openssh_install_firstuse |
| scp | https://man.openbsd.org/scp |
| Standard Linux tools used below: `cd`, `cp`, `mkdir`, `chmod`, `chown`, `find`, `printf`, `date`, `df`, `su`, `passwd`, `systemctl`, `reboot` | https://man7.org/linux/man-pages/ (one page per tool, for example https://man7.org/linux/man-pages/man1/chmod.1.html) |
| `nano` text editor (Ctrl+O saves, Ctrl+X exits) | https://www.nano-editor.org/dist/latest/nano.html |

## Facts from the documentation that shape this setup

1. **Image.** The official image is `ghcr.io/spotware/ctrader-console`. Its
   README lists `5.9.11` as the newest stable tag and says tags other than
   `latest` are immutable. The compose file pins `5.9.11`. (The image's git
   repository also has a `5.10.1` tag, which the README does not list; from 5.10
   the image moves to .NET 10. Do not use it until it has passed the fault tests
   on demo, spec 8.6 and 10.2.)
2. **.NET version. Read this.** The CLI pages say the `.algo` file must be built
   for .NET 8, and "cTrader CLI does not support older .NET cBots". This
   project builds for `net6.0` (`AGENTS.md`). The image README says it carries
   reference packs for older .NET versions. Whether the CLI runs our `.algo` is
   **to check on first start** (laptop: "Start in external process"; VPS:
   step 12.2). If it refuses, stop and tell the developer: retargeting is a code
   change, not a deployment step.
3. **Password.** The CLI reads the cTrader ID password only from a file
   (`--pwd-file`, or `PWD_FILE` in the environment). `run` rejects
   `--password`. The compose file mounts the file as a Docker secret at
   `/run/secrets/ctrader_password`. The cTrader ID and account number come from
   an environment file (`/opt/zfvg/.env`).
4. **Parameters.** A `.cbotset` file is the second argument of `run`, or
   `CBOT_SET` in the environment. The documented example writes it as
   `Name=Value` lines; the FAQ says `.cbotset` files come from the desktop app.
   If the CLI reports "Invalid .cbotset file format", save the parameters from
   cTrader desktop instead (troubleshooting page). **To check on first start**:
   the bot refuses to start while `RiskEpochDate` is empty or invalid, so a
   successful start proves the file was read.
5. **Full access.** Not used. Our cBot declares `AccessRights.None`, which is
   enough for HTTP (network-access page) and local storage (local-storage
   page). The image README adds that on Linux there is no sandbox either way;
   leaving out `--full-access` only prints a warning and waits five seconds.
6. **Local storage and logs.** cTrader keeps local storage under
   `Documents/cAlgo/LocalStorage/...` in the user's home directory; an Instance
   scope value lives in a folder named after the instance's unique identifier.
   The documentation does not say where that is inside the container. The
   compose file sets `HOME=/data` on a named volume, so whatever the CLI writes
   under its home survives container re-creation. Everything the cBot prints
   goes to standard output (`docker compose logs`), rotated at 5 files of
   10 MB per container. **To check on first start** (step 12.5): the folder
   really is under `/data`, and a restarted container reuses the same instance
   folder. If every start creates a new folder, the news download limit across
   restarts (spec 6.2, fault test 14) and the daily-budget memory do not work
   under the CLI: stop and tell the developer.
7. **Restart.** `--exit-on-stop` makes the container exit when the cBot stops;
   without it a container with `restart: unless-stopped` never restarts after
   the cBot stops (image README, "Operating the container"). What the CLI does
   after a disconnect is not documented (spec 10.2); fault tests 1, 2 and 5
   prove it.

---

## Entry mode (spec v0.10)

The bot has three entry modes (spec 3.7, 3.8 and section 7). Each instance
runs one of them, chosen with the parameter `EntryMode` at start:

- **Breakout** (the default until spec v0.13): in the direction of the D1 trend, the bot
  waits for an M15 bar that closes beyond the previous day's high or low
  inside the trading window, then places a stop order beyond that bar.
- **ZoneFvg** (the default from spec v0.14 to v0.15, owner request of 2026-11-24,
  with the Choch entry model below, all day, with the D1 trend filter): the
  zone entry of spec 3.2 to 3.5 and 3.9.
- **SessionRange** (added on 23 November 2026, spec 3.8): in the direction
  of the D1 trend, a stop order at the edge of the range that price made
  from 19:00 to 03:00 New York time. The order ends at 12:00 New York. It is
  cancelled when price breaks the other edge first (the setup is "void").
  An open position is closed at 16:30 New York. It is meant for gold only.
  **The default from spec v0.16** (owner request of 27 November 2026, rows
  AS to AX): `EntryMode=SessionRange` with `EntryModel=Fvg`, all day, a
  fixed 0.75% of balance per trade and the drawdown brake (see "Which mode
  and which values go live" below). Only the XAUUSD instance is started
  with it; no currency-pair instance runs SessionRange. The gates before the
  Challenge are in "Before SessionRange trades on the Challenge account"
  below.

Everything else (sizing, account limits, news, recovery, FTMO guards,
alerts) is the same in all modes. The twelve parameters of the group
"Entry mode" are the first twelve lines of every file in `deploy/params/`:

| Parameter | Value in the example files | What it does |
|---|---|---|
| `EntryMode` | `SessionRange` | `SessionRange` (the default from spec v0.16), `ZoneFvg` or `Breakout`. Any other text makes the bot refuse to start and name the parameter. Breakout and SessionRange also need `EntryModel=Fvg`; Breakout also needs a window that ends before 24:00 (`SessionStartUtc=07:00`, `SessionEndUtc=17:00`) |
| `BreakoutEntryBufferAtr` | `0.1` | Breakout mode: how far beyond the confirming bar the stop order is placed, in ATR(M15) |
| `BreakoutStopAtr` | `1.0` | Breakout mode: stop distance from the entry, in ATR(H4) |
| `TrendSlowEmaPeriod` | `200` | Breakout mode: trend filter. A buy needs the D1 EMA(`BiasEmaPeriod`) above the D1 EMA(`TrendSlowEmaPeriod`), a sell below it (spec 3.1). `0` switches it off. Allowed: 0 or 2 to 10000 |
| `BreakoutLastConfirmUtc` | `08:00` | Breakout mode: a bar that closes at or after this UTC time confirms nothing and ends the day's breakout (spec 3.7). Equal to `SessionEndUtc` switches it off. Must be after `SessionStartUtc` and not after `SessionEndUtc` |
| `RangeStartNy` | `19:00` | SessionRange mode: start of the range window, New York time, `HH:mm` |
| `RangeEndNy` | `03:00` | SessionRange mode: end of the range window, New York time. The window must be at least 16 bars of 15 minutes (4 hours) |
| `RangeLastEntryNy` | `12:00` | SessionRange mode: the day's order ends at this New York time. Not before `RangeEndNy` |
| `RangeExitNy` | `16:30` | SessionRange mode: an open position is closed at this New York time. After `RangeLastEntryNy` |
| `RangeMinWidthAtr` | `0.5` | SessionRange mode: smallest range, in ATR(D1). 0 or more |
| `RangeStopAtr` | `1.0` | SessionRange mode: stop distance from the entry, in ATR(H4). More than 0 |
| `RangeTrendFastEma` | `20` | SessionRange mode (spec v0.17, row AY): the side of the day's order. A buy when the D1 EMA(20) is above the D1 EMA(`BiasEmaPeriod`, 50) on the last closed D1 bar, a sell when it is below, no order when they are equal. `0` switches it off: the side then comes from the last D1 close against the D1 EMA(`BiasEmaPeriod`) (spec 3.1), as before v0.17. Allowed: 0, or 2 to `BiasEmaPeriod` − 1 |

The eleven values after `EntryMode` are checked at start in every mode: an
invalid value (for example `BreakoutStopAtr` = 0, `TrendSlowEmaPeriod` = 1,
`RangeStopAtr` = 0, `RangeExitNy` = `11:00` or `RangeTrendFastEma` = 1)
makes the bot refuse to start and name the parameter, also in the other
modes. That `RangeTrendFastEma` is below `BiasEmaPeriod` is checked in
SessionRange mode, the only mode that uses it. The four range times are
New York wall-clock times, written `HH:mm`, each on a 15-minute boundary,
and read in the order of the trading day, which starts at 17:00 New York.
The four breakout values are used in Breakout mode only, the seven range
values in SessionRange mode only; in the other modes they produce no signal
and are not part of the parameter hash. The gold file uses the same twelve
values as the forex files.

**Overnight (spec 3.10, v0.18).** `EntryMode=Overnight` is the fourth mode,
for the second gold instance only (see "Gold as two instances" below). Its
seven parameters, group "Overnight", are the next seven lines of every file,
directly after `RangeTrendFastEma`: `NightEntryNy` 18:15 and `NightLatestNy`
19:00 (the evaluation bar closes at or after the first and before the second,
New York time), `NightExitNy` 02:45 (an open position is closed),
`NightEntryBufferAtr` 0.03 (the buy stop lies this many ATR(M15) above the
close plus the spread), `NightStopAtr` 1.0 (stop distance in ATR(D1)),
`NightTargetR` 5.0 (safety target in R) and `NightOrderMinutes` 60 (life of
the order). They are checked at start in every mode (times `HH:mm` in the
order of the trading day, the three numbers above 0, minutes 1 to 180) and
used in Overnight mode only; the parameter hash covers them in Overnight
mode only. Overnight also needs `EntryModel=Fvg`.

**Entry model of ZoneFvg mode (spec 3.9, v0.11).** In ZoneFvg mode the
parameter `EntryModel` chooses the trigger at a touched zone: `Choch` (the
change of character of your manual method, spec 3.9; the default from spec
v0.14) or `Fvg` (the fair value gap of spec 3.4, the default until v0.13). The eight parameters of the group "Entry
model" are the next eight lines of every file in `deploy/params/`:

| Parameter | Value in the example files | What it does |
|---|---|---|
| `EntryModel` | `Fvg` | `Fvg` (the default from spec v0.16) or `Choch`, exactly so. `Choch` with an `EntryMode` other than `ZoneFvg` makes the bot refuse to start and name both parameters |
| `RequireSweep` | `false` | Choch: a change of character counts only after a sweep of a recent M15 swing |
| `ChochWindowBars` | `32` | Choch: M15 bars after the touch in which the change of character must come. 4 to 96 |
| `ChochUseBias` | `true` | Choch: `true` (the default from spec v0.14) trades a zone only when the D1 trend matches it |
| `ChochStopAtDistal` | `false` | Choch: `true` puts the stop beyond the zone's far edge as well |
| `ChochTargetBufferAtr` | `0.1` | Choch: the target sits this far (in ATR(H4)) before the next opposing level. 0 to 1 |
| `ChochMinStopPips` | `0.0` | Choch: smallest stop in pips, in place of `MinStopPips`. `0` = automatic (spec 7.1): 5 pips for the forex pairs, 3.00 USD of price for gold at whatever pip size the broker reports |
| `MinRewardRisk` | `2.0` | Choch: a setup whose target gives less than this reward to risk is skipped. 1 to 10 |

The eight values are checked at start in every mode, like the values of
"Entry mode". From spec v0.16 the files carry the cBot's defaults,
`EntryMode=SessionRange`, `EntryModel=Fvg`, `ChochUseBias=true`,
`SessionStartUtc=00:00` and `SessionEndUtc=24:00` (you trade all day;
`24:00` is accepted for `SessionEndUtc` only, and in Breakout mode the bot
still refuses it). For ZoneFvg with the Choch model set
`EntryMode=ZoneFvg` and `EntryModel=Choch` (the files of v0.14 and v0.15);
for Breakout set `EntryModel=Fvg`, `SessionStartUtc=07:00` and
`SessionEndUtc=17:00`. The Choch values are part of the parameter
hash only with `EntryModel=Choch`, so the Fvg model keeps its hash.

**Warm-up of the trend filter.** The D1 EMA of period n reads 4 x n closed
D1 bars, so with `TrendSlowEmaPeriod` 200 the bot needs 801 closed D1 bars
(about 3.1 years) before it can trade its first breakout; with the filter
off it needs 201 (`BiasEmaPeriod` 50). ZoneFvg and SessionRange need the
same 201 D1 bars, and every mode needs 248 H4 and 1968 M15 bars with the
default values; the Choch model needs 2087 M15 bars (its 32-bar window and
the 100-bar M15 swing lookback). The bot writes one Log line at start,
`closed bars loaded at start against the warm-up: D1 ... of 201, H4 ... of
248, M15 ... of 2087`, so you can see whether the history was enough. The bot loads them from the broker's
history at start. Live this is normally available; if it is not, recovery
reports the D1 series as a data problem and the bot does not trade (fail
closed).

**Which mode and which values go live (spec 13.1, "What goes live").** The
owner chose SessionRange on gold (27 November 2026, spec row AX), so the
example files carry `EntryMode=SessionRange` with `EntryModel=Fvg`, the
cBot's default from spec v0.16. Only the XAUUSD instance is started with
these files; the four currency-pair files stay as examples of the format and
no currency-pair instance is started with SessionRange. For any other mode or
symbol, before the laptop stage, write into the files:

- `EntryMode`: the mode chosen by the acceptance procedure
  ([`docs/backtest-howto.md`](../docs/backtest-howto.md), Part 4). A mode
  that did not pass, or was not chosen, is never set on the Challenge
  account.
- The accepted settings: only the searched parameters (spec 7), with the
  values of the backtest run that the acceptance report judged, symbol by
  symbol. In Breakout mode these are `BiasEmaPeriod`, `BreakoutStopAtr` and
  `RewardRisk`; in ZoneFvg mode `BiasEmaPeriod`, `ImpulseAtrMult`,
  `MinFvgAtr` and `RewardRisk`. In SessionRange mode spec 7 fixes the six
  range values; only `RangeMinWidthAtr` is tested two ways (0 and 0.5). Its
  file carries the values of the judged run and nothing else differs.
- Never copy the backtest-only values into a live file. `NewsEnabled`,
  `InitialCapital` and `DrawObjects` stay at their defaults (news on,
  100000 from spec v0.20, the FTMO Instant account, no drawings), and `RiskEpochDate` is the date step 10.3 names,
  not the backtest's start date. The parameter hash does not cover the news
  and risk values, so it cannot catch this mistake.
- Every other value stays at its default, except the values that step 10.3
  and "Gold (XAUUSD)" below tell you to set. The risk defaults of spec v0.12,
  v0.14 and v0.16 (owner decisions and requests of 2026-11-24 and
  2026-11-27): `InitialCapital=100000.0` (FTMO Instant from spec v0.20, see
  "FTMO Instant account" below),
  `DailyLossLimitPercent=2.0`, the monthly loss limit
  `MonthlyLossLimitPercent=2.0` (spec v0.19, row BF: once the account's closed
  result for the Prague-time calendar month reaches -2% of the initial capital,
  -2,000 USD at 100,000, no new entry until the next month and pending entry
  orders are cancelled; it counts every closed trade of the account, so both
  gold instances stop together; it never closes a trade; `0` switches it off),
  `MaxVolumeUnits=0.0` (automatic: 3,000,000
  units, gold 1,000 ounces), `RiskPercent=0.75` with `LadderEnabled=false`
  (a fixed 0.75% of balance per trade; `true` for the ladder, the one value
  you choose), the drawdown brake off, `DrawdownBrakePercent=0.0` (from
  spec v0.18, row BC; the start line ends with `brake=off`), with
  `DrawdownBrakeFactor=0.5` (with fixed sizing only and `DrawdownBrakePercent=3`:
  while the balance is 3% or more below its peak since `RiskEpochDate`, a new
  trade risks 0.375%; the full 0.75% comes back as soon as it is less than 3%
  below; the ladder and Recovery ignore it; the start line then ends with
  `brake=3/0.5`), the seven Overnight values after the range values
  (`NightEntryNy=18:15`, `NightLatestNy=19:00`, `NightExitNy=02:45`,
  `NightEntryBufferAtr=0.03`, `NightStopAtr=1.0`, `NightTargetR=5.0`,
  `NightOrderMinutes=60`; used in Overnight mode only, checked at start in
  every mode),
  `SizingPlan=Ladder`, `RecoveryRunLossPercent=1.0`,
  `RecoveryMaxRiskPercent=1.28`, and the per-trade sanity cap, the open-risk
  cap and the currency cap off (`SanityCapEnabled=false` with
  `SanityCapPercent=0.55`, `OpenRiskCapEnabled=false`,
  `CurrencyCapEnabled=false`). `SizingPlan=Recovery` is set only when the
  owner chooses that plan after its backtest
  ([`docs/backtest-howto.md`](../docs/backtest-howto.md), Part 7).

The laptop stage, the fault tests and the soak test run with these files
(spec 12.3 and 13.1), not with the example values.

**Check the hash.** The core `start` line shows `hash=` followed by eight
characters. It must equal, symbol by symbol, the `hash=` of the accepted
backtest run of that symbol (the same line in its Log). The hash covers the
signal and execution values of the files (from spec v0.14 an automatic
`MaxSpreadPips`, `MinStopPips` or `ChochMinStopPips` as its profile value,
for example `auto:0.4USD`, so gold's hash does not depend on the pip size
the broker reports) and, in Breakout mode, the breakout
values with the trading window (`TrendSlowEmaPeriod` and
`BreakoutLastConfirmUtc` only when they are not switched off); in
SessionRange mode the six range values and `RangeTrendFastEma` when it is
not 0. It does not cover the risk and
news values (`src/ZoneFvg.Core/Execution/ParameterHash.cs`). If a hash
differs, a value differs from the accepted run: stop that instance and
compare the files before it trades. For the example files as they are:

| Settings | Four forex files | Gold file |
|---|---|---|
| As the files are (SessionRange, Fvg, `RangeTrendFastEma=20`, all day, the four values automatic) | `3280d399` | `2dea1bc3` |
| The same with `RangeTrendFastEma=0` (the files of v0.16) | `f949f461` | `469b1f35` |
| `EntryMode=ZoneFvg`, `EntryModel=Choch`, all day, `ChochUseBias=true` (the files of v0.14 and v0.15) | `13502944` | `0e7d9dc3` |
| `EntryMode=Breakout`, `EntryModel=Fvg`, 07:00 to 17:00 | `b8e70ce0` | `faade1ee` |
| `EntryMode=SessionRange`, `EntryModel=Fvg`, `RangeTrendFastEma=20`, 07:00 to 17:00 | `afa64011` | `9078773f` |
| `EntryMode=SessionRange`, `EntryModel=Fvg`, `RangeTrendFastEma=0`, 07:00 to 17:00 | `f99c0872` | `57c90d56` |
| `EntryMode=ZoneFvg`, `EntryModel=Fvg`, 07:00 to 17:00 | `a1e63feb` | `8655594c` |

The gold hashes are the same at a pip of 1 and of 0.01. `RangeTrendFastEma=20`
(spec v0.17, WP42) changed the SessionRange hashes; with 0 they are those of
v0.16. The risk values,
the drawdown brake among them, are not in the hash, so the change of
`RiskPercent` and the two brake values changes no hash. The hashes changed
with spec v0.14 (WP39), because the values became automatic; the files of
v0.13 gave `8aa3f28a` / `fbdef461` (Breakout), `f681dff2` / `05ee8872`
(ZoneFvg) and `3f0f30a3` / `377b6162` (SessionRange), and those values set
by hand still give them. (Computed with the rule of `ParameterHash`:
SHA-256 of the canonical text, first eight hex characters, with Python's
hashlib. The first two rows are also asserted in
`tests/ZoneFvg.Core.Tests/App/Plt020SessionRangeDefaultsTests.cs`, the
second in `tests/ZoneFvg.Core.Tests/Execution/ParameterHashProfileTests.cs`.)

**Check after each start.** The first adapter line ends with the mode, for
example `ZFVG adapter version 0.1.0, symbol EURUSD, backtest no, mode Breakout`.
In Breakout mode the core `start` line also has `mode=Breakout`, in
SessionRange mode `mode=SessionRange`, in Overnight mode `mode=Overnight`;
in ZoneFvg mode it has no `mode=` field. With fixed sizing
(`SizingPlan=Ladder`, `LadderEnabled=false`) the line ends with `brake=off`
(the default from spec v0.18; `brake=3/0.5` with `DrawdownBrakePercent=3`),
after every other field.

**A change of mode is a parameter change.** Change `EntryMode` (or any other
parameter) only when the bot is flat on that symbol (spec 10.2,
"Deployments"). **Flat** means: no open position of the bot and no pending
order of the bot. Why:

- A position opened in the other mode (or under other values) is a
  stranger after the restart: it is kept with its existing stop loss and
  take profit, counted in the risk totals and the Friday close, and
  reported (spec 8.3, step 6).
- A pending order placed under the old values of the same mode is kept
  across the restart with its old levels and size (`docs/architecture.md`
  section 10, row "A pending order placed under other parameter values").
  After a change of the mode itself, the restart cancels a pending order of
  the other mode, because the new mode does not know its zone or breakout.
  In both cases the order is not what the new values would place. So
  before you change a parameter or the mode, wait until no bot order is
  pending (a ZoneFvg or Breakout entry order lives two hours at most:
  `EntryExpiryBars` 8 bars of 15 minutes; a SessionRange order lives until
  12:00 New York), or cancel it by hand in the cTrader app.
- The timed exit at `RangeExitNy` (16:30 New York) runs only in an instance
  in SessionRange mode. A SessionRange position (label with `RH-` or
  `RL-`) that is open when the instance starts in another mode keeps its
  stop loss, its take profit and the Friday close, but it is **not** closed
  at 16:30 New York. So do not change `EntryMode` while the instance has an
  order or a position.

**Before Breakout mode trades on the Challenge account.** Two records must
be run and signed by you on a demo or FTMO Free Trial account first
(`docs/architecture.md` section 10, rows "The cTrader stop-order call" and
"Daily and H4 bar boundaries on cTrader"):

1. The stop-order record
   [`docs/test-records/WP26-stop-orders.md`](../docs/test-records/WP26-stop-orders.md)
   (PLT-011): the bot's stop orders are placed, trigger, expire and are
   cancelled as expected.
2. The D1 boundary: step 3 of
   [`docs/test-records/WP08-adapter.md`](../docs/test-records/WP08-adapter.md),
   with the observed open hour of the D1 bars written in. Expected is 21:00
   UTC while New York is on daylight saving time and 22:00 UTC otherwise,
   five daily bars a week and no short Sunday bar. If the D1 bars open at
   00:00 UTC, or a short Sunday bar exists, write it down: Breakout mode
   then does not go live before a follow-up decides how Monday's reference
   day is formed. The entry-mode record
   [`docs/test-records/WP28-entry-mode.md`](../docs/test-records/WP28-entry-mode.md)
   asks for the same observation (its step 7). You sign this gate in the
   last row of the sign-off table of that record (steps 7 and WP08 step 3).

Until both are signed, no instance trades in Breakout mode on the Challenge
account.

**Before SessionRange trades on the Challenge account.** You chose
SessionRange on gold for use on 27 November 2026 (spec row AX, open question
4 answered). It goes live through the gates already written, not around
them. On a demo or FTMO Free Trial account, with the XAUUSD file, first:

1. The stop-order record
   [`docs/test-records/WP26-stop-orders.md`](../docs/test-records/WP26-stop-orders.md)
   (PLT-011), as for Breakout: SessionRange enters with stop orders too.
2. The gold spread: compare the live XAUUSD spread with the automatic
   limit of 0.40 USD (the `start` line shows it in pips, for example
   `maxSpreadPips=40` at a pip of 0.01). If the normal spread is above 0.40
   USD, every gold entry is blocked (`docs/architecture.md` section 10, row "Gold's stop and spread
   limits in the platform's pip").
3. The SessionRange record
   [`docs/test-records/WP35-session-range.md`](../docs/test-records/WP35-session-range.md).
4. **Proposal of the orchestrating session**, yours to accept or waive: two
   to four weeks on the Free Trial with these files before the first trade
   on the Challenge account.

Then the Challenge. When it is started stays your decision (spec 15.7).
The gold instance's `hash=` must equal that of the run you judged
(`2dea1bc3` with the example files as they are, all day; `9078773f` with
07:00 to 17:00; `469b1f35` and `57c90d56` with `RangeTrendFastEma=0`).

**Gold as two instances: SessionRange and Overnight (spec v0.18, rows AZ to
BE).** Gold runs as two instances on the same account and symbol: the
SessionRange instance with `deploy/params/xauusd.cbotset.example`
(`RiskPercent=0.75`) and the Overnight instance with
`deploy/params/xauusd-overnight.cbotset.example` (`EntryMode=Overnight`,
`RiskPercent=1.0`, its own `HeartbeatUrl`; every other line equals the
XAUUSD file). On the VPS the second instance is the container
`xauusd-overnight` with the folder `params/xauusd-overnight/` (step 10).
The Overnight instance places a buy stop at 18:15 New York on Monday to
Thursday evenings just above the price (the close of the 18:15 bar, plus the
spread, plus 0.03 x ATR(M15)) that lives 60 minutes, with a stop one daily
ATR below the entry and a safety target of 5R; an open position is closed at
02:45 New York. Its stop is not limited by `MaxStopAtr` x ATR(H4) (that
limit applies to the other modes only); the smallest stop (`MinStopPips`,
automatic 8 USD) applies. Its orders carry `ON-yyyyMMdd` in the label. Neither
instance cancels, closes, modifies or time-exits the other's order or
position (only the emergency guard closes every bot trade of the account),
and an instance does not enter while any bot order or position is open on
XAUUSD, so the two never hold a position together. **The account limits are
shared:** the daily budget (2%), the kill switch (8%), the monthly loss
limit and the FTMO guard count both instances' trades. The Overnight
instance's `hash=` is `826c617f` with the example file as it is (all day,
the four values automatic, at a pip of 1 and of 0.01). The drawdown brake is
off in all six files from v0.18 (`DrawdownBrakePercent=0.0`, row BC; the
`start` line ends with `brake=off`).

**Before the Overnight instance trades on the Challenge account (row BD,
the cost gate).** The effect of this mode depends on the cost: with a spread
of 0.50 USD the research total fell from +23R to +14R, with 0.80 USD to +1R.
On the Free Trial, with both gold files, fill in
[`docs/test-records/WP43-overnight-spread.md`](../docs/test-records/WP43-overnight-spread.md)
for at least ten Overnight evaluations: the spread of the journal line and
whether the entry was taken or skipped. Entries are made only while the
spread is within `MaxSpreadPips` (automatic: 0.40 USD). If most entries are
skipped for spread, or the taken ones paid more than about 0.40 USD on
average, the Overnight instance is not started on the Challenge. The
stop-order record and the other gates above apply to it too.

---

## Part A. Stage 1: your laptop (spec 10.5)

Goal: a few days on an FTMO Free Trial with cTrader desktop on your Mac,
before you pay for a server.

**Mac notes (the owner uses a Mac):**
- Use the native **cTrader for Mac** app. It runs cBots built for .NET 6, which
  is what CI produces (the `.algo` from the `ZoneFvgBot-algo` artifact). Sources:
  https://community.ctrader.com/forum/announcements/42478/ and
  https://community.ctrader.com/forum/ctrader-mac/43106/.
- Import the bot: download and unzip the artifact, rename `src.algo` to
  `ZoneFvgBot.algo`, then double-click it (or drag it onto the cTrader window,
  or use Algo > cBots > import). If cTrader asks to install the .NET SDK to
  build algos, allow it.
- Keep the Mac awake for the whole test: System Settings > Battery (or Energy)
  > prevent automatic sleeping when the display is off, keep it on power, and
  leave the lid open (a closed lid sleeps a Mac unless an external display is
  attached). As a backup, run `caffeinate -dimsu` in Terminal and leave that
  window open; it keeps the Mac awake until you press Ctrl+C. Source:
  https://ss64.com/mac/caffeinate.html (Apple's `man caffeinate`).
- Pause macOS automatic updates for the test days: System Settings > General
  > Software Update > Automatic updates, turn off "Install macOS updates".

1. Open an FTMO Free Trial on **cTrader** (spec 15.6: confirm a trial can be
   opened on cTrader; if only MetaTrader is offered, use another cTrader demo
   account and tell the developer).
2. Laptop settings for the test period: never sleep or hibernate, on battery or
   on power; closing the lid does nothing; keep it plugged in; pause automatic
   update restarts (spec 10.5).
3. Install the bot and check one instance exactly as in
   [`docs/test-records/WP08-adapter.md`](../docs/test-records/WP08-adapter.md),
   "Preparation" and steps 1 to 5. Fill in that record. Then set the EURUSD
   instance to the mode and the accepted settings of your EURUSD file
   (section "Entry mode", "Which mode and which values go live"); the
   preparation's "leave everything else at its default" is for the record
   only. Check that the `hash=` of the `start` line equals the accepted run's.
4. Add four more instances on **M15** charts of GBPUSD, USDJPY, AUDUSD and
   XAUUSD (gold), each with the values of its own file (only `HeartbeatUrl`
   differs among the forex files, one monitor per symbol). From spec v0.14
   the gold file differs only in its heartbeat (see "Gold (XAUUSD)" below).
   All five instances use the chosen `EntryMode`, and each `hash=` equals
   the accepted run of its symbol (see "Entry mode" above).
5. Optional, one instance only: right-click it and choose **Start in external
   process**, then close the cTrader window; the cBot keeps running in a CLI
   process (source: https://help.ctrader.com/ctrader-cli/cbots/, "Launch from
   the Desktop Application"). This is the first real test of fact 2 above. If
   the log says the cBot needs a newer .NET, stop it, run it inside the app as
   in step 3, and tell the developer.
6. Run the laptop variant of the fault tests:
   [`docs/test-records/WP11-fault-injection.md`](../docs/test-records/WP11-fault-injection.md),
   column "Laptop". Test 2 cannot be done on a laptop; test 10 needs a Friday
   evening.
7. Move to the VPS when every applicable test passed and there was no position
   without a stop loss, no duplicate order and no recovery that needed your help
   (spec 10.5).

---

## Part B. Stage 2: the VPS (spec 10.2)

### 1. Rent the server

- A small Linux VPS in a commercial data centre **outside the United States**
  (FTMO does not accept a US location, spec 15.2).
- Operating system: **Ubuntu 24.04 LTS or 26.04 LTS, 64-bit** (both supported by
  Docker, see the Docker install page).
- Size: see "Decisions for the human" in the pull request; recommended start is
  2 vCPU, 4 GB RAM, 40 GB disk. Step 12.6 measures real use.
- If the provider lets you add an **SSH public key** when ordering, do step 3.1
  first and paste the key there. Then you never need a root password.

### 2. First login and system update

From the laptop. On a Mac, open **Terminal** (Applications > Utilities); `ssh`,
`scp` and `ssh-keygen` are built in. (Windows 10/11 also has an OpenSSH client
under Settings > Optional features > OpenSSH Client. Source:
https://learn.microsoft.com/windows-server/administration/openssh/openssh_install_firstuse):

```bash
ssh root@SERVER_IP
```

Source: same Microsoft page ("ssh user@host" form). Answer `yes` to the
host-key question the first time only.

```bash
apt update
apt upgrade
```

Source: https://ubuntu.com/blog/ubuntu-updates-best-practices-for-updating-your-instance

### 3. Your own user with SSH keys only

3.1 On the laptop, create a key pair (once; press Enter for the default file and
choose a passphrase):

```bash
ssh-keygen -t ed25519
```

Source: https://ubuntu.com/server/docs/how-to/security/openssh-server/

3.2 On the server (as root), create your user and give it administrator rights.
Use a user name of your choice instead of `zfvg`:

```bash
adduser zfvg
adduser zfvg sudo
```

Source: https://ubuntu.com/server/docs/how-to/security/user-management/

3.3 Put your public key on the server for that user. On a Mac, the simplest
way is one command in Terminal (it asks for the new user's password once):

```bash
ssh-copy-id zfvg@SERVER_IP
```

Then skip to 3.4. Otherwise (Windows), open the file
`C:\Users\YOU\.ssh\id_ed25519.pub` in Notepad (on a Mac:
`cat ~/.ssh/id_ed25519.pub` in Terminal) and copy its one line. On the server:

```bash
su - zfvg
mkdir -p ~/.ssh
nano ~/.ssh/authorized_keys
```

Paste the line, save with Ctrl+O, Enter, exit with Ctrl+X. Then:

```bash
chmod 700 ~/.ssh
chmod go-w ~/.ssh/authorized_keys
exit
```

Source: https://ubuntu.com/server/docs/how-to/security/openssh-server/ (the
`authorized_keys` file and `chmod go-w`). (On Linux or macOS you can use
`ssh-copy-id zfvg@SERVER_IP` instead, same page.)

3.4 **Open a second terminal** on the laptop and check that the key works. Keep
the first window open until step 3.6 is done.

```bash
ssh zfvg@SERVER_IP
sudo whoami
```

Expected: you log in without the account password (only the key passphrase) and
`sudo whoami` prints `root`.

3.5 Switch password login and root login off. As `zfvg`, create a small settings
file. Its name starts with `01-` so that it is read before any provider file in
the same folder (sshd uses the first value it finds; the main file includes
`/etc/ssh/sshd_config.d/*.conf` at the top):

```bash
sudo nano /etc/ssh/sshd_config.d/01-zfvg.conf
```

Content:

```
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
```

Sources: https://ubuntu.com/server/docs/how-to/security/openssh-server/
(drop-in folder, first value wins) and
https://man7.org/linux/man-pages/man5/sshd_config.5.html (the three settings
and their values).

3.6 Test the settings, then restart SSH:

```bash
sudo sshd -t
sudo systemctl restart ssh.service
```

Source: https://ubuntu.com/server/docs/how-to/security/openssh-server/
(`sshd -t` prints nothing when the file is valid).

From a **third** terminal, `ssh zfvg@SERVER_IP` must still work, and
`ssh root@SERVER_IP` must be refused. Only then close the first window.

3.7 Lock the root password:

```bash
sudo passwd -l root
```

Source: https://ubuntu.com/server/docs/how-to/security/user-management/

### 4. Firewall

Allow SSH first, then switch the firewall on. Nothing else is opened: the bot
only makes outgoing connections.

```bash
sudo ufw allow ssh
sudo ufw enable
sudo ufw status verbose
```

Sources: https://help.ubuntu.com/community/UFW (`allow ssh`, default deny
incoming) and https://ubuntu.com/server/docs/how-to/security/firewalls/
(`enable`, `status verbose`).

Expected: `Status: active`, `Default: deny (incoming), allow (outgoing)`, and a
rule allowing 22/tcp.

Note: ports published by Docker bypass ufw (Docker install page, firewall
note). The compose file publishes no port, so this does not apply. Never add a
`ports:` line to it.

### 5. Automatic security updates

```bash
sudo apt install unattended-upgrades
sudo nano /etc/apt/apt.conf.d/20auto-upgrades
```

The file must contain:

```
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
```

Source: https://ubuntu.com/server/docs/how-to/software/automatic-updates/
(results are logged in `/var/log/unattended-upgrades/`). Automatic reboot is
off by default (`Unattended-Upgrade::Automatic-Reboot` is `false`); keep it off.
You reboot by hand at the weekend when the runbook's weekly check says a reboot
is needed (see "Decisions for the human").

### 6. Time synchronisation

The bot uses broker server time, never the machine clock (spec 8.4), but a
correct clock keeps logs readable and TLS working.

```bash
timedatectl status
```

Source: https://ubuntu.com/server/docs/how-to/networking/timedatectl-and-timesyncd/

Expected: `System clock synchronized: yes` and `NTP service: active`. Ubuntu
25.10 and later (so 26.04) use chrony by default; there you can also run:

```bash
chronyc -N tracking
```

Source: https://ubuntu.com/server/docs/how-to/networking/chrony-client/

### 7. Docker

Follow "Install using the apt repository" on
https://docs.docker.com/engine/install/ubuntu/ exactly as written there
(remove conflicting packages, add Docker's key and `docker.sources`, then):

```bash
sudo apt install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo docker run hello-world
```

Source: https://docs.docker.com/engine/install/ubuntu/

Let your user run Docker without `sudo` (the `docker` group gives
administrator-level rights, so only your own user gets it), then log out and in
again:

```bash
sudo usermod -aG docker $USER
```

Source: https://docs.docker.com/engine/install/linux-postinstall/

**To check on first start:** Docker itself starts at boot.

```bash
systemctl is-enabled docker
```

Expected: `enabled`. If not, see "Configure Docker to start on boot" on the
post-installation page. Fault test 2 proves it.

### 8. Directory layout

Everything lives in `/opt/zfvg`, outside any git checkout:

```
/opt/zfvg/
  docker-compose.yml         copy of deploy/docker-compose.yml
  .env                       from deploy/.env.example        (chmod 600)
  secrets/ctrader-cli.pwd    cTrader ID password, one line   (chmod 600)
  algo/current/ZoneFvgBot.algo   the version that runs
  algo/previous/ZoneFvgBot.algo  the version before, for roll-back
  params/eurusd/bot.cbotset  from deploy/params/eurusd.cbotset.example (chmod 600)
  params/gbpusd/bot.cbotset
  params/usdjpy/bot.cbotset
  params/audusd/bot.cbotset
  params/xauusd/bot.cbotset  from deploy/params/xauusd.cbotset.example (gold)
  params/xauusd-overnight/bot.cbotset  from deploy/params/xauusd-overnight.cbotset.example (gold, Overnight)
  log-archive/               copies of container logs saved before updates
```

```bash
sudo mkdir -p /opt/zfvg
sudo chown $USER:$USER /opt/zfvg
cd /opt/zfvg
mkdir -p secrets algo/current algo/previous log-archive params/eurusd params/gbpusd params/usdjpy params/audusd params/xauusd params/xauusd-overnight
chmod 700 secrets params
```

Source: standard tools (`mkdir`, `chown`, `chmod`), see "Documentation used".
The parameter folders are mounted one per
container, read-only, so each container sees only its own `bot.cbotset`.

### 9. Copy the files from the laptop

On the laptop, in a folder that holds the repository files and the `.algo`
from the CI artifact `ZoneFvgBot-algo` of the commit you want to run (WP08
record, "Preparation", step 1):

```bash
scp deploy/docker-compose.yml deploy/.env.example zfvg@SERVER_IP:/opt/zfvg/
scp deploy/params/eurusd.cbotset.example zfvg@SERVER_IP:/opt/zfvg/params/eurusd/bot.cbotset
scp deploy/params/gbpusd.cbotset.example zfvg@SERVER_IP:/opt/zfvg/params/gbpusd/bot.cbotset
scp deploy/params/usdjpy.cbotset.example zfvg@SERVER_IP:/opt/zfvg/params/usdjpy/bot.cbotset
scp deploy/params/audusd.cbotset.example zfvg@SERVER_IP:/opt/zfvg/params/audusd/bot.cbotset
scp deploy/params/xauusd.cbotset.example zfvg@SERVER_IP:/opt/zfvg/params/xauusd/bot.cbotset
scp deploy/params/xauusd-overnight.cbotset.example zfvg@SERVER_IP:/opt/zfvg/params/xauusd-overnight/bot.cbotset
scp ZoneFvgBot.algo zfvg@SERVER_IP:/opt/zfvg/algo/current/ZoneFvgBot.algo
```

Source: https://man.openbsd.org/scp (`scp source ... [user@]host:[path]`).

Write down the commit of the `.algo` in the test record.

### 10. Secrets and parameters (on the server)

10.1 Account file:

```bash
cd /opt/zfvg
cp .env.example .env
chmod 600 .env
nano .env
```

Replace `your-ctrader-id` and `your-account-number`. Start with the **FTMO Free
Trial** account, never the Challenge account (spec 15.7).

10.2 Password file. Type the command below with a space in front of it, so the
shell does not keep it in its history, and put your password between the
quotes:

```bash
 printf '%s' 'YOUR-CTRADER-ID-PASSWORD' > /opt/zfvg/secrets/ctrader-cli.pwd
chmod 600 /opt/zfvg/secrets/ctrader-cli.pwd
```

Source: image README, "Quickstart" step 1 (one line, no trailing spaces,
`chmod 600`); https://help.ctrader.com/ctrader-cli/setup/ (keep it out of
version control and restrict read access). Spec 10.2: readable only by the
service user.

10.3 Parameter files. In each `params/<symbol>/bot.cbotset`, replace every
`CHANGE_ME...` value:

| Parameter | What to put |
|---|---|
| `RiskEpochDate` | The first day of this account, `yyyy-MM-dd`. It means 00:00 Prague time that day (architecture section 10). Same value in every file. On the FTMO Instant account it stays that first day, also after a reward. Runbook: "Changing RiskEpochDate" |
| `NewsFeedUrl` | The Forex Factory weekly calendar export address (JSON) you used on the laptop. Same in all five files |
| `TelegramBotToken`, `TelegramChatId` | From your Telegram bot (spec 11). Same in all five files |
| `HeartbeatUrl` | One check per symbol at your heartbeat-monitoring service, so an alert names the symbol that stopped |
| `ProfitTargetPercent` | 10 in the Challenge, 5 in the Verification |

The eleven values of the group "Entry mode" and the accepted settings are
those of section "Entry mode", "Which mode and which values go live": the
chosen mode, never simply the default. Leave every other value at the spec
default. `MaxSpreadPips`, `MinStopPips`, `ChochMinStopPips` and
`MaxVolumeUnits` stay `0.0` (automatic, spec 7.1) in all five files until the
broker conditions (spec 10.4) say otherwise. A value
left as `CHANGE_ME...` makes the bot refuse to start (`RiskEpochDate`) or fail
safely (no calendar means no entries; failed alerts are logged).

```bash
chmod 600 /opt/zfvg/params/*/bot.cbotset
```

### FTMO Instant account (spec 15.8)

From spec v0.20 (owner decision of 27 November 2026) the files are set for
an FTMO Instant account of 100,000 USD. Every file under `deploy/params/`
carries these lines; leave them as they are:

| Line | Meaning |
|---|---|
| `InitialCapital=100000.0` | The account size; every percent below is a percent of it |
| `FtmoDailyLossPercent=3.0` | FTMO Instant's daily limit: equity must stay above the 00:00 Prague balance minus 3,000 |
| `FtmoMaxLossPercent=10.0` | FTMO Instant's maximum loss: equity must stay above P minus 10,000 |
| `FtmoMaxLossMode=TrailingBalance` | P is the highest closed-trade balance since `RiskEpochDate`, at least 100,000; it rises, never falls and is not reset by a reward. `Static` is the 2-Step rule (a fixed floor from `InitialCapital`) |
| `FtmoGuardBufferPercent=0.5` | The emergency guard closes everything and halts 500 USD before either floor: at the 00:00 balance minus 2,500, or at P minus 9,500. If P cannot be read it uses the static floor (90,500), which is never looser |
| `ConsistencyMaxPercent=30.0` | The daily `consistency` journal line: is the best Prague day at most 30% of the profit since the last reward? Information only. `0` switches the line off |
| `RewardPeriodStartDate=` | Empty: the line counts from `RiskEpochDate`. After each reward set it to the reward's date, `yyyy-MM-dd` |

The daily budget (2%, 2,000 USD), the kill switch (8% below P) and the
monthly loss limit (2%, 2,000 USD) are unchanged in percent.

- `RiskEpochDate` stays the account's first day; do not move it after a
  reward, because FTMO's floor does not reset either.
- A reward (withdrawal) lowers the balance but not P. Withdraw only so much
  that the balance stays at least about 5% above P x 0.92, the kill switch
  line.
- After each reward, set `RewardPeriodStartDate` to that date in every file,
  only while the bot is flat (no open position and no pending order of the
  bot): stop, edit, start, as for `RiskEpochDate` (runbook section 5). How to
  read the line: runbook section 9.

### Gold (XAUUSD)

Gold runs as a fifth instance (spec v0.9, owner decision D of 2026-11-17,
requirement PLT-010): its own container `xauusd`, parameter file
`params/xauusd/bot.cbotset`, storage and log volumes and heartbeat check.
From spec v0.14 its parameter file equals the forex files apart from
`HeartbeatUrl` (its own check, so an alert names gold). From spec v0.16 it
is the one file that is started: SessionRange, a fixed 0.75% and the
drawdown brake. `MaxSpreadPips`,
`MinStopPips`, `ChochMinStopPips` and `MaxVolumeUnits` are `0.0`, automatic
(spec 7.1): at start the bot sees that XAUUSD is gold (base XAU, quote USD)
and uses 0.40 USD of spread, 8.00 USD of stop, 3.00 USD of Choch stop and
1,000 ounces, converted with the pip size the broker reports. Two of your
backtests reported a gold pip of 1 USD and of 0.01 USD; both give the same
limits in price. The `start` line shows the profile, the pip size and the
values (`profile=Gold pipSize=... maxSpreadPips=...`). A value you type
yourself is checked: outside one tenth to ten times the automatic value in
price (for example the old 0.4 at a pip of 0.01, which is 0.004 USD) the bot
refuses to start and its Log line names the parameter, the pip size and the
automatic value. (Until spec v0.13 the gold file set `MaxSpreadPips=0.4`,
`MinStopPips=8.0` and `MaxVolumeUnits=1000.0` for a pip of 1 USD.)

If the accepted settings differ between symbols (section "Entry mode"),
each file carries those of its own symbol as well.

No other parameter is in pips; the ATR and R multiples scale by themselves.
From spec v0.14 the cBot's defaults are automatic, so a gold instance started
without its parameter file also uses gold's limits; still load
`xauusd.cbotset` for the heartbeat and the other live values.

Before the gold instance trades on the demo account, open the XAUUSD symbol
information in cTrader and compare it with the adapter's start line
(`symbol XAUUSD ... pipSize ... digits ... tickSize ... lotSize ...
commission ... volumeMin ... step ... max ... sessionsAsReported ...`):
digits, tick size, pip size (1, that is 1 USD, is assumed by the values above; if it shows
0.01, the gold values are 100 times too small: stop and tell the developer), lot size
(ounces per lot), commission and its type, volume minimum and step, and the
trading sessions with the daily break. Write them into
`docs/test-records/WP11-fault-injection.md` with the PLT-010 check.

### 11. Check the compose file

```bash
cd /opt/zfvg
docker compose config --quiet
```

Source: https://docs.docker.com/reference/cli/docker/compose/config/
Expected: no output. An error names the line to fix.

### 12. First start (FTMO Free Trial only)

Do this on a weekday during market hours. Fill in "First-start checks" in the
fault-injection record as you go.

12.1 Pull the pinned image and write down its digest:

```bash
docker compose pull
docker image inspect --format '{{index .RepoDigests 0}}' ghcr.io/spotware/ctrader-console:5.9.11
```

Sources: https://docs.docker.com/reference/cli/docker/compose/pull/ ;
https://docs.docker.com/reference/cli/docker/image/inspect/ ; image README
("Pin a version tag in production, or a digest").

12.2 The CLI can read the `.algo` (no account needed). This is the .NET check:

```bash
docker compose run --rm --no-deps eurusd metadata /mnt/algo/ZoneFvgBot.algo
```

Sources: https://docs.docker.com/reference/cli/docker/compose/run/ (the words
after the service name replace its command); `metadata`:
https://help.ctrader.com/ctrader-cli/cbots/ ("Read cBot Metadata").
Expected: the parameter list of ZoneFvgBot (EntryMode, BreakoutEntryBufferAtr,
BreakoutStopAtr, TrendSlowEmaPeriod, BreakoutLastConfirmUtc, RangeStartNy,
RangeEndNy, RangeLastEntryNy, RangeExitNy, RangeMinWidthAtr, RangeStopAtr,
RangeTrendFastEma, BiasTimeFrame ... DrawObjects).
If it says the cBot is built for an older .NET: stop here and tell the
developer.

12.3 The account and the symbol names:

```bash
docker compose run --rm --no-deps eurusd accounts --environment-variables
docker compose run --rm --no-deps eurusd symbols --environment-variables
```

Sources: image README, "What each command accepts" (`accounts`/`symbols` read
`CTID`, `PWD_FILE`, `ACCOUNT` from the environment); command reference
`accounts`, `symbols`. Expected: your trial account is listed; the symbol list
contains `EURUSD`, `GBPUSD`, `USDJPY`, `AUDUSD` and `XAUUSD` spelled exactly like that.
If FTMO uses other names (for example with a suffix), change `SYMBOL` in the
compose file and tell the developer.

12.4 Start **one** instance and watch it:

```bash
docker compose up -d eurusd
docker compose logs -f eurusd
```

Sources: https://docs.docker.com/reference/cli/docker/compose/up/ ;
https://docs.docker.com/reference/cli/docker/compose/logs/
Press Ctrl+C to stop watching (the bot keeps running). Expected, as in WP08
record step 1: `ZFVG adapter version ...`, `symbol EURUSD`, `backtest no`,
`mode Breakout` (or `mode ZoneFvg` or `mode SessionRange`): the chosen mode of section "Entry
mode"; a core `start` line whose `hash=` equals the accepted run of that
symbol; a recovery report; state Running (or Blocked with a reason);
a Telegram "Started" message; no token or address visible in the log.

12.5 Local storage location and persistence (fact 6):

```bash
sudo find "$(docker volume inspect --format '{{ .Mountpoint }}' zfvg_eurusd-data)" -type d -name LocalStorage
```

Sources: https://docs.docker.com/reference/cli/docker/volume/inspect/ ; folder
name from https://help.ctrader.com/ctrader-algo/guides/local-storage/
Expected: one `.../cAlgo/LocalStorage` folder. Under it,
`cBots/ZoneFvgBot/<id>/`. Write down `<id>`. Then:

```bash
docker compose restart eurusd
```

Source: https://docs.docker.com/reference/cli/docker/compose/restart/
Wait two minutes and list `cBots/ZoneFvgBot/` again. Expected: the **same**
`<id>`, no new one; the second start's log shows the calendar taken from the
cache, without a new download within the hour (spec 6.2). If a new `<id>`
appears at every start, stop and tell the developer (fact 6).

12.6 Start the other three and measure resources:

```bash
docker compose up -d
docker compose ps
docker stats --no-stream
```

Sources: compose `up`, `ps` (https://docs.docker.com/reference/cli/docker/compose/ps/);
https://docs.docker.com/reference/cli/docker/container/stats/
Expected: five containers `zfvg-eurusd-1`, `zfvg-gbpusd-1`, `zfvg-usdjpy-1`,
`zfvg-audusd-1`, `zfvg-xauusd-1`, status `Up`. Write down memory use per container.

12.7 Run the fault-injection tests (record, VPS column), then the four-week soak
test (spec 12.3). Only after both, think about the Challenge (spec 15.6).

### 13. Everyday commands

| Task | Command | Source |
|---|---|---|
| Status of the five containers | `docker compose ps` | compose `ps` |
| Last 200 log lines of one symbol | `docker compose logs --tail 200 eurusd` | compose `logs` |
| Log lines of the last 24 hours with times | `docker compose logs --since 24h --timestamps eurusd` | compose `logs` |
| Stop one symbol (no new trades; stops stay at the broker) | `docker compose stop eurusd` | https://docs.docker.com/reference/cli/docker/compose/stop/ |
| Start it again | `docker compose start eurusd` | https://docs.docker.com/reference/cli/docker/compose/start/ |
| Restart after editing its `bot.cbotset` (only when flat, section "Entry mode") | `docker compose restart eurusd` | compose `restart` |
| Disk use | `df -h /` and `docker system df` | standard tool; https://docs.docker.com/reference/cli/docker/system/df/ |

Run every `docker compose` command from `/opt/zfvg` (`cd /opt/zfvg` first).

### 14. Save the logs before re-creating containers

`docker compose logs` keeps working across restarts and reboots, but the log
of a container is deleted when the container is re-created (an update, a
roll-back, `docker compose down`). Save it first:

```bash
cd /opt/zfvg
docker compose logs --no-color --timestamps > log-archive/all-$(date -u +%Y%m%dT%H%MZ).log
```

Source: compose `logs`. The per-symbol `*-logs` volumes (mounted at `/logs` in
each container) are reserved for log files the CLI may write itself; whether it
does is noted at first start. They are kept across re-creation.

### 15. Update the bot to a new version (spec 10.2)

A new `.algo` is deployed **only when the bot is flat** (no bot position and no
pending bot order on any symbol), **or** after the adoption test for exactly
this pair of versions (old to new) has passed on a trial account (fault test 11
procedure, "Adoption test" in the record).

1. Read the release notes in the pull request. If they change a parameter, edit
   the five `bot.cbotset` files in step 6, not before.
2. Prefer Saturday. Check in the cTrader app that there is no bot position and
   no bot pending order, or that the adoption test for this version pair is
   signed.
3. Save the logs (section 14).
4. Keep the running version for roll-back:

   ```bash
   cd /opt/zfvg
   cp algo/current/ZoneFvgBot.algo algo/previous/ZoneFvgBot.algo
   ```

5. Copy the new `.algo` from the laptop to `algo/current/ZoneFvgBot.algo`
   (step 9).
6. Re-create the containers so they load the new file:

   ```bash
   docker compose up -d --force-recreate
   docker compose logs -f
   ```

   Source: compose `up` (`--force-recreate`).
7. Expected: five start lines with the **new** version, five recovery reports,
   five Telegram "Started" messages. If anything is unexpected, roll back
   (section 16).

### 16. Roll back

1. Save the logs (section 14).
2. Put the previous version back and re-create:

   ```bash
   cd /opt/zfvg
   cp algo/previous/ZoneFvgBot.algo algo/current/ZoneFvgBot.algo
   docker compose up -d --force-recreate
   ```

3. Check the start lines show the old version. A position opened by the newer
   version is adopted only if the older version can read it; if the recovery
   report lists it as a stranger, it keeps its stop and target at the broker
   (spec 8.3, step 6) and is reported. If unsure, close it in the cTrader app.

### 17. Change the cTrader CLI image version

Spec 8.6 and 10.2: only after all fault tests have passed on demo with the new
version.

1. On a **trial account** server (or with the five containers pointing at a
   trial account), change the tag in `image:` of `docker-compose.yml`, never to
   `latest`.
2. `docker compose pull`, save the logs, `docker compose up -d --force-recreate`.
3. Run the fault-injection record again with the new version.
4. Only then change the live server the same way. To roll back, put the old tag
   back and repeat step 2.

### 18. Reboot the server

Only at the weekend when the bot is flat, or as fault test 2:

```bash
pro system reboot-required
sudo reboot
```

Source: https://ubuntu.com/blog/ubuntu-updates-best-practices-for-updating-your-instance
(`pro system reboot-required`); `reboot`: https://man7.org/linux/man-pages/man8/reboot.8.html.
After the reboot, `docker compose ps` must show
the five containers `Up` without any action from you.
