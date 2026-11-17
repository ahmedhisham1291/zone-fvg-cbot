# WP24: Gold (XAUUSD) as a fifth instance

## Read first

`AGENTS.md`; `docs/spec.md` section 0 (changes in v0.9, decision D),
sections 2, 4.3, 5.1, 6.1, 7, 8.3 and 13.1; `docs/architecture.md`
section 10 (the rows of 2026-11-17 on gold and on contract additions); the
requirement texts below; `deploy/README.md`, `deploy/docker-compose.yml`
and `deploy/params/eurusd.cbotset.example`. Depends on WP22 (EXE-017: prices
on the symbol's tick grid; gold quotes 2 digits) and WP23 (REC-003: a gap
left in the history, for example at a daylight-saving change of gold's
daily break, is accepted).

## Requirements to implement

REC-016, NWS-013, RSK-015, PLT-010, SIM-005 (changed threshold).

Owner decision D of 2026-11-17. The gold parameter values are fixed by the
owner: MaxSpreadPips 40, MinStopPips 800, MaxVolumeUnits 200. No other
parameter is in pips. Do not change any default of the cBot: the defaults
stay the forex values, and gold gets its values from its parameter file.

## Allowed paths

- `src/ZoneFvg.Core/Execution/Ports/SymbolInfo.cs` (one init-only member,
  `Sessions`) and a new `src/ZoneFvg.Core/Execution/Ports/TradingSession.cs`
- `src/ZoneFvg.Core/Recovery/BarSeriesCheck.cs`, `RecoveryService.cs`
- `src/ZoneFvg.Core/App/BotCore.cs` (pass the sessions to the live gap check
  and to recovery; start log line)
- `src/ZoneFvg.Core/Simulation/AcceptanceReport.cs`
- `src/ZoneFvg.Bot/CTraderBroker.cs` (sessions in `GetSymbol` and in
  `DescribeCosts`)
- `deploy/**` (gold parameter file, a fifth container, README)
- `docs/backtest-howto.md` (only to replace "after WP24" notes by the
  final wording)
- `tests/ZoneFvg.Core.Tests/**`
- the `status:` lines of the five requirements above

`News/**` and `Risk/**` are expected to need no change: NWS-013 and RSK-015
describe what the code already does with base XAU and quote USD, and their
tests prove it. If a test shows a defect there, stop and ask.

## Owner authorisation to change existing tests

None. With four symbols the acceptance report still shows "3 of 4", so the
existing `AcceptanceReportTests` stay as they are; with the session list
empty or covering the whole week, every existing REC-003 test stays as it
is. If an existing test fails, stop and ask.

## What to build, in this order

