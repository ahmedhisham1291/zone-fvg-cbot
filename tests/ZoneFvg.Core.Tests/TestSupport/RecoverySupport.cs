using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Recovery;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Hand-built data for the WP07 scenarios: one USDJPY instance whose engine finds the reference
/// demand zone of <see cref="EngineScenarios"/>, moved to Wednesday 2026-03-04 so that the signal
/// falls inside the trading session.
/// </summary>
/// <remarks>
/// <para><b>Bars.</b> H4 = <see cref="EngineScenarios.DemandH4"/> with its leg bar opening at
/// <see cref="Activation"/> - 4 hours, then flat bars P = (100.95, 101.0, 100.9, 100.95). M15 =
/// flat bars (100.6, 100.65, 100.55, 100.6) up to the activation, the reference tail m0, m1, m2
/// (<see cref="EngineScenarios.TouchAndGap"/>), then bars P. D1 = rising dojis (bias Long) whose
/// last pre-activation bar closes at the activation, then dojis at 70.0 (still above the EMA).
/// Every series is continuous, also over the weekend, so no gap exists unless a test removes a
/// bar.</para>
/// <para><b>Zone and signal.</b> Zone D-20260303-1600 (base starting 16 hours before the
/// activation). m2 closes at 08:45 and confirms a buy signal: entry 100.675, stop 99.481785714,
/// target 103.061428571 (see <see cref="EngineScenarios"/>).</para>
/// <para><b>Order.</b> USDJPY: pip 0.01, pip value 0.0001 USD per unit, volume step 1,000, spread
/// 0.01 (1 pip), no commission. Prices are rounded to 3 decimals: entry 100.675, stop 99.482,
/// target 103.061, stop distance 119.3 pips. Volume = 500 / (119.3 x 0.0001) = 41,911.1, rounded
/// down to 41,000. Risk money = 41,000 x 119.3 x 0.0001 = 489.13. Expiry = 08:45 + 8 x 15
/// minutes = 10:45. Label ZFVG|v1|USDJPY|D-20260303-1600.</para>
/// <para><b>News.</b> Embedded history with high-impact USD events on Friday 2026-03-06 13:30 and
/// Wednesday 2026-03-11 13:30, so the weeks of 2 and 9 March are covered and no window touches the
/// signal.</para>
/// </remarks>
public static class RecoveryData
{
    public const string Symbol = "USDJPY";

    public const string ZoneId = "D-20260303-1600";

    public const string Label = "ZFVG|v1|USDJPY|D-20260303-1600";

    public const double Entry = 100.675;

    public const double Stop = 99.482;

    public const double Target = 103.061;

    public const double Volume = 41000.0;

    public const string News = "2026-03-06 13:30,USD,High,Non-Farm Employment Change\n2026-03-11 13:30,USD,High,CPI m/m\n";

