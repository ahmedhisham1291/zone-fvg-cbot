using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-009: the engine reports, after each bar, the zones created and the zones that ended with
/// their reason; the bot counts them per CE(S)T day for live bars only and writes one line at the
/// first live M15 bar of a later day. Engine data: <see cref="EngineScenarios"/> (reference demand
/// zone D-20260301-0800, proximal 100.2, distal 99.5, active from 2026-03-02 00:00). Bot data:
/// <see cref="RecoveryData"/> (zone D-20260303-1600 created by the H4 bar closing 2026-03-04
/// 08:00, signalled at 08:45).
/// </summary>
public class ZoneActivityTests
{
    private static readonly (double Open, double High, double Low, double Close)[] Flat16 =
        Enumerable.Repeat((100.6, 100.65, 100.55, 100.6), 16).ToArray();

    private static readonly (double Open, double High, double Low, double Close)[] CloseBelowDistal =
    {
        (100.6, 100.6, 99.0, 99.2),
    };

    private static (int Created, List<ZoneEnd> Ended) Run(IReadOnlyList<FeedBar> feed)
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);
        int created = 0;
        var ended = new List<ZoneEnd>();
        foreach (FeedBar item in feed)
        {
            engine.OnBarClosed(item.TimeFrame, item.Bar, false);
            created += engine.LastActivity.Created;
            ended.AddRange(engine.LastActivity.Ended);
        }

        return (created, ended);
    }

    private static IReadOnlyList<FeedBar> WithH4After(IReadOnlyList<Bar> extraH4)
    {
        var h4 = new List<Bar>(EngineScenarios.DemandH4(EngineScenarios.H4Warmup));
        h4.AddRange(extraH4);
        return EngineFeed.Merge(EngineScenarios.D1(BiasHistory.Long, EngineScenarios.D1Warmup), h4, EngineScenarios.M15(EngineScenarios.M15Warmup));
    }

    [Fact]
    [Trait("Req", "OPS-009")]
    public void LastActivity_SignalledZone_CreatedOnceAndEndedAsSignalled()
    {
        (int created, List<ZoneEnd> ended) = Run(EngineScenarios.History(BiasHistory.Long, 0, 0, 0, false, EngineScenarios.TouchAndGap));

        Assert.Equal(1, created);
        Assert.Equal(new[] { new ZoneEnd(EngineScenarios.ZoneId, ZoneEndReason.Signalled) }, ended);
    }

    [Fact]
    [Trait("Req", "OPS-009")]
    public void LastActivity_NoGapWithin16Bars_EndedAsNoFvg()
    {
        (_, List<ZoneEnd> ended) = Run(EngineScenarios.History(BiasHistory.Long, 0, 0, 0, false, EngineScenarios.TouchWithoutGap, Flat16));

        Assert.Equal(new[] { new ZoneEnd(EngineScenarios.ZoneId, ZoneEndReason.NoFvg) }, ended);
    }

    [Fact]
    [Trait("Req", "OPS-009")]
    public void LastActivity_GapAgainstTheBias_EndedAsBiasMismatch()
    {
        (_, List<ZoneEnd> ended) = Run(EngineScenarios.History(BiasHistory.Short, 0, 0, 0, false, EngineScenarios.TouchAndGap));

        Assert.Equal(new[] { new ZoneEnd(EngineScenarios.ZoneId, ZoneEndReason.BiasMismatch) }, ended);
    }

    [Fact]
    [Trait("Req", "OPS-009")]
    public void LastActivity_CloseBeyondDistal_EndedAsInvalid()
    {
        (_, List<ZoneEnd> ended) = Run(EngineScenarios.History(BiasHistory.Long, 0, 0, 0, false, CloseBelowDistal));

        Assert.Equal(new[] { new ZoneEnd(EngineScenarios.ZoneId, ZoneEndReason.Invalid) }, ended);
    }

    [Fact]
    [Trait("Req", "OPS-009")]
    public void LastActivity_120H4BarsAfterTheActivation_EndedAsExpired()
    {
        IReadOnlyList<Bar> after = new BarBuilder(TimeFrame.H4).At(EngineScenarios.Activation).Filler(120).Build();

        (int created, List<ZoneEnd> ended) = Run(WithH4After(after));

        Assert.Equal(1, created);
        Assert.Equal(new[] { new ZoneEnd(EngineScenarios.ZoneId, ZoneEndReason.Expired) }, ended);
    }

    [Fact]
    [Trait("Req", "OPS-009")]
    public void LastActivity_NewerOverlappingZone_EndedAsSuperseded()
    {
        // Scenario A again from the activation: a second demand zone with the same edges replaces the untouched one.
        (int created, List<ZoneEnd> ended) = Run(WithH4After(ZoneScenarios.ScenarioA(start: EngineScenarios.Activation)));

        Assert.Equal(2, created);
        Assert.Equal(new[] { new ZoneEnd(EngineScenarios.ZoneId, ZoneEndReason.Superseded) }, ended);
    }

    [Fact]
    [Trait("Req", "OPS-009")]
    public void LastActivity_ReadAfterEveryBar_ZonesAndSignalsUnchanged()
    {
        IReadOnlyList<FeedBar> feed = EngineScenarios.History(BiasHistory.Long, 0, 0, 0, false, EngineScenarios.TouchAndGap, EngineScenarios.SecondGap);
        var reading = new SignalEngine(EngineScenarios.Defaults);
        var plain = new SignalEngine(EngineScenarios.Defaults);

        foreach (FeedBar item in feed)
        {
            IReadOnlyList<Signal> a = reading.OnBarClosed(item.TimeFrame, item.Bar, false);
            _ = reading.LastActivity;
            IReadOnlyList<Signal> b = plain.OnBarClosed(item.TimeFrame, item.Bar, false);
            Assert.Equal(b, a);
            Assert.Equal(plain.Zones, reading.Zones);
        }
    }

    [Fact]
    [Trait("Req", "OPS-009")]
    public void LastActivity_NewEngine_IsNone()
    {
        Assert.Same(ZoneActivity.None, new SignalEngine(EngineScenarios.Defaults).LastActivity);
        Assert.Throws<ArgumentNullException>(() => new ZoneActivity(0, null!));
    }

    // ------------------------------------------------------------------ per-day counts

    private static ZoneActivity Ending(ZoneEndReason reason)
    {
        return new ZoneActivity(0, new[] { new ZoneEnd("D-1", reason) });
    }

    [Fact]
    [Trait("Req", "OPS-009")]
    public void Counts_DayBoundary_TheBarClosing2300CountsForTheNextDay()
    {
        var counts = new ZoneOutcomeCounts();
        DateTime close2245 = new(2026, 3, 4, 22, 45, 0, DateTimeKind.Utc);
        DateTime close2300 = new(2026, 3, 4, 23, 0, 0, DateTimeKind.Utc);
        counts.Add(close2245, Ending(ZoneEndReason.Signalled));
        counts.Add(close2300, new ZoneActivity(1, new[] { new ZoneEnd("D-2", ZoneEndReason.Invalid) }));
        counts.Add(close2300, ZoneActivity.None);

        Assert.Empty(counts.TakeDaysBefore(close2245));
        (string Key, string? Value)[] fourth = Assert.Single(counts.TakeDaysBefore(close2300));
        (string Key, string? Value)[] fifth = Assert.Single(counts.TakeDaysBefore(close2300.AddDays(1.0)));

        Assert.Equal(
            new (string, string?)[] { ("day", "2026-03-04"), ("created", "0"), ("signalled", "1"), ("no-fvg", "0"), ("bias", "0"), ("invalid", "0"), ("expired", "0"), ("superseded", "0") },
            fourth);
        Assert.Equal(
            new (string, string?)[] { ("day", "2026-03-05"), ("created", "1"), ("signalled", "0"), ("no-fvg", "0"), ("bias", "0"), ("invalid", "1"), ("expired", "0"), ("superseded", "0") },
            fifth);
        Assert.Empty(counts.TakeDaysBefore(close2300.AddDays(5.0)));
    }

    [Fact]
    [Trait("Req", "OPS-009")]
    public void Counts_EachReasonUnderItsOwnName()
    {
        var counts = new ZoneOutcomeCounts();
        DateTime close = new(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
        foreach (ZoneEndReason reason in Enum.GetValues<ZoneEndReason>())
        {
            counts.Add(close, Ending(reason));
        }

        counts.Add(close, Ending(ZoneEndReason.Superseded));

        // Summer: the CE(S)T day of 2026-07-01 starts 2026-06-30 22:00 UTC.
        (string Key, string? Value)[] line = Assert.Single(counts.TakeDaysBefore(new DateTime(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(
            new (string, string?)[] { ("day", "2026-07-01"), ("created", "0"), ("signalled", "1"), ("no-fvg", "1"), ("bias", "1"), ("invalid", "1"), ("expired", "1"), ("superseded", "2") },
            line);
    }

    // ------------------------------------------------------------------ BotCore

    private static readonly DateTime A = RecoveryData.Activation;
    private static readonly DateTime NextDay = new(2026, 3, 4, 23, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Req", "OPS-009")]
    public void Bot_SignalledZone_WrittenAtTheFirstLiveM15BarOfTheNextCestDay()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(A.AddHours(-8.0)));
        h.Start(A.AddHours(-8.0));

        h.FeedTo(NextDay.AddMinutes(-15.0));
        Assert.Empty(h.Journal.Lines("zones"));
        h.FeedTo(NextDay);

        Assert.Equal(
            new[] { JournalFormat.Time(NextDay) + " zones day=2026-03-04 created=1 signalled=1 no-fvg=0 bias=0 invalid=0 expired=0 superseded=0" },
            h.Journal.Lines("zones"));
        h.FeedTo(NextDay.AddHours(2.0));
        Assert.Single(h.Journal.Lines("zones"));
    }

    [Fact]
    [Trait("Req", "OPS-009")]
    public void Bot_ZoneEndedDuringTheRecoveryReplay_NotCountedAndAZeroDayWritesNoLine()
    {
        // Started at 09:00: the zone was created and signalled in the replay.
        var h = new BotHarness();
        h.Start(A.AddHours(1.0));

        h.FeedTo(NextDay.AddHours(2.0));

        Assert.Empty(h.Journal.Lines("zones"));
    }
}
