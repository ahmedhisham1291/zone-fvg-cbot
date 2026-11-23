using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;
using Data = ZoneFvg.Core.Tests.SessionRangeBotData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-034: the signal engine in SessionRange mode, on the bars of <see cref="SessionRangeBotData"/>
/// with the default parameters (ready from 2026-02-21 on). Every expected value is worked out in
/// the remarks of that class.
/// </summary>
public class SignalEngineSessionRangeTests
{
    private sealed record Step(FeedBar Input, IReadOnlyList<Signal> Signals, SessionRangeSetup? Void, SessionRangeSetup? Setup);

    private static List<Step> Run(SignalEngine engine, IEnumerable<FeedBar> feed, bool replay = false)
    {
        var steps = new List<Step>();
        foreach (FeedBar item in feed)
        {
            IReadOnlyList<Signal> signals = engine.OnBarClosed(item.TimeFrame, item.Bar, replay).ToArray();
            steps.Add(new Step(item, signals, engine.LastRangeVoid, engine.RangeSetup));
        }

        return steps;
    }

    private static Signal OnlySignal(IReadOnlyList<Step> steps, DateTime expectedCloseUtc)
    {
        List<Step> with = steps.Where(step => step.Signals.Count > 0).ToList();
        Step step = Assert.Single(with);
        Assert.Equal(TimeFrame.M15, step.Input.TimeFrame);
        Assert.Equal(expectedCloseUtc, step.Input.CloseUtc);
        return Assert.Single(step.Signals);
    }