    /// <summary>Wednesday 2026-03-04 08:00 UTC: activation of the zone, open of m0.</summary>
    public static readonly DateTime Activation = new(2026, 3, 4, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>08:45: close of m2, the confirmation of the reference signal.</summary>
    public static readonly DateTime Confirmation = Activation.AddMinutes(45.0);

    /// <summary>Expiry of the reference order: 10:45.</summary>
    public static readonly DateTime Expiry = Activation.AddMinutes(165.0);

    public static string Hash => ParameterHash.Compute(new SignalParameters(), new ExecutionParameters());

    public static SymbolInfo UsdJpy(bool marketOpen = true, double spread = 0.01)
    {
        return new SymbolInfo(Symbol, "USD", "JPY", 0.01, 0.0001, 1000.0, 1000.0, 10000000.0, spread, marketOpen, 0.0) { Digits = 3, TickSize = 0.001 };
    }

    public static BotParameters Parameters(bool newsEnabled = true, string news = News)
    {
        return new BotParameters
        {
            Symbol = Symbol,
            Risk = RiskData.Parameters,
            News = new NewsParameters { NewsEnabled = newsEnabled },
            EmbeddedNewsHistory = news,
            NewsFeedUrl = NewsTestData.FeedUrl,
        };
    }

    /// <summary>
    /// <see cref="Parameters"/> with a heartbeat address (OPS-005). The harness's transport fails
    /// the call, so each heartbeat is journaled as <c>heartbeat event=failed ... state=&lt;state&gt;</c>.
    /// </summary>
    public static BotParameters WithHeartbeat()
    {
        return Parameters() with { Ops = new OpsParameters { HeartbeatUrl = "https://hb.example.invalid/ping" } };
    }

    /// <summary>The comment of the reference order, with the given parameter hash (default: the current one).</summary>
    public static string Comment(string? hash = null)
    {
        return OrderComment.Build(new CommentPlan(Entry, Stop, Target, 489.13, hash ?? Hash), 3);
    }

    /// <summary>The bar set: D1, H4 and M15 series with the given numbers of bars before and after the activation.</summary>
    public static BarSet Bars(int d1Before = 230, int h4Before = 300, int m15Before = 2400, int postDays = 6, int postH4 = 40, int postM15 = 520)
    {
        IReadOnlyList<Bar> d1 = new BarBuilder(TimeFrame.D1)
            .At(Activation.AddDays(-d1Before))
            .Repeat(d1Before, (b, i) => b.Doji(70.0 - (0.1 * (d1Before - 1 - i))))
            .Repeat(postDays, (b, _) => b.Doji(70.0))
            .Build();
        var h4 = new List<Bar>(EngineScenarios.DemandH4(h4Before, Activation));
        h4.AddRange(new BarBuilder(TimeFrame.H4).At(Activation).Repeat(postH4, (b, _) => b.Ohlc(100.95, 101.0, 100.9, 100.95)).Build());
        BarBuilder m15 = new BarBuilder(TimeFrame.M15)
            .At(Activation.AddMinutes(-15.0 * m15Before))
            .Repeat(m15Before, (b, _) => b.Ohlc(100.6, 100.65, 100.55, 100.6));
        foreach ((double open, double high, double low, double close) in EngineScenarios.TouchAndGap)
        {
            m15.Ohlc(open, high, low, close);
        }

        m15.Repeat(postM15, (b, _) => b.Ohlc(100.95, 101.0, 100.9, 100.95));
        return new BarSet(d1, h4, m15.Build());
    }

    public static DateTime At(int day, int hour, int minute, int second = 0)
    {
        return new DateTime(2026, 3, day, hour, minute, second, DateTimeKind.Utc);
    }
}

/// <summary>
/// Hand-built data for the WP27 scenarios (REC-017): one USDJPY instance in Breakout mode with the
/// default breakout parameters, next to <see cref="RecoveryData"/> (same symbol data, risk, news and
/// harness).
/// </summary>
/// <remarks>
/// <para><b>D1.</b> 230 bars opening at 00:00 UTC up to Tuesday 2026-03-03, closes rising by 0.001 a
/// day to 100.0, each bar (c, c + 0.3, c - 0.3, c): bias Long, and no earlier M15 close reaches a
/// reference high. The Tuesday bar (100.0, 100.3, 99.7, 100.0) is the reference of Wednesday 4 March.
/// Then Wednesday (100.0, 100.4, 99.95, 100.35), Thursday (100.35, 100.5, 100.3, 100.45) and Friday
/// (100.45, 100.6, 100.4, 100.55).</para>
/// <para><b>H4.</b> 300 bars before Wednesday 00:00 and 18 after, all (100.0, 100.2, 99.8, 100.0):
/// ATR(H4) 0.4.</para>
/// <para><b>M15.</b> 2,400 bars before Wednesday 00:00 at (100.0, 100.05, 99.95, 100.0), then 288
/// bars to Saturday 00:00: on Wednesday (100.25, 100.3, 100.2, 100.25) up to the Wednesday break bar
/// (100.25, 100.35, 100.25, 100.34), which closes above 100.3; then (100.34, 100.4, 100.3, 100.35) up
/// to the Thursday break bar (100.35, 100.45, 100.35, 100.44), which closes above 100.4; then (100.44,
/// 100.5, 100.4, 100.45) up to the Friday break bar (100.45, 100.55, 100.45, 100.54), which closes
/// above 100.5; then (100.54, 100.6, 100.5, 100.55). Every true range is 0.1 within 14 bars of a break
/// bar: ATR(M15) 0.1. The break bars open by default at Wednesday 09:00, Thursday 10:00 and Friday
/// 12:00.</para>
/// <para><b>Buy of Wednesday (break bar 09:00, close 09:15).</b> Signal entry 100.35 + 0.1 x 0.1 =
/// 100.36, stop 100.36 - 0.4 = 99.96, target 100.36 + 2 x 0.4 = 101.16. The order: entry 100.37 (the
/// spread 0.01 added to a buy stop's entry), stop 99.96, target 101.16, 41 pips; volume 500 / (41 x
/// 0.0001) = 121,951, rounded down to 121,000; risk 496.10; expiry 09:15 + 8 x 15 minutes = 11:15;
/// label ZFVG|v1|USDJPY|BH-20260303-0000. Thursday (break bar 10:00): entry 100.46 + 0.01 = 100.47,
/// stop 100.06, target 101.26, id BH-20260304-0000.</para>
/// <para><b>Sell (mirror).</b> Every price p becomes 200 - p (high and low swapped), so the D1 closes
/// fall, the bias is Short and the Wednesday break bar (99.75, 99.75, 99.65, 99.66) closes below the
/// reference low 99.7. Signal entry 99.65 - 0.01 = 99.64, stop 99.64 + 0.4 = 100.04, target 98.84.
/// The order: entry 99.64, stop 100.05 (the spread added to a sell's stop), target 98.84, 41 pips,
/// volume 121,000, label ZFVG|v1|USDJPY|BL-20260303-0000.</para>
/// <para><b>Quotes.</b> Buy set bid 100.25, ask 100.26; sell set bid 99.75, ask 99.76: below a buy
/// entry and above a sell entry, so the entry has not been reached.</para>
/// </remarks>
public static class BreakoutRecoveryData
{
    public const string BuyId = "BH-20260303-0000";

