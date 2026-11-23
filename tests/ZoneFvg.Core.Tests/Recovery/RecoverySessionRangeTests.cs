using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Recovery;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-034 in recovery: the session range parameters are validated, refused together with
/// breakout parameters (one mode per instance), and with the default signal parameters recovery
/// reads the spec 8.3 load (300 D1, 500 H4, 3,000 M15 bars), which covers the warm-up.
/// </summary>
public class RecoverySessionRangeTests
{
    private static RecoveryService NewService(BreakoutParameters? breakout, SessionRangeParameters? sessionRange, SignalParameters? signal = null)
    {
        var broker = new FakeBroker(SessionRangeBotData.Utc(2026, 3, 4, 6, 0), ExecutionData.Account(), SessionRangeBotData.Quote());
        var risk = RecoveryData.Parameters().Risk;
        return new RecoveryService(
            RecoveryData.Symbol,
            signal ?? new SignalParameters(),
            new ExecutionParameters(),
            risk,
            true,
            broker,
            new MemoryStore(),
            new RecordingTradeEvents(),
            new RequestCounter(risk),
            new RecordingEvents(),
            _ => BotState.Running,
            breakout: breakout,
            sessionRange: sessionRange);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void LoadCount_SessionRangeDefaults_TheSpecLoad()
    {
        RecoveryService service = NewService(null, new SessionRangeParameters());

        Assert.Equal(300, service.LoadCount(TimeFrame.D1));
        Assert.Equal(500, service.LoadCount(TimeFrame.H4));
        Assert.Equal(3000, service.LoadCount(TimeFrame.M15));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Constructor_BreakoutAndSessionRangeTogether_Refused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => NewService(new BreakoutParameters(), new SessionRangeParameters()));

        Assert.Equal("sessionRange", error.ParamName);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Constructor_InvalidSessionRangeParameters_Refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewService(null, new SessionRangeParameters { RangeEndNy = new TimeSpan(3, 10, 0) }));
    }
}