    private static void AssertBuy(Signal signal, DateTime tradingDate, DateTime expiryUtc, bool replay = false)
    {
        Assert.Equal(Data.BuyId(tradingDate), signal.ZoneId);
        Assert.Equal(Side.Buy, signal.Side);
        Assert.Equal(Data.Entry, signal.Entry, 9);
        Assert.Equal(Data.Stop, signal.Stop, 9);
        Assert.Equal(Data.Target, signal.Target, 9);
        Assert.Null(signal.Fvg);
        Assert.Equal(Data.Evaluation(tradingDate), signal.ConfirmationCloseUtc);
        Assert.Equal(replay, signal.FromReplay);
        Assert.Equal(Data.AtrH4, signal.AtrH4, 9);
        Assert.Equal(SignalKind.SessionRange, signal.Kind);
        Assert.NotNull(signal.Level);
        Assert.Equal(Data.Entry, signal.Level!.Value, 9);
        Assert.Equal(expiryUtc, signal.ExpiryUtc);
        Assert.Equal(DateTimeKind.Utc, signal.ExpiryUtc!.Value.Kind);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Constructor_NullSessionRangeParameters_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SignalEngine(new SignalParameters(), null!, TimeSpan.FromHours(12.0)));
        Assert.Throws<ArgumentNullException>(() => new SignalEngine(null!, new SessionRangeParameters(), TimeSpan.FromHours(12.0)));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Constructor_FridayCapNotATimeOfDayOrInvalidParameters_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SignalEngine(new SignalParameters(), new SessionRangeParameters(), TimeSpan.FromHours(24.0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SignalEngine(new SignalParameters(), new SessionRangeParameters { MinWidthAtr = -0.1 }, TimeSpan.FromHours(12.0)));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void WarmupBars_DefaultParameters_EqualThoseOfZoneFvgMode()
    {
        Assert.Equal(new SignalEngine(new SignalParameters()).WarmupBars.ToArray(), Data.Engine().WarmupBars.ToArray());
        Assert.Equal(new[] { 201, 248, 1968 }, Data.Engine().WarmupBars.Values.ToArray());
        Assert.False(Data.Engine().IsReady);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void WarmupBars_ShortParameters_M15CoversOneTradingDay()
    {
        var parameters = new SignalParameters { BiasEmaPeriod = 5, MaxZoneAgeBars = 1 };

        var engine = new SignalEngine(parameters, new SessionRangeParameters(), TimeSpan.FromHours(12.0));

        // ZoneFvg and Breakout need 21, 129 and 64 bars; one trading day is 100 M15 bars.
        Assert.Equal(new[] { TimeFrame.D1, TimeFrame.H4, TimeFrame.M15 }, engine.WarmupBars.Keys.ToArray());
        Assert.Equal(new[] { 21, 129, 100 }, engine.WarmupBars.Values.ToArray());
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OnBarClosed_WinterBuyDay_OneSignalAt0300NewYorkWithTheSetupLevelsAndExpiry()
    {
        SignalEngine engine = Data.Engine();

        List<Step> steps = Run(engine, Data.Bars(Data.Winter).Feed);

        // 03:00 New York is 08:00 UTC in winter; the order ends at 12:00 New York, 17:00 UTC.
        Signal signal = OnlySignal(steps, Data.Utc(2026, 3, 4, 8, 0));
        AssertBuy(signal, Data.Winter, Data.Utc(2026, 3, 4, 17, 0));
        Assert.All(steps, step => Assert.Null(step.Void));
        Assert.True(engine.IsReady);
        Assert.Equal(Bias.Long, engine.Bias);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OnBarClosed_SummerBuyDay_SignalAt0700UtcEndingAt1600Utc()
    {
        List<Step> steps = Run(Data.Engine(), Data.Bars(Data.Summer).Feed);

        Signal signal = OnlySignal(steps, Data.Utc(2026, 3, 12, 7, 0));
        AssertBuy(signal, Data.Summer, Data.Utc(2026, 3, 12, 16, 0));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OnBarClosed_Friday_TheExpiryIsTheFridayCap()
    {
        List<Step> steps = Run(Data.Engine(), Data.Bars(Data.Friday).Feed);

        // 12:00 New York is 16:00 UTC; FridayNoEntryUtc 12:00 UTC comes first.
        Signal signal = OnlySignal(steps, Data.Utc(2026, 3, 13, 7, 0));
        AssertBuy(signal, Data.Friday, Data.Utc(2026, 3, 13, 12, 0));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OnBarClosed_ShortBias_OneSellSignalAtTheRangeLow()
    {
        SignalEngine engine = Data.Engine();

        Signal signal = OnlySignal(Run(engine, Data.Bars(true, null, Data.Winter).Feed), Data.Utc(2026, 3, 4, 8, 0));

        Assert.Equal(Bias.Short, engine.Bias);
        Assert.Equal(Data.SellId(Data.Winter), signal.ZoneId);
        Assert.Equal(Side.Sell, signal.Side);
        Assert.Equal(Data.SellEntry, signal.Entry, 9);
        Assert.Equal(Data.SellStop, signal.Stop, 9);
        Assert.Equal(Data.SellTarget, signal.Target, 9);
        Assert.Equal(Data.SellEntry, signal.Level!.Value, 9);
        Assert.Equal(SignalKind.SessionRange, signal.Kind);
        Assert.Equal(Data.Utc(2026, 3, 4, 17, 0), signal.ExpiryUtc);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OnBarClosed_Replay_SignalCarriesTheReplayFlag()
    {
        Signal signal = OnlySignal(Run(Data.Engine(), Data.Bars(Data.Winter).Feed, replay: true), Data.Utc(2026, 3, 4, 8, 0));

        AssertBuy(signal, Data.Winter, Data.Utc(2026, 3, 4, 17, 0), replay: true);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OnBarClosed_SessionRangeMode_NoZonesNoZoneActivityNoBreakoutReference()
    {
        SignalEngine engine = Data.Engine();

        foreach (FeedBar item in Data.Bars(Data.Winter).Feed)
        {
            engine.OnBarClosed(item.TimeFrame, item.Bar, false);
            Assert.Empty(engine.Zones);
            Assert.Empty(engine.RecentFvgs);
            Assert.Same(ZoneActivity.None, engine.LastActivity);
            Assert.Null(engine.Reference);
        }
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OnBarClosed_SetupBeforeTheEngineIsReady_NoSignalButTheSetupAndItsVoidAreKnown()
    {
        // The void bar opens 05:00 New York (10:00 UTC) and reaches 99.99, below the range low 100.0.
        DateTime voidOpen = Data.Ny(Data.Early, 5, 0);
        SignalEngine engine = Data.Engine();

        List<Step> steps = Run(engine, Data.Bars(false, voidOpen, Data.Early).Feed.Where(item => item.CloseUtc <= Data.Utc(2026, 2, 4, 12, 0)));

        Assert.All(steps, step => Assert.Empty(step.Signals));
        Assert.False(engine.IsReady);
        Step evaluation = Assert.Single(steps, step => step.Input.TimeFrame == TimeFrame.M15 && step.Input.CloseUtc == Data.Utc(2026, 2, 4, 8, 0));
        Assert.Equal(Data.BuyId(Data.Early), evaluation.Setup!.Id);
        Step voided = Assert.Single(steps, step => step.Void is not null);
        Assert.Equal(voidOpen, voided.Input.Bar.OpenTimeUtc);
        Assert.Equal(TimeFrame.M15, voided.Input.TimeFrame);
        Assert.Equal(Data.BuyId(Data.Early), voided.Void!.Id);
        Assert.Equal(Data.BuyId(Data.Early), engine.RangeSetup!.Id);
        Assert.Null(engine.LastRangeVoid);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OnBarClosed_VoidBarWhileTheOrderIsPending_ReportedOnceForThatBarOnly()
    {
        DateTime voidOpen = Data.Ny(Data.Winter, 5, 0);

        List<Step> steps = Run(Data.Engine(), Data.Bars(false, voidOpen, Data.Winter).Feed);

        Step voided = Assert.Single(steps, step => step.Void is not null);
        Assert.Equal(voidOpen, voided.Input.Bar.OpenTimeUtc);
        Assert.Equal(Data.BuyId(Data.Winter), voided.Void!.Id);
        Assert.Equal(Data.RangeLow, voided.Void.RangeLow, 9);
        Assert.Empty(voided.Signals);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OrderNoLongerPending_AfterTheSetup_NoVoidIsReported()
    {
        DateTime voidOpen = Data.Ny(Data.Winter, 5, 0);
        IReadOnlyList<FeedBar> feed = Data.Bars(false, voidOpen, Data.Winter).Feed;
        SignalEngine engine = Data.Engine();
        DateTime evaluation = Data.Evaluation(Data.Winter);

        List<Step> before = Run(engine, feed.Where(item => item.CloseUtc <= evaluation));
        engine.OrderNoLongerPending("RH-20260305");
        engine.OrderNoLongerPending(Data.SellId(Data.Winter));
        engine.OrderNoLongerPending(Data.BuyId(Data.Winter));
        List<Step> after = Run(engine, feed.Where(item => item.CloseUtc > evaluation));

        Assert.Single(before.SelectMany(step => step.Signals));
        Assert.All(after, step => Assert.Null(step.Void));
        Assert.All(after, step => Assert.Empty(step.Signals));
        Assert.Throws<ArgumentNullException>(() => engine.OrderNoLongerPending(null!));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OnBarClosed_RepeatedM15Bar_RejectedAndNothingChanges()
    {
        SignalEngine engine = Data.Engine();
        IReadOnlyList<FeedBar> feed = Data.Bars(Data.Winter).Feed;
        List<FeedBar> upTo = feed.Where(item => item.CloseUtc <= Data.Utc(2026, 3, 4, 9, 0)).ToList();
        Run(engine, upTo);
        SessionRangeSetup? setup = engine.RangeSetup;
        FeedBar last = upTo[^1];
        Assert.Equal(TimeFrame.M15, last.TimeFrame);

        Assert.Throws<InvalidOperationException>(() => engine.OnBarClosed(TimeFrame.M15, last.Bar, false));

        Assert.Same(setup, engine.RangeSetup);
        Assert.Equal(Data.BuyId(Data.Winter), setup!.Id);
        List<Step> rest = Run(engine, feed.Where(item => item.CloseUtc > Data.Utc(2026, 3, 4, 9, 0)));
        Assert.All(rest, step => Assert.Empty(step.Signals));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OnBarClosed_NoD1AndNoH4Bars_NoAtrSoNoSetup()
    {
        SignalEngine engine = Data.Engine();

        List<Step> steps = Run(engine, Data.Bars(Data.Winter).Feed.Where(item => item.TimeFrame == TimeFrame.M15));

        Assert.All(steps, step => Assert.Empty(step.Signals));
        Assert.All(steps, step => Assert.Null(step.Setup));
        Assert.Equal(Bias.Neutral, engine.Bias);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OnBarClosed_SameBarsTwice_SameSignalsAndVoids()
    {
        IReadOnlyList<FeedBar> feed = Data.Bars(false, Data.Ny(Data.Winter, 6, 0), Data.Winter).Feed;

        List<Step> first = Run(Data.Engine(), feed);
        List<Step> second = Run(Data.Engine(), feed);

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Signals, second[i].Signals);
            Assert.Equal(first[i].Void, second[i].Void);
            Assert.Equal(first[i].Setup, second[i].Setup);
        }
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OnBarClosed_RebuiltByReplayDuringTheDay_SameSetupAndSameVoidAfterwards()
    {
        // A restart at 09:00 UTC: a new engine replays every closed bar, then goes on live.
        DateTime restart = Data.Utc(2026, 3, 4, 9, 0);
        IReadOnlyList<FeedBar> feed = Data.Bars(false, Data.Ny(Data.Winter, 6, 0), Data.Winter).Feed;
        List<Step> live = Run(Data.Engine(), feed);

        SignalEngine rebuilt = Data.Engine();
        List<Step> replayed = Run(rebuilt, feed.Where(item => item.CloseUtc <= restart), replay: true);
        List<Step> after = Run(rebuilt, feed.Where(item => item.CloseUtc > restart));

        Signal replaySignal = Assert.Single(replayed.SelectMany(step => step.Signals));
        Assert.True(replaySignal.FromReplay);
        Assert.Equal(live.Single(step => step.Signals.Count > 0).Signals[0] with { FromReplay = true }, replaySignal);
        List<Step> liveAfter = live.Where(step => step.Input.CloseUtc > restart).ToList();
        Assert.Equal(liveAfter.Count, after.Count);
        for (int i = 0; i < after.Count; i++)
        {
            Assert.Equal(liveAfter[i].Void, after[i].Void);
            Assert.Equal(liveAfter[i].Setup, after[i].Setup);
            Assert.Empty(after[i].Signals);
        }

        Assert.Single(after, step => step.Void is not null);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void OtherModes_NoSessionRangeTrackerIsRun()
    {
        IReadOnlyList<FeedBar> feed = Data.Bars(false, Data.Ny(Data.Winter, 5, 0), Data.Winter).Feed;
        var engines = new[] { new SignalEngine(new SignalParameters()), new SignalEngine(new SignalParameters(), new BreakoutParameters()) };

        foreach (SignalEngine engine in engines)
        {
            foreach (FeedBar item in feed)
            {
                IReadOnlyList<Signal> signals = engine.OnBarClosed(item.TimeFrame, item.Bar, false);
                Assert.DoesNotContain(signals, signal => signal.Kind == SignalKind.SessionRange);
                Assert.Null(engine.LastRangeVoid);
                Assert.Null(engine.RangeSetup);
            }

            engine.OrderNoLongerPending(Data.BuyId(Data.Winter));
            Assert.Null(engine.RangeSetup);
        }
    }
}