    public const string BuyLabel = "ZFVG|v1|USDJPY|BH-20260303-0000";

    public const string NextBuyId = "BH-20260304-0000";

    public const string NextBuyLabel = "ZFVG|v1|USDJPY|BH-20260304-0000";

    public const string FridayBuyId = "BH-20260305-0000";

    public const string SellId = "BL-20260303-0000";

    public const string SellLabel = "ZFVG|v1|USDJPY|BL-20260303-0000";

    /// <summary>Parameter hash with default signal, execution and breakout parameters (WP26, ParameterHashBreakoutTests).</summary>
    public const string Hash = "119a3864";

    /// <summary>Parameter hash of v0.9 with default signal and execution parameters (ZoneFvg mode).</summary>
    public const string ZoneFvgHash = "f681dff2";

    public const double Entry = 100.37;

    public const double Stop = 99.96;

    public const double Target = 101.16;

    public const double NextEntry = 100.47;

    public const double NextStop = 100.06;

    public const double NextTarget = 101.26;

    public const double SellEntry = 99.64;

    public const double SellStop = 100.05;

    public const double SellTarget = 98.84;

    public const double Volume = 121000.0;

    public const double RiskMoney = 496.1;

    private const int D1Before = 230;

    private const int H4Before = 300;

    private const int H4After = 18;

    private const int M15Before = 2400;

    private const int M15After = 288;

