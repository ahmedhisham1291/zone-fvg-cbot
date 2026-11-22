# WP29: Order and recovery guards after the reviews of WP26 and WP27

## Read first

`AGENTS.md`; `docs/spec.md` sections 5.2, 5.3, 8.3 (step 1), 10.2 and 12.3;
`docs/architecture.md` section 10 (every row of 2026-11-22 from "Resend
guard from the broker's history" on); the requirement texts below and those
of EXE-009, EXE-018, EXE-020, OPS-010, REC-003, REC-013 and REC-017; then
the code: `Execution/OrderManager.cs` (`TryPlace`, `ResolvePending`,
`Resolve`, `ResendBlocker`, `Send`), `Execution/PlannedOrder.cs`,
`Ops/SignalJournalEntry.cs`, `Recovery/BarSeriesCheck.cs`
(`ProblemOtherThanGap` and the code behind it), `Recovery/RecoveryService.cs`,
and the tests you build on: `Scenarios/Exe009*`,
`Scenarios/Exe018StopOrderScenarios.cs`,
`Scenarios/Exe020StopOrderResendScenarios.cs`,
`Scenarios/Rec013FaultScenarios.cs`,
`Scenarios/Rec017BreakoutModeScenarios.cs`, `Ops/BreakoutJournalTests.cs`,
`Execution/ExecutionData.cs`, `TestSupport/RecoverySupport.cs`
(`BreakoutRecoveryData`) and `Fakes/FakeBroker.cs`. Depends on WP26 and
WP27.

## Requirements to implement

EXE-021, EXE-022, REC-018, REC-019, and one hardening of OPS-010
(implemented in WP26; its text does not change).

Decided by the orchestrating session on 2026-11-22, on the questions of the
validators of pull requests 55 (WP26) and 58 (WP27).

- **EXE-021.** A buy stop triggers on the ask, so the bid bar can stay
  below the signal's entry while the order fills. If the outcome of the
  placement was unclear, the order was in fact accepted, filled and stopped
  out inside one M15 bar, nothing is at the broker and no bar "reached" the
  entry, so the order would be sent a second time. The broker's history
  closes that gap.
- **EXE-022.** A planned order of the wrong type for its signal can never
  be sent.
- **REC-018.** In Breakout mode the last closed D1 bar is the reference. A
  D1 series that lacks it must not pass recovery.
- **REC-019.** The fault scenarios that involve a signal at the moment of
  the fault also run in Breakout mode.

**Without an unclear outcome, for every valid order, and in ZoneFvg mode,
nothing may change:** the existing tests are the proof.

## Allowed paths

- `src/ZoneFvg.Core/Execution/OrderManager.cs`
- `src/ZoneFvg.Core/Ops/SignalJournalEntry.cs`
- `src/ZoneFvg.Core/Recovery/BarSeriesCheck.cs` and `RecoveryService.cs`
- `tests/ZoneFvg.Core.Tests/**` (new files, and new helpers in
  `TestSupport/RecoverySupport.cs`; existing tests only as authorised
  below)
- the `status:` lines of EXE-021, EXE-022, REC-018 and REC-019

No port, enum or record changes: no new `PlaceOutcome`, `SkipReason` or
`AlertKind` value. If you need another file, stop and ask.

## Owner authorisation to change existing tests

Only these two, decided by the orchestrating session:

1. `Signals/SignalEngineBreakoutPropertyTests.cs`, the test
   `OnBarClosed_EveryBar_ReferenceAndSignalsEqualThoseOfTheTrackerDrivenDirectly`:
   it may gain one assertion that at least one signal was compared over its
   seeds. Nothing else in the test changes. If its seeds give no signal,
   stop and ask; do not change the seeds.
2. `Scenarios/Rec017BreakoutModeScenarios.cs`: the four tests that do not
   end with `ScenarioAssert.Global`
   (`Hash_ZoneFvgMode_ReportAndOrderCommentCarryTheValueOfV09` and the
   three start-line tests after it) may gain that call as their last
   statement. Nothing else in them changes.

No other existing test changes, and no expected value. If an existing test
fails, stop and ask.

## What to build, in this order

1. **EXE-021 in `OrderManager.Resolve`.** After the query for a pending
   order and the query for a position have both found nothing, and before
   anything else is decided or any state is changed, read
   `_broker.GetHistory(order.Signal.ConfirmationCloseUtc)`.
   - A trade in that list whose `Label` equals the order's label (ordinal)
     means the order was applied, filled and closed: the placement is no
     longer unresolved, nothing is sent, and the result is
     `PlaceOutcome.Placed` with the detail
     `filled and closed as position <id>` (the `PositionId` of the first
     such trade in the list, written with the same helper as the existing
     "filled as position" detail).
   - A null list: the placement is no longer unresolved, nothing is sent,
     a skip record with `SkipReason.UnclearOutcomeNotResent` is raised as
     the existing abandon path raises it, with the detail
     `history not available, not resent`, and the result is
     `PlaceOutcome.Abandoned` with the same detail.
   - Otherwise the existing code runs unchanged (the wait for a closed bar,
     `ResendBlocker`, `Send`).
   - An exception of `GetHistory` is not caught here, exactly like the two
     queries before it: nothing has been changed, and the placement stays
     unresolved.
   - The history is not read when a pending order or a position with the
     label was found.
2. **EXE-022 in `OrderManager.Send`**, as its first statement, before any
   broker call: the order may be sent only if (`order.Signal.Kind` is
   `Breakout` and `order.EntryType` is `Stop`) or (`Kind` is `ZoneFvg` and
   `EntryType` is `Limit`). Otherwise nothing is sent and no broker query
   is made by `Send`: a skip record with `SkipReason.InvalidSignal` and the
   detail `entry type <EntryType> does not fit signal kind <Kind>` (the
   enum names, or the number for an undefined value) is raised, and the
   result is `PlaceOutcome.Abandoned` with the same detail. The time of the
   skip record is the broker's server time, as in the other skip records of
   `Send`.
3. **OPS-010, zone edges of a breakout line.** In
   `SignalJournalEntry.Taken` and `Skipped`: for a signal of Kind Breakout
   the zone edges of the entry are null whatever zone the caller passes, so
   the line writes `zoneProximal=NA zoneDistal=NA`. For a ZoneFvg signal
   nothing changes.
4. **REC-018, the D1 series in Breakout mode.** The recency rule of
   `BarSeriesCheck` ("last bar closed ..., not recent") uses two bar
   lengths of open-market time. Give `ProblemOtherThanGap` (and the private
   code behind it) one optional last argument, the number of bar lengths
   after which the last bar is stale, default 2, so that every existing
   call and test keeps its behaviour; a value below 1 throws
   `ArgumentOutOfRangeException`. `RecoveryService` passes 1 for the D1
   series when it was built with breakout parameters, and 2 in every other
   case (H4 and M15 in both modes, D1 in ZoneFvg mode). The problem text
   and everything that follows a series problem (one reload of history,
   then Recovering and one alert per outage, a new attempt at the next
   timer event) are the existing code.
5. **`RecoveryService` constructor:** when breakout parameters are given,
   call their `Validate()` there, beside the validation of the other
   parameters, so that invalid values fail at construction and not at the
   first recovery. Correct the class remarks so that they name both modes.
   Do not change any report or journal text.
6. **REC-019:** tests only (below).
7. Update the documentation comments you touch so that they say what the
   code now does and name the requirement ids.

## Decisions already made

- The history is read from the signal's confirmation close on; no further
  time filter is applied to the list.
- A closed trade with another label does not block a resend.
- The history check also runs before a bar has closed since the
  confirmation: a closed trade resolves the placement at once.
- No alert is raised by either order guard; the skip record and the
  "resolved" line of the core are the record.
- The fill model of `FakeBroker` is not changed.
- REC-018 changes the limit for the D1 series in Breakout mode only. A
  whole-day market holiday that is not in `MarketHolidays` therefore makes
  a recovery on the following day fail in Breakout mode until the next D1
  bar closes; that is accepted (fail closed), and the list is the owner's
  parameter.
- A pending order placed under other breakout values is kept across a
  restart (REC-006 does not read the hash). That is unchanged; do not add a
  rule for it.

## Tests required

Fixtures: for the order guards, the breakout harness and helpers that WP26
added to `ExecutionData` (`BreakoutBuy`: signal entry 1.0863, order entry
1.0864, stop 1.0823, target 1.0943; `BreakoutSell`: entry 1.0767, order
stop 1.0808, target 1.0687), at `ExecutionData.Now` (2026-01-14 10:00 UTC);
model the scenarios on `Exe020StopOrderResendScenarios`. For recovery and
the fault scenarios, `BotHarness` with `BreakoutRecoveryData` (WP27); model
them on `Rec013FaultScenarios` and `Rec017BreakoutModeScenarios`. Where a
test needs a broker that returns no history list, throws, or counts the
history reads, wrap the `FakeBroker` in a small test-only `IBroker` class
in your new test file, as `Exe017PriceGridScenarios` does for its purpose.
Verify every expected value yourself from the requirement text before you
assert it.

**EXE-021**
- The gap itself, buy stop. Place with the fault `UnclearApplied`: the
  order exists at the broker. `Tick` with bid 1.0862 and ask 1.0864 (a
  spread of 2 pips, wider than the 1 pip that was added) fills it at
  1.0864; a `Tick` with bid 1.0823 closes it at the stop. At 10:15 call
  `ResolvePending` with the bar 10:00 to 10:15 closed and its high at
  1.0862, below the signal's entry: one result, `Placed`, whose detail is
  `filled and closed as position <id>` with the id of that position; no
  second `PlaceStopOrder` request exists; no skip record is raised;
  nothing is unresolved afterwards (a second `ResolvePending` returns no
  result).
- Sell stop, the mirror: filled at 1.0767 by a bid of 1.0767, closed at
  1.0808 by an ask of 1.0808; bars whose low stays at 1.07671: `Placed`,
  closed, nothing resent.
- Limit order (`ExecutionData.Buy()` in the same harness): applied, filled
  and closed by ticks, then bars that did not trade through the entry:
  `Placed`, closed, no second `PlaceLimitOrder` request.
- A closed trade with another label on the same symbol since the
  confirmation does not block: after `UnclearNotApplied` the order is
  resent once, as the existing EXE-020 scenario says.
- Before a bar has closed since the confirmation: with the closed trade in
  the history the placement is resolved at once (`Placed`, closed); with an
  empty history it stays unresolved (no result), as today.
- No history list (the wrapper returns null), for a stop order and for a
  limit order: `Abandoned`, one skip record `UnclearOutcomeNotResent` with
  the detail above, nothing sent, nothing unresolved afterwards.
- `GetHistory` throws (the wrapper throws `InvalidOperationException`):
  the exception leaves `ResolvePending`, nothing was sent, and the
  placement is still unresolved (`UnresolvedCount` unchanged); at the next
  call, with the history readable and empty, the rule of EXE-020 applies.
- The history is read with the signal's confirmation close as its
  argument, and it is not read at all when the pending order exists (the
  `UnclearApplied` case without a fill) or when the position exists (the
  filled, still open case): the wrapper records the calls.
- The existing EXE-009 and EXE-020 scenarios pass unchanged.

**EXE-022** (planned orders built by hand from a valid one with
`with { EntryType = ... }`, or with a signal of the other kind)
- Kind Breakout with entry type Limit: `TryPlace` sends nothing (no
  `PlaceLimitOrder`, no `PlaceStopOrder`), raises one skip record
  `InvalidSignal` whose detail is
  `entry type Limit does not fit signal kind Breakout`, and returns
  `Abandoned` with that detail; the fake's `QueryCount` is the same before
  and after the call (no query for the label is made).
- Kind ZoneFvg with entry type Stop: the same, with
  `entry type Stop does not fit signal kind ZoneFvg`.
- An undefined entry type (`(EntryOrderType)99`) with each kind, and an
  undefined kind (`(SignalKind)99`) with each entry type: the same, with
  the number in the detail.
- The two valid combinations are sent exactly as before: one explicit case
  each (stop request for Breakout with Stop, limit request for ZoneFvg
  with Limit), and the existing EXE-008, EXE-009, EXE-018 and EXE-020
  scenarios pass unchanged.

**OPS-010**
- `SignalJournalEntry.Taken` and `Skipped` for a breakout signal with a
  zone passed (any zone snapshot of the fixtures): the journal line has
  `zoneProximal=NA zoneDistal=NA` and equals, character for character, the
  line produced with a null zone.
- A ZoneFvg signal with the same zone still writes the zone's edges (one
  explicit case; the three existing lines of `JournalTests` pass
  unchanged).

