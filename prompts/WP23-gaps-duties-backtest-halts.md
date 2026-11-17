# WP23: Accepted data gaps, duties while recovering, halts in backtests

## Read first

`AGENTS.md`; `docs/spec.md` section 0 (changes in v0.9, decisions A, B and
C), 8.3 to 8.5 and 12.1; `docs/architecture.md` section 10 (the recovery
rows of 2026-11-16 and the rows of 2026-11-17 on data gaps, duties while
recovering and halts in backtests); the requirement texts below; and the
code they change: `Recovery/BarSeriesCheck.cs`, `Recovery/RecoveryService.cs`
(`WaitForConnectionAndData`), `Recovery/RecoveryReport.cs`, `App/BotCore.cs`
(`ConnectedTimerDuties`, `TradeEvent`, `ApplyGuard`, `CheckEmergencyGuard`,
`EnterHalt`, `RecordError`, `PlaceSignal`, `ResolvePending`,
`CheckRequestLimit`) and `App/BotParameters.cs`. Depends on WP22 (EXE-016 is
part of the Friday close of REC-015, and OPS-008's repeat limits are
assumed).

## Requirements to implement

REC-003 (changed by decision A), REC-014 (decision C), REC-015 (decision
B).

These are rule changes the owner made on 2026-11-17. Build exactly what the
requirement texts say.

## Allowed paths

- `src/ZoneFvg.Core/Recovery/BarSeriesCheck.cs`, `RecoveryService.cs`,
  `RecoveryReport.cs`
- `src/ZoneFvg.Core/App/**`
- `src/ZoneFvg.Core/Execution/OrderManager.cs`, `PreTradeFilters.cs` and
  `src/ZoneFvg.Core/Risk/RiskEngine.cs`, only to pass the backtest flag so
  that the request limit does not stop a request and the daily budget does
  not refuse a trade in that mode. No computed value may change.
- `src/ZoneFvg.Bot/ZoneFvgBot.cs` (set the flag)
- `tests/ZoneFvg.Core.Tests/**`
- the `status:` lines of REC-003, REC-014 and REC-015

## Owner authorisation to change existing tests

Only these two tests encode the old "any gap fails recovery" rule. List both
in the pull request with old and new values.

1. `Scenarios/RecoveryScenarios.Recovery_MissingBarOnAWeekday_StaysRecovering`
   may be renamed `Recovery_MissingBarOnAWeekday_IsAcceptedAfterOneReload`.
   Old: state `Recovering`; `DataProblems` = `["M15: gap between 2026-03-04
   05:45 and 2026-03-04 06:15"]`; `MoreRequests` = `[(M15, 3000)]`.
   New: state `Running`; `DataProblems` empty; the accepted M15 gaps of the
   report = that one gap (count 1); `MoreRequests` unchanged.
2. `Scenarios/RecoveryScenarios.Recovery_MissingDayThatIsAMarketHoliday_IsNotAGap`:
   the part with the holiday stays (state `Running`, and now no accepted
   gap). The part without the holiday, old: state `Recovering`,
   `DataProblems` = the H4 gap 2026-03-02 20:00 / 2026-03-04 00:00 and the
   M15 gap 2026-03-02 23:45 / 2026-03-04 00:00. New: state `Running`,
   `DataProblems` empty, those two gaps reported as accepted (H4 count 1,
   M15 count 1).

`Recovery/BarSeriesCheckTests` does not change: `BarSeriesCheck.Problem`
keeps reporting gaps; recovery uses a new check that leaves gaps out plus a
separate list of gaps (architecture section 10). Every test that proves a
halt keeps passing unchanged, because the backtest flag is off by default in
the test fixtures. If any other existing test fails, stop and ask.

## What to build, in this order

1. **REC-003, accepted gaps.** In `BarSeriesCheck` add a function that
   reports the first problem other than a gap (too few bars, missing entry,
   unclosed bar, disorder, stale last bar) and one that lists every gap
   (timeframe, previous open, next open). `RecoveryService` asks for more
   history once per timeframe when either function finds something, then
   fails only on a problem; remaining gaps go into the report
   (`AcceptedGaps`: count per timeframe and the three newest, newest first)
   and into one journal line per timeframe:
   `recovery event=gaps-accepted timeframe=M15 count=1 newest=2026-03-04 05:45>2026-03-04 06:15`.
   The report text gains `gaps=` with the counts. The live gap path
   (`CheckLiveGap`, `_dataGap`, recovery at the next timer) stays as it is.
2. **REC-015, duties in every connected state.** In `ConnectedTimerDuties`
   run `CheckEmergencyGuard`, `FridayClose` (with EXE-016 from WP22) and
   `WeekendDuty` in every state, Recovering included; in `TradeEvent` run
   `WeekendDuty` in every state. Nothing changes while disconnected. A halt
   entered during Recovering must stay when recovery completes (check
   `EnterStateAfterRecovery` and the state machine; a transition the state
   machine does not allow today is a question for the owner, not a change
   you make).
