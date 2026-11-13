# WP03 — FVG detection, signal engine, determinism and replay

## Read first

`AGENTS.md`, `docs/architecture.md` (sections 2, 3), `docs/spec.md` sections
3.3 to 3.5, 8.3 and 9.4, and the requirement texts below.

## Requirements to implement

SIG-002, SIG-016, SIG-017, SIG-018, SIG-019, SIG-021.

## Allowed paths

`src/ZoneFvg.Core/Domain/Fvg.cs`, `src/ZoneFvg.Core/Domain/Signal.cs`,
`src/ZoneFvg.Core/Signals/FvgDetector.cs`, `src/ZoneFvg.Core/Signals/SignalEngine.cs`,
`tests/ZoneFvg.Core.Tests/**`, and the `status:` lines of the requirements
above. You may add members to `ZoneBook` only if listed under "What to build".

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
   - While `IsReady` is false, bars are stored but no zone or signal is
     produced.
4. The engine must not depend on wall-clock time, culture, or the order in
   which unrelated zones were inserted.

## Decisions already made

- An FVG is evaluated when c1 closes. c3 must have opened at or after the
  touch bar opened, so the earliest possible confirmation is two bars after
  the touch bar.
- If two zones would signal on the same bar, both signals are returned,
  ordered by zone id. The execution layer will take at most one per symbol.
- When bias does not match, the zone is still consumed (SIG-018).

## Tests required

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
- [ ] The six requirement statuses are `implemented`.
- [ ] Under "Decisions for the human": anything the property tests revealed
      that the spec does not settle.
