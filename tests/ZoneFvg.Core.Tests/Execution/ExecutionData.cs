using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Hand-written data for the execution scenarios (WP06). Every number is chosen so that the
/// expected results can be worked out on paper.
/// </summary>
/// <remarks>
/// <para>Time: Wednesday 2026-01-14 10:00 UTC (11:00 CET), inside the 07:00 to 17:00 session.
/// The reference signals confirm at 10:00, so they are zero minutes old.</para>
/// <para>EURUSD: pip 0.0001, pip value 0.0001 USD per unit, volume minimum and step 1,000,
/// maximum 10,000,000, spread 0.0001 (1.0 pip), no commission, market open; 5 price digits.
/// Account: balance, equity and free margin 100,000 USD. Margin 0.04 USD per unit.</para>
/// <para>Reference buy (zone D-20260114-0400): entry 1.10000, stop 1.09800 (20 pips), target
/// 1.10400; gap 1.09950 to 1.10050 (10 pips); ATR(H4) 0.00200, so the largest stop is 40 pips.
/// Risk 0.5% of 100,000 = 500; volume 500 / (20 x 0.0001) = 250,000; risk money 500.</para>
/// <para>Reference sell (zone S-20260114-0400): entry 1.10000, signal stop 1.10200, target
/// 1.09600. EXE-007 adds the 1-pip spread: stop 1.10210, 21 pips; volume 500 / (21 x 0.0001) =
/// 238,095.2, rounded down to 238,000; risk money 238,000 x 21 x 0.0001 = 499.80.</para>
/// <para>News: one high-impact USD event at 18:00 on the same day, a window from 17:30 to 18:30.
/// The normal expiry, 8 bars after 10:00, is 12:00, before that window.</para>
/// </remarks>
public static class ExecutionData
{
    public const string Symbol = "EURUSD";

    public const string BuyZone = "D-20260114-0400";

    public const string SellZone = "S-20260114-0400";

    /// <summary>The news history of the quiet day: one USD event at 18:00 UTC.</summary>
    public const string QuietNews = "2026-01-14 18:00,USD,High,Retail Sales m/m\n";

    /// <summary>Wednesday 2026-01-14 10:00 UTC.</summary>
    public static readonly DateTime Now = new(2026, 1, 14, 10, 0, 0, DateTimeKind.Utc);

    public static readonly SignalParameters SignalDefaults = new();

    public static readonly ExecutionParameters ExecutionDefaults = new();

    public static RiskParameters RiskDefaults => RiskData.Parameters;

    public static DateTime At(int day, int hour, int minute, int second = 0)
    {
        return new DateTime(2026, 1, day, hour, minute, second, DateTimeKind.Utc);
    }

    public static SymbolInfo EurUsd(double spread = 0.0001, double commissionPerUnit = 0.0, double maxVolume = 10000000.0, bool marketOpen = true)
    {
        return new SymbolInfo(Symbol, "EUR", "USD", 0.0001, 0.0001, 1000.0, 1000.0, maxVolume, spread, marketOpen, commissionPerUnit) { Digits = 5, TickSize = 0.00001 };
    }

    public static AccountSnapshot Account(double balance = 100000.0, double equity = 100000.0, double freeMargin = 100000.0)
    {
        return new AccountSnapshot(balance, equity, freeMargin, "USD");
    }

    /// <summary>A buy signal confirmed at <paramref name="closeUtc"/>, its gap bars the three bars before.</summary>
    public static Signal Buy(
        double entry = 1.1,
        double stop = 1.098,
        double target = 1.104,
        DateTime? closeUtc = null,
        double atrH4 = 0.002,
        double gapUpper = 1.1005,
        double gapLower = 1.0995,
        bool fromReplay = false,
        string zoneId = BuyZone)
    {
        DateTime close = closeUtc ?? Now;
        var fvg = new Fvg(Side.Buy, gapUpper, gapLower, close.AddMinutes(-45.0), close.AddMinutes(-30.0), close.AddMinutes(-15.0));
        return new Signal(zoneId, Side.Buy, entry, stop, target, fvg, close, fromReplay, atrH4);
    }

    /// <summary>A sell signal confirmed at <paramref name="closeUtc"/>, its gap bars the three bars before.</summary>
    public static Signal Sell(
        double entry = 1.1,
        double stop = 1.102,
        double target = 1.096,
        DateTime? closeUtc = null,
        double atrH4 = 0.002,
        double gapUpper = 1.1005,
        double gapLower = 1.0995,
        bool fromReplay = false,
        string zoneId = SellZone)
    {
        DateTime close = closeUtc ?? Now;
        var fvg = new Fvg(Side.Sell, gapUpper, gapLower, close.AddMinutes(-45.0), close.AddMinutes(-30.0), close.AddMinutes(-15.0));
        return new Signal(zoneId, Side.Sell, entry, stop, target, fvg, close, fromReplay, atrH4);
    }

    public static Zone Zone(string id, ZoneType type, double proximal, ZoneState state = ZoneState.Active)
    {
        double distal = type == ZoneType.Supply ? proximal + 0.001 : proximal - 0.001;
        return new Zone(id, type, proximal, distal, At(13, 0, 0), 0.002, state, null, null);
    }

    public static string Label(string zoneId)
    {
        return "ZFVG|v1|" + Symbol + "|" + zoneId;
    }

    public static Bar M15(DateTime openUtc, double open, double high, double low, double close)
    {
        return BarBuilder.One(TimeFrame.M15, openUtc, open, high, low, close);
    }
}

