using System.Globalization;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-018, follow-up of the review of WP29: the exception of a stale limit below 1 writes the
/// value with invariant formatting (AGENTS.md rule 7), so its message is the same under every
/// culture and contains the value. The other culture has the negative sign "~" and the decimal
/// separator ",".
/// </summary>
public class BarSeriesCheckStaleLimitMessageTests
{
    private static readonly TimeSpan FridayClose = TimeSpan.FromHours(20.0);

    private static ArgumentOutOfRangeException ThrownUnder(CultureInfo culture, int limit)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        IReadOnlyList<Bar> bars = new[] { BarBuilder.One(TimeFrame.D1, new DateTime(2026, 3, 2, 22, 0, 0, DateTimeKind.Utc), 1.0, 1.0, 1.0, 1.0) };
        DateTime now = new(2026, 3, 3, 23, 0, 0, DateTimeKind.Utc);
        try
        {
            CultureInfo.CurrentCulture = culture;
            return Assert.Throws<ArgumentOutOfRangeException>(() => BarSeriesCheck.ProblemOtherThanGap(TimeFrame.D1, bars, 1, now, FridayClose, staleBarLengths: limit));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [Trait("Req", "REC-018")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-25)]
    public void ProblemOtherThanGap_LimitBelowOne_MessageIsTheSameUnderAnotherCultureAndNamesTheValue(int limit)
    {
        var other = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        other.NumberFormat.NegativeSign = "~";
        other.NumberFormat.NumberDecimalSeparator = ",";

        ArgumentOutOfRangeException invariant = ThrownUnder(CultureInfo.InvariantCulture, limit);
        ArgumentOutOfRangeException underOther = ThrownUnder(other, limit);

        Assert.Equal(invariant.Message, underOther.Message);
        Assert.Contains(" but was " + limit.ToString(CultureInfo.InvariantCulture) + ".", underOther.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("~", underOther.Message, StringComparison.Ordinal);
        Assert.Equal("staleBarLengths", underOther.ParamName);
        Assert.Null(underOther.ActualValue);
    }
}
