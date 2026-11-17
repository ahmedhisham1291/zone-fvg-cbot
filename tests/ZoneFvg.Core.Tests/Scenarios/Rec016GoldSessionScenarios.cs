using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-016 through the full core: a gold instance (example symbol of <see cref="GoldData"/>,
/// sessions with a daily break from 21:00 to 22:00 UTC) whose history has no bars in the break.
/// The sessions are read from the broker port at each check; when the read fails the check runs
/// on the weekend and holiday model alone (more gaps, never fewer) and the failure is journaled.
/// The core starts on Wednesday 2026-03-04 at 10:00 with news off.
/// </summary>
public class Rec016GoldSessionScenarios
{
    private const string GoldSessionsText = "Sun\\s22:00-Mon\\s21:00,Mon\\s22:00-Tue\\s21:00,Tue\\s22:00-Wed\\s21:00,Wed\\s22:00-Thu\\s21:00,Thu\\s22:00-Fri\\s21:00";

    private static DateTime At(int day, int hour, int minute)
    {
        return new DateTime(2026, 3, day, hour, minute, 0, DateTimeKind.Utc);
    }

    private static BotHarness Harness(bool withSessions = true)
    {
        var gold = withSessions ? GoldData.Gold() : GoldData.Gold(Array.Empty<TradingSession>());
        return new BotHarness(GoldData.Bars(), GoldData.Broker(GoldData.Start, gold));
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void Recovery_GoldHistoryWithTheDailyBreak_RunningWithNoAcceptedGapAndNoReload()
    {
        BotHarness h = Harness();

        BotCore core = h.Start(GoldData.Start, GoldData.Parameters());

        Assert.Equal(BotState.Running, core.State);
        Assert.True(core.LastReport!.DataComplete);
        Assert.Empty(core.LastReport.AcceptedGaps);
        Assert.Empty(h.History.MoreRequests);
        Assert.Equal(0, h.Journal.Count("recovery", "event=gaps-accepted"));
        Assert.Equal(1, h.Journal.Count("start", " sessions=" + GoldSessionsText));
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void Recovery_SameHistoryWithoutSessions_AcceptsEveryBreakAsAnM15Gap()
    {
        // Control: with no session reported the breaks are gaps (accepted after one reload, REC-003 decision A).
        BotHarness h = Harness(withSessions: false);

        BotCore core = h.Start(GoldData.Start, GoldData.Parameters());

        Assert.Equal(BotState.Running, core.State);
        AcceptedGapList m15 = Assert.Single(core.LastReport!.AcceptedGaps);
        Assert.Equal(TimeFrame.M15, m15.TimeFrame);
        Assert.Equal(new BarGap(TimeFrame.M15, At(3, 20, 45), At(3, 22, 0)), m15.Newest[0]);
        Assert.Equal(new[] { (TimeFrame.M15, 3000) }, h.History.MoreRequests);
        Assert.Equal(1, h.Journal.Count("start", " sessions=none\\sreported"));
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void Live_GoldWeekFedBarByBar_RaisesNoDataGap()
    {
        BotHarness h = Harness();
        BotCore core = h.Start(GoldData.Start, GoldData.Parameters());
        int readsAfterStart = h.Port.SymbolReads;

        h.FeedTo(GoldData.Start.AddDays(7.0));

        Assert.False(core.DataGap);
        Assert.Equal(0, h.Journal.Count("bar", "event=gap"));
        Assert.Equal(0, h.Journal.Count("ops", "event=symbol-unavailable"));
        Assert.True(h.Port.SymbolReads > readsAfterStart);
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void Live_SameWeekWithoutSessions_TheFirstBreakIsADataGap()
    {
        BotHarness h = Harness(withSessions: false);
        BotCore core = h.Start(GoldData.Start, GoldData.Parameters());

        h.FeedTo(At(4, 22, 15));

        Assert.True(core.DataGap);
        Assert.Equal(1, h.Journal.Count("bar", "detail=M15\\sbars\\smissing\\sbetween\\s2026-03-04\\s20:45\\sand\\s2026-03-04\\s22:00"));
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void Live_GetSymbolThrowsAtTheBreak_CheckRunsWithoutSessionsAndTheFailureIsJournaled()
    {
        BotHarness h = Harness();
        BotCore core = h.Start(GoldData.Start, GoldData.Parameters());
        h.FeedTo(At(4, 21, 0));
        Assert.False(core.DataGap);

        h.Port.SymbolFault = new InvalidOperationException("symbol data unavailable");
        h.FeedTo(At(4, 22, 15));

        Assert.True(core.DataGap);
        Assert.Equal(1, h.Journal.Count("bar", "detail=M15\\sbars\\smissing\\sbetween\\s2026-03-04\\s20:45\\sand\\s2026-03-04\\s22:00"));
        Assert.True(h.Journal.Count("ops", "event=symbol-unavailable reason=InvalidOperationException:\\ssymbol\\sdata\\sunavailable") >= 1);
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void Start_GetSymbolThrows_SessionsUnavailableAndRecoveryUsesTheModelWithoutSessions()
    {
        BotHarness h = Harness();
        h.Port.SymbolFault = new InvalidOperationException("symbol data unavailable");

        BotCore core = h.Start(GoldData.Start, GoldData.Parameters());

        Assert.Equal(1, h.Journal.Count("start", " sessions=unavailable"));
        Assert.True(h.Journal.Count("ops", "event=symbol-unavailable") >= 2);
        Assert.True(core.LastReport!.DataComplete);
        AcceptedGapList m15 = Assert.Single(core.LastReport.AcceptedGaps);
        Assert.Equal(new BarGap(TimeFrame.M15, At(3, 20, 45), At(3, 22, 0)), m15.Newest[0]);
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void Recovery_SessionsChangedAtTheBroker_TheNextCheckReadsTheNewList()
    {
        // Never stored: a reconnect after the broker stops reporting sessions sees the breaks as gaps.
        BotHarness h = Harness();
        BotCore core = h.Start(GoldData.Start, GoldData.Parameters());
        Assert.Empty(core.LastReport!.AcceptedGaps);

        h.Broker.SetSymbol(GoldData.Gold(Array.Empty<TradingSession>()));
        core.OnConnected(h.History);

        Assert.Single(core.LastReport!.AcceptedGaps);
        Assert.Equal(1, h.Journal.Count("start", " sessions=none\\sreported"));
    }
}