    /// <summary>Wednesday 2026-03-04 00:00 UTC: close of the reference day of the first break.</summary>
    public static readonly DateTime Wednesday = new(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Wednesday 06:00: the usual start of a scenario.</summary>
    public static readonly DateTime Start = Wednesday.AddHours(6.0);

    /// <summary>Wednesday 09:15: close of the default Wednesday break bar.</summary>
    public static readonly DateTime Confirmation = Wednesday.AddHours(9.25);

    /// <summary>Wednesday 11:15: expiry of the Wednesday order.</summary>
    public static readonly DateTime Expiry = Wednesday.AddHours(11.25);

    /// <summary>Thursday 10:15: close of the default Thursday break bar.</summary>
    public static readonly DateTime NextConfirmation = Wednesday.AddDays(1.0).AddHours(10.25);

    /// <summary><see cref="RecoveryData.Parameters"/> with default breakout parameters (Breakout mode).</summary>
    public static BotParameters Parameters(bool newsEnabled = true, string news = RecoveryData.News)
    {
        return RecoveryData.Parameters(newsEnabled, news) with { Breakout = new BreakoutParameters() };
    }

    /// <summary>USDJPY with the quote of the buy or the sell set.</summary>
    public static SymbolInfo Quote(bool sell = false)
    {
        return sell ? RecoveryData.UsdJpy() with { Bid = 99.75, Ask = 99.76 } : RecoveryData.UsdJpy() with { Bid = 100.25, Ask = 100.26 };
    }

    /// <summary>A harness on the breakout bars whose broker starts at the given time with the quote of the set.</summary>
    public static BotHarness Harness(DateTime atUtc, bool sell = false, BarSet? bars = null, AccountSnapshot? account = null)
    {
        var broker = new FakeBroker(atUtc, account ?? ExecutionData.Account(), Quote(sell));
        broker.MarginPerUnit = 0.04;
        return new BotHarness(bars ?? Bars(sell), broker);
    }

    /// <summary>The comment of a bot order with the given levels, the risk 496.10 and the given hash.</summary>
    public static string Comment(double entry, double stop, double target, string hash = Hash)
    {
        return OrderComment.Build(new CommentPlan(entry, stop, target, RiskMoney, hash), 3);
    }

    /// <summary>The comment of the Wednesday buy.</summary>
    public static string BuyComment(string hash = Hash)
    {
        return Comment(Entry, Stop, Target, hash);
    }

    /// <summary>The bar set (see the remarks); the break bars open at the given times of day of Wednesday, Thursday and Friday.</summary>
    public static BarSet Bars(bool sell = false, TimeSpan? wednesday = null, TimeSpan? thursday = null, TimeSpan? friday = null)
    {
        DateTime[] breaks =
        {
            Wednesday + (wednesday ?? TimeSpan.FromHours(9.0)),
            Wednesday.AddDays(1.0) + (thursday ?? TimeSpan.FromHours(10.0)),
            Wednesday.AddDays(2.0) + (friday ?? TimeSpan.FromHours(12.0)),
        };
        BarBuilder d1 = new BarBuilder(TimeFrame.D1)
            .At(Wednesday.AddDays(-D1Before))
            .Repeat(D1Before, (b, i) =>
            {
                double close = 100.0 - (0.001 * (D1Before - 1 - i));
                Add(b, sell, (close, close + 0.3, close - 0.3, close));
            });
        Add(d1, sell, (100.0, 100.4, 99.95, 100.35));
        Add(d1, sell, (100.35, 100.5, 100.3, 100.45));
        Add(d1, sell, (100.45, 100.6, 100.4, 100.55));
        IReadOnlyList<Bar> h4 = new BarBuilder(TimeFrame.H4)
            .At(Wednesday.AddHours(-4.0 * H4Before))
            .Repeat(H4Before + H4After, (b, _) => b.Ohlc(100.0, 100.2, 99.8, 100.0))
            .Build();
        DateTime m15Start = Wednesday.AddMinutes(-15.0 * M15Before);
        IReadOnlyList<Bar> m15 = new BarBuilder(TimeFrame.M15)
            .At(m15Start)
            .Repeat(M15Before + M15After, (b, i) => Add(b, sell, M15At(m15Start.AddMinutes(15.0 * i), breaks)))
            .Build();
        return new BarSet(d1.Build(), h4, m15);
    }

    private static (double Open, double High, double Low, double Close) M15At(DateTime openUtc, DateTime[] breaks)
    {
        if (openUtc < Wednesday)
        {
            return (100.0, 100.05, 99.95, 100.0);
        }

        if (openUtc < breaks[0])
        {
            return (100.25, 100.3, 100.2, 100.25);
        }

        if (openUtc == breaks[0])
        {
            return (100.25, 100.35, 100.25, 100.34);
        }

        if (openUtc < breaks[1])
        {
            return (100.34, 100.4, 100.3, 100.35);
        }

        if (openUtc == breaks[1])
        {
            return (100.35, 100.45, 100.35, 100.44);
        }

        if (openUtc < breaks[2])
        {
            return (100.44, 100.5, 100.4, 100.45);
        }

        return openUtc == breaks[2] ? (100.45, 100.55, 100.45, 100.54) : (100.54, 100.6, 100.5, 100.55);
    }

    private static void Add(BarBuilder builder, bool sell, (double Open, double High, double Low, double Close) bar)
    {
        if (sell)
        {
            builder.Ohlc(200.0 - bar.Open, 200.0 - bar.Low, 200.0 - bar.High, 200.0 - bar.Close);
        }
        else
        {
            builder.Ohlc(bar.Open, bar.High, bar.Low, bar.Close);
        }
    }
}

/// <summary>The three bar series of a scenario and their merged feed in intake order.</summary>
public sealed class BarSet
{
    public BarSet(IReadOnlyList<Bar> d1, IReadOnlyList<Bar> h4, IReadOnlyList<Bar> m15)
    {
        D1 = d1;
        H4 = h4;
        M15 = m15;
        Feed = EngineFeed.Merge(d1, h4, m15);
    }