**REC-018** (open-market time is counted by `BarSeriesCheck` as it is
today: Saturday and Sunday are closed, Friday is open until
`FridayCloseUtc`, a listed holiday is closed all day; pick weekdays for the
cases that do not say otherwise)
- `ProblemOtherThanGap` for a D1 series, limit 1: the last bar closed
  exactly 24 hours of open-market time before now (for example it closes
  Tuesday 22:00 UTC and now is Wednesday 22:00 UTC): the "not recent"
  problem; one minute less: null. The same two cases with the default
  limit: null in both. With the default limit and exactly 48 hours: the
  problem, as today (the existing tests stay as they are).
- Weekend, limit 1: the last bar is Friday's (it closes Friday 22:00 UTC);
  now Monday 23:59 UTC: null; now Tuesday 00:00 UTC (24 hours of open
  market since Monday 00:00): the problem.
- Listed holiday, limit 1: the day after the last bar's close is a whole
  UTC day in the holiday list; now 07:00 UTC on the day after the holiday:
  null. Without the holiday in the list: the problem.
- A limit of 0 and of -1 throws `ArgumentOutOfRangeException`.
- Through the core in Breakout mode (`BotHarness`, `BreakoutRecoveryData`):
  a restart with a D1 series that lacks the bar that closed last, the M15
  series being current: the bot stays in Recovering, one alert is raised,
  no request is sent, also when later bars close above the level. When the
  D1 bar is there at the next attempt, recovery passes and the state is
  Running.
