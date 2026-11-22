# WP27: Breakout mode end to end in the core, and across a restart

## Read first

`AGENTS.md`; `docs/spec.md` section 0 (changes in v0.10) and sections 3.3,
3.7, 5.3, 8.3 and 8.5; `docs/architecture.md` section 3 (the note on
`BotParameters`) and section 10 (rows of 2026-11-22: "Entry mode",
"Parameter hash in the two modes", "Journal in Breakout mode"); the
requirement text below; `BotCore`, `BotParameters`, `RecoveryService` and
`tests/.../TestSupport/RecoverySupport.cs`. Depends on WP25 and WP26.

## Requirements to implement

REC-017.

Owner request of 2026-11-22. This package switches the mode on inside the
core: with breakout parameters set, one instance runs the Breakout engine
live and in recovery. **With `Breakout` null nothing may change:** every
existing test is the proof, and none of them may be touched.

## Allowed paths

- `src/ZoneFvg.Core/App/BotParameters.cs`, `BotCore.cs` and a new
  `src/ZoneFvg.Core/App/EntryMode.cs`
- `src/ZoneFvg.Core/Recovery/RecoveryService.cs`
- `tests/ZoneFvg.Core.Tests/**` (new files, and new helpers in
  `TestSupport/RecoverySupport.cs`)
- the `status:` line of REC-017

## Owner authorisation to change existing tests

None. New helpers may be added to `RecoverySupport.cs`; no existing helper,
default value or assertion changes. If an existing test fails, stop and
ask.

## What to build, in this order

1. **`BotParameters.Breakout`** (`BreakoutParameters?`, default null) and
   **`EntryMode { ZoneFvg, Breakout }`** in `App`, with a read-only
   `BotParameters.Mode` (Breakout when `Breakout` is set). `Validate`: when
   `Breakout` is set, validate it and require `WindowStartUtc` and
   `WindowEndUtc` to equal `Execution.SessionStartUtc` and
   `Execution.SessionEndUtc`; otherwise an `ArgumentException` that names
   `Breakout` and both pairs of times.
2. **`RecoveryService`**: one optional last constructor argument
   `BreakoutParameters? breakout = null`. It is used for the engine of the
   replay (`new SignalEngine(signalParameters, breakout)`) and for the
   parameter hash. Nothing else changes: a breakout id is known because the
   replay returned a signal with it, exactly as a signalled zone is known
   today.
3. **`BotCore`** passes `parameters.Breakout` to `ParameterHash.Compute`,
   `PreTradeFilters` and `RecoveryService`. In Breakout mode the start line
   gets one more field at its end, `mode=Breakout`; in ZoneFvg mode the
   line is unchanged. No other line changes. `ZoneSnapshot` and
   `FvgSnapshot` are empty in Breakout mode (the engine has none).
4. Nothing else. The duties, guards, states, news refresh, trade journal,
   trade log and operations code are mode-free already. If you find a
   place that needs the mode, stop and ask.

## Tests required

Use `BotHarness` with `RecoveryData.Parameters() with { Breakout = new
BreakoutParameters() }` and a bar set you build for the purpose (add a
helper beside `RecoveryData.Bars`): enough D1, H4 and M15 history to be
ready, a D1 history that gives bias Long, a reference day, and M15 bars on
the next day of which one closes above the reference high inside 07:00 to
17:00 UTC on a day that is not a Friday. Set `Bid` and `Ask` on the symbol.
Add the mirror set for a sell where a test says so.

**REC-017**
- Live buy: the bar that closes above the reference high leads to exactly
  one `PlaceStopOrder` request with the label
  `ZFVG|v1|<symbol>|BH-<date of the reference>`, stop loss, take profit and
  an expiry 8 bars later; the signal line has `kind=breakout`; no
  `PlaceLimitOrder` request is sent. A `Tick` through the entry fills it;
  the position keeps stop and target; a `Tick` to the target closes it and
  the trade journal and the `tradelog` line carry the breakout id.
- No second order on later bars of the same day. The next D1 bar gives a
  new reference, and a new close above it gives a new order with the new
  id.
- Live sell: the mirror set gives one sell stop with the `BL-` id and the
  spread added to its stop.
- A bar that closes above the reference high at 06:30 (outside the window)
  gives no signal; the first bar that closes above it from 07:00 on does.
- Replay: after a restart the signals of earlier days are journaled as
  skipped with `SignalFromReplay`, and no request is sent for them.
- Restart with the stop order pending: the recovery report lists it as
  kept, no cancel and no new order are sent, the state is Running, and the
  order later fills and is managed as above.
- Restart with the position open: adopted (not a stranger), protection
  checked, no new order on the symbol.
- Restart in ZoneFvg mode (`Breakout` null) with a breakout position and a
  breakout pending order at the broker: the position is a stranger, kept
  with its stop and target ("parameter hash ... differs" or "zone ...
  unknown"), and the order is cancelled. And the other way round: in
  Breakout mode a position labelled with a zone id is a stranger.
- A pending order whose `BH-` id the replay did not produce is cancelled
  ("unknown or ended").
- Breakout parameters with other values (`StopAtrMult` 1.5) after a
  restart: the earlier position is a stranger because the hash differs.
- Hash: the recovery report and the comment of a new order carry
  `119a3864` with all defaults in Breakout mode (if the harness defaults
  differ from the spec defaults, compute the expected value with
  `ParameterHash.Compute` and say so in the pull request) and the v0.9
  value in ZoneFvg mode.
- Validation: a window that differs from the session by one minute, at
  either end, makes the `BotCore` constructor throw `ArgumentException`
  naming `Breakout`; equal times are accepted; an invalid
  `BreakoutParameters` value throws its own exception.
- Start line: in Breakout mode it ends with ` mode=Breakout`; in ZoneFvg
  mode it contains no `mode=`.
- Friday: a confirmation at Friday 12:15 UTC is skipped with
  `FridayNoEntry`; a pending stop order is cancelled by the Friday close.
- News: a confirmation whose bar overlaps a news window is journaled as
  skipped with `NewsBreakoutBar` and nothing is sent.
- Halted and Blocked: a confirmation in either state is skipped with
  `BotNotRunning`.
- Backtest flag (REC-014): with `BacktestHaltsLogOnly` the breakout order
  is still placed when the daily budget is exhausted, and one `would-block`
  line is written, as for a ZoneFvg signal.
- `ScenarioAssert.Global` holds in every scenario: no duplicate order, no
  position without a stop.
- ZoneFvg regression: no existing test changed; state it in the pull
  request with the test count before and after.

## Out of scope

The cBot's parameters, deployment files and guides (WP28); chart drawing
for breakouts; daily counts for breakout references; any trading rule.

## Definition of done

- [ ] Tests first, all green in CI; coverage thresholds met.
- [ ] No existing test changed.
- [ ] Every command of `AGENTS.md` run and its output pasted (without a
      .NET SDK in the session: say so and paste the CI output).
- [ ] REC-017 is `implemented` (only if "Decisions for the human" is
      empty).
- [ ] A plain-language summary for the owner: what the bot now does in
      Breakout mode from bar to order to exit, and that ZoneFvg mode is
      untouched.
