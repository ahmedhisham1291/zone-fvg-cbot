# WP08 — cTrader adapter (the cBot)

## Read first

`AGENTS.md`, `docs/architecture.md` (sections 1, 3, 8), `docs/spec.md` sections
7, 9.3, 9.4 and 10.3, and the requirement texts below. Depends on WP07 and
WP09 interfaces.

Use the current cTrader Algo API reference at
https://help.ctrader.com/ctrader-algo/ for every API call. Do not write an API
member from memory: look it up, and put the reference page URL in a comment
next to the first use of each member. If the documentation and your memory
disagree, the documentation wins.

## Requirements to implement

PLT-002, PLT-003, PLT-005, PLT-006, PLT-007, PLT-009.

## Allowed paths

`src/ZoneFvg.Bot/**`, `docs/test-records/WP08-*.md`, and the `status:` lines of
the requirements above.

Follow-up for chart drawing (owner decision 2026-11-16): additionally
`src/ZoneFvg.Core/Signals/SignalEngine.cs`, `src/ZoneFvg.Core/App/BotCore.cs`
and `tests/ZoneFvg.Core.Tests/**`, only to expose read-only snapshots of the
current zones and of the FVGs confirmed in the last 24 hours per symbol, with
tests that the snapshots never change engine state and are equal in replay and
live. No trading behaviour may change.

## What to build

1. **`ZoneFvgBot : Robot`** with `AccessRights.None` and `TimeZone` UTC
   (PLT-003).
   - Parameters: every row of spec section 7, grouped, with the documented
     defaults (PLT-006). Secrets (Telegram token, chat id, heartbeat URL) are
     plain string parameters with empty defaults.
   - `OnStart`: build parameter records, call each `Validate()`, stop with a
     printed message on failure; construct adapters and `BotCore`; subscribe
     to the bar-closed events of the D1, H4 and M15 series; subscribe to
     server connected and disconnected events and to position and pending
     order events; start a one-minute timer; call `BotCore.OnStart`.
   - Event handlers only convert and forward. Each is one or two statements
     plus conversion (PLT-002).
   - `OnStop` does nothing required (PLT-009).
2. **`CTraderBars : IBarHistory`**: closed bars only. When a bar-closed event
   fires, pass the bar that just closed, never the newly opened one. Convert
   times to UTC.
3. **`CTraderBroker : IBroker`**: one method per port member. Map cTrader
   error codes to `OrderOutcome`: clear business rejections to `Rejected`;
   disconnection, timeout and technical errors to `Unclear`. Pending orders
   take stop and target as pip distances in cTrader; convert from the prices
   in `LimitOrderRequest` using the symbol's pip size and verify by read-back
   (the core does the comparison).
4. **`LocalStorageStore : IKeyValueStore`**, flushing after each write.
5. **`HttpTransport : IHttpTransport`** using the Algo API's HTTP access,
   never throwing, with a short timeout.
6. **`ChartPainter`** (PLT-005): draws zones and FVGs when `DrawObjects` is
   true and a chart exists. Every call is wrapped; failure is logged once.
7. **Embedded news history**: an embedded resource text file passed to the
   core as a string in backtest mode.
8. **Backtest detection**: pass `isBacktest` to `BotCore` from the platform's
   running-mode property.

## Decisions already made

- The bot runs on an M15 chart. If started on another timeframe it prints a
  message and does not trade.
- One instance per symbol.
- No trading rule, threshold or state lives in this project. If you find
  yourself writing an `if` about prices, risk or time, it belongs in Core and
  this work package is the wrong place.
- `RiskEpochDate` is entered as a date and means 00:00 Prague time (CE(S)T,
  via the core's `CestClock`) on that date, converted to UTC.
- `ClosedTradeInfo.NetProfit` is mapped to include commission and swap, as
  the core expects (architecture section 10).

## Verification (this project has no unit tests)

Record each item in `docs/test-records/WP08-adapter.md` with date, cTrader
version and evidence (log excerpts, screenshots described in words).

- CI builds the `.algo` (PLT-007).
- On a demo or FTMO Free Trial account in cTrader desktop:
  1. The bot starts, prints its version and parameter hash, and reaches
     Running after recovery.
  2. With `DrawObjects` true, zones and FVGs appear on the chart and match the
     definitions on at least 10 examples (PLT-005).
  3. An invalid parameter value stops the bot with a clear message (PLT-006).
  4. A visual backtest of one month runs to completion with no exception.
  5. The first bar passed to the core after start is a closed bar (log its
     open time and compare with the chart).
  6. Run the same one-month backtest twice; the trade lists are identical.
- Owner review: every handler in `ZoneFvgBot` is conversion and forwarding
  only (PLT-002).

## Definition of done

- [ ] `.algo` built in CI and attached.
- [ ] `docs/test-records/WP08-adapter.md` complete.
- [ ] Every API member has a documentation URL comment at first use.
- [ ] `tools/check_architecture.py` clean.
- [ ] The six requirement statuses are `implemented` (manual ones only after the owner signs the test record).
