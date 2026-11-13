# WP06 — Execution: broker port, FakeBroker, filters, order manager

## Read first

`AGENTS.md`, `docs/architecture.md` (all), `docs/spec.md` sections 3.6, 5 and
6.1, and the requirement texts below. Depends on WP03, WP04 and WP05 being
merged.

## Requirements to implement

EXE-001, EXE-002, EXE-003, EXE-004, EXE-005, EXE-006, EXE-007, EXE-008,
EXE-009, EXE-010, EXE-011, EXE-012, EXE-013, EXE-014, EXE-015, RSK-002,
RSK-003.

RSK-002 and RSK-003 were started in WP04, which built the decisions. WP06
completes them: a refused trade is recorded with its reason (EXE-014) and an
alert is raised for every RSK-002 refusal through the alert hook; the margin
estimate passed to the risk engine comes from `IBroker.EstimateMargin`. WP06
also checks the volume against the symbol's own maximum volume from the
broker and skips a trade above it.

## Allowed paths

`src/ZoneFvg.Core/Execution/**`, `tests/ZoneFvg.Core.Tests/**` (including
`Fakes/FakeBroker.cs` and `Scenarios/`), and the `status:` lines of the
requirements above.

## What to build

1. **`IBroker`** and its request and result records exactly as in
   `docs/architecture.md` section 3. `LimitOrderRequest` carries symbol, side,
   volume, entry price, stop-loss price, take-profit price, expiry (UTC),
   label, comment.
2. **`FakeBroker : IBroker`** in the test project. It is the heart of all
   scenario tests, so build it carefully and test it on its own:
   - holds account, symbols, positions, pending orders, history, server time;
   - `AdvanceTo(utc)` and `Tick(symbol, bid, ask)` fill limit orders, trigger
     stops and targets, expire orders, and write history rows;
   - scripted faults: next call returns `Rejected` with a code; next call
     returns `Unclear` but the order **was** accepted; next call returns
     `Unclear` and the order was **not** accepted; `IsConnected = false`;
     partial fill of a given fraction; an order accepted with its stop loss
     dropped;
   - records every request made, for assertions on request counts.
3. **`OrderLabel`** and **`OrderComment`** (EXE-008): build and parse, with
   invariant formatting, round-trip exactness to the symbol's digits, and a
   length guard. `ParameterHash` is the first 8 hex characters of a SHA256 hash of
   a canonical, invariant-formatted list of all signal and execution
   parameters in fixed order.
4. **`SkipReason`** enumeration (EXE-014), one value per rule in EXE-001 to
   EXE-006, EXE-015, the news rules (NWS-008, NWS-009) and each `RiskEngine`
   reason.
5. **`PreTradeFilters.Evaluate(signal, context)`**: applies, in this fixed
   order and stopping at the first failure: EXE-001, state is Running,
   EXE-002, NWS-008, NWS-009, EXE-003 (after EXE-007 adjustment), EXE-005,
   EXE-004, EXE-006, EXE-015, then sizing (RSK-001 to RSK-003) and
   `RiskEngine.CheckNewTrade`. Returns a planned order or a skip with measured
   values.
6. **`OrderManager`**:
   - `TryPlace(plannedOrder)` implements EXE-009 and EXE-010 and counts each
     broker request in the `RequestCounter`.
   - `ResolvePending()` is called at each M15 close for orders whose outcome
     was Unclear.
   - Holds at most a small in-memory list of unresolved attempts; losing it
     in a restart must be harmless because the first step is always the
     broker query.
7. **`TradeManager`**:
   - `EnsureProtection()` (EXE-011), called on every trade event and bar.
   - `FridayClose(serverTimeUtc)` (EXE-013).
   - `CancelEntryOrders(reason)` used by risk and news rules.
8. **`ExecutionParameters`** with spec defaults and `Validate()`.

## Decisions already made

- If two signals arrive for one symbol on the same bar, the one with the
  smaller zone id is evaluated first; the second is skipped with
  `SymbolBusy` once the first is placed.
- "Price has traded through the entry" for EXE-009: since the signal's
  confirmation close, any M15 bar low at or below entry (buy) or high at or
  above entry (sell).
- Commission for EXE-004 comes from `SymbolInfo.RoundTripCommissionPerUnit`.
- EXE-007 is applied before EXE-003 and before sizing, so the widened stop is
  the one checked and sized.

## Scenario tests required (drive `PreTradeFilters`, `OrderManager`, `TradeManager` through `FakeBroker`)

One test class per requirement; each named scenario is a separate test.

- EXE-001: fresh live signal placed; two-bar-old signal skipped; replay signal skipped.
- EXE-002: each filter alone blocks (seven scenarios), and passes at its boundary.
- EXE-003: 12.0 pips placed, 11.9 skipped; 2.0 ATR placed, above skipped.
- EXE-004: cost exactly 10% placed; above skipped.
- EXE-005: gap exactly 3 spreads placed; below skipped.
- EXE-006: opposing proximal at 1.49R skipped; at 1.5R placed; terminal zones ignored.
- EXE-007: sell stop and size include the spread; buy does not.
- EXE-008: placed order carries stop, target, expiry, label and a comment that parses back to the plan.
- EXE-009: existing order with the label means zero requests; clear rejection means one request and no retry; Unclear-but-accepted means exactly one order after `ResolvePending`; Unclear-not-accepted and still fresh means one resend; Unclear-not-accepted but price traded through means no resend.
- EXE-010: stop dropped by the broker leads to cancel and alert.
- EXE-011: position without stop gets it restored; two failures close the position; while a bot position has no stop, a valid new signal is skipped and no order is sent.
- RSK-002: each refusal is recorded with its reason and raises exactly one alert.
- RSK-003: the margin estimate used is the one returned by `FakeBroker.EstimateMargin`; free margin exactly 50% of equity placed, below skipped.
- Symbol maximum volume: volume exactly at the broker maximum placed; above it skipped.
- EXE-012: 40% partial fill is protected and managed; remainder expires.
- EXE-013: Friday 20:00 UTC closes and cancels; Thursday does not; restart after the time still closes.
- EXE-014: every skip in the scenarios above carries exactly one reason and its measured values.
- EXE-015: expiry shortened to the next window start; 14 minutes left skipped; 15 placed.

Global assertions run at the end of every scenario: no two orders or
positions share a label; every position has a stop loss; request count is
within limits.

## Out of scope

Recovery after restart (WP07), journaling formats (WP09), cTrader (WP08).

## Definition of done

- [ ] `FakeBroker` has its own tests, including each scripted fault.
- [ ] Tests first, all green; coverage thresholds met; `Execution/` at least 95% branches.
- [ ] All `AGENTS.md` commands clean; outputs in the pull request.
- [ ] The seventeen requirement statuses are `implemented`.