    public IReadOnlyList<Bar> D1 { get; }

    public IReadOnlyList<Bar> H4 { get; }

    public IReadOnlyList<Bar> M15 { get; }

    public IReadOnlyList<FeedBar> Feed { get; }

    public IReadOnlyList<Bar> Of(TimeFrame timeFrame)
    {
        return timeFrame switch
        {
            TimeFrame.D1 => D1,
            TimeFrame.H4 => H4,
            _ => M15,
        };
    }
}

/// <summary>
/// In-memory <see cref="IBarHistory"/>: the closed bars of a <see cref="BarSet"/> up to the
/// broker's server time. A test can limit how many bars are available, remove bars to make a
/// gap, and let <see cref="RequestMore"/> lift the limit.
/// </summary>
public sealed class MemoryBarHistory : IBarHistory
{
    private readonly BarSet _bars;
    private readonly Func<DateTime> _now;

    public MemoryBarHistory(BarSet bars, Func<DateTime> now)
    {
        _bars = bars;
        _now = now;
    }

    /// <summary>Most bars returned per timeframe; none when absent.</summary>
    public Dictionary<TimeFrame, int> Available { get; } = new();

    /// <summary>Open times of bars that are missing.</summary>
    public HashSet<DateTime> Missing { get; } = new();

    /// <summary>When true, <see cref="RequestMore"/> removes the limit and the missing bars of its timeframe.</summary>
    public bool RequestMoreHelps { get; set; }

    public List<(TimeFrame TimeFrame, int Count)> MoreRequests { get; } = new();

    public IReadOnlyList<Bar> ClosedBars(TimeFrame timeFrame, int count)
    {
        TimeSpan length = BarBuilder.LengthOf(timeFrame);
        DateTime now = _now();
        List<Bar> closed = _bars.Of(timeFrame).Where(bar => bar.OpenTimeUtc + length <= now && !Missing.Contains(bar.OpenTimeUtc)).ToList();
        int take = Math.Min(count, Available.TryGetValue(timeFrame, out int limit) ? limit : int.MaxValue);
        return closed.Skip(Math.Max(0, closed.Count - take)).ToList();
    }

    public void RequestMore(TimeFrame timeFrame, int count)
    {
        MoreRequests.Add((timeFrame, count));
        if (RequestMoreHelps)
        {
            Available.Remove(timeFrame);
            Missing.RemoveWhere(open => _bars.Of(timeFrame).Any(bar => bar.OpenTimeUtc == open));
        }
    }
}

/// <summary>Journal sink that keeps every line; can be told to throw.</summary>
public sealed class RecordingJournal : IJournalSink
{
    public List<(string Category, string Line)> Entries { get; } = new();

    public bool Throw { get; set; }

    public void Write(string category, string line)
    {
        if (Throw)
        {
            throw new InvalidOperationException("journal unavailable");
        }

        Entries.Add((category, line));
    }

    public IReadOnlyList<string> Lines(string category)
    {
        return Entries.Where(entry => entry.Category == category).Select(entry => entry.Line).ToList();
    }

    public int Count(string category, string fragment)
    {
        return Lines(category).Count(line => line.Contains(fragment, StringComparison.Ordinal));
    }
}

/// <summary>A broker port that forwards to a <see cref="FakeBroker"/> and can inject read failures.</summary>
public sealed class SwitchableBroker : IBroker
{
    public SwitchableBroker(FakeBroker inner)
    {
        Inner = inner;
    }

    public FakeBroker Inner { get; }

    /// <summary>When set, <see cref="GetPositions"/> throws it.</summary>
    public Exception? PositionsFault { get; set; }

    /// <summary>When set, <see cref="GetHistory"/> returns this instead of the real history.</summary>
    public Func<DateTime, IReadOnlyList<ClosedTradeInfo>>? History { get; set; }

    /// <summary>When set, <see cref="ServerTimeUtc"/> throws it.</summary>
    public Exception? TimeFault { get; set; }

