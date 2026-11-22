# WP25: Breakout tracker (signal layer, not yet wired)

## Read first

`AGENTS.md`; `docs/spec.md` section 0 (changes in v0.10), section 3
(introduction), 3.1 and 3.7; `docs/architecture.md` section 3 (the block
marked "Spec v0.10, WP25") and section 10 (the rows of 2026-11-22, above all
"Breakout signal layer"); the requirement texts below. Depends on nothing.
WP26 wires what you build into `SignalEngine`.

## Requirements to implement

SIG-022, SIG-023, SIG-024, SIG-025.

Owner request of 2026-11-22: a second entry mode. This package builds only
the pure logic of spec 3.7 as new types. It changes no existing file under
`src/`, so the ZoneFvg mode cannot change. If you find that you need to
change an existing source file, stop and ask.

## Allowed paths

- new files in `src/ZoneFvg.Core/Signals/`: `BreakoutParameters.cs`,
  `BreakoutReference.cs`, `BreakoutSetup.cs`, `BreakoutTracker.cs`
- `tests/ZoneFvg.Core.Tests/**` (new files only)
- the `status:` lines of the four requirements above

## Owner authorisation to change existing tests

None. `ClosedBarsOnlyApiTests` scans every public type of the `Signals`
namespace by reflection: no public member or parameter of the new types may
be named forming, current, partial or tick, or contain Forming, Partial or
Tick. `NamespaceDependencyTests` allows `Signals` to use `Domain` and
`Indicators` only. If an existing test fails, stop and ask.

## What to build, in this order

1. **`BreakoutParameters`** (record, immutable, same style as
   `SignalParameters`): `EntryBufferAtr` (default 0.1), `StopAtrMult`
   (default 1.0), `WindowStartUtc` (default 07:00), `WindowEndUtc` (default
   17:00). `Validate()` checks the values in that order and throws
   `ArgumentOutOfRangeException` naming the parameter, with the rule and the
   rejected value in invariant formatting: `EntryBufferAtr` finite and not
   negative; `StopAtrMult` finite and greater than zero; each window time
   from 00:00 up to but not including 24:00; `WindowEndUtc` after
   `WindowStartUtc`.
2. **`BreakoutReference(DateTime DayOpenUtc, double High, double Low, bool
   Armed)`** and **`BreakoutSetup(string Id, Side Side, double Entry, double
   Stop, double Target, double Level, DateTime ConfirmationCloseUtc, double
   AtrH4)`**: records, data only.
3. **`BreakoutTracker`** with exactly the public surface of
   `docs/architecture.md` section 3:
   - Constructor `(BreakoutParameters parameters, double rewardRisk)`:
     validates the parameters; `rewardRisk` must be finite and greater than
     zero (`ArgumentOutOfRangeException` otherwise).
   - `Reference`: null until the first D1 bar.
   - `OnD1Closed(Bar bar)` (SIG-022): the bar becomes the reference
     (`DayOpenUtc` = its open time, its high and low, `Armed` = true),
     replacing the old one. A bar that does not open after the current
     reference's bar throws `InvalidOperationException` and changes nothing.
   - `OnM15Closed(Bar bar, Bias bias, double? atrM15, double? atrH4)`
     (SIG-022 to SIG-025). Returns null and changes nothing when there is no
     reference, the reference is not Armed, the bar is not a checked bar,
     the bias is Neutral, or the close does not confirm. A checked bar:
     its open time is at or after `Reference.DayOpenUtc` + 24 hours, and the
     time of day of (its open time + 15 minutes) is at or after
     `WindowStartUtc` and before `WindowEndUtc`. Confirmation: bias Long and
     close above `High`, or bias Short and close below `Low`, with
     `PriceMath.GreaterThan` / `LessThan` and `PriceMath.DefaultTolerance`.
     On a confirmation the reference becomes Done (`Armed` = false) first.
     Then, if `atrM15` or `atrH4` is null, not finite or not greater than
     zero, or a computed level is not finite or not greater than zero,
     return null (fail closed). Otherwise return the setup of SIG-024 with
     the id of SIG-025.
4. The tracker reads no clock, keeps no bar history, and its only state is
   the reference. Use `CultureInfo.InvariantCulture` for the id.

## Decisions already made

- The bias is passed in at every M15 close. The tracker does not compute it.
- A close exactly at the level does not confirm. A wick beyond the level
  with a close inside does not confirm.
- A close beyond the level on the side the bias does not trade changes
  nothing: the reference stays Armed.
- The M15 bar that closes at the same instant as the D1 bar opened before
  the reference closed, so it is never a checked bar for that reference.
- A reference finished without a result (missing ATR) is not armed again.
- Bars are not sorted or checked for order beyond the D1 rule in item 3.

## Tests required