/// <summary>Records every skip and alert of the execution layer.</summary>
public sealed class RecordingEvents : IExecutionEvents
{
    public List<SkipRecord> Skips { get; } = new();

    public List<ExecutionAlert> Alerts { get; } = new();

    public void Skipped(SkipRecord skip)
    {
        Skips.Add(skip);
    }

    public void Alert(ExecutionAlert alert)
    {
        Alerts.Add(alert);
    }

    public int AlertsOf(AlertKind kind)
    {
        return Alerts.Count(alert => alert.Kind == kind);
    }
}

/// <summary>
/// The execution layer of one EURUSD instance wired to a <see cref="FakeBroker"/>, with a
/// backtest-mode news service fed from text (no network, no store).
/// </summary>
public sealed class ExecutionHarness
{
    public ExecutionHarness(
        FakeBroker? broker = null,
        string news = ExecutionData.QuietNews,
        RiskParameters? risk = null,
        ExecutionParameters? execution = null)
    {
        Broker = broker ?? DefaultBroker();
        RiskParameters riskParameters = risk ?? ExecutionData.RiskDefaults;
        ExecutionParameters executionParameters = execution ?? ExecutionData.ExecutionDefaults;
        News = new NewsService(new NewsParameters(), new[] { NewsTestData.ForexFactoryFeed() }, new MemoryStore(), new FakeTransport(NewsTestData.Failed()), true, news);
        Counter = new RequestCounter(riskParameters);
        Filters = new PreTradeFilters(executionParameters, ExecutionData.SignalDefaults, riskParameters, Broker, News, Events);
        Orders = new OrderManager(Broker, Counter, Events);
        Trades = new TradeManager(ExecutionData.Symbol, executionParameters, Broker, Counter, Events);
    }

    public FakeBroker Broker { get; }

    public RecordingEvents Events { get; } = new();

    public NewsService News { get; }

    public RequestCounter Counter { get; }

    public PreTradeFilters Filters { get; }

    public OrderManager Orders { get; }

    public TradeManager Trades { get; }

    public static FakeBroker DefaultBroker(SymbolInfo? symbol = null, DateTime? now = null, AccountSnapshot? account = null)
    {
        var broker = new FakeBroker(now ?? ExecutionData.Now, account ?? ExecutionData.Account(), symbol ?? ExecutionData.EurUsd());
        broker.MarginPerUnit = 0.04;
        return broker;
    }

    public PreTradeResult Evaluate(Signal signal, BotState state = BotState.Running, IReadOnlyList<Zone>? zones = null)
    {
        var context = new PreTradeContext(ExecutionData.Symbol, state, zones ?? Array.Empty<Zone>(), Orders.HasUnresolved(ExecutionData.Symbol));
        return Filters.Evaluate(signal, context);
    }

    /// <summary>Evaluates the signal and, when it passes, places it. Returns the placement, or null when skipped.</summary>
    public PlaceResult? EvaluateAndPlace(Signal signal, BotState state = BotState.Running, IReadOnlyList<Zone>? zones = null)
    {
        PreTradeResult result = Evaluate(signal, state, zones);
        return result.IsAllowed ? Orders.TryPlace(result.Order!) : null;
    }

    /// <summary>The one skip of an evaluation that must be skipped, checked to be the only record (EXE-014).</summary>
    public SkipRecord SkipOf(Signal signal, BotState state = BotState.Running, IReadOnlyList<Zone>? zones = null)
    {
        int before = Events.Skips.Count;
        PreTradeResult result = Evaluate(signal, state, zones);
        Assert.False(result.IsAllowed);
        Assert.Null(result.Order);
        Assert.Equal(before + 1, Events.Skips.Count);
        Assert.Same(result.Skip, Events.Skips[^1]);
        return result.Skip!;
    }

    /// <summary>The planned order of an evaluation that must pass; no skip may be recorded.</summary>
    public PlannedOrder OrderOf(Signal signal, BotState state = BotState.Running, IReadOnlyList<Zone>? zones = null)
    {
        int before = Events.Skips.Count;
        PreTradeResult result = Evaluate(signal, state, zones);
        string why = result.Skip is null ? string.Empty : result.Skip.Reason + ": " + result.Skip.Detail;
        Assert.True(result.IsAllowed, "expected an order but the signal was skipped with " + why);
        Assert.Equal(before, Events.Skips.Count);
        return result.Order!;
    }
}

/// <summary>Assertions that run at the end of every execution scenario (WP06).</summary>
public static class ScenarioAssert
{
    /// <summary>
    /// No two pending orders share a label, no two positions share a label, every bot position has
    /// a stop loss, and the number of write requests is within <paramref name="maxRequests"/> (at
    /// most the 20 per hour of RSK-010 by default).
    /// </summary>
    public static void Global(FakeBroker broker, int maxRequests = 20)
    {
        IReadOnlyList<PendingOrderInfo> orders = broker.GetPendingOrders();
        IReadOnlyList<PositionInfo> positions = broker.GetPositions();
        Assert.Equal(orders.Count, orders.Select(o => o.Label).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(positions.Count, positions.Select(p => p.Label).Distinct(StringComparer.Ordinal).Count());
        Assert.All(positions.Where(p => RiskEngine.IsBotLabel(p.Label)), position => Assert.True(position.StopLoss.HasValue, "position " + position.Id + " has no stop loss"));
        Assert.True(broker.WriteRequestCount <= maxRequests, "write requests " + broker.WriteRequestCount + " above " + maxRequests);
    }
}
