using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using Data = ZoneFvg.Core.Tests.GoldProfileData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The automatic gold profile through the core and a <see cref="FakeBroker"/> (EXE-030, EXE-032):
/// the owner's evidence of 2026-11-24 (one broker reports a gold pip of 1, another of 0.01). At both
/// pip sizes the spread limit is 0.40 USD and the Choch stop floor 3 USD, the order and its volume are
/// the same, and the start line, the order comment and recovery use one and the same hash, so a
/// restart recognises the bot's own gold order and position. Data: <see cref="GoldProfileData"/>.
/// </summary>
public class Exe032GoldProfileScenarios
{
    private static BotHarness Started(double pip, double factor = 16.0, double spread = 0.30)
    {
        BotHarness h = Data.Harness(pip, factor, spread);
        h.Start(Data.Start, Data.Parameters(pip));
        return h;
    }

    private static string StartLine(BotHarness h)
    {
        return h.Journal.Lines("start").Last();
    }

    // ------------------------------------------------------------------ EXE-032: one hash everywhere

    [Theory]
    [Trait("Req", "EXE-032")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void GoldAutomatic_StartLineOrderCommentAndRecovery_UseTheSameHash(double pip)
    {
        BotHarness h = Started(pip);
        Assert.Contains(" hash=" + Data.Hash + " ", StartLine(h), StringComparison.Ordinal);

        h.FeedTo(Data.Confirmation);
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Equal(Data.Label, request.Order!.Label);
        Assert.Contains(Data.Hash, request.Order.Comment, StringComparison.Ordinal);
        long id = Assert.Single(h.Broker.GetPendingOrders()).Id;

        BotCore restarted = h.Start(Data.Confirmation.AddMinutes(5.0), Data.Parameters(pip));
        h.FeedTo(Data.Confirmation.AddHours(1.0));

        Assert.Equal(new[] { id }, restarted.LastReport!.OrdersKept);
        Assert.Empty(restarted.LastReport.Strangers);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-032")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void GoldAutomatic_RestartWithTheOpenPosition_Adopted(double pip)
    {
        BotHarness h = Started(pip);
        h.FeedTo(Data.Confirmation);
        h.Broker.AdvanceTo(Data.Confirmation.AddMinutes(20.0));
        h.Broker.Tick(Data.Symbol, 1615.7, 1616.0);
        long id = Assert.Single(h.Broker.GetPositions()).Id;

        BotCore restarted = h.Start(Data.Confirmation.AddMinutes(30.0), Data.Parameters(pip));

        Assert.Equal(new[] { id }, restarted.LastReport!.PositionsAdopted);
        Assert.Empty(restarted.LastReport.Strangers);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void GoldAutomatic_Pip1AndPip001_TheSameOrderInPriceAndVolume()
    {
        BotHarness one = Started(1.0);
        BotHarness hundredth = Started(0.01);
        one.FeedTo(Data.Confirmation);
        hundredth.FeedTo(Data.Confirmation);

        LimitOrderRequest a = Assert.Single(one.Broker.Requests).Order!;
        LimitOrderRequest b = Assert.Single(hundredth.Broker.Requests).Order!;

        Assert.Equal(a, b);
        Assert.True(a.VolumeUnits >= 1.0 && a.VolumeUnits <= 1000.0);
    }

    [Theory]
    [Trait("Req", "EXE-032")]
    [InlineData(1.0, "pipSize=1 maxSpreadPips=0.4 minStopPips=8 chochMinStopPips=3")]
    [InlineData(0.01, "pipSize=0.01 maxSpreadPips=40 minStopPips=800 chochMinStopPips=300")]
    public void StartLine_GoldAutomatic_EndsWithTheProfile(double pip, string values)
    {
        BotHarness h = Started(pip);

        Assert.EndsWith(
            " mode=ZoneFvg model=Choch profile=Gold " + values + " maxVolumeUnits=1000 auto=MaxSpreadPips,MinStopPips,ChochMinStopPips,MaxVolumeUnits brake=off",
            StartLine(h),
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ EXE-030: the owner's evidence

    [Theory]
    [Trait("Req", "EXE-030")]
    [InlineData(1.0, 0.08)]
    [InlineData(1.0, 0.30)]
    [InlineData(0.01, 0.08)]
    [InlineData(0.01, 0.30)]
    public void GoldAutomatic_SpreadUpTo040_Passes(double pip, double spread)
    {
        BotHarness h = Started(pip, spread: spread);

        h.FeedTo(Data.Confirmation);

        Assert.Single(h.Broker.Requests);
        Assert.Equal(0, h.Journal.Count("skip", "reason=SpreadTooWide"));
    }

    [Theory]
    [Trait("Req", "EXE-030")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void GoldAutomatic_Spread041_SkippedAsSpreadTooWide(double pip)
    {
        BotHarness h = Started(pip, spread: 0.41);

        h.FeedTo(Data.Confirmation);

        Assert.Empty(h.Broker.Requests);
        Assert.True(h.Journal.Count("skip", "reason=SpreadTooWide") >= 1);
    }

    [Theory]
    [Trait("Req", "EXE-030")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void GoldAutomatic_ChochStopBelow3Usd_SkippedAsStopTooSmall_Above3Placed(double pip)
    {
        BotHarness small = Started(pip, factor: 2.0, spread: 0.08);
        BotHarness large = Started(pip, factor: 4.0, spread: 0.08);

        small.FeedTo(Data.Confirmation);
        large.FeedTo(Data.Confirmation);

        Assert.Empty(small.Broker.Requests);
        Assert.True(small.Journal.Count("skip", "reason=StopTooSmall") >= 1);
        LimitOrderRequest order = Assert.Single(large.Broker.Requests).Order!;
        Assert.True(Math.Abs(order.EntryPrice - order.StopLossPrice) > 3.0);
    }
}
