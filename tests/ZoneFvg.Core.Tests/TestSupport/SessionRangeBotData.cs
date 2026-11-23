using System.Globalization;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SessionRange mode end to end (SIG-034, EXE-024, EXE-025) on USDJPY with the default signal,
/// execution and session range parameters. Every value below was worked out by hand from the
/// bars; none is read from the code under test.
/// </summary>
/// <remarks>
/// <para>D1 bars open at 00:00 UTC every day from 2025-06-01 to 2026-03-13: open and close
/// 100.0 - 0.001 x (bars after this one), high close + 0.3, low close - 0.3. The closes rise, so
/// the bias is Long; ATR(D1) is 0.6, so the smallest range is 0.5 x 0.6 = 0.3.</para>
/// <para>H4 bars from 2026-01-01: open and close 100.0, high 100.2, low 99.8, so ATR(H4) is 0.4.</para>
/// <para>M15 bars from 2026-02-01: open and close 100.2, high 100.25, low 100.15 (a range of 0.1,
/// below the smallest range, so a trading day gives no setup), except on the planned trading
/// days. Their 32 range bars (19:00 to 02:45 New York) are the same bars, except that the second
/// has the high 100.4 and the second last the low 100.0. The range is 100.0 to 100.4, 0.4 wide.
/// The evaluation bar opens 02:45 New York and closes 03:00 New York: 08:00 UTC in winter (before
/// 2026-03-08), 07:00 UTC in summer.</para>
/// <para>Buy setup: entry 100.4, stop 100.4 - 0.4 = 100.0, target 100.4 + 2 x 0.4 = 101.2. The
/// order adds the spread 0.01 to the entry: a buy stop at 100.41, 41 pips of risk. A void bar has
/// the low 99.99, below the range low.</para>
/// <para>With <c>sell</c> every price p becomes 200 - p (high and low swap): the bias is Short, the
/// range is 99.6 to 100.0, the sell stop is at 99.6, the stop at 100.0 plus the spread, 100.01, and
/// the target at 98.8. A void bar has the high 100.01.</para>
/// <para>The embedded news history (<see cref="RecoveryData.News"/>) covers 2026-03-02 to
/// 2026-03-16; its USD events at 13:30 UTC on Friday 2026-03-06 and Wednesday 2026-03-11 make
/// windows from 13:00 to 14:00 UTC.</para>
/// </remarks>
public static class SessionRangeBotData
{
    public const string Symbol = RecoveryData.Symbol;

    /// <summary>Hash of the default parameters in SessionRange mode, computed with Python's hashlib from the canonical text.</summary>
    public const string Hash = "3f0f30a3";

    public const double RangeHigh = 100.4;

    public const double RangeLow = 100.0;

    public const double Entry = 100.4;

    public const double Stop = 100.0;

    public const double Target = 101.2;

    public const double OrderEntry = 100.41;

    public const double SellEntry = 99.6;

    public const double SellStop = 100.0;

    public const double SellOrderStop = 100.01;

    public const double SellTarget = 98.8;

    public const double AtrH4 = 0.4;

    /// <summary>Wednesday 2026-03-04, winter time (UTC-5).</summary>
    public static readonly DateTime Winter = new(2026, 3, 4);

    /// <summary>Wednesday 2026-03-11, summer time (UTC-4), with the 13:30 UTC news event.</summary>
    public static readonly DateTime NewsDay = new(2026, 3, 11);

    /// <summary>Thursday 2026-03-12, summer time (UTC-4), without news.</summary>
    public static readonly DateTime Summer = new(2026, 3, 12);

    /// <summary>Friday 2026-03-13, summer time (UTC-4), without news.</summary>
    public static readonly DateTime Friday = new(2026, 3, 13);

    /// <summary>Wednesday 2026-02-04, winter time: its setup comes before the engine is ready (the M15 warm-up of 1968 bars ends on 2026-02-21).</summary>
    public static readonly DateTime Early = new(2026, 2, 4);

    public static readonly DateTime End = Utc(2026, 3, 14, 0, 0);

    private static readonly DateTime D1Start = Utc(2025, 6, 1, 0, 0);

    private static readonly DateTime H4Start = Utc(2026, 1, 1, 0, 0);

    private static readonly DateTime M15Start = Utc(2026, 2, 1, 0, 0);

    private static readonly TimeSpan M15 = TimeSpan.FromMinutes(15.0);

    public static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    /// <summary>The UTC instant of a New York time of day in the trading day named by the date.</summary>
    public static DateTime Ny(DateTime tradingDate, int hour, int minute)
    {
        return NewYorkClock.UtcInTradingDay(tradingDate, new TimeSpan(hour, minute, 0));
    }

