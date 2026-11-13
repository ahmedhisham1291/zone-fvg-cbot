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
`Risk`, `News` depend on `Domain` only; `Execution` depends on `Signals`,
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
public sealed record Bar(DateTime OpenTimeUtc, double Open, double High, double Low, double Close);

public sealed record Zone(
    string Id, ZoneType Type, double Proximal, double Distal,
    DateTime ActivationTimeUtc, double AtrAtCreation, ZoneState State,
    DateTime? TouchBarOpenUtc, double? ExtremeSinceTouch);

public sealed record Fvg(Side Side, double Upper, double Lower,
    DateTime C3OpenUtc, DateTime C2OpenUtc, DateTime C1OpenUtc);

public sealed record Signal(
    string ZoneId, Side Side, double Entry, double Stop, double Target,
    Fvg Fvg, DateTime ConfirmationCloseUtc, bool FromReplay);
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
    public IReadOnlyList<Zone> Zones { get; }          // non-terminal zones, ordered by id

    /// Feed one closed bar. Returns the signals confirmed by this bar (0 or 1 per zone).
    /// replay = true marks returned signals as FromReplay.
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