    /// <summary>When set, <see cref="GetSymbol"/> throws it (REC-016).</summary>
    public Exception? SymbolFault { get; set; }

    /// <summary>How many more <see cref="GetSymbol"/> calls throw <see cref="SymbolFault"/>; unlimited by default.</summary>
    public int SymbolFaultCalls { get; set; } = int.MaxValue;

    /// <summary>Number of <see cref="GetSymbol"/> calls, failed ones included.</summary>
    public int SymbolReads { get; private set; }

    public DateTime ServerTimeUtc => TimeFault != null ? throw TimeFault : Inner.ServerTimeUtc;

    public bool IsConnected => Inner.IsConnected;

    public AccountSnapshot GetAccount() => Inner.GetAccount();

    public SymbolInfo GetSymbol(string symbol)
    {
        SymbolReads++;
        if (SymbolFault != null && SymbolFaultCalls > 0)
        {
            SymbolFaultCalls--;
            throw SymbolFault;
        }

        return Inner.GetSymbol(symbol);
    }

    public IReadOnlyList<PositionInfo> GetPositions() => PositionsFault != null ? throw PositionsFault : Inner.GetPositions();

    public IReadOnlyList<PendingOrderInfo> GetPendingOrders() => Inner.GetPendingOrders();

    public IReadOnlyList<ClosedTradeInfo> GetHistory(DateTime sinceUtc) => History != null ? History(sinceUtc) : Inner.GetHistory(sinceUtc);

    public double EstimateMargin(string symbol, Side side, double volumeUnits) => Inner.EstimateMargin(symbol, side, volumeUnits);

    public OrderResult PlaceLimitOrder(LimitOrderRequest request) => Inner.PlaceLimitOrder(request);

    public OrderResult PlaceStopOrder(StopOrderRequest request) => Inner.PlaceStopOrder(request);

    public OrderResult CancelOrder(long orderId) => Inner.CancelOrder(orderId);

    public OrderResult ModifyPositionProtection(long positionId, double? stopLoss, double? takeProfit) => Inner.ModifyPositionProtection(positionId, stopLoss, takeProfit);

    public OrderResult ClosePosition(long positionId) => Inner.ClosePosition(positionId);
}

/// <summary>
/// One USDJPY bot instance (or several) wired to a <see cref="FakeBroker"/>, an in-memory history,
/// store, transport, journal and hooks. Bars are fed in intake order; before each bar the broker's
/// server time moves to the bar's close.
/// </summary>
public sealed class BotHarness
{
    private DateTime _fedUntil;

    public BotHarness(BarSet? bars = null, FakeBroker? broker = null, bool isBacktest = true, FakeTransport? http = null)
    {
        Bars = bars ?? RecoveryData.Bars();
        Broker = broker ?? NewBroker(RecoveryData.Activation);
        Port = new SwitchableBroker(Broker);
        History = new MemoryBarHistory(Bars, () => Broker.ServerTimeUtc);
        IsBacktest = isBacktest;
        Http = http ?? new FakeTransport(NewsTestData.Failed());
    }

    public BarSet Bars { get; }

    public FakeBroker Broker { get; }

    public SwitchableBroker Port { get; }

    public MemoryBarHistory History { get; }

    public MemoryStore Store { get; } = new();

    public FakeTransport Http { get; }

    public RecordingJournal Journal { get; } = new();

    public bool IsBacktest { get; }

    public BotCore Core { get; private set; } = null!;

    public static FakeBroker NewBroker(DateTime now, AccountSnapshot? account = null, bool marketOpen = true)
    {
        var broker = new FakeBroker(now, account ?? ExecutionData.Account(), RecoveryData.UsdJpy(marketOpen));
        broker.MarginPerUnit = 0.04;
        return broker;
    }

    public BotCore NewCore(BotParameters? parameters = null)
    {
        return new BotCore(parameters ?? RecoveryData.Parameters(), Port, Store, Http, Journal, IsBacktest);
    }

    /// <summary>Moves the broker to the time, creates a new core (the old one is dropped) and starts it.</summary>
    public BotCore Start(DateTime atUtc, BotParameters? parameters = null)
    {
        Broker.AdvanceTo(atUtc);
        Core = NewCore(parameters);
        Core.OnStart(History);
        _fedUntil = atUtc;
        return Core;
    }

