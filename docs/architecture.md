# Architecture

This document fixes the structure and the type contracts shared by all work
packages. Changing a contract here needs the human's approval.

## 1. Layers

```
+--------------------------------------------------------------+
| ZoneFvg.Bot  (cTrader adapter, net6.0, references cAlgo)     |
|   ZoneFvgBot : Robot          wiring of events only          |
|   CTraderBroker : IBroker     orders, positions, history     |
|   CTraderBars                 closed-bar feed per timeframe  |
|   LocalStorageStore : IKeyValueStore                         |
|   HttpTransport : IHttpTransport                             |
|   ChartPainter                optional drawings              |
+------------------------------|-------------------------------+
                               | ports (interfaces in Core)
+------------------------------v-------------------------------+
| ZoneFvg.Core  (pure C#, net6.0, no cAlgo, no IO, no clock)   |
|   Domain/       Bar, TimeFrame, Zone, Fvg, Signal, enums     |
|   Indicators/   Atr, Ema, SwingDetector                      |
|   Signals/      ZoneDetector, ZoneBook, FvgDetector,         |
|                 SignalEngine                                 |
|   Risk/         PositionSizer, RiskEngine, CestClock         |
|   News/         parsers, NewsCalendar, NewsService           |
|   Execution/    Ports, OrderManager, TradeManager, filters   |
|   Recovery/     BotStateMachine, RecoveryService             |
|   Ops/          Journal, AlertService, Heartbeat             |
|   App/          BotCore: the single entry point the adapter  |
|                 calls                                        |
+--------------------------------------------------------------+
```

Dependency direction inside Core: `Domain` <- `Indicators` <- `Signals`;
`Risk`, `News` depend on `Domain` only, except that `Risk` reads the data
records in `Execution/Ports` (section 10); `Execution` depends on `Signals`,
`Risk`, `News`; `Recovery` depends on `Execution`; `App` depends on all.
Nothing depends on `App`.

## 2. Two layers of state (spec 3.3)

- **Signal layer** (`Signals/`): a pure function of closed bars and
  parameters. Rebuilt by replay at any time. Knows nothing about orders.
- **Execution layer** (`Execution/`, `Recovery/`): decides whether a signal
  becomes an order. Its only source of truth is the broker port.

A zone's state never depends on whether an order was placed.

## 3. Core contracts

These signatures are binding. Add members only when a work package prompt says
so.

```csharp
namespace ZoneFvg.Core.Domain;

public enum TimeFrame { M15, H4, D1 }
public enum Side { Buy, Sell }
public enum ZoneType { Demand, Supply }
public enum ZoneState { Active, Touched, Signalled, Used, Invalid, Expired }
public enum Bias { Neutral, Long, Short }

/// A closed bar. OpenTimeUtc is the bar's open time in UTC.
/// Validated on construction. Properties are get-only, so a Bar cannot be
/// changed with a `with` expression (section 10).
public sealed record Bar(DateTime OpenTimeUtc, double Open, double High, double Low, double Close);

public sealed record Zone(
    string Id, ZoneType Type, double Proximal, double Distal,
    DateTime ActivationTimeUtc, double AtrAtCreation, ZoneState State,
    DateTime? TouchBarOpenUtc, double? ExtremeSinceTouch);

public sealed record Fvg(Side Side, double Upper, double Lower,
    DateTime C3OpenUtc, DateTime C2OpenUtc, DateTime C1OpenUtc);

public sealed record Signal(
    string ZoneId, Side Side, double Entry, double Stop, double Target,
    Fvg Fvg, DateTime ConfirmationCloseUtc, bool FromReplay, double AtrH4);
    // AtrH4 (added in WP06): the ATR(H4) used for this signal, for EXE-003.
```

