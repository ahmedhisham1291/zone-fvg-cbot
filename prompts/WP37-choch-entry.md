# WP37: CHoCH entry model (the owner's manual method)

## Read first

`AGENTS.md`; `docs/spec.md` 0 (row O), 3 (the paragraph "Entry model of the ZoneFvg mode"), 3.1 to 3.6, 3.9, 5.1 to 5.3, 6.1, 7, 11, 14 (question 5); SIG-035 to SIG-041, EXE-027, EXE-028, EXE-029, NWS-015, OPS-011, REC-020, PLT-016 and, for what they refer to, SIG-006, SIG-014, SIG-017 to SIG-021, EXE-003, EXE-006, EXE-019, EXE-022, NWS-009, NWS-014, OPS-010, REC-017 in `docs/requirements.yml`; `docs/architecture.md` section 10 (row "Owner's manual method: the CHoCH entry model"); `prompts/WP26-breakout-execution.md` and `prompts/WP34-session-range-wiring.md` (how a new kind was wired).

Code to reuse, not to copy: `Indicators/SwingDetector.cs` (M15 swings: call `Find` on the M15 bars with `SwingStrength`), `Signals/ZoneDetector.cs`, `Signals/ZoneBook.cs` (touch, `ExtremeSinceTouch`, invalidation, the confirmation window, `TouchedZones`), `Signals/SignalEngine.cs` (`OnM15Closed`, `Confirm`, `BiasAllows`, the Breakout and SessionRange branches), `Signals/FvgDetector.cs` (`BuildSignal`: the stop rule and ATR use), `Indicators/Atr.cs`, `Domain/PriceMath.cs`, `Execution/PreTradeFilters.cs` (`CheckStopBounds`, `CheckRoom`, the `EntryAlreadyReached` check of EXE-019), `Execution/OrderManager.cs`, `Execution/ParameterHash.cs`, `Ops/SignalJournalEntry.cs`, `App/BotParameters.cs`, `App/BotCore.cs`, `src/ZoneFvg.Bot/ZoneFvgBot.cs`, `src/ZoneFvg.Bot/ParameterText.cs`; tests: `TestSupport/BarBuilder.cs`, `TestSupport/EngineScenarios.cs`, `TestSupport/EngineFeed.cs`, `TestSupport/ZoneScenarios.cs`, `Fakes/FakeBroker`.

## Requirements

SIG-035, SIG-036, SIG-037, SIG-038, SIG-039, SIG-040, SIG-041, EXE-027, EXE-028, EXE-029, NWS-015, OPS-011, REC-020, PLT-016.

## Split (size limit)

The package is too large for one pull request of about 800 changed lines. Run it as three pull requests, in this order, each from an up-to-date `main`, each with only its ids:

| Part | Branch | Ids | Content |
|---|---|---|---|
| WP37a | `wp/37a-choch-signals` | SIG-035 to SIG-041 | Parameters, the CHoCH rules in the signal layer, the engine |
| WP37b | `wp/37b-choch-execution` | EXE-027, EXE-028, EXE-029, NWS-015, OPS-011, REC-020 | Orders, checks, news, journal, hash, recovery, all-day window |
| WP37c | `wp/37c-choch-cbot` | PLT-016 | cBot parameters, parameter files, backtest guide |

If a part still goes over about 800 changed lines (tests included), split it once more by requirement id (for example WP37a1 with SIG-035 to SIG-038, WP37a2 with SIG-039 to SIG-041) and say so in the pull request. Never move a requirement into a part of another layer.

## Allowed paths

- WP37a: `src/ZoneFvg.Core/Signals/**`, `src/ZoneFvg.Core/Domain/Signal.cs`, `src/ZoneFvg.Core/Domain/SignalKind.cs`, `src/ZoneFvg.Core/App/BotParameters.cs` (only the new parameter record and its validation), new files under `src/ZoneFvg.Core/Signals/`; new test files under `tests/ZoneFvg.Core.Tests/`; a helper under `tests/ZoneFvg.Core.Tests/TestSupport/` only by adding to it; `docs/requirements.yml` (status lines of SIG-035 to SIG-041).
- WP37b: `src/ZoneFvg.Core/**`; new test files under `tests/ZoneFvg.Core.Tests/`; an existing test file only where a test lists every value of `SignalKind`, `SkipReason` or `ZoneEndReason` or every property of `BotParameters` (name each such edit and its reason; no assertion is weakened); `docs/requirements.yml` (status lines of the six ids).
- WP37c: `src/ZoneFvg.Bot/**`; `deploy/**`; `docs/backtest-howto.md`; `docs/runbook.md`; `README.md`; new test files under `tests/ZoneFvg.Core.Tests/` if the cBot needs a small pure helper in the core (say which and why); `docs/requirements.yml` (status line of PLT-016).

