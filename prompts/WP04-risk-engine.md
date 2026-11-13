# WP04 — Risk engine: sizing, account limits, FTMO guard, circuit breakers

## Read first

`AGENTS.md`, `docs/architecture.md` (sections 3 to 5), `docs/spec.md` sections
4 and 15.2 to 15.4, and the requirement texts below. This module protects the
account. Read every requirement twice.

## Requirements to implement

RSK-001, RSK-002, RSK-003, RSK-004, RSK-005, RSK-006, RSK-007, RSK-008,
RSK-009, RSK-010, RSK-011, RSK-012.

## Allowed paths

`src/ZoneFvg.Core/Risk/**`, `src/ZoneFvg.Core/Execution/Ports/**` (data records
only: `AccountSnapshot`, `SymbolInfo`, `PositionInfo`, `PendingOrderInfo`,
`ClosedTradeInfo`), `tests/ZoneFvg.Core.Tests/**`, and the `status:` lines of
the requirements above.

## What to build

1. **Port records** (no behaviour): the five records named above, with the
   fields the risk rules need. `PositionInfo` and `PendingOrderInfo` carry
   symbol, side, volume in units, entry price, stop loss, take profit, label,
   comment, open or creation time. `ClosedTradeInfo` carries close time, net
   profit, commission, swap, balance after close, label, symbol.
2. **`CestClock`** (RSK-012): `DateTime StartOfCestDayUtc(DateTime utcNow)` and
   `bool IsSummerTime(DateTime utc)`, using the explicit EU rule. No
   `TimeZoneInfo`.
3. **`PositionSizer.Size(...)`** (RSK-001, RSK-002): returns a result with
   volume, risk in money, or a skip reason.
4. **`RiskSnapshot`**: an immutable input built by the caller from the broker
   port: account, all positions, all pending orders, history since the earlier
   of `RiskEpochDate` and the start of the CE(S)T day, symbol infos, server
   time. The engine never calls the broker itself (RSK-011).
5. **`RiskEngine`** (pure methods over a `RiskSnapshot` and `RiskParameters`):
   - `DayStartBalance(snapshot)` (RSK-007).
   - `OpenRiskAtStop(snapshot)`: money lost if every bot position and pending
     order is stopped out. A position without a stop loss is treated as
     risking its full sizing-rule risk and is reported as a violation.
   - `CheckNewTrade(snapshot, plannedTrade)`: applies RSK-003, RSK-004,
     RSK-005, RSK-006 and the entry counts of RSK-010; returns Allowed or the
     first failing reason code plus measured values.
   - `KillSwitch(snapshot)` (RSK-008).
   - `EmergencyGuard(snapshot)` (RSK-009): `None` or `CloseAllAndHalt`, with
     which floor triggered.
   - `DailyBudgetExhausted(snapshot)` (RSK-006).
6. **`RequestCounter`** and **`ErrorCounter`** (RSK-010): sliding one-hour and
   CE(S)T-day windows, fed with server time by the caller.
7. **`RiskParameters`** with spec defaults and `Validate()`.

## Decisions already made

- "Bot position" means label starts with `ZFVG|`. Positions without that
  label are ignored for caps but their closed results still count in the day
  start balance (RSK-007 uses all account trades).
- Percentages for RSK-004, RSK-006 and RSK-009 are of `InitialCapital`.
  RSK-001 uses current balance.
- Worst-case equity W for RSK-006 = current balance + sum over open bot
  positions of their result at stop (negative, or positive if the stop is in
  profit) + sum over pending bot orders of minus their risk at stop.
  Commission is included using the symbol's round-trip commission.
- Pending orders count toward open risk, the currency cap and entry counts.
- An account with no history since `RiskEpochDate` has Peak = current balance.
- Volume is rounded with `PriceMath.RoundDownToStep` (WP01). It treats a value
  within one billionth of a step below a whole multiple as that multiple, so
  floating-point noise cannot cost a whole step. The owner accepted this as
  consistent with "never round up" (architecture section 10). Add no second
  tolerance.

## Tests required

Every rule gets a worked numeric example in a comment, a test exactly at the
limit (allowed), and one just beyond (blocked).

- RSK-001: the spec example (10,000 balance, 20 pips, EURUSD = 25,000 units);
  100,000 balance cases from spec 4.1; rounding down; below minimum skipped;
  USDJPY-style pip value; a volume that is a whole number of steps only up to
  floating-point noise (such as 0.3 / 0.1 steps) keeps the full multiple; a
  volume one millionth of a step below a multiple rounds down to the multiple
  below.
- RSK-002: 0.56% risk skipped; volume above cap skipped; stop on wrong side.
- RSK-003: free margin exactly 50% allowed; below blocked.
- RSK-004: three trades at 0.5% allowed; a fourth blocked; pending orders count.
- RSK-005: long EURUSD + long GBPUSD + long AUDUSD blocked on USD; long
  EURUSD + long GBPUSD + long USDJPY allowed; mirror for sells.
- RSK-006: budget exactly used (allowed) and exceeded (blocked); floating
  loss counts; a stop in profit frees budget; exhaustion flag.
- RSK-007: day start rebuilt from history across a midnight CE(S)T boundary,
  in winter and in summer time.
- RSK-008: 7.99% no halt, 8.00% halt; peak from history; same result after
  "restart" (rebuilt snapshot).
- RSK-009: with defaults, equity 96,000.01 with M = 100,000 no action; 96,000
  action; 91,000 action; M moves with the day's start balance.
- RSK-010: third entry on a symbol blocked; seventh on the account blocked;
  21st request in an hour and 101st in a day halt; sixth error in an hour halts.
- RSK-011: reflection test that `RiskEngine` has no instance fields other
  than readonly parameters.
- RSK-012: last Sunday of March and of October for 2025 to 2030, one second
  before and at the switch; start-of-day for both offsets.

## Out of scope

Calling the broker, placing or closing orders. This package only decides.

## Definition of done

- [ ] Tests first, all green; `Risk/` at 100% branch coverage.
- [ ] All `AGENTS.md` commands clean; outputs in the pull request.
- [ ] The twelve requirement statuses are `implemented`.
- [ ] Any rule whose wording allowed two readings is listed under "Decisions
      for the human" with both readings and the one you tested.