Worked example for all tests unless stated: reference D1 bar opens
2026-03-02 22:00 UTC (a Monday) with open 1.0800, high 1.0850, low 1.0780,
close 1.0840; `EntryBufferAtr` 0.1, `StopAtrMult` 1.0, window 07:00 to
17:00, `rewardRisk` 2.0; ATR(M15) 0.0010, ATR(H4) 0.0040. Compare prices
with `PriceMath` and its default tolerance.

**SIG-022**
- Before any D1 bar: `Reference` is null and `OnM15Closed` returns null for
  a bar that would confirm.
- After `OnD1Closed`: `Reference` is (2026-03-02 22:00, 1.0850, 1.0780,
  Armed).
- Window, bias Long, bars with close 1.0858 on 2026-03-04: the bar opening
  06:30 (closes 06:45) is not checked; opening 06:45 (closes 07:00) is
  checked and confirms; opening 16:30 (closes 16:45) is checked; opening
  16:45 (closes 17:00) is not checked.
- Rule (a), with the window set to 00:00 to 23:00: the bar opening
  2026-03-03 21:45 (closes 22:00, with the D1 bar) is not checked; the bar
  opening 2026-03-03 22:00 is checked.
- A second D1 bar replaces the reference: after a confirmation the new
  reference is Armed with the new values; an unconfirmed reference is
  replaced without a result.
- A D1 bar that opens at or before the current reference's open time throws
  `InvalidOperationException`; `Reference` is unchanged.

**SIG-023** (bar opening 2026-03-04 08:00 unless stated)
- Long, close 1.0858: confirms; `Reference.Armed` is false afterwards.
- Long, close exactly 1.0850: null, still Armed. Close 1.08501: confirms.
- Long, high 1.0862 and close 1.0849: null, still Armed.
- Short, close 1.0772: confirms a sell. Close exactly 1.0780: null. Close
  1.07799: confirms.
- Neutral, close 1.0858: null, still Armed.
- Long, close 1.0772 (below the low): null, still Armed; a later bar with
  close 1.0858 confirms.
- After a confirmation a second bar with close 1.0870 returns null.

**SIG-024**
- Buy: bar opening 2026-03-04 08:00 with open 1.0845, high 1.0862, low
  1.0843, close 1.0858 gives entry 1.0863, stop 1.0823, target 1.0943,
  level 1.0850, confirmation close 2026-03-04 08:15, AtrH4 0.0040.
- Sell: bias Short, bar opening 2026-03-04 09:30 with open 1.0785, high
  1.0788, low 1.0768, close 1.0772 gives entry 1.0767, stop 1.0807, target
  1.0687, level 1.0780, confirmation close 2026-03-04 09:45.
- `EntryBufferAtr` 0: buy entry 1.0862. `StopAtrMult` 1.5: buy stop 1.0803
  and target 1.0983. `rewardRisk` 3.0: buy target 1.0983.
- ATR(M15) null, ATR(H4) null, 0, negative, NaN and infinity, each alone:
  the result is null and the reference is Done.
- `Validate`: each rejection of item 1 throws and names its parameter; the
  defaults pass. Constructor: `rewardRisk` 0, negative, NaN and infinity
  throw; null parameters throw `ArgumentNullException`.

**SIG-025**
- Ids: `BH-20260302-2200` for the buy and `BL-20260302-2200` for the sell
  above. A reference opened 2026-11-05 21:00 gives `BH-20261105-2100`.
- One result per reference: over a day with three bars closing above the
  high, exactly one result.
- Determinism: 20 seeded histories from `RandomWalkHistory.Generate` (at
  least 6,000 M15 bars each). A test helper drives a tracker the way the
  engine will: bars in intake order (`BarIntakeOrder`), each D1 bar to
  `OnD1Closed`, each M15 bar to `OnM15Closed` with the bias from
  `BiasCalculator.Compute` over the D1 bars so far (use an EMA period of 5
  so that the bias is not always Neutral) and the ATR values from
  `Atr.Value` over the M15 and H4 bars so far. Two fresh trackers give
  identical result lists, every double compared bit for bit. Print the seed
  on failure. At least 10 of the 20 histories must produce a result, or the
  test fails as not proving anything.
- Mirror: for each history, `MirroredFeed(pivot)` with the bias swapped
  (Long for Short) gives, result for result, the other side, the other id
  prefix with the same date and time, and entry, stop, target and level
  reflected around the pivot, within `PriceMath` tolerance; AtrH4 and the
  confirmation close are equal.

## Out of scope

`SignalEngine`, `Signal`, orders, spread, news, sessions of the execution
layer, the parameter hash and the cBot. WP26 and WP27 do those.

## Definition of done

- [ ] Tests first, all green in CI; coverage thresholds met; the new files
      are fully covered.
- [ ] No existing file under `src/` changed.
- [ ] Every command of `AGENTS.md` run and its output pasted in the pull
      request (this session may have no .NET SDK: then say so, and paste
      the CI output instead).
- [ ] SIG-022 to SIG-025 are `implemented` (only if "Decisions for the
      human" is empty).
