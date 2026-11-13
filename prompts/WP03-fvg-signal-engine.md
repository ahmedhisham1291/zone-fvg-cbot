# WP03 — FVG detection, signal engine, determinism and replay

## Read first

`AGENTS.md`, `docs/architecture.md` (sections 2, 3, 6, 10), `docs/spec.md`
section 3 (introduction) and sections 3.1, 3.3 to 3.5, 8.3 and 9.4, and the
requirement texts below.

## Requirements to implement

SIG-002, SIG-004, SIG-014, SIG-015, SIG-016, SIG-017, SIG-018, SIG-019,
SIG-020, SIG-021.

SIG-004 and SIG-020 were started in WP01, which built `BarStore`,
`BarIntakeOrder` and `WarmupCalculator` with tagged tests. SIG-014 and SIG-015
were started in WP02, which built the lifecycle in `ZoneBook`, `MarkSignalled`,
`MarkUsed` and bias-free detection and tracking, with tagged tests. WP03
completes all four in the engine and changes their status lines.

## Allowed paths

`src/ZoneFvg.Core/Domain/Fvg.cs`, `src/ZoneFvg.Core/Domain/Signal.cs`,
`src/ZoneFvg.Core/Domain/BarIntakeOrder.cs` (add the two members of item 4
only), `src/ZoneFvg.Core/Signals/FvgDetector.cs`,
`src/ZoneFvg.Core/Signals/SignalEngine.cs`, `tests/ZoneFvg.Core.Tests/**`, and
the `status:` lines of the requirements above. You may add members to
`ZoneBook` only if listed under "What to build".

## What to build

1. **`FvgDetector.Find(m15Buffer, zone, barsSinceTouch, atrM15, atrH4, parameters)`**:
   pure function implementing SIG-016 for both sides. Returns an `Fvg` or
   null.
2. **Signal construction** (SIG-017): entry, stop, target for both sides.
   `ExtremeSinceTouch` comes from the zone and already includes the touch bar.
3. **`SignalEngine`** with the exact public surface in `docs/architecture.md`
   section 3. `OnBarClosed` does, per timeframe:
   - D1: store bar, recompute bias.
   - H4: store bar, update swings, `ZoneDetector.Detect`, `ZoneBook.Add`,
     `ZoneBook.OnH4Closed`.
   - M15: store bar; `ZoneBook.OnM15Closed`; for each Touched zone in id
     order, look for an FVG; on a valid FVG mark the zone Signalled then Used
     and, if bias matches (SIG-018), emit a `Signal`.
   - Before any of the above, the intake-order check of item 5. A rejected
     bar changes nothing.
   - Warm-up gate (SIG-020): `WarmupBars` is `WarmupCalculator.For(parameters)`
     and `IsReady` is true once every timeframe holds that many bars. While
     `IsReady` is false the engine still stores bars and keeps bias, swings
     and zone states up to date, because SIG-019 needs that state to be exact
     from the first ready bar. But it emits nothing: `Zones` is empty and
     `OnBarClosed` returns no signals.
4. **Close times** (SIG-004): add two members to `BarIntakeOrder` and nothing
   else: `TimeSpan Length(TimeFrame)` (M15 is 15 minutes, H4 is 4 hours, D1 is
   24 hours) and `DateTime CloseTimeUtc(TimeFrame, Bar)`, the open time plus
   the length. Do not change the existing members.
5. **Intake order** (SIG-004): the engine accepts bars only in strictly
   increasing order of the pair (close time, rank), where rank is
   `BarIntakeOrder.Rank`: D1, then H4, then M15. `OnBarClosed` throws
   `InvalidOperationException`, before any state changes, for a bar whose pair
   is not greater than the pair of the last accepted bar. So bars that close
   at the same instant are accepted only in the order D1, H4, M15, and no bar
   is accepted after a bar that closed later.
6. The engine must not depend on wall-clock time, culture, or the order in
   which unrelated zones were inserted.

## Decisions already made

- An FVG is evaluated when c1 closes. c3 must have opened at or after the
  touch bar opened, so the earliest possible confirmation is two bars after
  the touch bar.
- If two zones would signal on the same bar, both signals are returned,
  ordered by zone id. The execution layer will take at most one per symbol.
- When bias does not match, the zone is still consumed (SIG-018).
- The engine does not sort bars. Its callers (the tests here, recovery replay
  in WP07, the live feed in WP08) present bars in close-time order, D1 then
  H4 then M15 at equal close times. A bar out of that order is a programming
  error of the caller, so it throws (architecture section 6).
