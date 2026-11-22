using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Recovery;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;
using Data = ZoneFvg.Core.Tests.BreakoutRecoveryData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-018: in Breakout mode recovery treats the D1 series as stale after one bar length of
/// open-market time since the close of its last bar; in ZoneFvg mode, and for H4 and M15 in both
/// modes, the limit of REC-003 (two bar lengths) stays. A D1 series that lacks the bar that closed
/// last therefore keeps the bot in Recovering, alerts once and sends no order, until the bar is
/// there. Also: <see cref="RecoveryService"/> validates the breakout parameters in its constructor
/// (REC-019, decided with it). Data: <see cref="BreakoutRecoveryData"/>; the D1 bars open at
/// 00:00 UTC, so on Thursday 5 March at 06:00 the last closed D1 bar is Wednesday's (it closed at
/// Thursday 00:00) and without it the last bar closed at Wednesday 00:00, 30 hours earlier.
/// </summary>
public class Rec018BreakoutD1Scenarios
{
    private static readonly DateTime Thursday = Data.Wednesday.AddDays(1.0);

    private static int WaitsForData(BotHarness h)
    {
        return h.AlertLines(AlertKind.Started).Count(line => line.Contains("recovery\\swaits\\sfor\\sdata", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "REC-018")]
    public void Restart_BreakoutModeD1LacksTheBarThatClosedLast_StaysRecoveringAlertsOnceSendsNothing_PassesOnceTheBarIsThere()
    {
        DateTime start = Thursday.AddHours(6.0);
        BotHarness h = Data.Harness(start);
        h.History.Missing.Add(Data.Wednesday);

        BotCore core = h.Start(start, Data.Parameters());

        Assert.Equal(BotState.Recovering, core.State);
        Assert.False(core.LastReport!.DataComplete);
        Assert.Equal(new[] { "D1: last bar closed 2026-03-04 00:00, not recent at 2026-03-05 06:00" }, core.LastReport.DataProblems);
        Assert.Contains(h.History.MoreRequests, request => request.TimeFrame == TimeFrame.D1);
        Assert.Equal(1, WaitsForData(h));
        Assert.Empty(h.Broker.Requests);

        // A retry with the bar still missing: still Recovering, no second alert.
        h.Timer(start.AddMinutes(15.0));
        Assert.Equal(BotState.Recovering, core.State);
        Assert.Equal(1, WaitsForData(h));

        // The Thursday break bar (10:00 to 10:15) closes at 100.44, above the reference high 100.4; later bars close at 100.45.
        h.FeedTo(Data.NextConfirmation.AddMinutes(30.0));
        Assert.Equal(BotState.Recovering, core.State);
        Assert.Empty(h.Broker.Requests);
        Assert.Empty(h.Journal.Lines("signal"));

        // At the next attempt the D1 bar is there: recovery passes; the Thursday breakout is replayed, not executed.
        h.History.Missing.Clear();
        h.Timer(Data.NextConfirmation.AddMinutes(45.0));

        Assert.Equal(BotState.Running, core.State);
        Assert.True(core.LastReport!.DataComplete);
        Assert.Empty(core.LastReport.DataProblems);
        Assert.Equal(1, WaitsForData(h));
        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Equal(1, h.Journal.Count("skip", "zone=" + Data.NextBuyId + " reason=SignalFromReplay"));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-018")]
    public void Restart_ZoneFvgModeD1LacksTheBarThatClosedLast_PassesAsToday()
    {
        DateTime start = Thursday.AddHours(6.0);
        BotHarness h = Data.Harness(start);
        h.History.Missing.Add(Data.Wednesday);

        BotCore core = h.Start(start, RecoveryData.Parameters());

        Assert.Null(RecoveryData.Parameters().Breakout);
        Assert.True(core.LastReport!.DataComplete);
        Assert.Empty(core.LastReport.DataProblems);
        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(0, WaitsForData(h));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-018")]
    public void Restart_BreakoutModeH4AndM15BetweenOneAndTwoBarLengthsOld_Pass()
    {
        // Thursday 09:50. The H4 bar 04:00 to 08:00 is missing, so the last H4 bar closed at 04:00, 5 h 50 min
        // ago (between 4 and 8 hours); the M15 bar 09:30 to 09:45 is missing, so the last M15 bar closed at
        // 09:30, 20 minutes ago (between 15 and 30). Missing M15 04:00 is a gap the reload does not fill:
        // accepted (REC-003). The last D1 bar closed at Thursday 00:00.
        DateTime start = Thursday.AddHours(9.0).AddMinutes(50.0);
        BotHarness h = Data.Harness(start);
        h.History.Missing.Add(Thursday.AddHours(4.0));
        h.History.Missing.Add(Thursday.AddHours(9.5));
        h.Broker.AdvanceTo(start);
        Assert.Equal(Thursday.AddHours(4.0), h.History.ClosedBars(TimeFrame.H4, 1000)[^1].OpenTimeUtc.AddHours(4.0));
        Assert.Equal(Thursday.AddHours(9.5), h.History.ClosedBars(TimeFrame.M15, 3000)[^1].OpenTimeUtc.AddMinutes(15.0));
        Assert.Equal(Thursday, h.History.ClosedBars(TimeFrame.D1, 300)[^1].OpenTimeUtc.AddDays(1.0));

        BotCore core = h.Start(start, Data.Parameters());

        Assert.True(core.LastReport!.DataComplete);
        Assert.Empty(core.LastReport.DataProblems);
        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(0, WaitsForData(h));
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ constructor validation

    private static RecoveryService NewService(BreakoutParameters? breakout)
    {
        var broker = new FakeBroker(Data.Start, ExecutionData.Account(), Data.Quote());
        var risk = RecoveryData.Parameters().Risk;
        return new RecoveryService(
            RecoveryData.Symbol,
            new SignalParameters(),
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

    [Theory]
    [Trait("Req", "REC-019")]
    [InlineData(0.0, 0.1, "StopAtrMult")]
    [InlineData(1.0, -0.1, "EntryBufferAtr")]
    public void RecoveryService_InvalidBreakoutParameters_ThrowsFromTheConstructor(double stopAtrMult, double entryBufferAtr, string name)
    {
        var breakout = new BreakoutParameters { StopAtrMult = stopAtrMult, EntryBufferAtr = entryBufferAtr };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => NewService(breakout));

        Assert.Equal(name, exception.ParamName);
    }

    [Fact]
    [Trait("Req", "REC-019")]
    public void RecoveryService_ValidOrNoBreakoutParameters_Constructs()
    {
        Assert.NotNull(NewService(new BreakoutParameters()));
        Assert.NotNull(NewService(new BreakoutParameters { StopAtrMult = 1.5, EntryBufferAtr = 0.2 }));
        Assert.NotNull(NewService(null));
    }
}
