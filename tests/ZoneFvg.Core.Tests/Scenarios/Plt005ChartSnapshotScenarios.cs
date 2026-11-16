using System.Collections.ObjectModel;
using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The read-only chart snapshots of <see cref="BotCore"/> (PLT-005, owner decision of 2026-11-16)
/// through a <see cref="FakeBroker"/>: reading them never changes orders, journal, alerts or state;
/// a bot rebuilt by recovery shows the same snapshots as one that never stopped. Data:
/// <see cref="RecoveryData"/> (zone D-20260303-1600, gap from 100.6 to 100.75 confirmed at 08:45).
/// </summary>
public class Plt005ChartSnapshotScenarios
{
    private static readonly DateTime A = RecoveryData.Activation;

    [Fact]
    [Trait("Req", "PLT-005")]
    public void Snapshots_BeforeTheFirstRecovery_Empty()
    {
        var h = new BotHarness();
        BotCore core = h.NewCore();

        Assert.Empty(core.ZoneSnapshot);
        Assert.Empty(core.FvgSnapshot);
    }

    [Fact]
    [Trait("Req", "PLT-005")]
    public void Snapshots_RecoveryWaitingForData_Empty()
    {
        var h = new BotHarness();
        h.History.Available[TimeFrame.M15] = 1000;

        BotCore core = h.Start(A.AddHours(1.0));

        Assert.Equal(BotState.Recovering, core.State);
        Assert.Empty(core.ZoneSnapshot);
        Assert.Empty(core.FvgSnapshot);
    }

    [Fact]
    [Trait("Req", "PLT-005")]
    public void Snapshots_AreReadOnlyCopiesThatLaterBarsDoNotChange()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A.AddMinutes(30.0));
        IReadOnlyList<Zone> zones = core.ZoneSnapshot;
        Zone touched = Assert.Single(zones);
        Assert.Equal(RecoveryData.ZoneId, touched.Id);
        Assert.Equal(ZoneState.Touched, touched.State);
        Assert.IsType<ReadOnlyCollection<Zone>>(zones);
        Assert.NotSame(zones, core.ZoneSnapshot);
        Assert.Throws<NotSupportedException>(() => ((IList<Zone>)zones).Clear());

        h.FeedTo(A.AddHours(1.0));

        Assert.Empty(core.ZoneSnapshot);
        Assert.Equal(touched, Assert.Single(zones));
        IReadOnlyList<Fvg> fvgs = core.FvgSnapshot;
        Assert.IsType<ReadOnlyCollection<Fvg>>(fvgs);
        Fvg fvg = Assert.Single(fvgs);
        Assert.Equal(100.75, fvg.Upper);
        Assert.Equal(100.6, fvg.Lower);
        Assert.Equal(RecoveryData.Confirmation, fvg.C1OpenUtc.AddMinutes(15.0));
        Assert.Throws<NotSupportedException>(() => ((IList<Fvg>)fvgs).Clear());

        h.FeedTo(A.AddHours(26.0));

        Assert.Empty(core.FvgSnapshot);
        Assert.Equal(fvg, Assert.Single(fvgs));
    }

    [Theory]
    [Trait("Req", "PLT-005")]
    [InlineData(1438)]
    [InlineData(1440)]
    [InlineData(1441)]
    public void FvgSnapshot_GapAge_ListedUpToExactly24HoursOfBarTime(int minutesAfterConfirmation)
    {
        // Age is measured in bar time: the close of the last bar minus the close of c1 (08:45).
        // At 1438 minutes the last bar closed 23 h 45 min after it; at 1440 and 1441 exactly 24 hours
        // after it (listed). The bar closing 24 h 15 min after it (1455) drops the gap.
        var h = new BotHarness(broker: BotHarness.NewBroker(A.AddHours(-8.0)));
        BotCore core = h.Start(A.AddHours(-8.0));

        h.FeedTo(RecoveryData.Confirmation.AddMinutes(minutesAfterConfirmation));

        Assert.Single(core.FvgSnapshot);
        h.FeedTo(RecoveryData.Confirmation.AddMinutes(1455.0));
        Assert.Empty(core.FvgSnapshot);
    }

    [Theory]
    [Trait("Req", "PLT-005")]
    [InlineData(30, 0)]
    [InlineData(60, 1)]
    [InlineData(360, 1)]
    [InlineData(1440, 1)]
    [InlineData(1500, 0)]
    public void Snapshots_AfterARestart_EqualToABotThatNeverStopped(int restartMinutes, int gaps)
    {
        DateTime restart = A.AddMinutes(restartMinutes);
        var live = new BotHarness(broker: BotHarness.NewBroker(A.AddHours(-8.0)));
        live.Start(A.AddHours(-8.0));
        live.FeedTo(restart);
        var restarted = new BotHarness();
        restarted.Start(restart);

        Assert.Equal(live.Core.ZoneSnapshot, restarted.Core.ZoneSnapshot);
        Assert.Equal(live.Core.FvgSnapshot, restarted.Core.FvgSnapshot);
        Assert.Equal(gaps, restarted.Core.FvgSnapshot.Count);

        live.FeedTo(A.AddHours(27.0));
        restarted.FeedTo(A.AddHours(27.0));

        Assert.Equal(live.Core.ZoneSnapshot, restarted.Core.ZoneSnapshot);
        Assert.Equal(live.Core.FvgSnapshot, restarted.Core.FvgSnapshot);
    }

    [Fact]
    [Trait("Req", "PLT-005")]
    public void ReadingSnapshotsAfterEveryBar_SameOrdersJournalAlertsAndStateAsNotReading()
    {
        var unread = new BotHarness(broker: BotHarness.NewBroker(A.AddHours(-8.0)));
        var read = new BotHarness(broker: BotHarness.NewBroker(A.AddHours(-8.0)));
        unread.Start(A.AddHours(-8.0));
        read.Start(A.AddHours(-8.0));
        int stepsWithGap = 0;

        foreach (DateTime close in CloseTimes(read.Bars, A.AddHours(-8.0), A.AddHours(4.0)))
        {
            unread.FeedTo(close);
            read.FeedTo(close);
            _ = read.Core.ZoneSnapshot;
            _ = read.Core.FvgSnapshot;
            stepsWithGap += read.Core.FvgSnapshot.Count > 0 ? 1 : 0;
            Assert.Equal(unread.Core.Zones, read.Core.Zones);
            Assert.Equal(unread.Core.State, read.Core.State);
        }

        Assert.True(stepsWithGap > 0);
        Assert.Equal(1, read.Count(BrokerCall.Place));
        Assert.Equal(unread.Broker.Requests, read.Broker.Requests);
        Assert.Equal(unread.Broker.GetPendingOrders(), read.Broker.GetPendingOrders());
        Assert.Equal(unread.Journal.Entries, read.Journal.Entries);
    }

    private static IEnumerable<DateTime> CloseTimes(BarSet bars, DateTime afterUtc, DateTime untilUtc)
    {
        return bars.Feed.Select(item => item.CloseUtc).Where(close => close > afterUtc && close <= untilUtc).Distinct();
    }
}
