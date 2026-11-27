using System.Globalization;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Overnight mode end to end (SIG-043, EXE-033) on an example XAUUSD, and the SessionRange mode on
/// the same symbol for the two-instance scenarios. Every value below was worked out by hand from
/// the bars; none is read from the code under test.
/// </summary>
/// <remarks>
/// <para>Symbol (an EXAMPLE, not FTMO's specification): pip 0.01, pip value 0.01 USD per ounce,
/// Digits 2, tick 0.01, volume 1 to 10,000 ounces in steps of 1, spread 0.30, no commission, no
/// reported sessions, bid 2650.00 and ask 2650.30. Execution: all day (00:00 to 24:00 UTC),
/// MaxSpreadPips 40 (0.40 USD), MinStopPips 800 (8 USD), everything else the default.</para>
/// <para>D1 bars open at 00:00 UTC every day from 2025-06-01 to 2026-03-19: close
/// 2650 - 0.05 x (bars after this one), high close + 20, low close - 20. The closes rise, so the
/// bias is Long; ATR(D1) is 40.</para>
/// <para>H4 bars from 2026-01-01: open and close 2650, high 2662, low 2638: ATR(H4) is 24 (the
/// SessionRange stop; the Overnight stop is not bounded by it).</para>
/// <para>M15 bars from 2026-02-01: open and close 2650, high 2651.5, low 2648.5: ATR(M15) is 3.
/// The 18:15 New York evaluation bar closes at 2650, so the Overnight setup is entry 2650 +
/// 0.03 x 3 = 2650.09, stop 2650.09 - 40 = 2610.09, target 2650.09 + 5 x 40 = 2850.09; the order
/// adds the spread 0.30 to all three: a buy stop at 2650.39, stop 2610.39, target 2850.39, expiry
/// 60 minutes after the evaluation bar's close. 18:15 New York is 22:15 UTC in summer time (from
/// 2026-03-08) and 23:15 UTC in winter time; 02:45 New York is 06:45 or 07:45 UTC.</para>
/// <para>On the SessionRange days the second range bar (19:15 New York) has the high 2662 and the
/// second last (02:30 New York) the low 2640: a range of 22, at least 0.5 x 40. The SessionRange
/// setup is a buy stop at 2662 + 0.30 = 2662.30, stop 2662 - 24 = 2638, target 2662 + 48 = 2710,
/// placed at 03:00 New York, closed at 16:30 New York.</para>
/// </remarks>
public static class OvernightBotData
{
    public const string Symbol = "XAUUSD";

    /// <summary>Hash of the parameters below in Overnight mode, computed with Python's hashlib from the canonical text.</summary>
    public const string Hash = "76489619";

    public const double SignalEntry = 2650.09;

    public const double SignalStop = 2610.09;

    public const double SignalTarget = 2850.09;

    public const double OrderEntry = 2650.39;

    public const double OrderStop = 2610.39;

    public const double OrderTarget = 2850.39;

    public const double RangeOrderEntry = 2662.30;

    public const double RangeStop = 2638.0;

    public const double RangeTarget = 2710.0;

    public static readonly DateTime End = Utc(2026, 3, 19, 0, 0);

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

    /// <summary>Close of the Overnight evaluation bar: 18:15 New York of the evening before the trading date.</summary>
    public static DateTime Evaluation(DateTime tradingDate)
    {
        return Ny(tradingDate, 18, 15);
    }

    /// <summary>The Overnight timed exit: 02:45 New York of the trading date.</summary>
    public static DateTime Exit(DateTime tradingDate)
    {
        return Ny(tradingDate, 2, 45);
    }

    public static string Id(DateTime tradingDate)
    {
        return "ON-" + tradingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }

    public static string RangeId(DateTime tradingDate)
    {
        return "RH-" + tradingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }

    public static string Label(string id)
    {
        return "ZFVG|v1|" + Symbol + "|" + id;
    }

