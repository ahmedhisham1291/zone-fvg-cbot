# WP26: Breakout mode in the engine, stop-entry orders and their checks

## Read first

`AGENTS.md`; `docs/spec.md` section 0 (changes in v0.10) and sections 3.6,
3.7, 5.2, 5.3, 6.1 and 11; `docs/architecture.md` section 3 (every block
marked "Spec v0.10") and section 10 (all rows of 2026-11-22); the
requirement texts below; then the code you will change: `SignalEngine`,
`PreTradeFilters`, `OrderManager`, `PlannedOrder`, `NewsService.CheckEntry`,
`SignalJournalEntry`, `Journal.Signal`, `CTraderBroker.PlaceLimitOrder`, and
`tests/.../Fakes/FakeBroker.cs`. Depends on WP25 (`BreakoutTracker`). WP27
wires the mode into `BotCore` and recovery; WP28 adds the cBot parameters.

## Requirements to implement

SIG-026, EXE-018, EXE-019, EXE-020, NWS-014, OPS-010, PLT-011.

Owner request of 2026-11-22. After this package the core can produce a
breakout signal and turn it into a verified stop order, but no instance
runs in Breakout mode yet: `BotCore` and `RecoveryService` still build the
engine without breakout parameters. **Nothing in ZoneFvg mode may change:**
not a signal, a level, a check, the order of the checks, an order, a
journal line or the parameter hash. If a change of yours would alter one,
stop and ask.

This is the largest package of the four. Do not split the `Signal`
contract change from its consumers; everything must compile and pass in
one pull request.

## Allowed paths

- `src/ZoneFvg.Core/Domain/Signal.cs` and a new
  `src/ZoneFvg.Core/Domain/SignalKind.cs`
- `src/ZoneFvg.Core/Signals/SignalEngine.cs`
- `src/ZoneFvg.Core/Execution/Ports/IBroker.cs` (one member and the
  `StopOrderRequest` record) and `Ports/SymbolInfo.cs` (two init-only
  members)
- `src/ZoneFvg.Core/Execution/PreTradeFilters.cs`, `OrderManager.cs`,
  `PlannedOrder.cs`, `SkipReason.cs`, `ParameterHash.cs` and a new
  `src/ZoneFvg.Core/Execution/EntryOrderType.cs`
- `src/ZoneFvg.Core/News/NewsService.cs` and `NewsReports.cs`
- `src/ZoneFvg.Core/Ops/SignalJournalEntry.cs` and `Journal.cs`
- `src/ZoneFvg.Bot/CTraderBroker.cs`
- `tests/ZoneFvg.Core.Tests/**`
- a new file `docs/test-records/WP26-stop-orders.md`
- the `status:` lines of the requirements above

`App/**`, `Recovery/**` and `Risk/**` need no change: the new constructor
arguments are optional and last. If you need one of them, stop and ask.

## Owner authorisation to change existing tests

Only these, and only as described. List each one in the pull request.

1. **Classes in the tests that implement `IBroker`** (`Fakes/FakeBroker.cs`,
   `SwitchableBroker` in `TestSupport/RecoverySupport.cs`,
   `StopShiftingBroker` in `Scenarios/Exe017PriceGridScenarios.cs`,
   `ThrowingAccountBroker` in `App/StartChecksTests.cs`) gain the new port
   member. `FakeBroker` models stop orders (item 4 below); the other three
   forward to their inner broker or behave as their `PlaceLimitOrder` does.
2. **`BrokerRequest`** (in `FakeBroker.cs`) gains an init-only member
   `StopOrder` (`StopOrderRequest?`). For a limit order `Order` is set and
   `StopOrder` is null, as today; for a stop order it is the other way
   round.
3. **`TestSupport/ExactRecords.cs`:** `Key(Signal)` may handle a signal
   without an Fvg (write `-` for each of its six gap parts) and append
   `Kind` and `Level` at the end. The key of a ZoneFvg signal keeps its
   existing parts in the same order.
4. **Fixtures that build `SymbolInfo`** may set the new `Bid` and `Ask`
   (setup only).
5. **`Domain/EnumContractTests.cs`** may gain one test for `SignalKind`.
6. **Existing tests that read the gap of a signal.** `Signal.Fvg` becomes
   nullable, and the tests are compiled with warnings as errors, so these
   lines no longer compile as they are: `Signals/SignalLevelsTests.cs`
   (140 to 142), `Signals/SignalEngineWarmupTests.cs` (179),
   `Signals/SignalEngineLifecycleTests.cs` (47, 48, 147, 148, 165) and
   `Signals/SignalEnginePropertyTests.cs` (283 to 289), as numbered on main
   at e1508fc. Each read of a member of `signal.Fvg` may get the
   null-forgiving operator (`signal.Fvg!.C1OpenUtc`) or be preceded by
   `Assert.NotNull(signal.Fvg)`. Nothing else in those lines changes: no
   expected value, no comparison, no tolerance. If the compiler asks for
   the same in a line not listed here, apply the same rule and list the
   line in the pull request.