    /// <summary>Close of the evaluation bar: 03:00 New York.</summary>
    public static DateTime Evaluation(DateTime tradingDate)
    {
        return Ny(tradingDate, 3, 0);
    }

    public static string BuyId(DateTime tradingDate)
    {
        return "RH-" + tradingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }

    public static string SellId(DateTime tradingDate)
    {
        return "RL-" + tradingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }

    public static string Label(string id)
    {
        return "ZFVG|v1|" + Symbol + "|" + id;
    }

    public static BotParameters Parameters(bool newsEnabled = true, string news = RecoveryData.News)
    {
        return RecoveryData.Parameters(newsEnabled, news) with { SessionRange = new SessionRangeParameters() };
    }

    public static SignalEngine Engine()
    {
        return new SignalEngine(new SignalParameters(), new SessionRangeParameters(), new ExecutionParameters().FridayNoEntryUtc);
    }

    /// <summary>USDJPY quoted below a buy entry (bid 100.2, ask 100.21) or above a sell entry (bid 99.79, ask 99.8).</summary>
    public static SymbolInfo Quote(bool sell = false)
    {
        return sell ? RecoveryData.UsdJpy() with { Bid = 99.79, Ask = 99.8 } : RecoveryData.UsdJpy() with { Bid = 100.2, Ask = 100.21 };
    }

    public static BotHarness Harness(DateTime atUtc, BarSet bars, bool sell = false)
    {
        var broker = new FakeBroker(atUtc, ExecutionData.Account(), Quote(sell));
        broker.MarginPerUnit = 0.04;
        return new BotHarness(bars, broker);
    }

    /// <summary>A harness started at <paramref name="atUtc"/> in SessionRange mode.</summary>
    public static BotHarness Started(DateTime atUtc, BarSet bars, bool sell = false, BotParameters? parameters = null)
    {
        BotHarness h = Harness(atUtc, bars, sell);
        h.Start(atUtc, parameters ?? Parameters());
        return h;
    }

    /// <summary>
    /// The bars of the remarks with a range on each of <paramref name="days"/> and, when
    /// <paramref name="voidOpenUtc"/> is set, a void bar opening then.
    /// </summary>
    public static BarSet Bars(bool sell, DateTime? voidOpenUtc, params DateTime[] days)
    {
        var d1 = new List<Bar>();
        int d1Count = (int)((End - D1Start).TotalDays);
        for (int i = 0; i < d1Count; i++)
        {
            double close = 100.0 - (0.001 * (d1Count - 1 - i));
            d1.Add(Make(TimeFrame.D1, D1Start.AddDays(i), sell, close, close + 0.3, close - 0.3, close));
        }

        var h4 = new List<Bar>();
        for (DateTime open = H4Start; open < End; open = open.AddHours(4.0))
        {
            h4.Add(Make(TimeFrame.H4, open, sell, 100.0, 100.2, 99.8, 100.0));
        }

        var m15 = new List<Bar>();
        for (DateTime open = M15Start; open < End; open = open + M15)
        {
            m15.Add(M15Bar(open, sell, voidOpenUtc, days));
        }

        return new BarSet(d1, h4, m15);
    }

    public static BarSet Bars(params DateTime[] days)
    {
        return Bars(false, null, days);
    }

    private static Bar M15Bar(DateTime open, bool sell, DateTime? voidOpenUtc, DateTime[] days)
    {
        if (voidOpenUtc.HasValue && open == voidOpenUtc.Value)
        {
            return Make(TimeFrame.M15, open, sell, 100.1, 100.15, 99.99, 100.05);
        }

        foreach (DateTime day in days)
        {
            DateTime start = Ny(day, 19, 0);
            if (open == start + M15)
            {
                return Make(TimeFrame.M15, open, sell, 100.2, 100.4, 100.15, 100.2);
            }

            if (open == start + (30 * M15))
            {
                return Make(TimeFrame.M15, open, sell, 100.2, 100.25, 100.0, 100.2);
            }
        }

        return Make(TimeFrame.M15, open, sell, 100.2, 100.25, 100.15, 100.2);
    }

    private static Bar Make(TimeFrame timeFrame, DateTime open, bool sell, double o, double h, double l, double c)
    {
        return sell
            ? BarBuilder.One(timeFrame, open, 200.0 - o, 200.0 - l, 200.0 - h, 200.0 - c)
            : BarBuilder.One(timeFrame, open, o, h, l, c);
    }
}