1. **REC-016, sessions.** `TradingSession(DayOfWeek StartDay, TimeSpan
   StartTime, DayOfWeek EndDay, TimeSpan EndTime)`, UTC. `SymbolInfo` gains
   `IReadOnlyList<TradingSession> Sessions` (init-only, default empty). The
   open-market time of `BarSeriesCheck` counts a moment as open only when
   the existing weekend and holiday model says open and, if the list is not
   empty, the moment lies inside one of the sessions (start inclusive, end
   exclusive; a session may run across midnight and across the week's end).
   Recovery and `CheckLiveGap` read the sessions from `GetSymbol` at the
   moment of the check; if `GetSymbol` fails, the check uses the model
   without sessions (more gaps, never fewer). The start line of the core
   writes `sessions=` with the list, or `sessions=none reported`.
   Adapter: map `Symbol.MarketHours.Sessions`. Confirm on the cTrader
   reference page of `MarketHours` / `TradingSession` that the times are
   UTC and cite the page in a comment. If the reference does not say, stop
   and ask; do not guess a time zone.
2. **SIM-005.** The profitable-symbols threshold becomes max(3, N - 1),
   N the distinct symbols of the development logs, used for both segments;
   the threshold text reads "<threshold> of <N>" ("3 of 4", "4 of 5").
3. **NWS-013 and RSK-015**: tests only, unless they fail (then stop).
4. **PLT-010.** `deploy/params/xauusd.cbotset.example`: a copy of the
   EURUSD file with `MaxSpreadPips=40.0`, `MinStopPips=800.0`,
   `MaxVolumeUnits=200.0` and `HeartbeatUrl=CHANGE_ME_heartbeat_address_for_xauusd`;
   a `xauusd` service, volumes and `SYMBOL: XAUUSD` in
   `deploy/docker-compose.yml` in the same style as the others; the README
   lists five instances and the gold values.

## Tests required

Gold test symbol: name XAUUSD, base XAU, quote USD, pip 0.01, Digits 2, tick
0.01, sessions Sunday 22:00 to Monday 21:00, Monday 22:00 to Tuesday 21:00,
Tuesday 22:00 to Wednesday 21:00, Wednesday 22:00 to Thursday 21:00,
Thursday 22:00 to Friday 21:00 (a one-hour daily break). 2026-03-03 is a
Tuesday.

**REC-016**
- M15 bars Tuesday 20:45 then Tuesday 22:00: not a gap with the gold
  sessions; a gap without sessions. Bars 20:30 then 22:00: a gap with the
  sessions too (the 20:45 bar is missing inside a session).
- Recency: last bar opened 20:45 (closed 21:00). At 21:50: recent. At
  22:29:59: recent. At 22:30: not recent (30 minutes of open session time).
- A forex symbol with sessions Sunday 22:00 to Friday 22:00 (no break):
  results identical to no sessions for every existing REC-003 case (run the
  existing inputs through both and compare).
- Sessions never open time: with a session over Saturday, Saturday is
  still closed.
- Recovery on a full week of gold M15, H4 and D1 history built with the
  break: `Running` with no accepted gap. Live: a week of gold M15 bars fed
  one by one raises no `DataGap`.
- `GetSymbol` throws during the live check: the check runs without
  sessions (the 20:45 / 22:00 pair is then a gap) and the exception is
  journaled.

**NWS-013** (XAUUSD; NewsBeforeMin and NewsAfterMin 30)
- High-impact USD at 2026-03-04 13:30: entries blocked from 13:00 to 14:00,
  with the same boundary rules as the existing NWS-001 tests.
- High-impact EUR at 10:00: not blocked at 10:00.
- An "All" event at 15:00: blocked.
- ExtraBlackouts `2026-03-04 16:00 XAU`: blocked from 15:30 to 16:30.
- Embedded history line for USD in a backtest: blocked.

**RSK-015** (MaxPositionsPerCurrency 2)
- Open bot buys on EURUSD and GBPUSD (two short USD): a new XAUUSD buy
  (short USD) is refused for the USD cap; a new XAUUSD sell (long USD) is
  allowed.
- Open bot buys on XAUUSD and EURUSD: a new GBPUSD buy is refused for USD.
- Open bot buy on XAUUSD and sell on EURUSD (long USD): a new GBPUSD buy
  is allowed (short USD count 2 including it).

**SIM-005**
- Five symbols, four profitable: Pass, value "4 of 5", threshold "4 of 5".
- Five symbols, three profitable: Fail, threshold "4 of 5".
- Four symbols, three profitable: Pass, "3 of 4" (as today).
- Three symbols, three profitable: threshold max(3, 2) = 3, Pass "3 of 3".
- Development with five symbols, holdout log with four symbols all
  profitable: holdout threshold 4, Pass.

**PLT-010**: manual. The owner checks the parameter file and the compose
file against the requirement and records it in
`docs/test-records/WP11-fault-injection.md` when the gold instance first
runs on the demo account.

## Definition of done

- [ ] All tests pass in CI; coverage gates hold; every command of
      `AGENTS.md` run and pasted.
- [ ] REC-016, NWS-013, RSK-015 and SIM-005 are `implemented` (only if
      "Decisions for the human" is empty); PLT-010 stays `planned` for the
      owner's manual check.
- [ ] The pull request states the XAUUSD symbol data the adapter printed in
      a backtest start line (digits, tick size, pip size, sessions, lot
      size, commission), or says that no backtest was run.