No existing test changes its expectations in any part. Every Fvg-model, Breakout and SessionRange test, scenario and hash value must pass unchanged. If one fails, stop and ask.

## Build

### WP37a (signals)

1. A parameter record (suggested name `ChochParameters`) with `RequireSweep` false, `WindowBars` 32, `UseBias` false, `StopAtDistal` false, `TargetBufferAtr` 0.1, `MinStopPips` 5.0, `MinRewardRisk` 2.0, and `Validate()` with the ranges of SIG-035 and messages in the style of `BreakoutParameters`. `BotParameters.Choch`: null is the Fvg model; a value is the Choch model and is refused together with `Breakout` or `SessionRange` (SIG-035). `EntryModel` itself is the cBot's name for "Choch is set"; the core needs no separate enum unless it makes the start line simpler (decide and say so).
2. `SignalKind.Choch`. `Signal` gains what SIG-039 asks for that it lacks (suggested: `Swept` as `bool?`, null for other kinds, and `TargetSource` as `string?`); `Level` is the entry for this kind.
3. In the engine, with Choch parameters: H4 bars as in the Fvg model (zones, swings, ageing). The zone book's confirmation window is `WindowBars` (SIG-036): pass it in without changing the book's behaviour for the Fvg model. At each M15 close, after `ZoneBook.OnM15Closed`, for each Touched zone in id order: find the M15 swings once per bar (not once per zone), apply SIG-037 and SIG-038, and on a confirmation build the signal of SIG-039 and SIG-040 and consume the zone exactly as `Confirm` does (Signalled then Used; with `UseBias` true and a mismatch, Used and `ZoneEndReason.BiasMismatch`). Then `EndConfirmationWindows`; a window that ends without a CHoCH is reported with a new `ZoneEndReason.NoChoch` in this model only (the Fvg model keeps `NoFvg`).
4. "No bar after H's bar and before c1 closed above H" (SIG-037) and the sweep (SIG-038) are decided from the stored M15 bars only, so a replay reaches the same answer (SIG-041). The sweep needs, for each bar b from the touch bar to c1, the most recent swing low confirmed on the bars up to b: compute it from the bars, do not keep a list that a restart would lose.
5. H4 swings for the target (SIG-040) are `SwingDetector.Find` on the stored H4 bars with `SwingStrength`; opposing zones are `ZoneBook.Zones` of the other type. ATR(H4) for the buffer is the latest value at c1, as in the stop rule.
6. Warm-up (SIG-041): the M15 count covers the 100-bar swing lookback plus `WindowBars` plus the swing strength, if it does not already. Report the new number in the pull request.
7. Not in WP37a: orders, filters, journal, hash, recovery, cBot. With Choch parameters the execution layer must not yet be able to receive a Choch signal: `BotParameters.Validate()` refuses `Choch` until WP37b with a message that says the model is not wired yet (fail closed). WP37b removes that refusal.

### WP37b (execution, journal, hash, recovery, window)