No expected value of an existing test may change, and no other existing
test may change. If one fails, stop and ask. In particular the pinned
canonical text and the hash `f681dff2` in `Execution/OrderFormatTests.cs`
and `Scenarios/Exe008OrderFormatScenarios.cs`, and the three signal lines
in `Ops/JournalTests.cs`, stay exactly as they are.

## What to build, in this order

1. **`Signal` contract** (architecture section 3). `Fvg` becomes
   `Fvg? Fvg`. New init-only members outside the positional list:
   `SignalKind Kind` (default `ZoneFvg`) and `double? Level`. New enum
   `SignalKind { ZoneFvg, Breakout }` in `Domain` (values 0 and 1).
   `FvgDetector.BuildSignal` is not changed: its signals have Kind ZoneFvg
   and a gap.
2. **SIG-026, engine mode.** `SignalEngine` gains the constructor
   `(SignalParameters parameters, BreakoutParameters? breakout)`; the
   existing constructor calls it with null. With breakout parameters the
   engine owns a `BreakoutTracker(breakout, parameters.RewardRisk)` and, per
   accepted bar: D1: store, recompute the bias, then
   `tracker.OnD1Closed(bar)`; H4: store only (no swings, no zone detection,
   the zone book stays empty); M15: store, then
   `tracker.OnM15Closed(bar, Bias, atrM15, atrH4)` with the ATR values
   computed exactly as `OnM15Closed` computes them today, and, if a setup
   comes back and `IsReady`, return one `Signal` built from it (Kind
   Breakout, `ZoneId` = the setup id, `Fvg` null, `Level`, the replay
   flag). The intake-order check and the warm-up gate are the existing
   code. `Reference` returns the tracker's reference, or null without
   breakout parameters. `Zones`, `RecentFvgs` and `LastActivity` are empty
   in Breakout mode.
3. **Port.** `StopOrderRequest` with the same nine members as
   `LimitOrderRequest` (`EntryPrice` is the stop price). `IBroker` gains
   `OrderResult PlaceStopOrder(StopOrderRequest request)`, synchronous,
   with the outcome rules of the existing requests. `SymbolInfo` gains
   `double Bid` and `double Ask` as init-only members outside the
   positional list (default 0).
4. **`FakeBroker`.** `PlaceStopOrder` uses the same scripted faults
   (`BrokerCall.Place`), refusals and ids as `PlaceLimitOrder` and records
   the request with `StopOrder` set. Fill model on `Tick`: a buy stop fills
   when the ask is at or above its entry, a sell stop when the bid is at or
   below it, at the entry price; everything after the fill (position,
   stop, target, partial fill, expiry on `AdvanceTo`) is the existing
   model. Do not model a refusal for a stop price at or through the
   market. `GetSymbol` is unchanged: tests set `Bid` and `Ask` on the
   symbol record.
5. **`ParameterHash`.** `Canonical` and `Compute` take
   `BreakoutParameters? breakout = null` as a third argument. Null: the
   text and the hash are exactly today's. A value: today's text followed by
   `EntryMode=Breakout;BreakoutEntryBufferAtr=<R>;BreakoutStopAtr=<R>;BreakoutWindowStartUtc=<c>;BreakoutWindowEndUtc=<c>;`
   with the formats the class already uses.
6. **`PlannedOrder`** gains an init-only `EntryType` (new enum
   `EntryOrderType { Limit, Stop }` in `Execution`, default Limit) and
   `ToStopRequest()`.
7. **EXE-019 and NWS-014 in `PreTradeFilters`.** The constructor takes
   `BreakoutParameters? breakout = null` as its last argument, used only
   for the parameter hash. For a signal of Kind Breakout:
   - inputs: (a) of EXE-019;
   - news: `NewsService.CheckBreakoutEntry` (item 8) instead of
     `CheckEntry`; reason `BreakoutBarInWindow` maps to the skip reason
     `NewsBreakoutBar`, with the bar's open time in the detail;
   - levels: for a Buy, entry = round(signal entry + spread); stop and
     target as today (for a Sell that includes the spread on the stop,
     EXE-007);
   - a new check directly after the stop bounds: (d) of EXE-019, skip
     reason `EntryAlreadyReached` with measured = the ask (buy) or bid
     (sell) and limit = the order's entry;
   - `CheckFvgVersusSpread` and `CheckRoom` are not run;
   - the planned order has `EntryType` Stop.
   For a signal of Kind ZoneFvg every check is today's code path.