    /// <summary>Feeds every bar closing after the last fed time and at or before the given time to the cores (default: the current core).</summary>
    public void FeedTo(DateTime untilUtc, params BotCore[] cores)
    {
        BotCore[] targets = cores.Length == 0 ? new[] { Core } : cores;
        foreach (FeedBar item in Bars.Feed.Where(item => item.CloseUtc > _fedUntil && item.CloseUtc <= untilUtc))
        {
            if (item.CloseUtc > Broker.ServerTimeUtc)
            {
                Broker.AdvanceTo(item.CloseUtc);
            }

            foreach (BotCore core in targets)
            {
                core.OnBarClosed(item.TimeFrame, item.Bar);
            }
        }

        if (untilUtc > Broker.ServerTimeUtc)
        {
            Broker.AdvanceTo(untilUtc);
        }

        _fedUntil = untilUtc;
    }

    /// <summary>Marks the bars up to the time as already seen, without feeding them (they closed while no core ran).</summary>
    public void SkipTo(DateTime untilUtc)
    {
        Broker.AdvanceTo(untilUtc);
        _fedUntil = untilUtc;
    }

    public void Timer(DateTime atUtc)
    {
        Broker.AdvanceTo(atUtc);
        Core.OnTimer();
    }

    /// <summary>Fills the reference buy limit: the ask touches the entry.</summary>
    public void FillReferenceOrder(DateTime atUtc)
    {
        Broker.AdvanceTo(atUtc);
        Broker.Tick(RecoveryData.Symbol, 100.66, 100.67);
    }

    public int Count(BrokerCall call)
    {
        return Broker.Count(call);
    }

    /// <summary>
    /// The alerts of the given kind the core raised, read from its journal: alert lines (queued,
    /// disabled or collapsed) of the kind's Telegram kind, or notice lines for a journal-only kind.
    /// An execution alert reads <c>ZFVG USDJPY &lt;Telegram kind&gt;: USDJPY &lt;kind&gt;: ...</c>, so
    /// execution kinds are counted by that text and the core's own alerts exclude it.
    /// </summary>
    public IReadOnlyList<string> AlertLines(AlertKind kind)
    {
        AlertKind? telegram = AlertKinds.Telegram(kind);
        if (!telegram.HasValue)
        {
            return Journal.Lines("notice").Where(line => line.Contains(" notice kind=" + kind + " ", StringComparison.Ordinal)).ToList();
        }

        string prefix = " text=ZFVG\\s" + RecoveryData.Symbol + "\\s" + telegram + ":\\s" + RecoveryData.Symbol + "\\s";
        List<string> lines = Journal.Lines("alert")
            .Where(line => RaiseEvents.Any(raise => line.Contains(" alert event=" + raise + " kind=" + telegram + " ", StringComparison.Ordinal)))
            .ToList();
        if (ExecutionKinds.Contains(kind))
        {
            return lines.Where(line => line.Contains(prefix + kind + ":", StringComparison.Ordinal)).ToList();
        }

        return lines.Where(line => !ExecutionKinds.Any(execution => line.Contains(prefix + execution + ":", StringComparison.Ordinal))).ToList();
    }

    /// <summary>Number of <see cref="AlertLines"/>.</summary>
    public int Alerts(AlertKind kind)
    {
        return AlertLines(kind).Count;
    }

    private static readonly string[] RaiseEvents = { "queued", "disabled", "collapsed" };

    // The kinds the execution layer raises as ExecutionAlert (OPS-004).
    private static readonly AlertKind[] ExecutionKinds =
    {
        AlertKind.SanityCap, AlertKind.OrderRejected, AlertKind.OrderMismatch, AlertKind.OrderNotFound, AlertKind.CancelFailed,
        AlertKind.StopLossMissing, AlertKind.StopLossRestored, AlertKind.PositionClosedWithoutStop, AlertKind.CloseFailed, AlertKind.RequestLimitReached,
    };

    public PendingOrderInfo? ReferenceOrder()
    {
        return Broker.GetPendingOrders().SingleOrDefault(order => order.Label == RecoveryData.Label);
    }

    public PositionInfo? ReferencePosition()
    {
        return Broker.GetPositions().SingleOrDefault(position => position.Label == RecoveryData.Label);
    }
}
