using System.Globalization;
using Xunit;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The enumerations are binding contracts (docs/architecture.md section 3). These tests fail
/// when a member is added, removed, renamed or reordered.
/// </summary>
public class EnumContractTests
{
    [Fact]
    public void TimeFrame_Members_MatchTheArchitectureContract()
    {
        AssertMembers<TimeFrame>("M15", "H4", "D1");
    }

    [Fact]
    public void Side_Members_MatchTheArchitectureContract()
    {
        AssertMembers<Side>("Buy", "Sell");
    }

    [Fact]
    public void ZoneType_Members_MatchTheArchitectureContract()
    {
        AssertMembers<ZoneType>("Demand", "Supply");
    }

    [Fact]
    public void ZoneState_Members_MatchTheArchitectureContract()
    {
        AssertMembers<ZoneState>("Active", "Touched", "Signalled", "Used", "Invalid", "Expired");
    }

    [Fact]
    public void Bias_Members_MatchTheArchitectureContract()
    {
        AssertMembers<Bias>("Neutral", "Long", "Short");
    }

    [Fact]
    [Trait("Req", "SIG-026")]
    public void SignalKind_Members_MatchTheArchitectureContract()
    {
        // WP34 (SIG-034) appends SessionRange; the first two members keep their names and values.
        // WP37a (SIG-041) appends Choch; the first three members keep their names and values.
        // WP43 (SIG-043) appends Overnight; the first four members keep their names and values.
        AssertMembers<SignalKind>("ZoneFvg", "Breakout", "SessionRange", "Choch", "Overnight");
    }

    [Fact]
    public void Bias_UnsetValue_IsNeutral()
    {
        // An unset bias must never allow a trade: the zero value is Neutral.
        Bias unset = default;

        Assert.Equal(Bias.Neutral, unset);
    }

    private static void AssertMembers<TEnum>(params string[] expectedNames)
        where TEnum : struct, Enum
    {
        string[] names = Enum.GetNames<TEnum>();
        int[] values = Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture)).ToArray();

        // Members are declared in contract order with the values 0, 1, 2, ...
        Assert.Equal(expectedNames, names);
        Assert.Equal(Enumerable.Range(0, expectedNames.Length).ToArray(), values);
    }
}
