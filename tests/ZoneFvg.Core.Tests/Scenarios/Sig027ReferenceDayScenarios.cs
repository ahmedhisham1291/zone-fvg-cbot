using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using Data = ZoneFvg.Core.Tests.BreakoutRecoveryData;
using Weekend = ZoneFvg.Core.Tests.BreakoutMondayData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-027 through the core in Breakout mode: a restart on Monday with a D1 series that lacks
/// Friday's bar, at a server time at which REC-018 still lets recovery pass. Data:
/// <see cref="BreakoutMondayData"/>. The last D1 bar (Thursday's) closed on Friday 00:00; with the
/// default FridayCloseUtc (20:00) and the weekend closed, one bar length (24 hours) of open-market
/// time has passed only at Monday 04:00 (20 hours on Friday, 4 on Monday). The first M15 bar that
/// opens at or after Friday 00:00 opens at Friday 00:00, so Thursday's reference serves the M15
/// bars up to Friday 23:45 and no Monday bar is checked against it.
/// </summary>
public class Sig027ReferenceDayScenarios
{
    [Fact]
    [Trait("Req", "SIG-027")]
    public void Restart_D1LacksFridaysBarBeforeMonday0400_RecoveryPassesAndMondaysBreakIsNotTraded()
    {
        DateTime start = Weekend.Monday.AddHours(3.0);
        BotHarness h = Data.Harness(start, bars: Weekend.WithoutFridayD1());

        BotCore core = h.Start(start, Data.Parameters());

        Assert.Equal(BotState.Running, core.State);
        Assert.Empty(core.LastReport!.DataProblems);
        Assert.Equal(Data.Wednesday.AddDays(1.0), h.History.ClosedBars(TimeFrame.D1, 300)[^1].OpenTimeUtc);
        int signals = h.Journal.Lines("signal").Count;

        // From the bar 06:45 to 07:00 on, every Monday bar closes at 100.55, above the reference high 100.5,
        // inside the trading window 07:00 to 17:00.
        h.FeedTo(Weekend.Monday.AddHours(17.0));

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(signals, h.Journal.Lines("signal").Count);
        Assert.DoesNotContain(h.Journal.Entries, entry => entry.Line.Contains(Data.FridayBuyId, StringComparison.Ordinal));
        Assert.Empty(h.Broker.Requests);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-018")]
    public void Restart_D1LacksFridaysBarAtMonday0400_StaysRecovering()
    {
        // The other side of the window: at Monday 04:00 24 hours of open-market time have passed.
        DateTime start = Weekend.Monday.AddHours(4.0);
        BotHarness h = Data.Harness(start, bars: Weekend.WithoutFridayD1());

        BotCore core = h.Start(start, Data.Parameters());

        Assert.Equal(BotState.Recovering, core.State);
        Assert.Equal(new[] { "D1: last bar closed 2026-03-06 00:00, not recent at 2026-03-09 04:00" }, core.LastReport!.DataProblems);
        Assert.Empty(h.Broker.Requests);
    }
}
