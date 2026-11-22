# WP29: Order guards after the review of WP26

## Read first

`AGENTS.md`; `docs/spec.md` sections 5.2 and 5.3; `docs/architecture.md`
section 10 (the rows of 2026-11-22 "Resend guard from the broker's history",
"Entry type fits the signal" and "Journal of a breakout signal: zone
edges"); the requirement texts below and those of EXE-009, EXE-018, EXE-020
and OPS-010; then the code: `Execution/OrderManager.cs` (`TryPlace`,
`ResolvePending`, `Resolve`, `ResendBlocker`, `Send`),
`Execution/PlannedOrder.cs`, `Ops/SignalJournalEntry.cs`, and the tests you
build on: `Scenarios/Exe009*`, `Scenarios/Exe018StopOrderScenarios.cs`,
`Scenarios/Exe020StopOrderResendScenarios.cs`, `Ops/BreakoutJournalTests.cs`,
`Execution/ExecutionData.cs` and `Fakes/FakeBroker.cs`. Depends on WP26.
Independent of WP27.

## Requirements to implement

EXE-021, EXE-022, and one hardening of OPS-010 (implemented in WP26; its
text does not change).

Decided by the orchestrating session on 2026-11-22, on the validator's
questions in pull request 55. The validator found a limit of the resend
rule itself: a buy stop triggers on the ask, so the bid bar can stay below
the signal's entry while the order fills. If the outcome of the placement
was unclear, the order was in fact accepted, filled and stopped out inside
one M15 bar, nothing is at the broker and no bar "reached" the entry, so
the order would be sent a second time. The broker's history closes that
gap. The second guard makes a planned order of the wrong type impossible
to send. **Without an unclear outcome, and for every valid order, nothing
may change:** the existing scenarios are the proof.

## Allowed paths

- `src/ZoneFvg.Core/Execution/OrderManager.cs`
- `src/ZoneFvg.Core/Ops/SignalJournalEntry.cs`
- `tests/ZoneFvg.Core.Tests/**` (new files; existing files only as
  authorised below)
- the `status:` lines of EXE-021 and EXE-022

No port, enum or record changes: no new `PlaceOutcome`, `SkipReason` or
`AlertKind` value. If you need another file, stop and ask.

## Owner authorisation to change existing tests

Only this one, decided by the orchestrating session:

1. `Signals/SignalEngineBreakoutPropertyTests.cs`, the test
   `OnBarClosed_EveryBar_ReferenceAndSignalsEqualThoseOfTheTrackerDrivenDirectly`:
   it may gain one assertion that at least one signal was compared over its
   seeds. Nothing else in the test changes. If its seeds give no signal,
   stop and ask; do not change the seeds.

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
   enum names, or the number for an undefined value) is raised,
   and the result is `PlaceOutcome.Abandoned` with the same detail. The
   time of the skip record is the broker's server time, as in the other
   skip records of `Send`.
3. **OPS-010, zone edges of a breakout line.** In
   `SignalJournalEntry.Taken` and `Skipped`: for a signal of Kind Breakout
   the zone edges of the entry are null whatever zone the caller passes, so
   the line writes `zoneProximal=NA zoneDistal=NA`. For a ZoneFvg signal
   nothing changes.
4. Update the documentation comments you touch so that they say what the
   code now does and name the requirement ids.

## Decisions already made

- The history is read from the signal's confirmation close on; no further
  time filter is applied to the list.
- A closed trade with another label does not block a resend.
- The history check also runs before a bar has closed since the
  confirmation: a closed trade resolves the placement at once.
- No alert is raised by either guard; the skip record and the "resolved"
  line of the core are the record.
- The fill model of `FakeBroker` is not changed.

## Tests required

Fixture: the breakout harness and helpers that WP26 added to
`ExecutionData` (`BreakoutBuy`: signal entry 1.0863, order entry 1.0864,
stop 1.0823, target 1.0943; `BreakoutSell`: entry 1.0767, order stop
1.0808, target 1.0687), at `ExecutionData.Now` (2026-01-14 10:00 UTC).
Model the scenarios on `Exe020StopOrderResendScenarios`. Where a test needs
a broker that returns no history list or counts the history reads, wrap
the `FakeBroker` in a small test-only `IBroker` class in your new test
file, as `Exe017PriceGridScenarios` does for its purpose.

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
  result). State in the pull request that this test fails on `main`
  without your change (it resends), and how you know.
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

**Authorised assertion**: the one of item 1 of the authorisation.

## Out of scope

`BotCore`, `RecoveryService`, `PreTradeFilters`, the broker port, the
adapter, any trading rule, alerts, the other non-blocking findings of the
WP26 review (a test name; two copies of `StopShiftingBroker`).

## Definition of done

- [ ] Tests first, all green in CI; coverage thresholds met; in the
      per-file listing of the `Coverage gate` step, `OrderManager.cs` and
      `SignalJournalEntry.cs` have no more uncovered lines or branches than
      on `main`.
- [ ] No existing test changed beyond the one authorised assertion.
- [ ] Every command of `AGENTS.md` run and its output pasted (without a
      .NET SDK in the session: say so and paste the CI output).
- [ ] EXE-021 and EXE-022 are `implemented` (only if "Decisions for the
      human" is empty).
- [ ] A plain-language summary for the owner: when the bot now refuses to
      send or resend an order, and that nothing changes otherwise.