8. **`NewsService.CheckBreakoutEntry(string symbol, DateTime nowUtc,
   DateTime confirmationCloseUtc)`**: the calendar check and the
   current-time check of `CheckEntry`, then one bar from
   `confirmationCloseUtc` minus 15 minutes to `confirmationCloseUtc` with
   the same overlap rule. `NewsBlockReason` gains `BreakoutBarInWindow` at
   the end. `CheckEntry` is unchanged.
9. **`SkipReason`** gains `NewsBreakoutBar` and `EntryAlreadyReached`, at
   the end, so no existing value moves. Neither is a sanity cap.
10. **EXE-018 and EXE-020 in `OrderManager`.** `Send` calls
    `PlaceStopOrder(order.ToStopRequest())` for `EntryType` Stop and
    `PlaceLimitOrder` otherwise; request counting, the request limit, the
    read-back and every result are the same code. The resend blocker for a
    stop order uses the rule of EXE-020 with `order.Signal.Entry`; for a
    limit order it is unchanged.
11. **OPS-010.** `SignalJournalEntry`: `FvgUpper` and `FvgLower` become
    `double?`; new init-only members `SignalKind Kind` and `double? Level`;
    `Taken` and `Skipped` fill them from the signal. `Journal.Signal`
    appends `kind=breakout` and `level=` (with `JournalFormat.Price`) only
    for Kind Breakout.
12. **PLT-011, adapter.** `CTraderBroker.PlaceStopOrder` mirrors
    `PlaceLimitOrder`: one call
    `Robot.PlaceStopOrder(tradeType, symbolName, volume, targetPrice,
    label, stopLoss, takeProfit, ProtectionType.Absolute, expiration,
    comment)` through the existing `Send`. `GetSymbol` fills `Bid` and
    `Ask` from `Symbol.Bid` and `Symbol.Ask`; `DescribeCosts` prints `ask`
    after `bid`. Confirm two things on the cTrader reference and cite the
    pages in comments, as the file already does:
    - the `Robot` page lists that exact `PlaceStopOrder` overload (prices
      with `ProtectionType`, then expiration, then comment). A staff post
      on the cTrader forum shows the eight-parameter overload ending in
      `ProtectionType? protectionType`; the ten-parameter one must be on
      the reference page;
    - the default trigger of a stop order is the trade side: a buy stop
      triggers on the ask and a sell stop on the bid (`StopTriggerMethod`).
    If either is not as described, stop and ask. Do not switch to pips, to
    placing and then modifying, to another overload with a different
    meaning, or to another package version.
13. **Test record** `docs/test-records/WP26-stop-orders.md` in the style of
    `WP08-adapter.md`: steps for the owner on a demo or Free Trial account
    in Breakout mode (possible after WP28): a breakout stop order appears
    under pending orders as a stop order with stop loss, take profit and
    expiry at the journaled prices; it fills when price passes it and the
    position keeps stop and target; an unfilled one disappears at its
    expiry; the Friday close cancels one; the start line shows `bid` and
    `ask`. Each step has "Observed" and "Pass / fail" lines.

## Tests required

Fixture for the execution tests: `ExecutionData.EurUsd()` (pip 0.0001, tick
0.00001, spread 0.0001) at `ExecutionData.Now` (Wednesday 2026-01-14 10:00
UTC), ladder as in the existing fixture, and a harness whose filters were
built with default `BreakoutParameters`. Add helpers to `ExecutionData`:
`BreakoutBuy` (entry 1.0863, stop 1.0823, target 1.0943, level 1.0850,
ATR(H4) 0.0040, id `BH-20260112-2200`, confirmed at Now) and `BreakoutSell`
(entry 1.0767, stop 1.0807, target 1.0687, level 1.0780, id
`BL-20260112-2200`). Symbol prices unless stated: bid 1.0858, ask 1.0859
for the buy; bid 1.0772, ask 1.0773 for the sell.

**SIG-026** (use `RandomWalkHistory`; where a hand-built case is clearer,
use parameters with a short warm-up, for example BiasEmaPeriod 5 and
MaxZoneAgeBars 1)
- On every bar of 5 histories the engine's `Reference` and its signals
  equal those of a `BreakoutTracker` driven directly (the WP25 helper),
  from the first ready bar on; each signal has Kind Breakout, the id in
  `ZoneId`, no Fvg, `Level`, the ATR(H4) of the setup and the replay flag
  it was fed with.