- The same series in ZoneFvg mode (`Breakout` null): recovery passes, as
  today.
- H4 and M15 in Breakout mode keep the limit of 2: a series whose last bar
  closed between one and two bar lengths ago passes.
- `RecoveryService` built with invalid breakout parameters
  (`StopAtrMult` 0) throws `ArgumentOutOfRangeException` from the
  constructor; with valid ones and with null it does not.

**REC-019** (through the core in Breakout mode; each ends with
`ScenarioAssert.Global`)
- Fault 5, a disconnect of 1, 10 and 60 minutes across the confirming bar:
  recovery runs; the confirmation that fell into the gap is journaled as
  skipped with `SignalFromReplay`; no request is sent for it; later bars of
  the same day give no order (the reference is finished).
- Fault 6, a disconnect at the moment the stop order is sent, for each
  fault the existing `Fault06` theory uses: after the reconnect there is
  exactly one order or position with the breakout id, or none where the
  rules abandon the placement; never two.
- Fault 6 with the gap of EXE-021, through the core: the unclear placement
  was applied, the order filled and closed at its stop before the next bar
  (ticks as in the EXE-021 test); at the next bars no second
  `PlaceStopOrder` request is sent and the "resolved" line says
  `filled and closed`.
- Fault 8, two cores on one symbol and one broker: both see the same
  confirmation; exactly one stop order exists.
