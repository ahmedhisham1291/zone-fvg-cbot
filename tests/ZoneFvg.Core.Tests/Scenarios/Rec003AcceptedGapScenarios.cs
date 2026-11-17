using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Recovery;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-003 as changed by owner decision A of 2026-11-17: a gap that is still in the history after
/// the one reload is the broker's data and is accepted, journaled per timeframe and counted in the
/// recovery report. Recovery still fails when not connected, on too few bars, a missing or unclosed
/// bar, disorder or stale data. Data: <see cref="RecoveryData"/> (Wednesday 2026-03-04, activation
/// 08:00) unless stated.
/// </summary>
public class Rec003AcceptedGapScenarios
{
    private static readonly DateTime A = RecoveryData.Activation;

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    private static string GapLine(string timeFrame, int count, string newest)
    {
        return " recovery event=gaps-accepted timeframe=" + timeFrame + " count=" + count + " newest=" + newest.Replace(" ", "\\s", StringComparison.Ordinal);
    }

    /// <summary>
    /// Flat bars on every calendar day up to 2026-01-05 10:00 (D1 from 2025-01-01, H4 from 2025-08-01, M15
    /// from 2025-12-01), except the M15 bars of the
    /// real Christmas hours: the last bar on Wednesday 2025-12-24 opens 21:45 and the next one on
    /// Friday 2025-12-26 at 00:00 (25 December is a default holiday).
    /// </summary>
    private static BarSet ChristmasBars()
    {
        DateTime first = Utc(2025, 12, 1, 0, 0);
        DateTime firstH4 = Utc(2025, 8, 1, 0, 0);
        DateTime firstD1 = Utc(2025, 1, 1, 0, 0);
        DateTime end = Utc(2026, 1, 5, 10, 0);
        IReadOnlyList<Bar> d1 = new BarBuilder(TimeFrame.D1).At(firstD1).Repeat((int)(end - firstD1).TotalDays, (b, _) => b.Doji(1.0)).Build();
        IReadOnlyList<Bar> h4 = new BarBuilder(TimeFrame.H4).At(firstH4).Repeat((int)((end - firstH4).TotalHours / 4.0), (b, _) => b.Doji(1.0)).Build();
        DateTime lastBefore = Utc(2025, 12, 24, 21, 45);
        DateTime firstAfter = Utc(2025, 12, 26, 0, 0);
        IReadOnlyList<Bar> m15 = new BarBuilder(TimeFrame.M15).At(first).Repeat((int)((end - first).TotalMinutes / 15.0), (b, _) => b.Doji(1.0)).Build()
            .Where(bar => bar.OpenTimeUtc <= lastBefore || bar.OpenTimeUtc >= firstAfter)
            .ToList();
        return new BarSet(d1, h4, m15);
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Recovery_RealChristmasHoursInTheM15History_GapAcceptedAndRunning()
    {
        // With the rule of 2026-11-16 this start stayed in Recovering until the gap left the 3,000 M15 bars in February.
        DateTime start = Utc(2026, 1, 5, 10, 0);
        var h = new BotHarness(ChristmasBars(), BotHarness.NewBroker(start));

        BotCore core = h.Start(start, RecoveryData.Parameters(newsEnabled: false));

        Assert.Equal(BotState.Running, core.State);
        Assert.True(core.LastReport!.DataComplete);
        AcceptedGapList m15 = Assert.Single(core.LastReport.AcceptedGaps);
        Assert.Equal(TimeFrame.M15, m15.TimeFrame);
        Assert.Equal(1, m15.Count);
        Assert.Equal(new[] { new BarGap(TimeFrame.M15, Utc(2025, 12, 24, 21, 45), Utc(2025, 12, 26, 0, 0)) }, m15.Newest);
        Assert.Equal(new[] { (TimeFrame.M15, 3000) }, h.History.MoreRequests);
        Assert.Contains(" gaps=M15:1 ", core.LastReport.Text, StringComparison.Ordinal);
        Assert.Equal(1, h.Journal.Count("recovery", GapLine("M15", 1, "2025-12-24 21:45>2025-12-26 00:00")));
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Recovery_FiveM15Gaps_CountFiveAndTheThreeNewestNewestFirst()
    {
        var h = new BotHarness();
        // M15 bars opening 01:30, 02:30, 04:30, 05:30 and 06:30 are missing (none is an H4 or D1 open time).
        foreach (double hours in new[] { 1.5, 2.5, 3.5, 5.5, 6.5 })
        {
            h.History.Missing.Add(A.AddHours(-hours));
        }

        BotCore core = h.Start(A);

        Assert.Equal(BotState.Running, core.State);
        AcceptedGapList m15 = Assert.Single(core.LastReport!.AcceptedGaps);
        Assert.Equal(5, m15.Count);
        Assert.Equal(
            new[]
            {
                new BarGap(TimeFrame.M15, RecoveryData.At(4, 6, 15), RecoveryData.At(4, 6, 45)),
                new BarGap(TimeFrame.M15, RecoveryData.At(4, 5, 15), RecoveryData.At(4, 5, 45)),
                new BarGap(TimeFrame.M15, RecoveryData.At(4, 4, 15), RecoveryData.At(4, 4, 45)),
            },
            m15.Newest);
        Assert.Contains(" gaps=M15:5 ", core.LastReport.Text, StringComparison.Ordinal);
        Assert.Equal(1, h.Journal.Count("recovery", GapLine("M15", 5, "2026-03-04 06:15>2026-03-04 06:45,2026-03-04 05:15>2026-03-04 05:45,2026-03-04 04:15>2026-03-04 04:45")));
        Assert.Equal(1, h.Journal.Count("recovery", "event=gaps-accepted"));
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Recovery_GapThatTheReloadRemoves_IsNotReported()
    {
        var h = new BotHarness();
        h.History.Missing.Add(A.AddHours(-2.0));
        h.History.RequestMoreHelps = true;

        BotCore core = h.Start(A);

        Assert.Equal(BotState.Running, core.State);
        Assert.Empty(core.LastReport!.AcceptedGaps);
        Assert.DoesNotContain(" gaps=", core.LastReport.Text, StringComparison.Ordinal);
        Assert.Equal(new[] { (TimeFrame.M15, 3000) }, h.History.MoreRequests);
        Assert.Equal(0, h.Journal.Count("recovery", "event=gaps-accepted"));
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Recovery_D1WithTenBars_StillFails()
    {
        var h = new BotHarness();
        h.History.Available[TimeFrame.D1] = 10;
        int needed = WarmupCalculator.For(new SignalParameters())[TimeFrame.D1];

        BotCore core = h.Start(A);

        Assert.Equal(BotState.Recovering, core.State);
        Assert.Equal(new[] { "D1: 10 bars, " + needed + " needed" }, core.LastReport!.DataProblems);
        Assert.Empty(core.LastReport.AcceptedGaps);
        Assert.Equal(new[] { (TimeFrame.D1, 300) }, h.History.MoreRequests);
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Recovery_LastM15BarClosedExactlyThirtyMinutesAgo_StillFailsAsNotRecent()
    {
        // The bars of 07:30 and 07:45 are missing: the last bar opens 07:15 and closed 07:30, 30 minutes before 08:00.
        var h = new BotHarness();
        h.History.Missing.Add(A.AddMinutes(-30.0));
        h.History.Missing.Add(A.AddMinutes(-15.0));

        BotCore core = h.Start(A);

        Assert.Equal(BotState.Recovering, core.State);
        Assert.Equal(new[] { "M15: last bar closed 2026-03-04 07:30, not recent at 2026-03-04 08:00" }, core.LastReport!.DataProblems);
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Recovery_LastM15BarClosed29Minutes59SecondsAgo_IsRecent()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(A.AddSeconds(-1.0)));
        h.History.Missing.Add(A.AddMinutes(-30.0));

        BotCore core = h.Start(A.AddSeconds(-1.0));

        Assert.Equal(BotState.Running, core.State);
        Assert.Empty(core.LastReport!.DataProblems);
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void LiveGap_BarMissingLiveAndInTheHistory_BlocksThenTheNextRecoveryAcceptsItAndALaterSignalIsPlaced()
    {
        // The M15 bar opening 07:15 never arrives and is not in the broker's history either.
        DateTime seven = A.AddHours(-1.0);
        var h = new BotHarness(broker: BotHarness.NewBroker(seven));
        h.History.Missing.Add(A.AddMinutes(-45.0));
        BotCore core = h.Start(seven);
        h.FeedTo(A.AddMinutes(-45.0));
        h.SkipTo(A.AddMinutes(-30.0));

        h.FeedTo(A.AddMinutes(-15.0));

        Assert.Equal(BotState.Blocked, core.State);
        Assert.True(core.DataGap);
        Assert.Equal(1, h.Alerts(AlertKind.DataGap));

        h.Timer(A.AddMinutes(-14.0));

        Assert.Equal(BotState.Running, core.State);
        Assert.False(core.DataGap);
        AcceptedGapList m15 = Assert.Single(core.LastReport!.AcceptedGaps);
        Assert.Equal(new[] { new BarGap(TimeFrame.M15, A.AddHours(-1.0), A.AddMinutes(-30.0)) }, m15.Newest);

        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.NotNull(h.ReferenceOrder());
        ScenarioAssert.Global(h.Broker);
    }
}