```csharp
namespace ZoneFvg.Core.Signals;

public sealed class SignalEngine
{
    public SignalEngine(SignalParameters parameters);

    /// Bars needed per timeframe before the engine is ready (SIG-020).
    public IReadOnlyDictionary<TimeFrame, int> WarmupBars { get; }
    public bool IsReady { get; }
    public Bias Bias { get; }
    public IReadOnlyList<Zone> Zones { get; }          // non-terminal zones, ordered by id; empty until IsReady

    /// Feed one closed bar. Returns the signals confirmed by this bar (0 or 1 per zone);
    /// none until IsReady (SIG-020). replay = true marks returned signals as FromReplay.
    /// Bars must arrive in increasing close-time order, D1 then H4 then M15 at equal
    /// close times. Any other bar throws InvalidOperationException and changes nothing (SIG-004).
    public IReadOnlyList<Signal> OnBarClosed(TimeFrame timeFrame, Bar bar, bool replay);
}
```

```csharp
namespace ZoneFvg.Core.Execution.Ports;

public interface IBroker
{
    DateTime ServerTimeUtc { get; }
    bool IsConnected { get; }
    AccountSnapshot GetAccount();                                   // balance, equity, free margin, currency
    SymbolInfo GetSymbol(string symbol);                            // pip size, pip value per unit, volume min/step/max, spread, market open
    IReadOnlyList<PositionInfo> GetPositions();                     // all account positions
    IReadOnlyList<PendingOrderInfo> GetPendingOrders();             // all account pending orders
    IReadOnlyList<ClosedTradeInfo> GetHistory(DateTime sinceUtc);   // closed trades with balance after close
    double EstimateMargin(string symbol, Side side, double volumeUnits);
    OrderResult PlaceLimitOrder(LimitOrderRequest request);         // synchronous
    OrderResult CancelOrder(long orderId);
    OrderResult ModifyPositionProtection(long positionId, double? stopLoss, double? takeProfit);
    OrderResult ClosePosition(long positionId);
}

public enum OrderOutcome { Success, Rejected, Unclear }             // Unclear = timeout, disconnect, technical error
public sealed record OrderResult(OrderOutcome Outcome, long? Id, string? ErrorCode);

public interface IKeyValueStore { string? Get(string key); void Set(string key, string value); }
public interface IHttpTransport { HttpResult Get(string url, int timeoutSeconds); }   // never throws
public interface IJournalSink { void Write(string category, string line); }
```

```csharp
namespace ZoneFvg.Core.App;

/// The only class the cTrader adapter talks to.
public sealed class BotCore
{
    public BotCore(BotParameters parameters, IBroker broker, IKeyValueStore store,
                   IHttpTransport http, IJournalSink journal, bool isBacktest);

    public void OnStart(IBarHistory history);            // runs recovery
    public void OnConnected(IBarHistory history);        // runs recovery
    public void OnDisconnected();
    public void OnBarClosed(TimeFrame timeFrame, Bar bar);
    public void OnTimer();                               // once a minute
    public void OnTradeEvent();                          // any position or order event
    public void OnException(Exception exception);
}
```

## 4. Time

- All times are UTC `DateTime` with `Kind = Utc`.
- The core never reads a clock. "Now" is `IBroker.ServerTimeUtc` or a bar time.
- The CE(S)T day boundary is computed by `CestClock` with the explicit EU
  daylight-saving rule (RSK-012). `TimeZoneInfo` is not used.

## 5. Numbers

- Prices and volumes are `double`.
- `PriceMath` provides tolerant comparison, pip conversion and rounding down
  to a step. Raw equality on computed doubles is not allowed.
- `PriceMath.RoundDownToStep` treats a value within one billionth of a step
  below a whole multiple as that multiple, so floating-point noise cannot cost
  a whole step. The result can exceed the input by at most one billionth of a
  step (section 10).
- All parsing and formatting uses `CultureInfo.InvariantCulture`.

## 6. Errors

- Core methods called from event handlers do not throw for expected conditions;
  they return a result with a reason code.
- Programming errors throw. `BotCore` catches at the event boundary, journals,
  counts (RSK-010) and continues (REC-011).

## 7. Labels and comments (EXE-008)

- Label: `ZFVG|v1|<symbol>|<zoneId>`
- Comment: `e=<entry>;s=<stop>;t=<target>;r=<riskMoney>;h=<hash8>` with
  invariant formatting, at most 100 characters.

## 8. What the adapter may do