- `Zones` and `RecentFvgs` are empty and `LastActivity` is
  `ZoneActivity.None` at every bar of a history on which a ZoneFvg engine
  holds zones.
- Warm-up: `WarmupBars` equals that of a ZoneFvg engine with the same
  parameters; a confirmation during the warm-up returns no signal, leaves
  the reference Done, and no signal follows for that reference once ready.
- Intake order: one rejected bar (an M15 bar that closed before the last
  accepted bar) throws and leaves `Reference`, `Bias` and `IsReady`
  unchanged.
- Determinism (20 histories, bit for bit), replay equals live (10 cut
  points per history: identical `Reference` and signals after T) and
  mirror symmetry, as the SIG-002, SIG-019 and SIG-021 tests do for zones.
  At least half of the histories must produce a signal.
- Without breakout parameters: on 5 histories
  `new SignalEngine(p, null)` and `new SignalEngine(p)` give identical
  zones and signals bit for bit, `Reference` is null, and every signal has
  Kind ZoneFvg and a null `Level`.

**EXE-018**
- `BreakoutBuy`: one `PlaceStopOrder` request and no `PlaceLimitOrder`;
  entry 1.0864, stop loss 1.0823, take profit 1.0943, expiry 12:00, label
  `ZFVG|v1|EURUSD|BH-20260112-2200`, comment prices with five digits and
  the hash `119a3864`; outcome Placed, verified.
- `BreakoutSell`: entry 1.0767, stop loss 1.0808, take profit 1.0687.
- A ZoneFvg signal (`ExecutionData.Buy()`) in the same harness: one
  `PlaceLimitOrder` request, no `PlaceStopOrder`, the levels of the
  existing EXE-008 scenario.
- Fill model: after the buy order is placed, `Tick` with ask 1.0863 leaves
  it pending, ask 1.0864 fills it at 1.0864 with stop and target; a later
  bid of 1.0823 closes it at the stop. Mirror for the sell (bid 1.0767
  fills; ask 1.0808 stops).
- Idempotent: a second `TryPlace` of the same order sends nothing
  (AlreadyExists), also after the fill.
- Faults on `BrokerCall.Place`: Rejected (skip `OrderRejected`, alert, no
  retry); DropStopLoss, DropTakeProfit and DropExpiry (cancel sent, alert,
  MismatchCancelled); FilledAtOnce (Placed, "filled as position");
  PartialFill (position kept, remainder expires); SuccessNotApplied
  (NotFound, alert).
- `TradeManager`: `CancelEntryOrders` and the Friday close cancel a pending
  stop order; `EnsureProtection` restores a missing stop of a filled
  breakout position from its comment.
- The request counter counts a stop order as one request; with the hour
  limit reached the order is not sent (RequestLimitReached).

**EXE-019**
- (a) A Breakout signal with an Fvg, with `Level` null, 0, negative or NaN:
  `InvalidSignal`. A ZoneFvg signal with a null Fvg: `InvalidSignal`.
- (b) `BreakoutBuy` with a supply zone in the context whose proximal is
  1.0R above its entry: allowed. `ExecutionData.Buy()` with a supply zone
  1.0R above its own entry: `NoRoom`, as today.
- (c) Buy with spread 0.0001: order entry 1.0864, stop pips 41.0. Spread
  0.00013: entry 1.08643. Sell with spread 0.0001: entry 1.0767, stop
  1.0808, stop pips 41.0. Stop bounds use these levels: a buy whose signal
  distance is 11.5 pips is allowed with spread 0.0001 (12.5 pips) and
  skipped `StopTooSmall` with spread 0.00004 (11.9 pips).
- (d) Buy: ask 1.0864 and above skip `EntryAlreadyReached`; ask 1.08639 is
  allowed. Sell: bid 1.0767 and below skip; bid 1.07671 is allowed. Bid or
  ask 0 (the default), negative, NaN, or ask below bid: `BrokerDataInvalid`.
  A ZoneFvg signal is not affected by `Bid` and `Ask` at all (0 is fine).
- Order of the checks: a Breakout signal that is stale, outside the
  session, in a news window, too wide in spread and also "already reached"
  reports the same first reason a ZoneFvg signal reports today, one case
  per existing check up to the stop bounds; `EntryAlreadyReached` comes
  before `CostTooHigh`.