- Close time is nominal: open time plus the length of the timeframe. Whether
  the platform's bars always match this is checked in WP08. Add no special
  cases here.
- A zone whose FVG is confirmed while the engine is not ready is consumed
  exactly as when it is ready (it ends Used); only the signal is not returned.
  Otherwise a restarted engine could signal a zone that a continuously running
  engine has already used.

## Tests required

- SIG-004 (intake order in the engine): bars that close at the same instant
  are accepted in the order D1, H4, M15, and also when only two of them close
  at that instant (D1 then M15, H4 then M15, D1 then H4). Every other order of
  two same-instant bars (M15 then H4, M15 then D1, H4 then D1) throws
  `InvalidOperationException` on the second bar. A bar that closed earlier
  than the last accepted bar throws, in both directions: an H4 bar after an
  M15 bar that closed 15 minutes later, and an M15 bar after an H4 bar that
  closes later. The first bar that closes after the last accepted bar is
  accepted. After each rejection the engine is unchanged: `IsReady`, `Bias`
  and `Zones` are as before, and continuing with the correct sequence gives
  the same zones and signals as a fresh engine that never saw the rejected
  bar. `Length` and `CloseTimeUtc` for each timeframe.
- SIG-020 (warm-up gate): `WarmupBars` equals
  `WarmupCalculator.For(parameters)`. With each timeframe in turn one bar
  short of its requirement and the other two satisfied, `IsReady` is false,
  `Zones` is empty and no signal is returned, on a history where an engine
  that is already ready holds zones and returns a signal at the same bar. The
  bar that completes the last timeframe makes `IsReady` true. From that bar
  on, `Zones` and the returned signals equal those of an engine fed a longer
  history that ends with the same bars. A zone whose FVG was confirmed during
  warm-up is never signalled afterwards.
- SIG-014 (lifecycle through the engine): on the bar whose close confirms a
  valid FVG, a Touched zone moves to Signalled and then Used, and `Zones` no
  longer contains it after that bar. An FVG confirmed on the last bar of the
  confirmation window is used, because the window-end runs after the FVG
  search. A zone that reaches the end of the window without a valid FVG ends
  Used and returns no signal.
- SIG-015 (bias gates only the signal): run the same H4 and M15 history with
  three D1 histories that give Long, Short and Neutral bias. At every bar the
  zones (ids, edges, states) are identical in the three engines. Only the
  returned signals differ: Buy signals from demand zones under Long, Sell
  signals from supply zones under Short, none under Neutral.

- SIG-016: a minimal three-bar FVG; gap exactly at `MinFvgAtr` (kept) and one
  tick under (rejected); c2 bearish (rejected); c3 before the touch bar
  (rejected); confirmation at exactly `ConfirmWindowBars` (kept) and one bar
  later (rejected); lower edge exactly at `proximal + 0.5 ATR(H4)` (kept) and
  above (rejected); second FVG after a valid first one is ignored; full mirror
  for bearish.
- SIG-017: levels on a worked example for buy and sell with
  `EntryLevelPct` 0, 50 and 100; stop uses the lower of distal and the extreme
  since touch.
- SIG-018: one signal per zone; bias mismatch consumes the zone without a
  signal.
- SIG-002 (determinism): generate 20 seeded random-walk histories of at least
  6,000 M15 bars with consistent H4 and D1 aggregation; run each twice in
  fresh engines; assert identical zone lists and signal lists using exact
  equality of every field.
- SIG-019 (replay equals live): for each of those histories and for 10 cut
  points T, run engine A over the full history, and engine B from
  `T - warm-up` only; assert that at every bar after T both hold identical
  non-terminal zones and emit identical signals. A test helper aggregates M15
  bars into H4 and D1 bars with fixed UTC boundaries.
- SIG-021 (mirror): reflect each history around a constant; assert zone types
  swap, edges reflect, and signals swap side with reflected levels, within
  `PriceMath` tolerance.

The generator lives in the test project, takes a seed, and prints it on
failure.

## Out of scope

Spread, news, sessions, sizing, orders. The engine knows none of them.

## Definition of done

- [ ] Tests first, all green; coverage thresholds met.
- [ ] `SignalEngine` public surface matches `docs/architecture.md` exactly.
- [ ] The replay and determinism tests run in under 60 seconds in CI.
- [ ] All `AGENTS.md` commands clean; outputs in the pull request.
- [ ] The ten requirement statuses are `implemented`.
- [ ] Under "Decisions for the human": anything the property tests revealed
      that the spec does not settle.
