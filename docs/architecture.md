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
|                 BreakoutTracker, SignalEngine                |
|   Risk/         PositionSizer, RiskEngine, CestClock         |
|   News/         parsers, NewsCalendar, NewsService           |
|   Execution/    Ports, OrderManager, TradeManager, filters   |
|   Recovery/     BotStateMachine, RecoveryService             |
|   Ops/          Journal, AlertService, Heartbeat             |
|   Simulation/   trade log, FTMO challenge simulator          |
|   App/          BotCore: the single entry point the adapter  |
|                 calls                                        |
+--------------------------------------------------------------+
```

Dependency direction inside Core: `Domain` <- `Indicators` <- `Signals`;
`Risk`, `News` depend on `Domain` only, except that `Risk` reads the data
records in `Execution/Ports` (section 10); `Execution` depends on `Signals`,
`Risk`, `News`; `Recovery` depends on `Execution`; `App` depends on all.
Nothing depends on `App`. `News` also reads `Execution/Ports`; `Ops` depends
on `Domain`, `Execution`, `Execution/Ports`, `News`, `Risk` and `Simulation`;
`Simulation` depends on `Domain` and `Risk`. The test
`NamespaceDependencyTests` enforces the allowed list; a new edge needs the
human's approval.

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

// Spec v0.10, added in WP26 (section 10, rows of 2026-11-22). In Signal the
// parameter Fvg becomes `Fvg? Fvg` (null for a breakout signal), and the record
// gains two init-only members outside the positional list:
//     public SignalKind Kind { get; init; }   // default ZoneFvg
//     public double? Level { get; init; }     // Breakout: the reference high or low that was broken
// For a breakout signal ZoneId holds the breakout id "BH-..." or "BL-..." (SIG-025).
public enum SignalKind { ZoneFvg, Breakout }
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

    // Spec v0.10, added in WP26 (SIG-026). With breakout parameters the engine runs in
    // Breakout mode: no zones, breakout signals at M15 closes. With null it is the engine above.
    public SignalEngine(SignalParameters parameters, BreakoutParameters? breakout);
    public BreakoutReference? Reference { get; }      // Breakout mode: the reference day; null otherwise and before the first D1 bar
}

// Spec v0.10, WP25 (SIG-022 to SIG-025). Pure, closed bars only.
public sealed record BreakoutParameters
{
    public double EntryBufferAtr { get; init; } = 0.1;                       // x ATR(M15)
    public double StopAtrMult { get; init; } = 1.0;                          // x ATR(H4)
    public TimeSpan WindowStartUtc { get; init; } = TimeSpan.FromHours(7.0);   // must equal SessionStartUtc (WP27)
    public TimeSpan WindowEndUtc { get; init; } = TimeSpan.FromHours(17.0);    // must equal SessionEndUtc (WP27)
    public void Validate();
}
public sealed record BreakoutReference(DateTime DayOpenUtc, double High, double Low, bool Armed);
public sealed record BreakoutSetup(
    string Id, Side Side, double Entry, double Stop, double Target,
    double Level, DateTime ConfirmationCloseUtc, double AtrH4);
public sealed class BreakoutTracker
{
    public BreakoutTracker(BreakoutParameters parameters, double rewardRisk);
    public BreakoutReference? Reference { get; }
    public void OnD1Closed(Bar bar);                                           // new Armed reference
    public BreakoutSetup? OnM15Closed(Bar bar, Bias bias, double? atrM15, double? atrH4);
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
    OrderResult PlaceStopOrder(StopOrderRequest request);           // synchronous; spec v0.10, added in WP26 (EXE-018). Same fields as LimitOrderRequest
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

// Spec v0.10, added in WP27: BotParameters gains `BreakoutParameters? Breakout`.
// Null (the default) is ZoneFvg mode; a value is Breakout mode. One mode per instance.
// Also added in WP27 (from its prompt): `enum EntryMode { ZoneFvg, Breakout }` in App,
// the read-only `EntryMode BotParameters.Mode`, and an optional last constructor
// argument `BreakoutParameters? breakout = null` of RecoveryService.
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

- Label: `ZFVG|v1|<symbol>|<zoneId>`. In Breakout mode (spec v0.10) the
  breakout id `BH-yyyyMMdd-HHmm` or `BL-yyyyMMdd-HHmm` stands in the zone id's
  place; the bot name and the format do not change.
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

Decisions the spec does not settle. They bind later work packages. A
validator treats them as part of the contract. Until 2026-11-22 each was
accepted by the owner. Since the owner decision of that day (`AGENTS.md`,
"Who merges") the orchestrating session decides and tells the owner; such a
row says so in its last column. The owner's money decisions are not decided
here.

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
| 2026-11-16 | Ladder sizing details (RSK-013) | Filled entries are numbered and judged by fill time (the broker keeps no order time after a fill); a close at the same instant as a placement counts as before it; "newest first" is by close time, later entry wins ties; a partial fill or partial close is one entry with summed net; trades before RiskEpochDate are ignored, also for the win run; break-even ends the run, open trades are skipped; core default `LadderEnabled = true` while the pre-RSK-013 test fixture sets it false; parameter ranges: growth >= 1, first weight <= 1/3.375, percents in (0, 100]; five cBot parameters including `MaxRiskPercentPerTrade`; invalid bot broker data skips the signal as `BrokerDataInvalid`; open trades without a stop still count at the fixed 0.5% in the open-risk cap; simulator ladder mode is opt-in (`--sizing ladder`); the ladder percent goes into the journal in a later change | Owner decision on pull request #35 |
| 2026-11-16 | Trade journal MAE/MFE (OPS-002) | MAE and MFE in R are measured on closed M15 bars from the fill bar to the close bar plus the exit price (whole fill and close bars count); trades open longer than 30 days, or without bars back to the fill, report NA | Owner decision on pull request #33 |
| 2026-11-16 | Clean-up structure | One alert enum `AlertKind` lives in `Execution` (in `Ops` it would create a cycle); members used only by tests are made `internal`, not deleted, so no existing assertion is removed; the namespace graph above is pinned by a test | Owner decision on pull requests #34 and #36 |
| 2026-11-16 | Simultaneous entries and the ladder (RSK-013) | The four instances size on the same M15 close from broker data; two entries placed in the same moment can both be sized as entry t, so the second may use the ladder percent of t instead of t+1. Accepted: the 0.5% cap and every other limit still hold, and cross-instance coordination is not worth its complexity | Owner decision on the review of main a11e340, finding 1 |
| 2026-11-17 | Ladder step columns (RSK-013, WP18) | A win ends every open deal. Steps live in columns C, D, E, F (1st to 4th trade after the last win; 5th and later use step 0); a win raises its position's column, not past a column still at 0; once a trade's raw percent exceeds 0.5% no step rises any more; all steps reset to 0 only with a new `RiskEpochDate`, set by the owner after a payout or for a new account (owner change of 2026-11-17: no monthly reset). Deal risk stays 0.30, cap 0.5% per trade | Owner decision, checked against the owner's 10-trade example (88.86, 141.50, 225.33, 201.52, 302.28, 453.43, 225.33, 358.81, 320.90, 428.72 on 100,000). Simulation at a 40% win rate, 2R, 150 trades: pass 39%, fail 1.1%, average risk 0.32% |
| 2026-11-17 | Ladder edge cases (WP18) | Wins closing at the same instant raise their columns in entry order; an entry that closes at its own placement instant closes right after it; a bot entry placed or closed after the broker's server time skips the signal as `BrokerDataInvalid`; the freeze needs a raw percent above 0.5 by more than 1e-9 (an infinite raw percent counts as above); each simulated path starts with all columns at 0 and never resets inside the path | Owner decision on pull request #41 |
| 2026-11-14 | Terminal zones | Used, Invalid and Expired zones leave the book at once and are reported once. Their ids are remembered, so a repeated id is still discarded | `Zones` holds non-terminal zones only (section 3). A Used zone has no further effect on signals |
| 2026-11-17 | Data gaps (REC-003, WP23; decision A) | A gap still in the history after one reload is the broker's data (early close on 24 and 31 December, a broker outage) and is accepted: journaled per timeframe and counted in the recovery report. Recovery fails only when not connected, on too few bars, a missing or unclosed bar, disorder or stale data. `BarSeriesCheck.Problem` keeps its meaning (it still reports gaps); recovery uses a check that leaves gaps out and a separate list of gaps. A live gap still blocks entries until the next recovery reloads the history. Replaces the "fail on any gap" part of the 2026-11-16 recovery row | Owner decision ("Accept all") on the core audit, finding F1. One holiday gap kept the bot in Recovering for 6 to 7 weeks |
| 2026-11-17 | Duties while recovering (REC-015, WP23; decision B) | The emergency guard and Friday close run on every timer event in every connected state, Recovering included, and the pending weekend close at timer and trade events. A guard that fires in Recovering closes everything and halts; the halt stays after recovery | Owner decision on core audit F2. Both read only broker data |
| 2026-11-17 | Halts in backtests (REC-014, WP23; decision C) | Automatic, no cBot parameter: `BotParameters` gains a flag (named in the WP23 prompt) that the adapter sets to `RunningMode != RealTime`, like `WriteTradeLog`; `BotParameters.Validate(isBacktest: false)` refuses it. In that mode kill switch, emergency guard, error limit, request limit and daily budget only write "would-halt" / "would-block" once per cause and CE(S)T day; nothing is halted, closed, cancelled or refused for those causes. The per-trade budget check is included because the portfolio combiner applies the daily budget across symbols | Owner decision on core audit F4. Automatic rather than a parameter, so it can never be switched on live by a wrong parameter file, and the test fixtures (flag off by default) keep proving the live halts |
| 2026-11-17 | Gold (decision D, WP24) | XAUUSD is a fifth instance: MaxSpreadPips 40, MinStopPips 800, MaxVolumeUnits 200 oz, all other parameters as for the forex pairs (no other parameter is in pips; the ATR and R multiples scale by themselves). News: USD, "All" and manual USD/XAU entries only (NWS-013). Currency cap: a gold buy is long XAU and short USD (RSK-015). Gap check: the symbol's weekly trading sessions from the broker add closed time (REC-016). Acceptance: profitable symbols max(3, N - 1) of N (SIM-005). Proposed and marked in the plan pull request: 4 of 5 profitable symbols; 300/60 trades and the 60% single-symbol share unchanged; 15 pips of stress slippage and live slippage alert for gold; gold keeps the forex values of the four optimised parameters | Owner decision ("Accept all") on both audits' gold sections |
| 2026-11-17 | Contract additions (WP22, WP24) | `SymbolInfo` gains `Digits` (int) and `TickSize` (double) in WP22 and `Sessions` (weekly trading sessions, record `TradingSession` in `Execution/Ports`) in WP24, as init-only members outside the positional list; the adapter fills them from `Symbol.Digits`, `Symbol.TickSize` and `Symbol.MarketHours.Sessions` and prints them in the start line. `SignalEngine` reports the zones created and the zones that ended, with their reason, at each `OnBarClosed` (journal only, OPS-009). A start check (RSK-014) is a core function the adapter calls before `OnStart`; a refusal uses the adapter's existing "refused to start" path. `PriceGrid`'s pip-size functions stay (internal if unused) so their tests keep running | Needed for EXE-017, REC-016, OPS-009 and RSK-014 |
| 2026-11-17 | Fixes without a rule change (WP22) | Accepted by the owner: drawings removed by remembered name (PLT-005); Log repeat limits of 1 hour for errors and risk-unavailable lines and 24 hours for a waiting recovery (OPS-008); inactivity count starts no earlier than RiskEpochDate and a restart does not repeat a warning within 3 days (OPS-006); refuse to start when RiskEpochDate is after the server time, or in a backtest when the balance differs from InitialCapital by more than 0.01 (RSK-014); no Friday close request against a closed market (EXE-016); prices on the broker's tick grid (EXE-017); daily zone outcome counts (OPS-009) | Adapter audit W1, N1, N2, N4, W3; core audit F3, F5, F9, F10 |
| 2026-11-22 | Entry mode (spec v0.10, decision E) | One mode per instance, fixed at start. In the core `BotParameters.Breakout` selects it: null (the default) is ZoneFvg, so every existing fixture and test keeps running the old mode unchanged; a value is Breakout. The cBot parameter `EntryMode` (default Breakout) decides whether the adapter sets it. `BotParameters.Validate` refuses breakout parameters whose window differs from `SessionStartUtc` and `SessionEndUtc` | Owner request of 2026-11-22 |
| 2026-11-22 | Breakout signal layer (SIG-022 to SIG-026) | `BreakoutTracker` holds one reference, the last closed D1 bar, and is fed by the engine: each D1 close, then each M15 close with the current bias and the latest ATR(M15) and ATR(H4). An M15 bar is checked when it opened at or after the reference bar's nominal close and the time of day of its own nominal close is at or after `WindowStartUtc` and before `WindowEndUtc`. A confirmation finishes the reference whatever follows: during the warm-up, or without an ATR value, the reference is finished and no signal is returned. The warm-up requirement is the one of ZoneFvg mode (SIG-020), so recovery loads the same history in both modes. In Breakout mode the engine keeps no zones and reports no zone activity | Exact rebuild after a restart; one code path for bar intake |
| 2026-11-22 | Contract additions (WP25 to WP27) | New in `Signals` (WP25): `BreakoutParameters`, `BreakoutReference`, `BreakoutSetup`, `BreakoutTracker`. WP26: `SignalEngine` gains the constructor `(SignalParameters, BreakoutParameters?)` and `Reference`; `Signal.Fvg` becomes nullable and `Signal` gains the init-only members `Kind` (`SignalKind`, default ZoneFvg) and `Level`; `IBroker` gains `PlaceStopOrder(StopOrderRequest)`; `SymbolInfo` gains the init-only members `Bid` and `Ask` (default 0, filled by the adapter from `Symbol.Bid` and `Symbol.Ask`); `PlannedOrder` gains an init-only entry type (limit or stop); `SkipReason` gains `NewsBreakoutBar` and `EntryAlreadyReached` at the end; `NewsBlockReason` gains `BreakoutBarInWindow` at the end. WP27: `BotParameters` gains `Breakout` | Needed for SIG-022 to SIG-026, EXE-018 to EXE-020, NWS-014 and REC-017 |
| 2026-11-22 | Stop-entry orders (EXE-018 to EXE-020) | A breakout signal becomes a stop order through `IBroker.PlaceStopOrder`; the order manager picks the port call from the planned order's entry type. Spread: added to the entry of a buy, because a buy stop triggers on the ask, and by EXE-007 to the stop of a sell; nothing else moves. The check "entry already reached" reads `SymbolInfo.Bid` and `Ask` and runs right after the stop bounds. ZoneFvg signals and limit orders are untouched | A stop order placed at or through the market is rejected or fills at once |
| 2026-11-22 | Parameter hash in the two modes | `ParameterHash.Canonical` and `Compute` take the breakout parameters as an optional third argument. Null gives exactly the v0.9 text and hash (`f681dff2` for the defaults). A value appends `EntryMode=Breakout;BreakoutEntryBufferAtr=<R>;BreakoutStopAtr=<R>;BreakoutWindowStartUtc=<c>;BreakoutWindowEndUtc=<c>;` in that order, numbers in the round-trip format and times of day in the "c" format | Trades placed in the other mode, or under other breakout values, are strangers after a restart; ZoneFvg trades of v0.9 stay adoptable |
| 2026-11-22 | Journal in Breakout mode (OPS-010) | A breakout signal's line has the keys of OPS-001 in the same order, with `zoneProximal`, `zoneDistal`, `fvgUpper` and `fvgLower` written as `NA`, followed by `kind=breakout` and `level=<price>`. A ZoneFvg line is unchanged byte for byte. In Breakout mode the core's start line ends with `mode=Breakout`; in ZoneFvg mode it is unchanged | Existing journal tests keep their expected text |
| 2026-11-22 | Breakout levels fail closed (SIG-024, review of WP25) | After a confirmation `BreakoutTracker.OnM15Closed` returns no setup, with the reference finished, when ATR(M15) or ATR(H4) is null, not finite or not greater than zero (the text of SIG-024), and also when a computed entry, stop or target is not finite or not greater than zero (item 3 of the WP25 prompt). The engine returns no signal in either case | Decided by the orchestrating session on the validator's question in pull request 53. The second half was written only in the prompt. It only fails closed (hard rule 10); no requirement text changes |
| 2026-11-22 | Per-file coverage in the CI log | `tools/check_coverage.py` prints, after the gate result, the covered and total lines and branches of every file of the package, marked `full` or `PART`. Information only: the gate, its thresholds and the exit code are unchanged. Where a work package asks that its new files are fully covered, the proof is these lines in the `build-test` log of the head commit | Decided by the orchestrating session on the validator's question in pull request 53: the CI artifact with the coverage report cannot be downloaded from the agents' sessions |
| 2026-11-22 | Breakout bar and news windows: the boundary (NWS-014) | The confirming bar of a breakout is tested against news windows with the unchanged overlap rule of NWS-009 (`NewsCalendar.OverlapsWindow`): the bar runs from its open to its close, the close excluded, and a window includes both of its ends. So a bar that opens at the last instant of a window overlaps it and the signal is skipped (`NewsBreakoutBar`); a window that ends one minute earlier does not block. Example with 30 minutes before and after: an event at 09:15 blocks the bar 09:45 to 10:00, an event at 09:14 does not | Decided by the orchestrating session on the implementer's question in pull request 55. The WP26 prompt named this rule but gave the 09:15 case as allowed; the example was wrong and is corrected in the prompt. No rule changes: at the boundary the bot skips the trade, as it does for a gap bar |
| 2026-11-22 | Resend guard from the broker's history (EXE-021, WP29) | When a placement with an unclear outcome is resolved and the broker shows neither a pending order nor a position with the label, `OrderManager` reads `IBroker.GetHistory` from the signal's confirmation close on, before anything else is decided. A closed trade with the label ends the placement as `Placed` ("filled and closed as position <id>"); a null list ends it as `Abandoned` with `UnclearOutcomeNotResent`; nothing is sent in either case. Otherwise EXE-009 and EXE-020 apply unchanged. No port or enum changes | Decided by the orchestrating session on the validator's question in pull request 55: a buy stop triggers on the ask, so an order that was applied, filled and stopped out inside one M15 bar could leave no bar that reached the signal's entry, and the order would have been sent a second time |
| 2026-11-22 | Entry type fits the signal (EXE-022, WP29) | `OrderManager.Send` sends a stop order only for Kind Breakout with entry type Stop and a limit order only for Kind ZoneFvg with entry type Limit. Anything else, including an undefined kind or entry type, sends nothing: a skip record with `InvalidSignal` and the result `Abandoned`. The filters never build such an order | Decided by the orchestrating session on a finding of the validator in pull request 55 (any entry type other than Stop was sent as a limit order). Hard rule 10 |
| 2026-11-22 | Journal of a breakout signal: zone edges (OPS-010, WP29) | `SignalJournalEntry.Taken` and `Skipped` write `NA` for `zoneProximal` and `zoneDistal` of a signal of Kind Breakout whatever zone the caller passes | Finding of the validator in pull request 55: the line was right only because the caller finds no zone with a breakout id |
| 2026-11-22 | The cTrader stop-order call (PLT-011) | The ten-argument call `Robot.PlaceStopOrder(tradeType, symbolName, volume, targetPrice, label, stopLoss, takeProfit, ProtectionType.Absolute, expiration, comment)` is accepted as confirmed by three things: the compile in CI against the pinned `cTrader.Automate` package (the argument types in positions 8 to 10 fit no other parameter list); the example on the `StopTriggerMethod` reference page, which shows that parameter order and that the default trigger is the trade side (a buy stop on the ask, a sell stop on the bid); and the `ProtectionType` reference page (`Absolute` is price based). The text of the Robot reference page lists 32 overloads but shows only the deprecated pips signatures, so the signature could not be read there. **Owner's gate:** the stop-order record `docs/test-records/WP26-stop-orders.md` must pass on a demo or Free Trial account before any instance trades in Breakout mode on the Challenge account | Decided by the orchestrating session before WP26 started; the validator of pull request 55 agreed and asked for this row and for the gate |
| 2026-11-22 | Replay and live in Breakout mode (note) | Replay equals live holds when the replay starts at least one reference day (96 M15 bars) before the cut. The property test proves it for one parameter set whose M15 warm-up covers that. Recovery replays at least 3,000 M15 bars, so the bot is not affected and no parameter check is added | Finding of the validator in pull request 55: with `MaxZoneAgeBars` 1 or 2 the M15 warm-up alone would be shorter than a day |
| 2026-11-22 | A pending order placed under other parameter values (REC-006, REC-017) | Recovery keeps a pending bot order by its id, stop, target and expiry and does not read the parameter hash of its comment; that is unchanged in both modes. A breakout id does not depend on the breakout values, so after a restart with changed values such an order is kept with its old levels and size; when it fills it is managed normally and is a stranger from the next restart on. The rule that goes with it: parameters are changed only when flat, and flat means no open position and no pending order of the bot (spec 10.2); the deployment guide says so | Decided by the orchestrating session on the validator's question in pull request 58. Cancelling such orders at the restart would be a rule change for both modes and was not made |
| 2026-11-22 | The D1 series in Breakout mode (REC-018, WP29) | `BarSeriesCheck.ProblemOtherThanGap` gains an optional last argument, the number of bar lengths of open-market time after which the last bar is stale (default 2, a value below 1 throws). `RecoveryService` passes 1 for the D1 series when it was built with breakout parameters and 2 otherwise. A whole-day holiday that is missing from `MarketHolidays` makes a recovery on the following day fail in Breakout mode until the next D1 bar closes: accepted, fail closed | Decided by the orchestrating session on the validator's question in pull request 58: with the limit of two bar lengths a D1 series one bar behind passed recovery, and the bot would have traded the range of the day before. Live, the adapter passes bars by nominal close time in (close, rank) order, so the daily rollover itself does not lose the bar |
| 2026-11-22 | Fault scenarios in Breakout mode (REC-019, WP29) | The automated scenarios of spec 12.3 numbers 5, 6, 8 and 14 also run through the core in Breakout mode, scenario 6 with the case of EXE-021, plus recovery on the sell side. `RecoveryService` validates the breakout parameters in its constructor | Decided by the orchestrating session on the validator's question in pull request 58: the scenarios of REC-013 run in ZoneFvg mode only, and spec 12.3 says the tests are run in the mode that goes live |
| 2026-11-22 | A reference serves one day (SIG-027, WP30) | `BreakoutTracker` keeps, in a private field, the open time of the first M15 bar it received at or after the close of the current reference bar; `OnD1Closed` empties it. An M15 bar that opens 24 hours or more after that time is not checked and changes nothing. The public surface, `BreakoutReference` and the engine do not change. With a complete D1 series the rule does not apply as long as no daily bar lasts longer than 24 hours with trades after its nominal close: the next D1 bar closes first. (The validator of pull request 62 found the one shape in which it would apply, a 25-hour daily bar that trades in its last hour; the three M15 bars concerned open at 21:00 to 21:30 UTC, outside the trading window, and spec 3.7 does not check them either. Accepted.) | Decided by the orchestrating session on the validator's question in pull request 60. REC-018 (the stale limit of one bar length for the D1 series at recovery) counts open-market time, so closed time delays it: a missing Friday bar is seen only from Monday 02:00 UTC on, with 00:00 bars from Monday 04:00, and with trading sessions one hour after each close. In those windows recovery passes and the bot would have traded the range of the day before. The guard was therefore put where the reference is used; it needs no calendar, holds across the two changes of daylight saving time and is the same in the replay and live. REC-018 stays as the early alert |
| 2026-11-22 | A history list with a missing entry (EXE-023, WP30) | `OrderManager.Resolve` treats a history list with a null entry like no list: nothing is resent, `Abandoned`, `UnclearOutcomeNotResent`, also when another entry carries the label | Decided by the orchestrating session on the validator's question in pull request 60. Hard rule 10: an unexpected value means no order |
| 2026-11-22 | Notes from the review of WP29 (no change) | (1) When a broker read throws inside `OrderManager.ResolvePending`, `BotCore`'s bar handler stops there, as it did before for the two existing queries: the error is journaled and counted, nothing is sent, and a signal confirmed by that bar is neither placed nor journaled as skipped. (2) A resend after an unclear outcome needs a placement that is exactly one bar length old (EXE-009, EXE-020). Live, the server time at a bar event is later than the nominal close, so an unclear placement that was not applied is in practice abandoned and not resent. Both fail closed | Findings of the validator in pull request 60, recorded for the owner; neither is changed |
| 2026-11-22 | Daily and H4 bar boundaries on cTrader | Spotware's statements in the cTrader community forum: daily bars are aggregated at 17:00 New York time, that is 21:00 UTC in summer and 22:00 UTC in winter (community.ctrader.com/forum/cbot-support/3219); the first daily bar of the week opens on Sunday at 21:00 UTC, a bar is formed every 24 hours and the convention is the same for all platforms (community.ctrader.com/forum/ctrader-support/35987); H4 bars count from the weekly open (community.ctrader.com/forum/ctrader-support/24201). So there are five daily bars a week and no short Sunday bar, and Monday's reference in Breakout mode is Friday's bar. No rule of the core depends on the hour: close time is open time plus the nominal length, and bars are ordered by nominal close. The D1 bars of the test fixtures open at 00:00 UTC; that is a property of the fixtures only. The expectation "D1 bars open at 00:00 UTC" in `docs/test-records/WP08-adapter.md` step 3 was wrong and is corrected by WP28. **Owner's gate:** the observed D1 open hour is written into that record before any instance trades in Breakout mode on the Challenge account; if it shows 00:00 UTC bars or a short Sunday bar, Breakout mode does not go live before a follow-up decides how Monday's reference is formed | Decided by the orchestrating session on the validator's question in pull request 62. With bars on the New York close and complete M15 bars, SIG-027 leaves no case in which a D1 series one bar behind is traded (the validator's own model: 0 of 331 single missing bars); with 00:00 bars and a Sunday bar one case would remain |
| 2026-11-23 | Entry mode parameters in the adapter (PLT-012, WP28) | `BreakoutEntryBufferAtr` and `BreakoutStopAtr` are checked at start in both modes, by the core's own `BreakoutParameters.Validate()` called from the adapter after `BotParameters.Validate`; the adapter has no range of its own. The value is passed to the core in Breakout mode only. A refusal of a breakout value starts with the name of the cBot parameter (`BreakoutEntryBufferAtr`, `BreakoutStopAtr`), followed by the core's message. A value of the mode that the adapter does not know is refused and never run as ZoneFvg. The order and text of every refusal that existed before WP28 are unchanged | Decided by the orchestrating session on the implementer's questions in pull request 64 (2026-11-22) and on the validator's findings there. PLT-012 says the cBot validates the three parameters at start without a condition on the mode |
| 2026-11-23 | What goes live, and the holdout benchmark (spec 13.1) | The live parameter files carry the chosen mode and the accepted settings; the laptop stage, the fault tests and the soak run with these files; the hash at start equals, symbol by symbol, the hash of the accepted backtest run (`ParameterHash` covers the signal, execution and breakout values, not the risk and news values). The example files in `deploy/params/` carry `EntryMode=Breakout` because it is the cBot default, not because it is chosen. When ZoneFvg is the chosen mode, Breakout is run once on the holdout, after the choice, as the benchmark input of the ZoneFvg holdout row only | Decided by the orchestrating session on the validator's blocking finding and its question 4 in pull request 64: the deployment guide told the owner to leave `EntryMode` at its default, which could have put a mode on the Challenge account that had not passed or was not chosen; and without Breakout logs of the holdout the ZoneFvg holdout report could never be complete. The go or no-go itself stays with the owner |
| 2026-11-23 | Bar order in the owner's record (WP08 record, step 3) | The record no longer expects a journal line per processed bar, which the bot does not print. It expects that no `error` line about a rejected bar appears around the daily close; the order D1, H4, M15 is enforced by code and proven by automated tests (SIG-004) | Decided by the orchestrating session on the validator's question 3 in pull request 64. The old sentence came from the first version of the record and was repeated in item 6 of the WP28 prompt |
| 2026-11-23 | Backtest defaults (PLT-014, WP31) | The two backtest-only rules are pure functions in the core (`App/BacktestDefaults`), called by the adapter at start: the risk epoch for an empty text in a backtest, and whether the news filter is on, from the flag, the backtest mode and the number of events of the embedded history. Live behaviour is unchanged | Owner request of 2026-11-23: a backtest with the default parameters had blocked every entry for three years because the embedded news history is empty, and refused to start without a typed RiskEpochDate |
| 2026-11-23 | Breakout filters (SIG-028, SIG-029, WP32) | `BreakoutParameters` gains `TrendSlowEmaPeriod` and `LastConfirmUtc`. Their record defaults are the values that switch them off (0 and the window end), so that `new BreakoutParameters()` and the hash `119a3864` keep meaning the rule before 23 November; the cBot defaults are 200 and 08:00. The parameter hash appends `TrendSlowEmaPeriod` and `BreakoutLastConfirmUtc` only when they differ from the off values, so a configuration without the filters keeps its hash and its trades | Owner request of 2026-11-23 after five Breakout backtests (−25R in total). The first-hour rule is the only filter that held in both halves of 2023–2026 in those logs; the trend filter could not be tested from logs and is judged by the next backtests |
| 2026-11-23 | Research before a new entry rule (`research/`) | An entry rule is tested on exported price history, outside the bot, before it is specified or built: many rules at once, with costs, chosen on older years and judged once on recent years that the choice never saw. Only a rule that passes becomes a spec change and a work package. The tools are in `research/`; nothing there is part of the cBot or places an order. `research/ExportBars` is a cBot that only writes closed bars to files; the workflow `research-tools` builds it and is not a merge gate. The acceptance backtests of spec section 13 are unchanged and still decide go or no-go | Owner's go of 2026-11-23. The backtests with the two Breakout filters (started 22 October 2020, ended 22 November 2026) made 0.17% a month on gold, counted from its first trade in February 2022, and nothing on EURUSD, against the owner's target of 2% a month. EURUSD lost 20R before October 2023, the years the first-hour filter was not chosen on; gold's profit came from 2025 and 2026 alone |