- Fault 14, ten restarts in ten minutes with the stop order pending: the
  calendar is fetched at most once, the order is kept each time, no second
  order is sent.
- Sell side of recovery: a pending sell stop with a `BL-` id that the
  replay produced is kept across a restart; a sell position with such an
  id is adopted, its stop and target checked.

**Authorised assertions**: the two items of the authorisation.

## Out of scope

`BotCore`, `PreTradeFilters`, the broker port, the adapter, any trading
rule, alerts, REC-006 (a pending order is kept whatever hash its comment
carries), the other non-blocking findings of the two reviews (a test name;
two copies of `StopShiftingBroker`; the report texts that say "zone").

## Definition of done

- [ ] Tests first, all green in CI; coverage thresholds met; in the
      per-file listing of the `Coverage gate` step, `OrderManager.cs`,
      `SignalJournalEntry.cs`, `BarSeriesCheck.cs` and `RecoveryService.cs`
      have no more uncovered lines or branches than on `main`.
- [ ] No existing test changed beyond the two authorised items.
- [ ] Every command of `AGENTS.md` run and its output pasted (without a
      .NET SDK in the session: say so and paste the CI output).
- [ ] EXE-021, EXE-022, REC-018 and REC-019 are `implemented` (only if
      "Decisions for the human" is empty).
- [ ] A plain-language summary for the owner: when the bot now refuses to
      send or resend an order, when it now refuses to leave Recovering in
      Breakout mode, and that nothing changes otherwise.
