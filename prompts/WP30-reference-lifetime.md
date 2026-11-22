# WP30: A reference serves one day, and follow-ups of the WP29 review

## Read first

`AGENTS.md`; `docs/spec.md` sections 3.7 ("Bars that are checked"), 5.3
and 8.3 (step 1); `docs/architecture.md` section 3 (the block "Spec v0.10,
WP25") and section 10 (the rows of 2026-11-22 from "A reference serves one
day" on); the requirement texts below and those of SIG-022, SIG-023,
SIG-025, EXE-021, REC-018 and REC-019; then the code:
`Signals/BreakoutTracker.cs`, `Execution/OrderManager.cs` (`Resolve`, and
the summaries of `PlaceOutcome` and `TryPlace`),
`Recovery/BarSeriesCheck.cs` (the argument check of the stale limit); and
the tests you build on: `Signals/Breakout*Tests.cs`,
`TestSupport/BreakoutData.cs`, `TestSupport/BreakoutFeed.cs`,
`Scenarios/Exe021HistoryResendGuardScenarios.cs`,
`Scenarios/Rec018BreakoutD1Scenarios.cs`,
`Scenarios/Rec019BreakoutFaultScenarios.cs`,
`Recovery/BarSeriesCheckStaleLimitTests.cs` and
`TestSupport/RecoverySupport.cs` (`BreakoutRecoveryData`). Depends on WP25,
WP27 and WP29.

## Requirements to implement

SIG-027 and EXE-023.

Decided by the orchestrating session on 2026-11-22, on the questions of the
validator of pull request 60 (WP29).

- **SIG-027.** REC-018 makes recovery fail when the D1 series is one bar
  behind, but only once 24 hours of open-market time have passed since the
  last D1 close. Closed time delays that: when Friday's bar is missing,
  nothing is seen before Monday 02:00 UTC, and a restart before then would
  trade Monday against Thursday's range. The guard therefore sits where the
  reference is used: a reference serves one day of M15 bars and no more.
- **EXE-023.** A history list with a missing entry is an unexpected value.
  It counts as no history list: nothing is resent.

**With a complete D1 series nothing may change:** every existing tracker,
engine and scenario test must pass unchanged, except the one authorised
below.

## Allowed paths

- `src/ZoneFvg.Core/Signals/BreakoutTracker.cs`
- `src/ZoneFvg.Core/Execution/OrderManager.cs`
- `src/ZoneFvg.Core/Recovery/BarSeriesCheck.cs`
- `tests/ZoneFvg.Core.Tests/**` (new files, and new helpers in
  `TestSupport/`; existing tests only as authorised below)
- the `status:` lines of SIG-027 and EXE-023

No change to the public surface of `BreakoutTracker`, to
`BreakoutReference`, to any port, enum or record. If you need another
file, stop and ask.

## Owner authorisation to change existing tests

Only this one, decided by the orchestrating session:

1. `Scenarios/Exe021HistoryResendGuardScenarios.cs`, the test
   `HistoryWithAMissingEntry_TheEntryIsSkippedAndTheClosedTradeStillFound`:
   its expectation changes to the rule of EXE-023 (the placement is
   abandoned, one skip record `UnclearOutcomeNotResent` with the detail
   `history not available, not resent`, nothing sent, nothing unresolved),
   and it is renamed to say so. Its set-up stays.

No other existing test changes, and no other expected value. If an
existing test fails, stop and ask.

## What to build, in this order

1. **SIG-027 in `BreakoutTracker`.** One private field: the open time of
   the first M15 bar that the tracker received at or after the close of the
   current reference bar (the reference's open time plus 24 hours); empty
   until such a bar arrives, and emptied by `OnD1Closed` when it sets a new
   reference.
   - In `OnM15Closed`, for every bar given while a reference exists,
     whatever the bias, the ATR values and the state of the reference
     (Armed or finished): if the bar opens at or after the reference
     bar's close and the field is empty, the field takes the bar's open
     time. This happens before any other return of the method.
   - A bar that opens 24 hours or more after the time in the field is not
     checked: the method returns null and changes nothing (the reference
     keeps its state, the field keeps its value).
   - A bar inside those 24 hours is handled exactly as today (the window
     rule of SIG-022, then SIG-023 and SIG-024).
   - `Reference` and the `BreakoutReference` record do not change.
2. **EXE-023 in `OrderManager.Resolve`.** A history list with a null entry
   is treated exactly like the null list of EXE-021: the placement is no
   longer unresolved, nothing is sent, the same skip record and the same
   `Abandoned` result with the detail `history not available, not resent`,
   also when another entry of the list carries the label.
3. **`BarSeriesCheck`:** the `ArgumentOutOfRangeException` of the stale
   limit writes the value into its message with the invariant helper the
   other range errors of the core use, and is built without the
   "actual value" argument, so that the message does not depend on the
   culture (hard rule 7). The parameter name stays.
4. **Documentation comments in `OrderManager.cs`:** the summary of
   `PlaceOutcome.Abandoned` also names EXE-021, EXE-022 and EXE-023; the
   summary of `TryPlace` states the order of its checks as the code has it
   (an unresolved earlier placement of the label first, then the entry
   type).

## Decisions already made

- The anchor is the first M15 bar the tracker RECEIVED at or after the
  reference's close. After a gap at the start of a day it is later than
  the first bar of the day, and the reference then serves until 24 hours
  after it. That is accepted.
- The limit is 24 hours from the anchor, by open time: a bar that opens
  exactly 24 hours after it is not checked.
- Nothing is journaled or alerted by the tracker; it has no such port.
- REC-018 keeps its code. Its windows are known and are named in the spec.

## Tests required

**SIG-027** (tracker tests; worked example of WP25: the reference bar
opens 2026-03-02 22:00, high 1.0850, low 1.0780; `BreakoutData` has it.
Verify every expected value yourself.)
- Inside the day, default window: after a first bar that opens 2026-03-03
  22:00 (it is outside the window and returns null), the buy bar of the
  worked example (opens 2026-03-04 08:00) confirms exactly as today.
- Outside the day: same reference and first bar, no new D1 bar; a bar that
  opens 2026-03-05 08:00 and closes above the high, bias Long, valid ATR
  values: null, and the reference is still Armed. The sell mirror with a
  close below the low and bias Short.
- The boundary, window 00:00 to 23:00: first bar opens 2026-03-03 22:00 and
  closes below the high. In one tracker a bar that opens 2026-03-04 21:45
  (23 hours 45 minutes later) and closes above the high confirms; in
  another tracker a bar that opens 2026-03-04 22:00 (exactly 24 hours) and
  closes above the high returns null and the reference stays Armed.
- Weekend: the reference bar opens Thursday 2026-03-05 22:00 (it closes
  Friday 22:00). The first bar after it opens Sunday 2026-03-08 22:00. A
  bar that opens Monday 2026-03-09 08:00 confirms. In another tracker,
  without a new D1 bar, bars that open Monday 22:00 and Tuesday 08:00
  return null.
- The series one bar behind: the reference bar opens Wednesday 2026-03-04
  22:00 (it closes Thursday 22:00) and no D1 bar follows. Friday's M15 bars
  arrive from Thursday 22:00 on. A bar that opens Monday 2026-03-09 08:00
  and closes above the high returns null; the reference stays Armed.
- The anchor is the first bar received at or after the close: a bar that
  opens before the reference's close (2026-03-03 21:45) does not set it;
  with the next bar opening 2026-03-04 10:00, a bar that opens 2026-03-05
  09:45 is still checked (window permitting) and one that opens 2026-03-05
  10:00 is not.
- The anchor is set whatever the call brings: with bias Neutral, with
  missing ATR values, and by a bar outside the window; each shown by a
  later bar 24 hours after it that is not checked.
- `OnD1Closed` empties it: after a new reference, a bar that is more than
  24 hours after the old anchor but inside the new reference's day
  confirms.
- A finished reference: after a confirmation, later bars return null as
  today, inside and outside the 24 hours.
- The existing tracker, engine and property tests pass unchanged. Add one
  property test on the seeded histories of `BreakoutTrackerPropertyTests`
  (complete D1 series): in no history does an M15 bar open 24 hours or
  more after the first M15 bar that follows the close of the D1 bar before
  it; so the rule is never the reason for a missing confirmation there.
  (Computed in the test from the bars alone.)
- Through the core in Breakout mode (`BotHarness`, `BreakoutRecoveryData`):
  a restart with a D1 series that lacks the bar that closed last, at a
  server time at which REC-018 still lets recovery pass (closed time lies
  in between: for the fixtures' D1 bars a missing Friday bar and a restart
  before Monday 04:00 UTC). Recovery ends in Running; on that Monday bars
  close above the reference high inside the trading window; no signal is
  journaled and no request is sent. If the fixtures cannot build this case
  with new helpers only, stop and ask.

**EXE-023**
- A history list with a null entry and no trade with the label, for a stop
  order and for a limit order: `Abandoned`, one skip record
  `UnclearOutcomeNotResent` with the detail
  `history not available, not resent`, nothing sent, nothing unresolved
  afterwards; also before a bar has closed.
- A history list with a null entry and a trade with the label: the same
  result (the authorised test).

**Follow-ups of the WP29 review**
- EXE-021: two closed trades with the label in the list: the detail names
  the `PositionId` of the first. A closed trade whose label differs only in
  letter case does not count: the rules of EXE-020 apply (resent once in
  the `UnclearNotApplied` case).
- REC-019: through the core in Breakout mode, an unclear placement that
  was not applied, with bars that do not reach the entry (the bar set of
  the existing gap scenario): the stop order is sent a second time once,
  and exactly one order with the breakout id exists afterwards.
- `BarSeriesCheck`: for a limit of 0 and of -1 the message of the
  exception is the same under the invariant culture and under a culture
  with another negative sign and decimal separator, and it contains the
  value.

## Out of scope

`SignalEngine`, `BotCore`, `RecoveryService`, `PreTradeFilters`, the port,
the adapter, REC-018's rule, any other trading rule, alerts.

## Definition of done

- [ ] Tests first, all green in CI; coverage thresholds met; in the
      per-file listing of the `Coverage gate` step, `BreakoutTracker.cs`
      stays `full`, and `OrderManager.cs` and `BarSeriesCheck.cs` have no
      more uncovered lines or branches than on `main`.
- [ ] No existing test changed beyond the one authorised item.
- [ ] Every command of `AGENTS.md` run and its output pasted (without a
      .NET SDK in the session: say so and paste the CI output).
- [ ] SIG-027 and EXE-023 are `implemented` (only if "Decisions for the
      human" is empty).
- [ ] A plain-language summary for the owner: when a breakout is now not
      traded because yesterday's daily bar is missing, and that nothing
      changes otherwise.
