using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The gold test symbol of WP24 (REC-016, NWS-013, RSK-015). Every value here is an EXAMPLE
/// chosen for the tests, not FTMO's XAUUSD specification, which the owner has not supplied yet:
/// the bot reads the real values from the broker at runtime.
/// </summary>
/// <remarks>
/// Example values: name XAUUSD, base XAU, quote USD, pip 0.01, Digits 2, tick 0.01, pip value
/// 0.01 USD per ounce (quote currency USD, USD account), volume minimum and step 1 ounce, maximum
/// 10,000 ounces, spread 0.30, no commission. Sessions, UTC: Sunday 22:00 to Monday 21:00 and
/// then each weekday 22:00 to the next day 21:00, the last one ending Friday 21:00 (a one-hour
/// daily break). 2026-03-03 is a Tuesday.
/// </remarks>
public static class GoldData
{
    public const string Symbol = "XAUUSD";

    /// <summary>The example gold sessions: a one-hour daily break from 21:00 to 22:00 UTC.</summary>
    public static readonly IReadOnlyList<TradingSession> Sessions = new[]
    {
        Session(DayOfWeek.Sunday, 22, DayOfWeek.Monday, 21),
        Session(DayOfWeek.Monday, 22, DayOfWeek.Tuesday, 21),
        Session(DayOfWeek.Tuesday, 22, DayOfWeek.Wednesday, 21),
        Session(DayOfWeek.Wednesday, 22, DayOfWeek.Thursday, 21),
        Session(DayOfWeek.Thursday, 22, DayOfWeek.Friday, 21),
    };

    /// <summary>A forex week without a daily break: Sunday 22:00 to Friday 22:00 UTC.</summary>
    public static readonly IReadOnlyList<TradingSession> ForexSessions = new[]
    {
        Session(DayOfWeek.Sunday, 22, DayOfWeek.Friday, 22),
    };

    /// <summary>Start of the scenarios: Wednesday 2026-03-04 10:00 UTC.</summary>
    public static readonly DateTime Start = new(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc);

    public static TradingSession Session(DayOfWeek startDay, int startHour, DayOfWeek endDay, int endHour)
    {
        return new TradingSession(startDay, TimeSpan.FromHours(startHour), endDay, TimeSpan.FromHours(endHour));
    }

    /// <summary>The example gold symbol, with the given sessions (default: <see cref="Sessions"/>).</summary>
    public static SymbolInfo Gold(IReadOnlyList<TradingSession>? sessions = null)
    {
        return new SymbolInfo(Symbol, "XAU", "USD", 0.01, 0.01, 1.0, 1.0, 10000.0, 0.30, true, 0.0)
        {
            Digits = 2,
            TickSize = 0.01,
            Sessions = sessions ?? Sessions,
        };
    }

    /// <summary>The parameters of a gold instance (news off), with the owner's gold values of decision D.</summary>
    public static BotParameters Parameters()
    {
        return RecoveryData.Parameters(newsEnabled: false) with { Symbol = Symbol };
    }

    /// <summary>
    /// True when the example gold sessions are open at the instant: Sunday 22:00 to Friday 21:00
    /// except 21:00 to 22:00 on Monday to Thursday. Written independently of the code under test.
    /// </summary>
    public static bool InSession(DateTime utc)
    {
        int minute = ((int)utc.DayOfWeek * 1440) + (utc.Hour * 60) + utc.Minute;
        const int Sunday2200 = (0 * 1440) + (22 * 60);
        const int Friday2100 = (5 * 1440) + (21 * 60);
        if (minute < Sunday2200 || minute >= Friday2100)
        {
            return false;
        }

        return !(utc.Hour == 21 && utc.DayOfWeek >= DayOfWeek.Monday && utc.DayOfWeek <= DayOfWeek.Thursday);
    }

    /// <summary>
    /// Flat gold history with the break: a bar of each timeframe exists when at least one of its
    /// 15-minute slots lies in a session. D1 from 2025-01-01, H4 from 2025-12-01, M15 from
    /// 2026-01-19, all up to <paramref name="endUtc"/> (default: a week after <see cref="Start"/>).
    /// </summary>
    public static BarSet Bars(DateTime? endUtc = null)
    {
        DateTime end = endUtc ?? Start.AddDays(7.0);
        return new BarSet(
            Series(TimeFrame.D1, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), end),
            Series(TimeFrame.H4, new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), end),
            Series(TimeFrame.M15, new DateTime(2026, 1, 19, 0, 0, 0, DateTimeKind.Utc), end));
    }

    /// <summary>A broker holding the gold symbol only.</summary>
    public static FakeBroker Broker(DateTime now, SymbolInfo? gold = null)
    {
        var broker = new FakeBroker(now, ExecutionData.Account(), gold ?? Gold());
        broker.MarginPerUnit = 0.04;
        return broker;
    }

    private static IReadOnlyList<Bar> Series(TimeFrame timeFrame, DateTime first, DateTime end)
    {
        TimeSpan length = BarBuilder.LengthOf(timeFrame);
        int count = (int)((end - first).Ticks / length.Ticks);
        return new BarBuilder(timeFrame).At(first).Repeat(count, (b, _) => b.Doji(2650.0)).Build()
            .Where(bar => AnySlotInSession(bar.OpenTimeUtc, length))
            .ToList();
    }

    private static bool AnySlotInSession(DateTime open, TimeSpan length)
    {
        for (DateTime slot = open; slot < open + length; slot = slot.AddMinutes(15.0))
        {
            if (InSession(slot))
            {
                return true;
            }
        }

        return false;
    }
}
