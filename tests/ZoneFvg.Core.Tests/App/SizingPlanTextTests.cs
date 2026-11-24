using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>RSK-018 (for PLT-017): the cBot text of SizingPlan is read in the core, exactly "Ladder" or "Recovery".</summary>
public class SizingPlanTextTests
{
    [Theory]
    [Trait("Req", "RSK-018")]
    [InlineData("Ladder", SizingPlan.Ladder)]
    [InlineData("Recovery", SizingPlan.Recovery)]
    public void Parse_TheTwoValues_AreAccepted(string text, SizingPlan expected)
    {
        Assert.Equal(expected, SizingPlanText.Parse(text));
    }

    [Theory]
    [Trait("Req", "RSK-018")]
    [InlineData("recovery")]
    [InlineData("LADDER")]
    [InlineData("")]
    [InlineData(" Recovery")]
    [InlineData("Martingale")]
    [InlineData(null)]
    public void Parse_AnyOtherText_IsRefusedNamingTheParameter(string? text)
    {
        var exception = Assert.Throws<ArgumentException>(() => SizingPlanText.Parse(text));

        Assert.Equal("SizingPlan", exception.ParamName);
        Assert.StartsWith("SizingPlan must be one of: Ladder, Recovery.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-018")]
    public void Constants_AreTheCBotTexts()
    {
        Assert.Equal("Ladder", SizingPlanText.Ladder);
        Assert.Equal("Recovery", SizingPlanText.Recovery);
    }
}
