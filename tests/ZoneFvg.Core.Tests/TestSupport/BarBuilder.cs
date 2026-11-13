using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Fluent builder of closed-bar sequences for tests. Bar times start at the time given to
/// <see cref="At"/> and advance by the length of the timeframe after every bar, so a test
/// never reads a clock.
/// </summary>
/// <example>
/// <code>
/// var bars = new BarBuilder(TimeFrame.H4)
///     .At(start)
///     .Bullish(1.1000, 1.1040).Range(1.0990, 1.1050)
///     .Then()
///     .Bearish(1.1040, 1.1010)
///     .Build();
/// </code>
/// </example>
public sealed class BarBuilder
{
    private readonly List<Bar> _bars = new();
    private DateTime? _nextOpenUtc;
    private Draft? _draft;

    public BarBuilder(TimeFrame timeFrame)
    {
        Step = LengthOf(timeFrame);
    }

    /// <summary>Time between the opens of two consecutive bars.</summary>
    public TimeSpan Step { get; }

    /// <summary>Nominal length of one bar of the timeframe.</summary>
    public static TimeSpan LengthOf(TimeFrame timeFrame)
    {
        return timeFrame switch
        {
            TimeFrame.M15 => TimeSpan.FromMinutes(15.0),
            TimeFrame.H4 => TimeSpan.FromHours(4.0),
            TimeFrame.D1 => TimeSpan.FromDays(1.0),
            _ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unknown timeframe."),
        };
    }

    /// <summary>Sets the open time of the next bar. Finishes the bar being defined, if any.</summary>
    public BarBuilder At(DateTime openTimeUtc)
    {
        if (openTimeUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Bar times in tests must be UTC.", nameof(openTimeUtc));
        }

        Commit();
        _nextOpenUtc = openTimeUtc;
        return this;
    }

    /// <summary>Defines the next bar from its four prices.</summary>
    public BarBuilder Ohlc(double open, double high, double low, double close)
    {
        Commit();
        _draft = new Draft(open, high, low, close);
        return this;
    }

    /// <summary>Defines the next bar as bullish with no wicks: low = open, high = close.</summary>
    public BarBuilder Bullish(double open, double close)
    {
        if (close <= open)
        {
            throw new ArgumentException("A bullish bar closes above its open.", nameof(close));
        }

        return Ohlc(open, close, open, close);
    }

    /// <summary>Defines the next bar as bearish with no wicks: high = open, low = close.</summary>
    public BarBuilder Bearish(double open, double close)
    {
        if (close >= open)
        {
            throw new ArgumentException("A bearish bar closes below its open.", nameof(close));
        }

        return Ohlc(open, open, close, close);
    }

    /// <summary>Defines the next bar with all four prices equal.</summary>
    public BarBuilder Doji(double price)
    {
        return Ohlc(price, price, price, price);
    }

    /// <summary>Defines one flat bar per close, in order.</summary>
    public BarBuilder Closes(params double[] closes)
    {
        foreach (double close in closes)
        {
            Doji(close);
        }

        return this;
    }

    /// <summary>Replaces the low and the high of the bar defined just before this call.</summary>
    public BarBuilder Range(double low, double high)
    {
        if (_draft is null)
        {
            throw new InvalidOperationException("Range(low, high) must follow a bar definition.");
        }

        _draft = _draft with { Low = low, High = high };
        return this;
    }

    /// <summary>Finishes the bar defined just before this call and moves on to the next bar time.</summary>
    public BarBuilder Then()
    {
        if (_draft is null)
        {
            throw new InvalidOperationException("Then() must follow a bar definition.");
        }

        Commit();
        return this;
    }

    /// <summary>Calls <paramref name="define"/> once per index, 0 to count - 1, to define that many bars.</summary>
    public BarBuilder Repeat(int count, Action<BarBuilder, int> define)
    {
        for (int i = 0; i < count; i++)
        {
            define(this, i);
        }

        return this;
    }

    /// <summary>Returns the bars defined so far, oldest first, as a separate copy.</summary>
    public IReadOnlyList<Bar> Build()
    {
        Commit();
        return _bars.ToArray();
    }

    private void Commit()
    {
        if (_draft is null)
        {
            return;
        }

        if (_nextOpenUtc is null)
        {
            throw new InvalidOperationException("Call At(time) before defining the first bar.");
        }

        DateTime openTimeUtc = _nextOpenUtc.Value;
        _bars.Add(new Bar(openTimeUtc, _draft.Open, _draft.High, _draft.Low, _draft.Close));
        _nextOpenUtc = openTimeUtc + Step;
        _draft = null;
    }

    private sealed record Draft(double Open, double High, double Low, double Close);
}
