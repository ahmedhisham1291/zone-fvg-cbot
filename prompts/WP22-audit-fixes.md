# WP22: Audit fixes without a rule change

## Read first

`AGENTS.md`; `docs/architecture.md` sections 3, 5, 8 and 10 (all rows of
2026-11-17); `docs/spec.md` sections 0 (changes in v0.9), 5.5, 9.4 and 11;
the requirement texts below; and the two audits these fixes come from, in
the orchestrator's worktrees if they are still there (adapter audit findings
W1, W3, N1, N2, N4; core audit findings F3, F5, F9, F10). Everything you
need is also written in this prompt. Depends on nothing; WP24 depends on
EXE-017 from this package.

## Requirements to implement

PLT-005 (text extended), OPS-006 (changed), OPS-008, OPS-009, EXE-016,
EXE-017, RSK-014.

The owner accepted all of them on 2026-11-17 as bug fixes that fit the
current spec. None changes a trading rule. If anything you build would
change which trades are taken, at which price or size, or when the bot
halts, stop and ask.

## Allowed paths

- `src/ZoneFvg.Core/Execution/Ports/SymbolInfo.cs` (two init-only members)
- `src/ZoneFvg.Core/Execution/PriceGrid.cs`, `PreTradeFilters.cs`,
  `OrderManager.cs`, `PlannedOrder.cs`, `OrderComment.cs`,
  `TradeManager.cs`
- `src/ZoneFvg.Core/Recovery/SafetyActions.cs` (only if the market-open
  check of EXE-016 belongs there)
- `src/ZoneFvg.Core/App/**`
- `src/ZoneFvg.Core/Ops/**` (inactivity, repeat limits, the `zones` line)
- `src/ZoneFvg.Core/Signals/SignalEngine.cs`, `ZoneBook.cs` and new files in
  `src/ZoneFvg.Core/Signals/` for the zone-end report (OPS-009)
- `src/ZoneFvg.Bot/ChartPainter.cs`, `CTraderBroker.cs`, `ZoneFvgBot.cs`
- `tests/ZoneFvg.Core.Tests/**`
- a new file `docs/test-records/WP22-drawing-cleanup.md`
- the `status:` lines of the seven requirements above

No new namespace dependency (architecture section 1): count zone outcomes in
`App`, not in `Ops`.

## Owner authorisation to change existing tests

Only these, and only as described. List each one in the pull request with
its old and new value.

1. **Test fixtures that build `SymbolInfo`** (`Execution/ExecutionData.cs`,
   `Risk/RiskData.cs`, `TestSupport/RecoverySupport.cs`,
   `Fakes/FakeBroker.cs`, `Risk/PortRecordTests.cs`,
   `Risk/PositionSizerTests.cs`, `Execution/SymbolCostMathTests.cs`) may set
   the two new members to the values the old rule derived: pip 0.0001 gives
   `Digits` 5 and `TickSize` 0.00001; pip 0.01 (USDJPY) gives 3 and 0.001.
   Setup only; no expected value changes. If `PortRecordTests` lists the
   members of `SymbolInfo`, the two new members may be added to that list.
2. The `PriceGrid_*` tests in `Execution/OrderFormatTests.cs` stay as they
   are. Keep the pip-size functions they call (make them `internal` if
   production code no longer uses them; architecture section 10 allows
   this).

No other existing test may change. If one fails, stop and ask.

## What to build, in this order

1. **EXE-017, price grid from the broker.** `SymbolInfo` gains `int Digits`
   and `double TickSize` as init-only members outside the positional list
   (architecture section 10, contract additions). Entry, stop and target are
   rounded half away from zero to a whole multiple of `TickSize` (compute
   in `decimal`, or with an equivalent method that passes the tests below
   exactly); the comment uses `Digits`; the read-back tolerance of
   `OrderManager` is half a tick (accept `|difference| <= 0.5 x tick + 1e-9
   x tick`). Invalid data (rule in the requirement) skips the signal as
   `BrokerDataInvalid` before anything is sent. Adapter: fill both from
   `Symbol.Digits` and `Symbol.TickSize` in `CTraderBroker.GetSymbol`, and
   add `digits` and `tickSize` to `DescribeCosts` (cite the Symbol reference
   page in a comment, as the file already does).
