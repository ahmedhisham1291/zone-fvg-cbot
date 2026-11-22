using Xunit;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The Signal contract of spec v0.10 (docs/architecture.md section 3, SIG-026): the gap is
/// optional, and two init-only members outside the positional list say which entry mode produced
/// the signal and, for a breakout, which level was broken. Their defaults keep every signal built
/// the old way a ZoneFvg signal.
/// </summary>
public class SignalContractTests
{
    private static readonly DateTime Close = new(2026, 1, 14, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Req", "SIG-026")]
    public void PositionalSignal_DefaultsToKindZoneFvgWithoutLevel()
    {
        var gap = new Fvg(Side.Buy, 1.1005, 1.0995, Close.AddMinutes(-45.0), Close.AddMinutes(-30.0), Close.AddMinutes(-15.0));

        var signal = new Signal("D-20260114-0400", Side.Buy, 1.1, 1.098, 1.104, gap, Close, false, 0.002);

        Assert.Equal(SignalKind.ZoneFvg, signal.Kind);
        Assert.Null(signal.Level);
        Assert.Same(gap, signal.Fvg);
    }

    [Fact]
    [Trait("Req", "SIG-026")]
    public void BreakoutSignal_HasNoGapAndCarriesKindAndLevel()
    {
        var signal = new Signal("BH-20260112-2200", Side.Buy, 1.0863, 1.0823, 1.0943, null, Close, true, 0.004)
        {
            Kind = SignalKind.Breakout,
            Level = 1.085,
        };

        Assert.Null(signal.Fvg);
        Assert.Equal(SignalKind.Breakout, signal.Kind);
        Assert.Equal(1.085, signal.Level);
        Assert.True(signal.FromReplay);
    }

    [Fact]
    [Trait("Req", "SIG-026")]
    public void KindAndLevel_TakePartInRecordEquality()
    {
        var plain = new Signal("BH-20260112-2200", Side.Buy, 1.0863, 1.0823, 1.0943, null, Close, false, 0.004);
        Signal breakout = plain with { Kind = SignalKind.Breakout, Level = 1.085 };

        Assert.NotEqual(plain, breakout);
        Assert.NotEqual(breakout, breakout with { Level = 1.0851 });
        Assert.Equal(breakout, plain with { Kind = SignalKind.Breakout, Level = 1.085 });
    }
}
