using System.Reflection;
using Xunit;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

public class BarTests
{
    private static readonly DateTime Time = new(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Constructor_ValidValues_ExposesThem()
    {
        var bar = new Bar(Time, 1.1000, 1.1050, 1.0990, 1.1020);

        Assert.Equal(Time, bar.OpenTimeUtc);
        Assert.Equal(DateTimeKind.Utc, bar.OpenTimeUtc.Kind);
        Assert.Equal(1.1000, bar.Open);
        Assert.Equal(1.1050, bar.High);
        Assert.Equal(1.0990, bar.Low);
        Assert.Equal(1.1020, bar.Close);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Constructor_OpenTimeNotUtc_Throws(DateTimeKind kind)
    {
        var time = new DateTime(2026, 1, 5, 8, 0, 0, kind);

        Assert.Throws<ArgumentException>("OpenTimeUtc", () => new Bar(time, 1.0, 2.0, 0.5, 1.5));
    }

    [Theory]
    [InlineData("Open", double.NaN)]
    [InlineData("Open", double.PositiveInfinity)]
    [InlineData("Open", double.NegativeInfinity)]
    [InlineData("High", double.NaN)]
    [InlineData("High", double.PositiveInfinity)]
    [InlineData("High", double.NegativeInfinity)]
    [InlineData("Low", double.NaN)]
    [InlineData("Low", double.PositiveInfinity)]
    [InlineData("Low", double.NegativeInfinity)]
    [InlineData("Close", double.NaN)]
    [InlineData("Close", double.PositiveInfinity)]
    [InlineData("Close", double.NegativeInfinity)]
    public void Constructor_PriceNotFinite_Throws(string field, double bad)
    {
        double open = field == "Open" ? bad : 1.0;
        double high = field == "High" ? bad : 2.0;
        double low = field == "Low" ? bad : 0.5;
        double close = field == "Close" ? bad : 1.5;

        Assert.Throws<ArgumentException>(field, () => new Bar(Time, open, high, low, close));
    }

    [Fact]
    public void Constructor_HighBelowLow_Throws()
    {
        // Open and close are equal and sit between the two, so only "high >= low" is broken first.
        Assert.Throws<ArgumentException>("High", () => new Bar(Time, 1.0000, 0.9000, 1.1000, 1.0000));
    }

    [Fact]
    public void Constructor_HighBelowOpen_Throws()
    {
        Assert.Throws<ArgumentException>("High", () => new Bar(Time, 1.2000, 1.1000, 1.0000, 1.0500));
    }

    [Fact]
    public void Constructor_HighBelowClose_Throws()
    {
        Assert.Throws<ArgumentException>("High", () => new Bar(Time, 1.0500, 1.1000, 1.0000, 1.2000));
    }

    [Fact]
    public void Constructor_LowAboveOpen_Throws()
    {
        Assert.Throws<ArgumentException>("Low", () => new Bar(Time, 1.0000, 1.2000, 1.1000, 1.1500));
    }

    [Fact]
    public void Constructor_LowAboveClose_Throws()
    {
        Assert.Throws<ArgumentException>("Low", () => new Bar(Time, 1.1500, 1.2000, 1.1000, 1.0000));
    }

    [Fact]
    public void Constructor_HighOneTickBelowTheBodyTop_Throws()
    {
        // Body top = max(open, close) = 1.10500. A high of 1.10499 is one tick too low.
        Assert.Throws<ArgumentException>("High", () => new Bar(Time, 1.10000, 1.10499, 1.09900, 1.10500));
    }

    [Fact]
    public void Constructor_LowOneTickAboveTheBodyBottom_Throws()
    {
        // Body bottom = min(open, close) = 1.10000. A low of 1.10001 is one tick too high.
        Assert.Throws<ArgumentException>("Low", () => new Bar(Time, 1.10000, 1.10600, 1.10001, 1.10500));
    }

    [Fact]
    public void Constructor_HighAndLowExactlyAtTheBody_IsAccepted()
    {
        var bullish = new Bar(Time, 1.10000, 1.10500, 1.10000, 1.10500);
        var bearish = new Bar(Time, 1.10500, 1.10500, 1.10000, 1.10000);

        Assert.Equal(1.10500, bullish.High);
        Assert.Equal(1.10000, bullish.Low);
        Assert.Equal(1.10500, bearish.High);
        Assert.Equal(1.10000, bearish.Low);
    }

    [Fact]
    public void Constructor_AllPricesEqual_IsAccepted()
    {
        var flat = new Bar(Time, 1.2345, 1.2345, 1.2345, 1.2345);

        Assert.Equal(flat.High, flat.Low);
    }

    [Fact]
    public void Constructor_NegativePrices_AreAcceptedWhenOrdered()
    {
        // Mirror tests reflect prices around a constant (SIG-021), which can give negative values.
        var bar = new Bar(Time, -1.0, -0.5, -2.0, -1.5);

        Assert.Equal(-0.5, bar.High);
        Assert.Equal(-2.0, bar.Low);
    }

    [Fact]
    public void Equals_SameValues_IsTrueAndHashCodesMatch()
    {
        var first = new Bar(Time, 1.0, 2.0, 0.5, 1.5);
        var second = new Bar(Time, 1.0, 2.0, 0.5, 1.5);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_AnyDifferentValue_IsFalse()
    {
        var bar = new Bar(Time, 1.0, 2.0, 0.5, 1.5);

        Assert.NotEqual(bar, new Bar(Time.AddMinutes(15.0), 1.0, 2.0, 0.5, 1.5));
        Assert.NotEqual(bar, new Bar(Time, 1.1, 2.0, 0.5, 1.5));
        Assert.NotEqual(bar, new Bar(Time, 1.0, 2.1, 0.5, 1.5));
        Assert.NotEqual(bar, new Bar(Time, 1.0, 2.0, 0.4, 1.5));
        Assert.NotEqual(bar, new Bar(Time, 1.0, 2.0, 0.5, 1.6));
        Assert.True(bar != new Bar(Time, 1.0, 2.0, 0.5, 1.6));
        Assert.False(bar.Equals(null));
    }

    [Fact]
    public void Deconstruct_ReturnsValuesInContractOrder()
    {
        var bar = new Bar(Time, 1.0, 2.0, 0.5, 1.5);

        (DateTime openTimeUtc, double open, double high, double low, double close) = bar;

        Assert.Equal(Time, openTimeUtc);
        Assert.Equal(1.0, open);
        Assert.Equal(2.0, high);
        Assert.Equal(0.5, low);
        Assert.Equal(1.5, close);
    }

    [Fact]
    public void ToString_NamesTheTypeAndItsValues()
    {
        string text = new Bar(Time, 1.0, 2.0, 0.5, 1.5).ToString();

        Assert.StartsWith("Bar", text);
        Assert.Contains("OpenTimeUtc", text);
        Assert.Contains("Close", text);
    }

    [Fact]
    public void Copy_OfABar_EqualsTheOriginal()
    {
        var bar = new Bar(Time, 1.0, 2.0, 0.5, 1.5);

        Bar copy = bar with { };

        Assert.Equal(bar, copy);
        Assert.NotSame(bar, copy);
    }

    [Fact]
    [Trait("Req", "SIG-001")]
    public void Properties_AreTheFiveContractValues_WithNoFormingFlagAndNoSetter()
    {
        PropertyInfo[] properties = typeof(Bar).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        string[] names = properties.Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        // A bar is always a closed bar: there is nothing on it that could mark it as forming,
        // and nothing that could change it after it was validated.
        Assert.Equal(new[] { "Close", "High", "Low", "Open", "OpenTimeUtc" }, names);
        Assert.All(properties, property => Assert.Null(property.SetMethod));
    }
}