3. **REC-014, halts recorded in backtests.** `BotParameters` gains
   `bool BacktestHaltsLogOnly` (default false). `Validate(isBacktest)`
   throws an `ArgumentException` when it is true and `isBacktest` is false.
   The adapter sets it to `isBacktest` in `BuildParameters` (no cBot
   parameter). With it on:
   - `EnterHalt` for `KillSwitch`, `EmergencyGuard`, `ErrorLimit` and
     `RequestLimit` writes `risk event=would-halt cause=<HaltCause>
     detail=<text>` instead, at most once per cause and CE(S)T day, and does
     nothing else: no state change, no alert of the halt kind, no cancels.
   - The emergency guard closes nothing.
   - A request past the request limit is sent (`OrderManager` and
     `PlaceSignal`), with the `would-halt` line.
   - The daily budget: no Blocked state, no cancels, no stored budget day,
     and the budget check of a new trade does not refuse it; the line
     `risk event=would-block cause=DailyBudget detail=<text>` once per CE(S)T
     day.
   - Every other check of a new trade refuses as before.
   Keep the code paths shared: one decision point that asks "record only?"
   rather than a second copy of the halt logic.

## Tests required

Use `BotHarness`; set `BacktestHaltsLogOnly = true` through the parameters
where a test needs the backtest behaviour. 2026-03-04 is a Wednesday,
2026-03-06 a Friday; InitialCapital 100,000; the emergency floor is 91,000.

**REC-003**
- The two changed scenarios above.
- Real holiday hours: M15 history whose last bar on Wednesday 2025-12-24
  opens 21:45, holidays default (25 December), next bar Friday 2025-12-26
  00:00, start on 2026-01-05 10:00 with 3,000 M15 bars: state `Running`,
  one accepted M15 gap 2025-12-24 21:45 / 2025-12-26 00:00. With the old
  rule this start stayed in Recovering until February.
- Five M15 gaps: count 5, the three newest listed, newest first.
- Still failing: D1 with 10 bars (`Recovering`, the bar-count problem
  worded as in the existing too-few-bars test); a last M15 bar that closed
  exactly 30 minutes of open-market time before the server time
  (`Recovering`, "not recent"), while 29 minutes 59 seconds gives
  `Running`; broker not connected (`Recovering`, no request).
- Live gap: m1 missing both live and in the history: `Blocked` with one
  `DataGap` alert; at the next timer recovery accepts the gap and the state
  is `Running`; a signal confirmed after that is placed.

**REC-015**
- Recovery stuck (D1 with 10 bars), a bot position and a pending entry on
  the symbol: timer on Friday 2026-03-06 at 19:59 sends nothing; at 20:00
  sends 1 close and 1 cancel; the state stays `Recovering`.
- Recovery stuck, equity 90,999 at a timer: all bot trades closed, state
  `Halted`, one `EmergencyGuard` alert. Equity 91,000.01: nothing. Then the
  data becomes complete: recovery completes and the state stays `Halted`.
- Recovery stuck, market closed at Friday close (EXE-016): the close is
  deferred; when the market opens again a timer event, and in a second test
  a trade event, closes the position while still `Recovering`.
- Disconnected: nothing sent (the existing test
  `EmergencyGuard_TimerWhileDisconnected_SendsNothing` keeps passing).

**REC-014** (flag on unless stated)
- Kill switch: equity 8% below the peak: state stays `Running`, one
  `would-halt cause=KillSwitch` line; a second M15 bar the same CE(S)T day
  writes no second line; the first bar of the next CE(S)T day writes one
  again; a signal confirmed after the first line is placed.
- Emergency guard, equity 90,999: no close, no cancel, state `Running`, one
  `would-halt cause=EmergencyGuard` line.
- Error limit: 6 errors within an hour: no halt, one `would-halt
  cause=ErrorLimit` line.
- Request limit 20 per hour: the 21st request of the hour is sent; one
  `would-halt cause=RequestLimit` line.
- Daily budget: M - W above 1,500: no `Blocked`, no cancel, nothing stored;
  a new trade that would exceed the budget is placed; one `would-block
  cause=DailyBudget` line per CE(S)T day.
- A sanity-cap skip (RSK-002) still skips with the flag on.
- Flag off: the same five situations halt, block or refuse exactly as the
  existing tests show (run them; add none).
- `Validate(isBacktest: false)` with the flag on throws an
  `ArgumentException` naming `BacktestHaltsLogOnly`; `Validate(true)` with
  it on does not.

## Definition of done

- [ ] All tests pass in CI; coverage gates hold; every command of
      `AGENTS.md` run and pasted.
- [ ] The two changed tests listed with old and new values.
- [ ] REC-003, REC-014 and REC-015 are `implemented` (only if "Decisions
      for the human" is empty).