1. EXE-027: Choch signals are placed as limit orders through the same path as ZoneFvg signals. EXE-022's table gains (Choch, Limit). Input checks and the "entry already reached" check for a limit order of this kind (a buy limit at or above the ask fills at once and is not the retest the owner waits for).
2. EXE-028: `ChochMinStopPips` in `CheckStopBounds` for this kind; no `CheckFvgSpread` and no `CheckRoom`; the reward check at the place of `CheckRoom`, with `SkipReason.RewardRiskTooLow`, measured on the signal's own levels.
3. EXE-029: `ExecutionParameters.Validate` accepts `SessionStartUtc` 00:00 and `SessionEndUtc` exactly 24:00; nothing else changes in `CheckSession` (a time of day is always below 24:00). Breakout's own window check keeps refusing 24:00.
4. NWS-015: the confirming bar's news check as NWS-014 does it for Breakout, with `SkipReason.NewsChochBar`.
5. OPS-011: the journal line; the zone counts of the daily summary name `nochoch` (or the existing style's equivalent) in this model.
6. REC-020: `ParameterHash` appends, only in this model and after every existing field, `EntryModel=Choch;RequireSweep=…;ChochWindowBars=…;ChochUseBias=…;ChochStopAtDistal=…;ChochTargetBufferAtr=…;ChochMinStopPips=…;MinRewardRisk=…;` in the formats the hash already uses. Recovery replays this model; the start line ends with `mode=ZoneFvg model=Choch`.

### WP37c (cBot and guides)

1. Group "Entry model" with the eight parameters of PLT-016 and their defaults; checked at start in every mode; Choch with an `EntryMode` other than ZoneFvg refused with a message naming both parameters.
2. `SessionStartUtc` "00:00" and `SessionEndUtc` "24:00" parse (24:00 only for the end).
3. The eight lines in all five parameter files under `deploy/`, with the defaults (gold: `ChochMinStopPips=3`).
4. `docs/backtest-howto.md`, Part 6 (written in the plan): check that every name and value in it matches the cBot as built, and correct the guide if not.
5. Say in the pull request whether the cBot loads enough M15 history for the new warm-up at the start of a backtest.

## Tests (tagged), with concrete numbers

The reference scenario is the zone of `EngineScenarios`: demand zone `D-20260301-0800`, proximal 100.2, distal 99.5, active from Monday 2026-03-02 00:00 UTC, ATR(H4) at the M15 bars 15 / 14 = 1.0714286 when no H4 bar follows the leg (if your feed adds H4 bars, recompute and write the value in the test). Before the activation the M15 bars are the flat bars (100.6, 100.65, 100.55, 100.6), true range 0.1. Bars below are (open, high, low, close); m0 opens at the activation.

**Tail A (CHoCH without sweep):**

| Bar | OHLC | Note |
|---|---|---|
| m0 | 100.6, 100.7, 100.55, 100.65 | |
| m1 | 100.65, 100.8, 100.6, 100.75 | |
| m2 | 100.75, 100.9, 100.7, 100.85 | |
| m3 | 100.85, 101.0, 100.8, 100.9 | swing high 101.0, confirmed at the close of m6 |
| m4 | 100.9, 100.95, 100.6, 100.65 | |
| m5 | 100.65, 100.7, 100.4, 100.45 | |
| m6 | 100.45, 100.5, 100.25, 100.3 | |
| m7 | 100.3, 100.35, 100.1, 100.15 | touch (low 100.1 at or below 100.2): bar 0 |
| m8 | 100.15, 100.2, 99.9, 100.0 | lowest low since the touch, 99.9; a swing low confirmed at m11 |
| m9 | 100.0, 100.3, 99.95, 100.25 | |
| m10 | 100.25, 100.6, 100.2, 100.55 | |
| m11 | 100.55, 101.1, 100.5, 101.05 | closes above 101.0: CHoCH, bar 4 of the window |

True ranges m0 to m11: 0.15, 0.2, 0.2, 0.2, 0.35, 0.3, 0.25, 0.25, 0.3, 0.35, 0.4, 0.6; with two flat bars of 0.1, ATR(M15) at m11 = 3.75 / 14 = 0.26785714. Buy signal: entry 101.0, Level 101.0, stop 99.9 - 0.026785714 = 99.87321429, stop distance 1.12678571, Swept no. With no opposing level above 101.0 (the H4 swing high of the scenario is 101.0, not strictly above): target 101.0 + 2 x 1.12678571 = 103.25357143, source `default`, reward 2.0 (passes EXE-028 exactly at the limit). With `ChochStopAtDistal` true: stop 99.5 - 0.026785714 = 99.47321429.

**Tail B (lower high, sweep, CHoCH):** m0 to m10 as tail A, then

| Bar | OHLC | Note |
|---|---|---|
| m11 | 100.55, 100.65, 100.3, 100.35 | swing low at m8 (99.9) confirmed; m11 becomes a swing high at 100.65, confirmed at m14 |
| m12 | 100.35, 100.4, 99.8, 99.85 | sweep: low 99.8 below the swing low 99.9 |
| m13 | 99.85, 100.3, 99.85, 100.25 | |
| m14 | 100.25, 100.6, 100.2, 100.55 | 100.65 is now the most recent swing high |
| m15 | 100.55, 100.8, 100.5, 100.75 | closes above 100.65: CHoCH, bar 8 of the window |

ATR(M15) at m15 = 4.7 / 14 = 0.33571429 (true ranges m2 to m15: 0.2, 0.2, 0.35, 0.3, 0.25, 0.25, 0.3, 0.35, 0.4, 0.35, 0.6, 0.45, 0.4, 0.3). Buy signal: entry 100.65, stop 99.8 - 0.033571429 = 99.76642857, stop distance 0.88357143, Swept yes, default target 102.41714286.

Check these numbers by hand before you rely on them; if one is wrong, stop and ask (do not adjust the rule to the number).

- **SIG-035:** defaults; every `Validate` rule at its boundary (ChochWindowBars 3, 4, 96, 97; ChochTargetBufferAtr -0.0001, 0, 1, 1.0001, NaN; ChochMinStopPips 0 and the smallest positive value; MinRewardRisk 0.9999, 1, 10, 10.0001) with the message; Choch with Breakout and with SessionRange refused; with Choch null the engine's signals on the existing reference histories are the same, field by field.
- **SIG-036:** a CHoCH on bar 32 of the window gives a signal, on bar 33 none and the zone is Used with reason NoChoch (move m11 of tail A later with flat bars); the Fvg window (16) does not end a Choch zone; invalidation by a close below 99.5 before the CHoCH; one signal per zone (a second break later gives nothing); `UseBias` false gives the buy under a Short bias, true gives none and BiasMismatch.
- **SIG-037:** tail A: m11 with close 101.0 (equal) confirms nothing, close 101.0 plus one tick (0.00001 above PriceMath's tolerance as the existing tests do it) confirms; tail B: the level is 100.65, not 101.0; a most recent swing high already closed above confirms nothing at c1 (build it); a missing ATR(M15) (fewer than 15 M15 bars) confirms nothing and the window goes on; supply mirror.
- **SIG-038:** tail B with `RequireSweep` true gives the signal at m15; with m12's low at 99.9 (equal) there is no sweep: Swept no, and with `RequireSweep` true no signal at m15; tail A with `RequireSweep` true gives no signal; a sweep on the touch bar itself counts; a sweep on c1 counts.
- **SIG-039:** both stops of tail A; tail B's stop; the sell mirror of tail A around `EngineScenarios.Pivot` 200.0: supply zone `S-20260301-0800`, sell limit at 99.0, stop 100.12678571, default target 96.74642857.
- **SIG-040:** tail A with a supply zone whose proximal is 103.5 (build it, or an H4 swing high at 103.5): target 103.5 - 0.1 x ATR(H4); with ATR(H4) = 15 / 14 that is 103.39285714, reward 2.39285714 / 1.12678571 = 2.12361; a level at 103.36071429 gives a reward of exactly 2.0 (EXE-028 passes) and a level one tick lower gives less (EXE-028 skips); a level at 101.0 exactly is not counted (strict); zone and swing at the same price: source `zone:<id>`; the nearest of several levels is used; no level: `default`; sell mirror.
- **SIG-041:** determinism; replay equals live for tails A and B (an engine started at any earlier point after warm-up emits the same signal); mirror symmetry with Swept and source; a CHoCH during warm-up consumes the zone without a signal; the new warm-up count.
- **EXE-027 to EXE-029, NWS-015, OPS-011, REC-020:** scenario tests through `FakeBroker`: tail A placed as a buy limit at 101.0 with stop, target and expiry 8 bars; filled on the retest then target; filled then stop; expired; a sell mirror with the spread added to the stop; `EntryAlreadyReached` with the ask at 101.0 and at 100.99, not at 101.01; stop exactly `ChochMinStopPips` passes and one tick less is `StopTooSmall` (choose the pip size so the numbers are exact and say how); the reward check at 2.0 exactly passes and just below is `RewardRiskTooLow` with the ratio in the record; the room check and the gap-spread check are not run (a supply zone 1.0R above the entry does not skip a Choch signal); a ZoneFvg signal is not affected by the reward check; the confirming bar in a news window gives `NewsChochBar`, the bar ending exactly at the window start does not; sessions 00:00 to 24:00 accept a signal at 23:45 and at 00:00 UTC, 07:00 to 17:00 still skips at 17:00; 24:00 refused as `SessionStartUtc` and in Breakout mode; the hash of the Choch model against a hand-computed value, and every existing hash value unchanged; a restart between placement and fill (the order is kept), a restart with an open position (adopted), a replayed CHoCH not executable, a Fvg-model order is a stranger in the Choch model; fault scenarios 5, 6, 8 and 14 in this model with zero duplicates and zero positions without a stop; the journal line of tail A byte for byte (`kind=choch level=101 sweep=no target=default rr=2` in the formats of the line).
- **PLT-016:** parameter texts to the core's parameters, each refusal message with the cBot's names, `24:00` accepted only for `SessionEndUtc`, the hash.

## Order of work

1. Read, then write down the hand-computed numbers above in the test helpers and check them by hand.
2. WP37a: tests of SIG-035 to SIG-041 first, then the code; pull request.
3. After WP37a is merged: WP37b, tests first; pull request.
4. After WP37b is merged: WP37c; pull request.

## Decisions for the human

The values marked "proposal" in spec 3.9 and 7 are not confirmed by the owner (spec 14, question 5). Build them as written; they are parameters, so a later answer changes defaults, not code. Do not add a rule the spec does not have (for example cancelling the order when price reaches the target before the retest): list it under "Decisions for the human" instead.

## Done

Per part: tests first, CI green, changed core files covered as the gate asks, the Python checks, statuses to `implemented` only for the part's ids and only if no decision is open, a plain summary for the owner in the pull request.
