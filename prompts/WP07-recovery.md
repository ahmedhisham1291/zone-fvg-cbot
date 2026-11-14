# WP07 — Recovery, bot states and the application core

## Read first

`AGENTS.md`, `docs/architecture.md` (all), `docs/spec.md` section 8 in full and
section 12.3, and the requirement texts below. Depends on WP06.

## Requirements to implement

REC-001, REC-002, REC-003, REC-004, REC-005, REC-006, REC-007, REC-008,
REC-009, REC-010, REC-011, REC-012, REC-013, RSK-006, RSK-008, RSK-010.

RSK-006, RSK-008 and RSK-010 were started in WP04, which built the decisions.
RSK-010 now requires the request limit to be checked before a request is
sent; change the WP04 counter accordingly, tests first. When the risk snapshot
cannot be built or history is incomplete, `BotCore` still runs
`EmergencyGuardFromEquity` and allows no new entries (architecture section 10).

WP07 completes them in `BotCore`: when the daily budget is exhausted, pending
entry orders are cancelled and the bot stays Blocked until the next CE(S)T day
(this cause clears only at the day change, not when risk falls); the kill
switch and the request and error limits put the bot into Halted.

## Allowed paths

`src/ZoneFvg.Core/Recovery/**`, `src/ZoneFvg.Core/App/**`,
`src/ZoneFvg.Core/Risk/**` (only the RSK-010 change below),
`tests/ZoneFvg.Core.Tests/**`, and the `status:` lines of the requirements
above.

## What to build

1. **`BotStateMachine`** (REC-001): states and transitions of spec 8.5, each
   transition carrying a reason. Only Running permits new orders.
2. **`IBarHistory`** port: for a timeframe, the last N closed bars in order,
   and a way to ask for more. The adapter implements it; tests use an
   in-memory version.
3. **`RecoveryService.Run(history)`** implementing spec 8.3 step by step
   (REC-002 to REC-010). Each step is a private method named after the step
   and returns a result that is added to the recovery report. The service is
   idempotent: running it twice in a row changes nothing the second time.
   - Step 1 checks connection, bar counts against the warm-up requirement and
     gaps (REC-003). A gap is a missing bar where the market was open; use a
     simple rule: consecutive M15 bars more than 15 minutes apart outside the
     weekend closure. If data is incomplete, request more once; if still
     incomplete, stay in Recovering.
   - Step 2 creates a fresh `SignalEngine` and feeds bars with `replay: true`
     in close-time order (`BarIntakeOrder.CloseTimeUtc`), D1 then H4 then M15
     at equal close times (REC-004). Ordering by open time is wrong: it would
     hand the engine a D1 or H4 bar before the M15 bars inside it. The engine
     rejects any other order (SIG-004).
   - Steps 3 to 6 use only the broker port (REC-005 to REC-007).
   - Step 7 backfills the journal exactly once per trade, keyed by position
     id (REC-008).
   - Step 9 performs catch-up duties (REC-009).
   - Step 10 emits the report (REC-010) and moves to Running, Blocked or
     Halted according to the risk and news status.
4. **`BotCore`** with the exact surface in `docs/architecture.md` section 3. It
   wires everything:
   - `OnBarClosed`: in Recovering, ignore; otherwise feed the engine, run
     `TradeManager.EnsureProtection`, risk guards (kill switch, emergency
     guard, daily budget), `OrderManager.ResolvePending`, then filters and
     placement for new signals.
   - `OnTimer`: heartbeat hook, news refresh hook, Friday close, inactivity
     hook. Hooks are interfaces here; WP09 supplies the implementations.
   - `OnTradeEvent`: protection check and journal hook.
   - `OnDisconnected` / `OnConnected`: mark and recover (REC-002).
   - Every public method is wrapped per REC-011.
   - No call to any clock (REC-012).
5. **Emergency actions**: when `RiskEngine.EmergencyGuard` returns
   `CloseAllAndHalt`, close all bot positions, cancel orders, enter Halted,
   alert. When the kill switch fires, cancel entry orders and enter Halted.

## Decisions already made

- Recovery never places an entry order.
- During Recovering, protection repair (EXE-011) is still allowed and
  required.
- A position is a "stranger" (REC-007) when its label parses but its zone id
  is not in the rebuilt zone book, or its parameter hash differs from the
  current one.
- Blocked causes: calendar Unavailable, daily budget exhausted, data gap.
  Blocked clears automatically when the cause clears.

## Scenario tests required

Automate spec 12.3 with `FakeBroker` (REC-013). Each is one test named after
the fault number:

| # | Scenario |
|---|---|
| 1 | Core destroyed while a position is open; new core recovers; position adopted; no order sent |
| 2 | Core destroyed with a limit order pending; order kept when valid |
| 3 | Order fills while no core exists; position adopted; stop and target verified |
| 4 | Trade hits target while no core exists; journal backfilled once; zone not traded again |
| 5 | Disconnect for 1, 10 and 60 minutes; recovery runs; signals from the gap are not executed |
| 6 | Disconnect at the moment of placement (both Unclear variants); exactly one order afterwards |
| 7 | Stop loss removed externally; restored at the next event; alert raised |
| 8 | Two cores on one symbol and one broker; at most one order per zone and per symbol |
| 9 | Calendar source returns garbage; cache used; stale cache blocks entries |
| 10 | Core absent across Friday close; on recovery the position is closed at once |
| 11 | Parameter change while a trade is open; position handled as a stranger |
| 13 | A handler throws repeatedly; bot halts after the limit; process continues |
| 14 | Ten restarts in ten minutes; at most one calendar fetch per source; no duplicate order |

Plus:

- REC-001: every legal transition, and each illegal one rejected.
- REC-002: running recovery twice gives an identical second report with no
  broker writes.
- REC-003: missing bars keep the state Recovering and raise one alert.
- REC-012: a test scans the Core assembly's IL for calls to
  `DateTime.get_Now`, `DateTime.get_UtcNow` and `Stopwatch` and finds none.

The global assertions of WP06 run at the end of every scenario.

## Out of scope

Real implementations of journal, alerts and heartbeat (WP09); cTrader (WP08).

## Definition of done

- [ ] Tests first, all green; coverage thresholds met.
- [ ] `BotCore` surface matches `docs/architecture.md` exactly.
- [ ] All `AGENTS.md` commands clean; outputs in the pull request.
- [ ] The sixteen requirement statuses are `implemented`.
- [ ] Scenario tests: budget exhausted cancels pending entries and blocks
      until the next CE(S)T day, also across a restart; kill switch at
      exactly 8% halts; the 21st request in an hour and the 6th error in an
      hour halt.