    public static SymbolInfo Gold(double spread = 0.30, bool marketOpen = true)
    {
        return new SymbolInfo(Symbol, "XAU", "USD", 0.01, 0.01, 1.0, 1.0, 10000.0, spread, marketOpen, 0.0)
        {
            Digits = 2,
            TickSize = 0.01,
            Bid = 2650.0,
            Ask = 2650.0 + spread,
        };
    }

    public static ExecutionParameters Execution()
    {
        return new ExecutionParameters { SessionStartUtc = TimeSpan.Zero, SessionEndUtc = TimeSpan.FromHours(24.0), MaxSpreadPips = 40.0, MinStopPips = 800.0 };
    }

    /// <summary>The parameters of a gold instance with news off, the sanity cap off and the given mode and risk.</summary>
    public static BotParameters Parameters(EntryMode mode = EntryMode.Overnight, double riskPercent = 1.0)
    {
        BotParameters common = RecoveryData.Parameters(newsEnabled: false) with
        {
            Symbol = Symbol,
            Execution = Execution(),
        };
        common = common with { Risk = common.Risk with { RiskPercent = riskPercent, SanityCapEnabled = false } };
        return mode == EntryMode.Overnight
            ? common with { Overnight = new OvernightParameters() }
            : common with { SessionRange = new SessionRangeParameters() };
    }

    public static BotHarness Harness(DateTime atUtc, BarSet bars, double spread = 0.30)
    {
        var broker = new FakeBroker(atUtc, ExecutionData.Account(), Gold(spread));
        broker.MarginPerUnit = 10.0;
        return new BotHarness(bars, broker);
    }

    /// <summary>A harness started at <paramref name="atUtc"/> in Overnight mode.</summary>
    public static BotHarness Started(DateTime atUtc, BarSet bars, BotParameters? parameters = null, double spread = 0.30)
    {
        BotHarness h = Harness(atUtc, bars, spread);
        h.Start(atUtc, parameters ?? Parameters());
        return h;
    }

    /// <summary>A second instance on the harness's broker, history and transport, with its own store and journal.</summary>
    public static BotCore Instance(BotHarness h, BotParameters parameters, MemoryStore store, RecordingJournal journal)
    {
        var core = new BotCore(parameters, h.Port, store, h.Http, journal, h.IsBacktest);
        core.OnStart(h.History);
        return core;
    }

    /// <summary>The bars of the remarks, with a session range on each of <paramref name="rangeDays"/>.</summary>
    public static BarSet Bars(params DateTime[] rangeDays)
    {
        var d1 = new List<Bar>();
        int d1Count = (int)(End - D1Start).TotalDays;
        for (int i = 0; i < d1Count; i++)
        {
            double close = 2650.0 - (0.05 * (d1Count - 1 - i));
            d1.Add(BarBuilder.One(TimeFrame.D1, D1Start.AddDays(i), close, close + 20.0, close - 20.0, close));
        }

        var h4 = new List<Bar>();
        for (DateTime open = H4Start; open < End; open = open.AddHours(4.0))
        {
            h4.Add(BarBuilder.One(TimeFrame.H4, open, 2650.0, 2662.0, 2638.0, 2650.0));
        }

        var m15 = new List<Bar>();
        for (DateTime open = M15Start; open < End; open = open + M15)
        {
            m15.Add(M15Bar(open, rangeDays));
        }

        return new BarSet(d1, h4, m15);
    }

    private static Bar M15Bar(DateTime open, DateTime[] rangeDays)
    {
        foreach (DateTime day in rangeDays)
        {
            DateTime start = Ny(day, 19, 0);
            if (open == start + M15)
            {
                return BarBuilder.One(TimeFrame.M15, open, 2650.0, 2662.0, 2648.5, 2650.0);
            }

            if (open == start + (30 * M15))
            {
                return BarBuilder.One(TimeFrame.M15, open, 2650.0, 2651.5, 2640.0, 2650.0);
            }
        }

        return BarBuilder.One(TimeFrame.M15, open, 2650.0, 2651.5, 2648.5, 2650.0);
    }
}
