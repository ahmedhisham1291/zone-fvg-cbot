# WP01 — Core primitives: bars, indicators, swings, bias, bar intake

## Read first

`AGENTS.md`, `docs/architecture.md` (sections 1, 3, 4, 5), `docs/spec.md`
section 3 (introduction, 3.1, 3.2 rule 2) and section 9.4.

## Requirements to implement

SIG-001, SIG-003, SIG-005, SIG-006, PLT-001, PLT-004.

WP01 also builds the parts of SIG-004 and SIG-020 that need no engine: the
per-timeframe order check in `BarStore`, the same-instant helper
`BarIntakeOrder` and the warm-up calculation. Tag those tests with SIG-004 and
SIG-020, but leave both status lines at `planned`. Both requirements are
completed and marked in WP03, where the engine exists.

## Allowed paths

`src/ZoneFvg.Core/Domain/**`, `src/ZoneFvg.Core/Indicators/**`,
`src/ZoneFvg.Core/Signals/BarStore.cs`, `src/ZoneFvg.Core/Signals/SignalParameters.cs`,
`tests/ZoneFvg.Core.Tests/**`, and the `status:` lines of the requirements
above.

## What to build

1. **Domain types** exactly as in `docs/architecture.md` section 3: `TimeFrame`,
   `Side`, `ZoneType`, `ZoneState`, `Bias`, `Bar`. Validate a `Bar` on
   construction: UTC kind, finite numbers, `High >= max(Open, Close)`,
   `Low <= min(Open, Close)`, `High >= Low`. Invalid input throws
   `ArgumentException`.
2. **`PriceMath`** (static, pure): `AlmostEqual(a, b, tolerance)`,
   `GreaterThan`, `LessThan` with an explicit tolerance argument;
   `RoundDownToStep(value, step)`; `ToPips(priceDistance, pipSize)`.
   The default tolerance constant is `1e-9`.
3. **`BarStore`**: one bounded, ordered buffer per timeframe. `Add(timeFrame, bar)`
   enforces SIG-004 (strictly increasing open time; otherwise throws
   `InvalidOperationException` and leaves state unchanged). Capacity per
   timeframe is the warm-up requirement plus a margin. Indexer `Last(timeFrame, n)`
   returns the n-th most recent closed bar (0 = newest).
4. **`Atr`**: `double? Value(IReadOnlyList<Bar> bars, int endIndex, int period = 14)`
   per SIG-003. Returns null when fewer than `period + 1` bars are available.
5. **`Ema`**: `double? Value(IReadOnlyList<Bar> bars, int endIndex, int period)`
   over exactly `4 * period` closes per SIG-003. Null when the window is not
   full.
6. **`SwingDetector`**: given the H4 buffer, returns confirmed swing highs and
   lows per SIG-006 (strictly greater or lower than the three bars on each
   side; confirmed when the third bar to the right has closed; retained only
   within the last 100 bars).
7. **`BiasCalculator`**: SIG-005.
8. **`SignalParameters`**: an immutable record holding every signal-layer
   parameter of spec section 7 with its default, and a `Validate()` method
   that rejects out-of-range values with a message naming the parameter.
9. **Warm-up** (SIG-020): `WarmupCalculator.For(SignalParameters)` returns the
   bars needed per timeframe. Derive it from the parameters: D1 needs
   `4 * BiasEmaPeriod + 1`; H4 needs `MaxZoneAgeBars + 100 + 2 * SwingStrength + MaxImpulseBars + MaxBaseBars + AtrPeriod + 2`;
   M15 needs `(MaxZoneAgeBars + 1) * 16 + ConfirmWindowBars + AtrPeriod + 2`.
   Assert in a test that the defaults stay within 300 / 500 / 3,000.
10. **Test helper `BarBuilder`** in the test project: fluent construction of
    bar sequences (`At(time).Bullish(open, close)`, `.Range(low, high)`,
    `.Then(...)`), with times advancing by the timeframe's length.

## Tests required

Tag each with the requirement id.

- SIG-003: ATR against a hand-computed 15-bar example; ATR unchanged when bars
  older than the window are altered; null with 14 bars. EMA against a
  hand-computed example with period 3 (window 12); EMA unchanged when bars
  older than the window are altered; null when the window is one bar short.
- SIG-004: duplicate time rejected; earlier time rejected; state unchanged
  after rejection; a documented helper orders same-instant bars D1, H4, M15.
- SIG-005: above, below, exactly equal (Neutral), window not full (Neutral),
  recomputed only on D1 close.
- SIG-006: a clean swing; equal highs are not a swing (strictness); not
  confirmed until the third right-hand bar closes; a swing 101 bars old is
  dropped; mirror for lows.
- SIG-020: defaults within limits; requirement grows when `BiasEmaPeriod` is 200.
- SIG-001: there is no public API that accepts a forming bar; a test documents
  this by reflection over public method names and parameter types of the
  `Signals` and `Indicators` namespaces (no parameter named `forming`,
  `current` or `partial`, no `OnTick`).
- `Bar` validation: each invalid case throws.
- `PriceMath`: boundaries of each helper.

PLT-001 and PLT-004 are proven by the build and by
`tools/check_architecture.py`; no tagged test is needed.

## Out of scope

Zones, FVGs, signals, anything outside the allowed paths.

## Definition of done

- [ ] Tests written before implementation, all green.
- [ ] Every command in `AGENTS.md` runs clean; outputs in the pull request.
- [ ] Coverage of `ZoneFvg.Core` at least 90% lines and 80% branches.
- [ ] The six requirement statuses are `implemented`. SIG-004 and SIG-020 stay
      `planned` (see "Requirements to implement").
- [ ] No file outside the allowed paths changed.
