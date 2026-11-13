using Xunit;

namespace ZoneFvg.Core.Tests;

/// <summary>Small assertion helpers shared by the test classes.</summary>
public static class Expect
{
    /// <summary>Fails the test when the indicator returned null; otherwise returns its value.</summary>
    public static double Value(double? value)
    {
        Assert.True(value.HasValue, "A value was expected but the indicator returned null.");
        return value.GetValueOrDefault();
    }
}