2. **RSK-014, start checks.** A core function (in `App`) that takes the
   parameters, the account snapshot, the server time and `isBacktest` and
   returns null or a message. The adapter calls it after `Create` and before
   `_core.OnStart`, and refuses through its existing `Refuse` method. An
   exception while reading the account or the server time is a refusal with
   the exception's message. Messages, exactly:
   `RiskEpochDate 2026-03-05 starts at 2026-03-04 23:00 UTC, after the server time 2026-03-04 10:00 UTC`
   and
   `backtest starting balance 10000.00 differs from InitialCapital 100000.00; set both to the same value`.
3. **EXE-016, Friday close against a closed market.** When Friday close is
   due and `SymbolInfo.IsMarketOpen` is false, send nothing and set the
   weekend-close-pending flag that `WeekendDuty` already handles. Journal one
   line `friday-close event=deferred reason=market-closed` per Friday, not
   per minute.
4. **OPS-006, inactivity.** Pass `RiskEpochDate` to the monitor; without any
   trade the reference is the later of the history start and
   `CestClock.StartOfCestDayUtc(RiskEpochDate)`. `BotCore` stores the time
   of the last inactivity alert under a new storage key (through
   `StorageKey`, like the other keys) and suppresses a new one within 3 days
   of it after a restart. An unreadable value is journaled once and ignored.
5. **OPS-008, repeat limits.** Fixed constants in `BotCore` (or a small
   helper class in `App`): 24 hours for a failed recovery, 60 minutes for
   repeated errors and for `risk event=unavailable`. The written lines carry
   `failed-runs=N` (recovery) and `left-out=N` (errors). The error counter of
   RSK-010 still counts every error.
6. **OPS-009, zone outcome counts.** `SignalEngine` reports, after each
   `OnBarClosed`, the number of zones created and the zones that ended with
   a reason enum (`Signalled`, `NoFvg`, `BiasMismatch`, `Invalid`,
   `Expired`, `Superseded`). The engine's zones, states and signals must not
   change: every SIG test must pass untouched. `BotCore` counts only bars fed
   live, by the CE(S)T day of the bar's close time, and writes the line at
   the first live M15 bar whose close is in a later CE(S)T day, before
   adding that bar's own counts.
7. **PLT-005, drawing clean-up.** In `ChartPainter`, keep the set of names
   drawn in the previous paint; after drawing, call
   `chart.RemoveObject(name)` for every remembered name not drawn this time,
   then remember the new set. Keep the existing removal over
   `chart.Objects` as well (it handles interactive objects). Do not make
   drawings interactive. Write `docs/test-records/WP22-drawing-cleanup.md`
   with the owner's manual check: EURUSD visual backtest of 12 months with
   DrawObjects true, pause after month 11, count rectangles against the
   `zones=` list of the latest report line, resume until a zone ends, pause
   again, the rectangle is gone. Leave the result fields empty for the
   owner; PLT-005 stays `planned` until the owner fills them in.

If the pull request grows past about 800 changed lines, split it as
`WP22a` (steps 1 to 3) and `WP22b` (steps 4 to 7), each with its own ids.

## Tests required

Tag every test with its requirement id. Times are UTC; 2026-03-04 is a
Wednesday, 2027-12-24 a Friday; CE(S)T is UTC+1 in winter.

**EXE-017**
- EURUSD (Digits 5, tick 0.00001): 1.0834550 rounds to 1.08346 and
  1.0834549 to 1.08345. USDJPY (3, 0.001): 150.1235 to 150.124.
- Gold (pip 0.01, Digits 2, tick 0.01): signal entry 2650.125 is sent as
  2650.13, stop 2641.994 as 2641.99; the comment contains `e=2650.13` and
  `s=2641.99`.
- Tick 0.05, Digits 2: 100.024 to 100.00, 100.025 to 100.05, 100.074 to
  100.05.
- Read-back on gold: broker stop 2641.99 gives Placed; 2642.00 gives
  `MismatchCancelled` and an `OrderMismatch` alert.
- Skipped as `BrokerDataInvalid` with no `PlaceLimitOrder` call: Digits -1,
  Digits 11, tick 0, tick -0.01, tick NaN, tick +infinity, tick 0.001 with
  Digits 2.
- Every existing EXE-008 and EXE-010 test passes unchanged.