Translate. Subscribe to cTrader events, convert objects to core records, call
`BotCore`, convert core requests to cTrader calls, draw on the chart. It holds
no trading rule, no threshold and no state other than references.

## 9. Spec changes recorded for implementation (v0.8)

| Topic | Decision | Reason |
|---|---|---|
| ATR and EMA | Fixed-window definitions (SIG-003) | Exact rebuild after restart |
| Overlapping zones | Newer zone replaces older untouched one (SIG-012) | Removes dependence on old history |
| Swing search | Last 100 H4 bars (SIG-006) | Bounded warm-up |
| FVG versus spread | Checked by execution layer (EXE-005) | Spread is not bar data |
| Currency cap | Same-direction count per currency (RSK-005) | Original wording blocked all USD pairs |
| CE(S)T | Explicit EU DST rule (RSK-012) | No OS time-zone dependency |

## 10. Owner decisions recorded during implementation

Decisions the spec does not settle, accepted by the owner. They bind later
work packages. A validator treats them as part of the contract.

| Date | Topic | Decision | Reason |
|---|---|---|---|
| 2026-11-13 | SIG-004 and SIG-020 | Completed and marked in WP03, not WP01. WP01 keeps the building blocks and their tagged tests | The behaviour needs the signal engine |
| 2026-11-13 | Bar intake order (SIG-004) | The engine accepts bars only in strictly increasing (close time, rank) order, rank being D1, H4, M15, and throws otherwise. It does not sort | A wrong feed order fails loudly instead of silently changing zones and signals |
| 2026-11-13 | Close time | Open time plus the nominal length of the timeframe: 15 minutes, 4 hours, 24 hours. To be confirmed against the platform's bars in WP08 | Defines "close at the same moment" without a clock |
| 2026-11-13 | Warm-up gate (SIG-020) | While not ready the engine keeps its state up to date but exposes no zones and returns no signals. A zone confirmed during warm-up is consumed without a signal | Replay equals live (SIG-019) needs exact state from the first ready bar |
| 2026-11-13 | Volume rounding (RSK-001) | `RoundDownToStep` snaps a value within one billionth of a step below a multiple to that multiple. Accepted as consistent with "never round up" in spec 4.1 | A strict floor can lose a whole step to floating-point noise (0.3 / 0.1) |
| 2026-11-13 | `Bar` | Get-only properties; no `with` mutation | Validation on construction cannot be bypassed |
| 2026-11-13 | Warm-up dictionary | `WarmupBars` enumerates D1, H4, M15 | No dependence on dictionary order |
| 2026-11-13 | WP01 defaults | The ranges checked by `SignalParameters.Validate()` (including bar counts from 1 to 10,000) and the 16-bar margin of `BarStore` | Not trading rules; accepted as implemented in WP01 |
| 2026-11-14 | WP01 remaining decisions | Decisions 3, 4, 5, 7, 8, 10, 11, 14, 16 and 17 of pull request #6 are accepted as written there. No extra tagged tests for SIG-001 and SIG-005 in WP03 or WP08 | Recorded on pull request #6 |
| 2026-11-14 | SIG-014 and SIG-015 | Completed and marked in WP03, not WP02. WP02 keeps the lifecycle in `ZoneBook`, the bias-free detection and their tagged tests | "A valid FVG appears" and the bias gate need the FVG detector and the engine |
| 2026-11-14 | Base at a wide bar (SIG-009) | If the bar immediately before the leg is wider than 1.0 x ATR(H4), the base is that bar alone | "Collected backward while" stops at the first wide bar; one bar is always taken |
| 2026-11-14 | Zone id (SIG-013) | The "first base bar" is the oldest base bar | First in time |
| 2026-11-14 | Activation time (SIG-013) | The close time of the leg's last bar, its open time plus 4 hours. Equal to the next bar's open when bars are contiguous; earlier across a market gap | The next bar's open is not known at detection. No M15 bar exists inside a gap, so nothing else changes |
| 2026-11-14 | Risk actions | RSK-002 and RSK-003 are completed in WP06; RSK-006, RSK-008 and RSK-010 in WP07. WP04 builds and tests the decisions | The actions need the broker port and the bot states |
| 2026-11-14 | Stop in profit | A position whose stop is at or beyond entry in profit has zero risk | It cannot lose money at its stop |
| 2026-11-14 | Daily budget (RSK-006) | Blocks only when strictly over the limit. Commission counts, so normally two full-size trades fit per day. Swap is not counted. Once exhausted, Blocked until the next CE(S)T day | Safer side; FTMO's daily limit also counts commission. The emergency guard watches equity |
| 2026-11-14 | Volume caps | One `MaxVolumeUnits` for all symbols; the broker's symbol maximum is checked in WP06. A stop or target exactly at entry is on the wrong side | |
| 2026-11-14 | Counting (RSK-010) | Partial closes count once; cancelled or expired orders are not counted; a request exactly one hour old has left the window; 20 requests and 5 errors per hour are parameters | |
| 2026-11-14 | Broker data mapping | `ClosedTradeInfo` carries `PositionId` and `EntryTimeUtc`; `NetProfit` includes commission and swap. `Risk` reads these records from `Execution/Ports` | |
| 2026-11-14 | RiskEpochDate | A date meaning 00:00 Prague time (CE(S)T) on that day | Same day boundary as FTMO |
| 2026-11-14 | Currency cap (RSK-005) | Only the two currencies of the new trade are checked | The literal reading refused every trade when an unrelated currency was over the cap |
| 2026-11-14 | Emergency guard on bad data (RSK-009) | If M cannot be computed, the static floor is still checked from equity alone | The guard must not go blind when history is broken |
| 2026-11-14 | No stop, no new entry (EXE-011) | While any bot position has no stop loss, no new entry is placed | Fail closed |
| 2026-11-14 | News source | Forex Factory alone; `NewsSources` accepts only ForexFactory. ShowMyTrades deferred until a real sample exists | Owner decision |
| 2026-11-14 | ATR in SIG-016 and SIG-017 | The latest ATR(M15) and ATR(H4) at the confirmation close | Owner decision on pull request #14 |
| 2026-11-14 | Stop distance (SIG-017) | No separate field on `Signal`; the distance is abs(Entry - Stop) | Owner decision on pull request #14 |
| 2026-11-14 | News details (WP05) | "All" events apply to every symbol; matched events keep their own windows; a malformed event rejects the whole download with an alert; without covering data the feed is retried after 60 minutes (also at the week roll-over); the 13 smaller choices listed in pull request #16 are accepted | Owner decision; fail closed without long outages |
| 2026-11-14 | Break of structure (SIG-008) | The reference is the most recent confirmed swing whose bar is before the leg, even when the leg's own bars confirm it. If the leg does not close beyond it, no zone | Fewer, cleaner zones. Review of main, question 1 |
| 2026-11-14 | ATR(H4) for EXE-003 | `Signal` gains `double AtrH4`, the value the engine used, so replay and live use the same number. Added in WP06 | Review of main, question 3 |
| 2026-11-14 | Request limit (RSK-010) | The limit is checked before a request is sent; the request that would exceed it is never sent, and the instance halts | Review of main, question 4 |
| 2026-11-14 | Live bar order (WP08) | The adapter feeds bars by (nominal close time, D1/H4/M15 rank), never by arrival group. At the weekend Friday's last M15 bar comes before the Friday H4 and D1 bars that close at the same nominal time order | Review of main, question 5 |
| 2026-11-14 | Bad broker data | When the risk snapshot cannot be built or history is incomplete, WP07 still runs `EmergencyGuardFromEquity` and treats the other risk calls as "no new entries" (they throw; catch and fail closed) | Review of main, findings |
| 2026-11-16 | WP06 answers | 17:00 UTC is outside the trading window; sell targets stay the signal's (only the stop widens by the spread); resend after an unclear result only with nothing at the broker, a fresh signal, price not through entry and at least 15 minutes of order life; entries stop at the request limit while stop repair, close and cancel are always sent; an opposing zone exactly at entry does not block; the 14 smaller choices of pull request #17 are accepted | Owner decision on pull request #17 |
| 2026-11-16 | Weekend restart | A restart on Saturday, Sunday or Friday after FridayCloseUtc with an open bot position or pending entry closes and cancels them at the market's reopen (WP07) | Spec: flat over the weekend |
| 2026-11-16 | NewsEnabled | Wired in WP07; when false the news checks are skipped and nothing is fetched; live and challenge runs keep it true | Owner decision |
| 2026-11-16 | Simulation (WP10) | The 13 decisions of pull request #18 are accepted as built: trade log with `risk_money`, net results, refused trades removed, drawdown from the closed-balance peak, equity exactly at a floor is a breach, kill switch on closed balance, emergency guard not modelled, independent phases with fixed seeds, INCOMPLETE verdict on missing input. CI builds `tools/sim` | Owner decision; pessimistic on purpose |
| 2026-11-16 | Operations (WP09) | Phase profit target is a parameter (10% Challenge, 5% Verification); summary after a mid-day restart reports broker figures exactly and says how many trades its R covers; alerts at most 3 per minute, identical text within 10 minutes not repeated, queue of 50, back-off retries; messages Telegram refuses (4xx except 429) are dropped and logged; HTTP timeout 5 s and WP08 sends in the background; the alert log stores the full masked text; a failed news download alerts once per outage of the source (WP07); the smaller choices of pull request #20 are accepted | Owner decision on pull request #20 |
| 2026-11-16 | Recovery (WP07) | Market holidays are a parameter (default 25 December and 1 January) and do not count as data gaps; the four REC-003 rules of pull request #21 are accepted (weekend Friday FridayCloseUtc to Monday 00:00 UTC; a gap is at least one whole bar of open-market time; last bar recent within two bar lengths of open-market time; retry every 15 minutes) | Owner decision on pull request #21 |
| 2026-11-16 | Daily budget across a restart (RSK-006) | Besides recomputing from the broker, the bot stores "budget exhausted on CE(S)T day X" through the key-value port as an extra block until the next CE(S)T day. A lost or unreadable value falls back to recomputing. This stored value can only block entries, never allow them, so it does not make local state a source of truth | Owner decision on pull request #21 |
| 2026-11-16 | Emergency guard frequency | Checked on every one-minute timer event as well as at M15 closes | Owner decision |
| 2026-11-16 | Request-limit halt | Lasts until the owner restarts the bot (spec 8.5 gives no exit) | Owner decision |
| 2026-11-16 | cTrader adapter (WP08) | Decisions 1 to 11 of pull request #23 are accepted: stop and target sent as prices; pip value from live conversion rates; commission per side doubled (over-stated), unknown commission type blocks orders; HTTP waits with a hard timeout instead of background sending (replaces the earlier "send in the background"); a bar counts as closed once its nominal close time has passed, one remembered time per timeframe; local storage per instance, to be checked in WP11; backtests without Telegram, heartbeat or news until the embedded history is filled; the error mapping; market open = trading enabled and session open; DayResetZone accepts only Europe/Prague. Chart drawing gets read-only core snapshots in a follow-up | Owner decision on pull request #23 |
| 2026-11-16 | Ladder sizing (RSK-013) | The owner's 4-part plan replaces the fixed 0.5% risk: deal risk 0.30% split 29.62/14.81/22.215/33.3225%, growth 1.5924 per consecutive win, at most 0.5% per trade; computed from broker data; all other limits unchanged | Owner decision after simulation: at a 40% win rate, about 51% pass and 1% fail of simulated challenges, against 63% and 4% for fixed 0.5% |
| 2026-11-16 | Closed-trade journal data | `ClosedTradeInfo` also carries `VolumeUnits`, `ClosingPrice` and `ExitReason` (`TradeExitReason`, in `Execution/Ports`), journal data only; no risk or trading rule reads them. The adapter keeps an in-memory map of position id to close reason from the `Positions.Closed` event (lost on restart, then `Unknown`). The machine-readable `tradelog` line is printed in backtests only, never in real time | Owner decision on pull request #31; SIM-001 trade log and OPS-002 exit reason |
| 2026-11-14 | Terminal zones | Used, Invalid and Expired zones leave the book at once and are reported once. Their ids are remembered, so a repeated id is still discarded | `Zones` holds non-terminal zones only (section 3). A Used zone has no further effect on signals |
