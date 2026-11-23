using Xunit;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>Execution parameters: spec defaults and range checks (EXE-002 to EXE-006, EXE-013, EXE-015), and the reason mapping (EXE-014).</summary>
public class ExecutionParametersTests
{
    [Fact]
    [Trait("Req", "EXE-002")]
    [Trait("Req", "EXE-003")]
    [Trait("Req", "EXE-004")]
    [Trait("Req", "EXE-005")]
    [Trait("Req", "EXE-006")]
    [Trait("Req", "EXE-013")]
    [Trait("Req", "EXE-015")]
    public void Defaults_AreTheSpecValues()
    {
        var p = new ExecutionParameters();

        Assert.Equal(TimeSpan.FromHours(7.0), p.SessionStartUtc);
        Assert.Equal(TimeSpan.FromHours(17.0), p.SessionEndUtc);
        Assert.Equal(TimeSpan.FromHours(12.0), p.FridayNoEntryUtc);
        Assert.Equal(TimeSpan.FromHours(20.0), p.FridayCloseUtc);
        Assert.Equal(1.5, p.MaxSpreadPips);
        Assert.Equal(12.0, p.MinStopPips);
        Assert.Equal(2.0, p.MaxStopAtr);
        Assert.Equal(0.10, p.CostToRiskMax);
        Assert.Equal(3.0, p.MinFvgSpreadMult);
        Assert.Equal(1.5, p.MinRoomR);
        Assert.Equal(8, p.EntryExpiryBars);
        Assert.Equal(15, ExecutionParameters.MinOrderLifeMinutes);
        Assert.Equal(15, ExecutionParameters.MaxSignalAgeMinutes);
        p.Validate();
    }

    public static IEnumerable<object[]> Invalid()
    {
        var p = new ExecutionParameters();
        yield return new object[] { "SessionStartUtc", p with { SessionStartUtc = TimeSpan.FromMinutes(-1.0) } };
        // EXE-029 (answer of 2026-11-23): 24:00 itself is now a valid end; one second above it is not.
        yield return new object[] { "SessionEndUtc", p with { SessionEndUtc = TimeSpan.FromHours(24.0) + TimeSpan.FromSeconds(1.0) } };
        yield return new object[] { "SessionEndUtc", p with { SessionEndUtc = TimeSpan.FromHours(7.0) } };
        yield return new object[] { "FridayNoEntryUtc", p with { FridayNoEntryUtc = TimeSpan.FromHours(25.0) } };
        yield return new object[] { "FridayCloseUtc", p with { FridayCloseUtc = TimeSpan.FromHours(-2.0) } };
        yield return new object[] { "MaxSpreadPips", p with { MaxSpreadPips = 0.0 } };
        yield return new object[] { "MaxSpreadPips", p with { MaxSpreadPips = double.NaN } };
        yield return new object[] { "MinStopPips", p with { MinStopPips = -12.0 } };
        yield return new object[] { "MaxStopAtr", p with { MaxStopAtr = double.PositiveInfinity } };
        yield return new object[] { "CostToRiskMax", p with { CostToRiskMax = 0.0 } };
        yield return new object[] { "CostToRiskMax", p with { CostToRiskMax = 1.01 } };
        yield return new object[] { "MinFvgSpreadMult", p with { MinFvgSpreadMult = 0.0 } };
        yield return new object[] { "MinRoomR", p with { MinRoomR = -1.5 } };
        yield return new object[] { "EntryExpiryBars", p with { EntryExpiryBars = 0 } };
        yield return new object[] { "EntryExpiryBars", p with { EntryExpiryBars = 97 } };
    }

    [Theory]
    [Trait("Req", "EXE-002")]
    [MemberData(nameof(Invalid))]
    public void Validate_ValueOutOfRange_ThrowsNamingTheParameter(string name, ExecutionParameters parameters)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal(name, exception.ParamName);
    }

    [Fact]
    [Trait("Req", "EXE-002")]
    public void Validate_BoundaryValues_AreAccepted()
    {
        new ExecutionParameters
        {
            SessionStartUtc = TimeSpan.Zero,
            SessionEndUtc = new TimeSpan(23, 59, 59),
            CostToRiskMax = 1.0,
            EntryExpiryBars = 96,
        }.Validate();
        new ExecutionParameters { EntryExpiryBars = 1 }.Validate();
    }

    [Fact]
    [Trait("Req", "EXE-014")]
    public void SkipReasons_EveryRiskRefusal_HasItsOwnSkipReason()
    {
        RiskReason[] refusals = Enum.GetValues<RiskReason>().Where(reason => reason != RiskReason.None).ToArray();

        SkipReason[] mapped = refusals.Select(SkipReasons.FromRisk).ToArray();

        Assert.Equal(refusals.Length, mapped.Distinct().Count());
        Assert.Equal(SkipReason.RiskInvalidInput, SkipReasons.FromRisk(RiskReason.InvalidInput));
        Assert.Equal(SkipReason.FreeMarginTooLow, SkipReasons.FromRisk(RiskReason.FreeMarginTooLow));
        Assert.Equal(SkipReason.AccountEntryLimitReached, SkipReasons.FromRisk(RiskReason.AccountEntryLimitReached));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkipReasons.FromRisk(RiskReason.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkipReasons.FromRisk((RiskReason)99));
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void SkipReasons_SanityCaps_AreExactlyTheFourOfRsk002()
    {
        SkipReason[] caps = Enum.GetValues<SkipReason>().Where(SkipReasons.IsSanityCap).ToArray();

        Assert.Equal(new[] { SkipReason.StopOnWrongSide, SkipReason.TargetOnWrongSide, SkipReason.VolumeAboveCap, SkipReason.RiskAboveSanityCap }, caps);
    }
}
