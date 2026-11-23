using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Recovery;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;
using Data = ZoneFvg.Core.Tests.BreakoutRecoveryData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-028 in recovery: the D1 warm-up covers the slow EMA, so recovery reads the larger of the
/// spec 8.3 load (300 D1, 500 H4, 3,000 M15) and the breakout-aware warm-up. With
/// TrendSlowEmaPeriod 200 and BiasEmaPeriod 50 the D1 warm-up is 4 x 200 + 1 = 801; with the
/// filter off, and in ZoneFvg mode, it stays 4 x 50 + 1 = 201, so recovery reads 300 D1 bars as before.
/// </summary>
public class RecoveryBreakoutWarmupTests
{
    private static RecoveryService NewService(SignalParameters signal, BreakoutParameters? breakout)
    {
        var broker = new FakeBroker(Data.Start, ExecutionData.Account(), Data.Quote());
        var risk = RecoveryData.Parameters().Risk;
        return new RecoveryService(
            RecoveryData.Symbol,
            signal,
            new ExecutionParameters(),
            risk,
            true,
            broker,
            new MemoryStore(),
            new RecordingTradeEvents(),
            new RequestCounter(risk),
            new RecordingEvents(),
            _ => BotState.Running,
            breakout: breakout);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void LoadCount_TrendFilter200_ReadsTheSlowEmaWarmupOf801D1Bars()
    {
        RecoveryService service = NewService(new SignalParameters(), new BreakoutParameters { TrendSlowEmaPeriod = 200 });

        Assert.Equal(801, service.LoadCount(TimeFrame.D1));
        Assert.Equal(500, service.LoadCount(TimeFrame.H4));
        Assert.Equal(3000, service.LoadCount(TimeFrame.M15));
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void LoadCount_TrendFilter100_ReadsTheSpecLoadWhenItIsLarger()
    {
        // 4 x 100 + 1 = 401 D1 bars; with 60 it would be 241, below the 300 of spec 8.3.
        Assert.Equal(401, NewService(new SignalParameters(), new BreakoutParameters { TrendSlowEmaPeriod = 100 }).LoadCount(TimeFrame.D1));
        Assert.Equal(300, NewService(new SignalParameters(), new BreakoutParameters { TrendSlowEmaPeriod = 60 }).LoadCount(TimeFrame.D1));
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void LoadCount_FilterOffOrZoneFvgMode_IsUnchanged()
    {
        RecoveryService off = NewService(new SignalParameters(), new BreakoutParameters());
        RecoveryService zoneFvg = NewService(new SignalParameters(), null);

        foreach (RecoveryService service in new[] { off, zoneFvg })
        {
            Assert.Equal(300, service.LoadCount(TimeFrame.D1));
            Assert.Equal(500, service.LoadCount(TimeFrame.H4));
            Assert.Equal(3000, service.LoadCount(TimeFrame.M15));
        }
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void LoadCount_BiasEmaPeriodLargerThanTheSlowPeriod_UsesTheBiasEmaPeriod()
    {
        RecoveryService service = NewService(new SignalParameters { BiasEmaPeriod = 250 }, new BreakoutParameters { TrendSlowEmaPeriod = 200 });

        Assert.Equal(1001, service.LoadCount(TimeFrame.D1));
    }
}