**RSK-014** (server time 2026-03-04 10:00)
- RiskEpochDate 2026-03-04 (starts 2026-03-03 23:00): accepted.
  RiskEpochDate 2026-03-05 (starts 2026-03-04 23:00): refused with the
  message above. Server time exactly 2026-03-04 23:00 with RiskEpochDate
  2026-03-05: accepted; one tick earlier: refused. Summer: RiskEpochDate
  2026-07-01 starts 2026-06-30 22:00.
- Backtest, InitialCapital 100,000: balance 100,000.00 and 100,000.01
  accepted; 100,000.02 and 99,999.98 refused; 10,000 refused with the
  message above. Live: balance 10,000 accepted.
- Account read throws: refused.

**EXE-016** (bot position and pending entry on the symbol)
- Friday 2027-12-24, market closed from 20:00: timer events every minute
  from 20:00 to 21:00 send no close and no cancel, raise no `CloseFailed`
  alert, do not halt (the request counter stays unchanged) and write one
  `deferred` line. Market open again Sunday 2027-12-26 22:00: the first
  timer event sends 1 close and 1 cancel and the symbol is flat.
- Market open at 20:00: closed and cancelled at 20:00 as before.
- Market closed at 20:00, open again at 20:05 the same Friday: closed at
  20:05.

**OPS-006**
- No trade, RiskEpochDate 2026-03-01 (starts 2026-02-28 23:00), now
  2026-03-04 10:00: no alert; the first alert at 2026-03-21 23:00, none at
  2026-03-21 22:59.
- No trade, RiskEpochDate 2026-01-01: reference is now minus 30 days and the
  alert comes at once, as before.
- Alert at T, restart at T + 1 hour with the same store: no alert. Restart
  at exactly T + 3 days: alert. Restart with an empty store: alert at once.
  Unreadable stored value: alert at once and one journal line.
- Backtest started 2026-03-04 with RiskEpochDate 2026-03-04 and no trade:
  no inactivity alert before 2026-03-24 23:00.

**OPS-008**
- Failed recovery (D1 has 10 bars), start at 2026-03-04 08:00, timer every
  minute for 24 hours (a run every 15 minutes, 97 runs): `recovery` and
  `report` lines are written for the run at 08:00 and for the run at
  2026-03-05 08:00 (`failed-runs=96`), not for the run at 2026-03-05 07:45.
  Data complete from 08:20: the run at 08:30 completes and is written. The
  `Started` / `Recovered` alert behaviour is unchanged. After a disconnect
  and reconnect the first failed run is written again.
- Errors: the same exception (handler `OnTimer`, `InvalidOperationException`,
  "store down") every minute for 3 hours: error lines and `Error` alerts at minutes 0, 60, 120 and 180 only, with
  `left-out=59` on the last three; the one at minute 59 is left out. The
  error-storm halt happens at the same minute as without the limit. An error
  with another message at minute 30 is written at once.
- `risk event=unavailable` with the same text on every M15 bar for 2 hours:
  written at bars 0, 4 and 8 (minutes 0, 60, 120) only. In a second test the
  text changes at bar 5 and stays: lines at bars 0, 4, 5 and 9.

**OPS-009** (use the `EngineScenarios` / `ZoneScenarios` helpers)
- One zone signalled: `created=1 signalled=1`, written at the first live M15
  bar of the next CE(S)T day; nothing else in that line is non-zero.
- One zone each ending by no FVG within 16 bars, by bias mismatch, by a
  close beyond distal, after 120 H4 bars, and by supersession: each counted
  under its own name.
- A zone that ends during a recovery replay is not counted.
- A day with all counts 0 writes no line.
- Day boundary: an event on the M15 bar that closes 2026-03-04 22:45 counts
  for 4 March; one on the bar closing 23:00 counts for 5 March.
- Every SIG test passes unchanged.

**PLT-005**: manual (test record above). Keep the existing PLT-005 tests.

## Definition of done

- [ ] All tests pass in CI; coverage gates hold; every command of
      `AGENTS.md` run and pasted.
- [ ] Each changed existing test listed with old and new value.
- [ ] EXE-016, EXE-017, RSK-014, OPS-006, OPS-008 and OPS-009 are
      `implemented` (only if "Decisions for the human" is empty for them);
      PLT-005 stays `planned` for the owner's manual check.
