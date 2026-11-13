using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>Helpers that derive a changed copy of a bar sequence; bars themselves cannot be changed.</summary>
public static class BarListExtensions
{
    /// <summary>Returns a copy of the sequence in which the bar at the index has new prices and the same time.</summary>
    public static IReadOnlyList<Bar> ReplaceAt(this IReadOnlyList<Bar> bars, int index, double open, double high, double low, double close)
    {
        var copy = new List<Bar>(bars);
        copy[index] = new Bar(bars[index].OpenTimeUtc, open, high, low, close);
        return copy;
    }

    /// <summary>Returns the first <paramref name="count"/> bars as a separate list.</summary>
    public static IReadOnlyList<Bar> FirstBars(this IReadOnlyList<Bar> bars, int count)
    {
        var copy = new List<Bar>(count);
        for (int i = 0; i < count; i++)
        {
            copy.Add(bars[i]);
        }

        return copy;
    }

    /// <summary>
    /// Reflects every price around <paramref name="pivot"/> (p becomes pivot - p), which swaps
    /// high with low and turns every bullish structure into the matching bearish one.
    /// </summary>
    public static IReadOnlyList<Bar> Mirror(this IReadOnlyList<Bar> bars, double pivot)
    {
        var copy = new List<Bar>(bars.Count);
        foreach (Bar bar in bars)
        {
            copy.Add(new Bar(bar.OpenTimeUtc, pivot - bar.Open, pivot - bar.Low, pivot - bar.High, pivot - bar.Close));
        }

        return copy;
    }
}