- Expiry: 8 bars after Now; shortened to the start of a news window; under
  15 minutes of life: `ExpiryTooShort`.

**EXE-020** (model on the existing EXE-009 scenarios)
- UnclearNotApplied, then at 10:15 with the bar 10:00 to 10:15 closed and
  its high below 1.0863: resent once, Placed.
- The same with the bar's high at 1.0863: abandoned,
  `UnclearOutcomeNotResent`, nothing sent. High 1.08629: resent.
- Sell: low at 1.0767 abandons; 1.07671 resends.
- UnclearApplied: the order is found at the next bar, verified, not resent.
- At 10:30 (two bars old): abandoned. Before any bar has closed since the
  confirmation: kept unresolved. Less than 15 minutes of life left:
  abandoned.
- A limit order after UnclearNotApplied behaves exactly as the existing
  scenarios say.

**NWS-014** (NewsBeforeMin and NewsAfterMin 30)
- Calendar unavailable: `NewsCalendarUnavailable`. Now inside a window:
  `NewsWindow`. Both as for a ZoneFvg signal.
- High-impact USD event at 09:20 (window 08:50 to 09:50), confirmation
  close 10:00, Now 10:00: the bar 09:45 to 10:00 overlaps the window:
  `NewsBreakoutBar`, detail with 09:45. Event at 09:15 (window ends 09:45):
  allowed, with the same boundary rule as the existing NWS-009 tests.
- A EUR event blocks EURUSD the same way; a GBP event does not.
- `CheckBreakoutEntry` directly: the three outcomes and
  `BarOpenUtc` = 09:45; an invalid symbol or a non-UTC time throws as
  `CheckEntry` does. `CheckEntry` with a gap is unchanged (one case).

**OPS-010**
- Taken `BreakoutBuy` (volume 121000, risk 496.10, symbol with commission
  0.00007 per unit, bias Long, news Available), exact line:
  `2026-01-14T10:00:00Z signal symbol=EURUSD result=taken bias=Long zone=BH-20260112-2200 zoneProximal=NA zoneDistal=NA fvgUpper=NA fvgLower=NA entry=1.0864 stop=1.0823 target=1.0943 stopPips=41.0 volume=121000 spreadPips=1.0 costToRisk=0.041 news=Available skip=none kind=breakout level=1.085`
- Skipped `BreakoutSell` without symbol data: the levels of the signal,
  `stopPips=NA volume=NA spreadPips=NA costToRisk=NA news=NA`, the skip
  reason, then `kind=breakout level=1.078`.
- The three existing ZoneFvg lines of `JournalTests` still pass unchanged
  (no `kind=` and no `level=` in them).
- If your line for the taken case differs from the text above in any
  character, stop and ask before changing the expectation.

**ParameterHash**
- `Canonical(new SignalParameters(), new ExecutionParameters(), null)`
  equals the existing pinned text and `Compute` gives `f681dff2`.
- With `new BreakoutParameters()` the text is the pinned text followed by
  `EntryMode=Breakout;BreakoutEntryBufferAtr=0.1;BreakoutStopAtr=1;BreakoutWindowStartUtc=07:00:00;BreakoutWindowEndUtc=17:00:00;`
  and the hash is `119a3864`.
- A completeness test in the style of `ParameterHashCompletenessTests`:
  every public property of `BreakoutParameters`, found by reflection,
  changes the text and the hash, and the appended part has one entry per
  property plus `EntryMode`.

**PLT-011**: the build of the cBot in CI, and the manual record of item 13.
The requirement stays `planned` until the owner has signed the record.

## Out of scope

`BotParameters`, `BotCore`, `RecoveryService` (WP27); cBot parameters,
deployment files and guides (WP28); a trailing stop or any other exit
change; index CFDs; chart drawing for breakouts.

## Definition of done

- [ ] Tests first, all green in CI; coverage thresholds met.
- [ ] Every pre-existing test passes with no change beyond the six
      authorised items.
- [ ] Every command of `AGENTS.md` run and its output pasted (without a
      .NET SDK in the session: say so and paste the CI output).
- [ ] SIG-026, EXE-018, EXE-019, EXE-020, NWS-014 and OPS-010 are
      `implemented` (only if "Decisions for the human" is empty); PLT-011
      stays `planned`.
- [ ] The pull request quotes the two cTrader reference pages of item 12
      and the overload it compiled against.
- [ ] A plain-language summary for the owner of what changes in orders:
      none in ZoneFvg mode; in Breakout mode a stop order instead of a
      limit order, with the spread on a buy's entry.
