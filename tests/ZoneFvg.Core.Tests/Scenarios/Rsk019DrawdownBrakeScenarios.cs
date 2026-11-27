using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-019 through the pre-trade filters with <see cref="FakeBroker"/>: the drawdown brake of
/// fixed sizing, its peak (the kill switch's), its boundary, what it leaves unchanged, and its
/// journal note.
/// </summary>
/// <remarks>
/// <para>Reference buy of <see cref="ExecutionData"/> on EURUSD: stop 20 pips, pip value 0.0001
/// per unit, volume step 1,000. Volume = balance x percent / 100 / (20 x 0.0001), rounded down;
/// risk money = volume x 0.002.</para>
/// <para>Fixed sizing with RiskPercent 0.75 and the brake 3 / 0.5, core defaults otherwise
/// (InitialCapital 200,000, daily budget 4,000, caps off). Epoch 1 January 2026. History: a trade
/// closed on 5 January with +10,000 (balance after 210,000) and one closed on 8 January that
/// brings the balance to B, so the balance at the epoch is 200,000 and the peak 210,000. 3% of
/// 210,000 is 6,300, so the brake starts at B = 203,700.</para>
/// <list type="bullet">
/// <item>B 203,700, braked 0.375%: 763.875 / 0.002 = 381,937.5 -> 381,000, risk 762.00.</item>
/// <item>B 203,700.01, 0.75%: 1,527.750075 / 0.002 = 763,875.04 -> 763,000, risk 1,526.00.</item>
/// <item>B 203,699.99, braked 0.375%: 763.8749625 / 0.002 = 381,937.48 -> 381,000, risk 762.00.</item>
/// <item>B 205,000, 0.75%: 1,537.5 / 0.002 = 768,750 -> 768,000, risk 1,536.00.</item>
/// <item>B 203,700, factor 1: 0.75%, 763,875 -> 763,000, risk 1,526.00.</item>
/// <item>B 199,500 (5% below), ladder on, first entry 0.08886%: 177.2757 / 0.002 = 88,637.85 -> 88,000, risk 176.00.</item>
/// </list>
/// </remarks>
public class Rsk019DrawdownBrakeScenarios
{
    private const string Manual = "manual";

    private static RiskParameters Fixed => new()
    {
        RiskEpochDate = RiskData.Epoch,
        LadderEnabled = false,
        RiskPercent = 0.75,
        DrawdownBrakePercent = 3.0,
        DrawdownBrakeFactor = 0.5,
    };

    private static ClosedTradeInfo Closed(long id, DateTime closeUtc, double net, double balanceAfter)
    {
        return new ClosedTradeInfo(id, "GBPUSD", Manual, closeUtc.AddHours(-2.0), closeUtc, net, 0.0, 0.0, balanceAfter);
    }

    /// <summary>The two trades of the remarks for the balance <paramref name="balance"/>.</summary>
    private static ClosedTradeInfo[] PeakThenBalance(double balance)
    {
        return new[]
        {
            Closed(1, ExecutionData.At(5, 12, 0), 10000.0, 210000.0),
            Closed(2, ExecutionData.At(8, 12, 0), balance - 210000.0, balance),
        };
    }

    private static Run Evaluate(RiskParameters risk, double balance, IEnumerable<ClosedTradeInfo>? history = null, Func<DateTime, IReadOnlyList<ClosedTradeInfo>>? historyOverride = null)
    {
        var run = new Run(risk, balance, history ?? PeakThenBalance(balance), historyOverride);
        run.Result = run.Filters.Evaluate(ExecutionData.Buy(), new PreTradeContext(ExecutionData.Symbol, BotState.Running, Array.Empty<Zone>(), false));
        return run;
    }

    private static PlannedOrder OrderOf(Run run)
    {
        PreTradeResult result = run.Result!;
        string why = result.Skip is null ? string.Empty : result.Skip.Reason + ": " + result.Skip.Detail;
        Assert.True(result.IsAllowed, "expected an order but the signal was skipped with " + why);
        return result.Order!;
    }

    private static SkipRecord SkipOf(Run run)
    {
        PreTradeResult result = run.Result!;
        Assert.False(result.IsAllowed);
        Assert.Null(result.Order);
        return Assert.Single(run.Events.Skips);
    }

    private static string TakenLine(PlannedOrder order)
    {
        var sink = new RecordingSink();
        Journal journal = OpsData.NewJournal(sink);
        return journal.Signal(SignalJournalEntry.Taken(order, Bias.Long, ExecutionData.Zone(ExecutionData.BuyZone, ZoneType.Demand, 1.099), ExecutionData.EurUsd(), NewsStatus.Available));
    }

    // ------------------------------------------------------------------ the boundary

    [Theory]
    [Trait("Req", "RSK-019")]
    [InlineData(203700.0, 381000.0, 762.0, true)]
    [InlineData(203700.01, 763000.0, 1526.0, false)]
    [InlineData(203699.99, 381000.0, 762.0, true)]
    [InlineData(205000.0, 768000.0, 1536.0, false)]
    public void Brake_AroundThreePercentBelowThePeak_HalvesTheRiskFromExactlyThreePercent(double balance, double volume, double risk, bool braked)
    {
        Run run = Evaluate(Fixed, balance);

        PlannedOrder order = OrderOf(run);
        Assert.Equal(volume, order.VolumeUnits);
        Assert.Equal(risk, order.RiskMoney, 6);
        Assert.Equal(braked, order.Brake is not null);
        if (braked)
        {
            Assert.Equal(210000.0, order.Brake!.Peak);
            Assert.Equal(balance, order.Brake.Balance);
            Assert.Equal(0.375, order.Brake.RiskPercent);
        }
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void Brake_BackAboveTheDistance_FullRiskAgainNothingRemembered()
    {
        Run braked = Evaluate(Fixed, 203000.0);
        Run back = Evaluate(Fixed, 205000.0, PeakThenBalance(203000.0).Append(Closed(3, ExecutionData.At(9, 12, 0), 2000.0, 205000.0)));

        Assert.NotNull(OrderOf(braked).Brake);
        Assert.Equal(768000.0, OrderOf(back).VolumeUnits);
        Assert.Null(OrderOf(back).Brake);
    }

    // ------------------------------------------------------------------ the peak is the kill switch's

    [Fact]
    [Trait("Req", "RSK-019")]
    public void Peak_HighestBalanceBeforeTheEpoch_DoesNotCount()
    {
        // The broker returns an older trade too (a snapshot may contain older ones): 220,000 before the epoch.
        ClosedTradeInfo old = Closed(9, new DateTime(2025, 12, 20, 12, 0, 0, DateTimeKind.Utc), 20000.0, 220000.0);
        ClosedTradeInfo[] history = PeakThenBalance(205000.0).Prepend(old).ToArray();

        Run run = Evaluate(Fixed, 205000.0, history, _ => history);

        // With 220,000 as peak, 205,000 would be 6.8% below and braked; with the kill switch's 210,000 it is not.
        PlannedOrder order = OrderOf(run);
        Assert.Equal(768000.0, order.VolumeUnits);
        Assert.Null(order.Brake);
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void Peak_SameHistory_SamePeakInTheKillSwitchAndInTheBrake()
    {
        ClosedTradeInfo old = Closed(9, new DateTime(2025, 12, 20, 12, 0, 0, DateTimeKind.Utc), 20000.0, 220000.0);
        ClosedTradeInfo[] history = PeakThenBalance(203700.0).Prepend(old).ToArray();

        Run run = Evaluate(Fixed, 203700.0, history, _ => history);
        var snapshot = new RiskSnapshot(ExecutionData.Now, ExecutionData.Account(203700.0, 203700.0, 203700.0), Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), history, RiskData.EpochStart, new[] { ExecutionData.EurUsd() });
        KillSwitchDecision kill = new RiskEngine(Fixed).KillSwitch(snapshot);

        Assert.Equal(210000.0, kill.Peak);
        Assert.Equal(kill.Peak, OrderOf(run).Brake!.Peak);
        Assert.Equal(kill.Peak, new RiskEngine(Fixed).PeakBalance(snapshot));
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void Peak_NoTradeSinceTheEpoch_IsTheBalanceAndNothingIsBraked()
    {
        Run run = Evaluate(Fixed, 205000.0, Array.Empty<ClosedTradeInfo>());

        Assert.Null(OrderOf(run).Brake);
        Assert.Equal(768000.0, OrderOf(run).VolumeUnits);
    }

    // ------------------------------------------------------------------ factor 1, brake off, ladder, Recovery

    [Fact]
    [Trait("Req", "RSK-019")]
    public void FactorOne_BrakedAndNotBraked_SameRisk_TheNoteStillWritten()
    {
        PlannedOrder braked = OrderOf(Evaluate(Fixed with { DrawdownBrakeFactor = 1.0 }, 203700.0));
        PlannedOrder off = OrderOf(Evaluate(Fixed with { DrawdownBrakePercent = 0.0 }, 203700.0));

        Assert.Equal(763000.0, braked.VolumeUnits);
        Assert.Equal(off.VolumeUnits, braked.VolumeUnits);
        Assert.Equal(off.RiskMoney, braked.RiskMoney);
        Assert.Equal(off.Comment, braked.Comment);
        Assert.Equal(0.75, braked.Brake!.RiskPercent);
        Assert.EndsWith(" skip=none brake=on peak=210000 balance=203700 riskPercent=0.75", TakenLine(braked), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void BrakeOff_SameSizingSameJournalLineAndNoExtraHistoryRead()
    {
        var withoutTheNewFields = new RiskParameters { RiskEpochDate = RiskData.Epoch, LadderEnabled = false, RiskPercent = 0.75 };

        Run plain = Evaluate(withoutTheNewFields, 203700.0);
        Run off = Evaluate(Fixed with { DrawdownBrakePercent = 0.0, DrawdownBrakeFactor = 0.3 }, 203700.0);

        PlannedOrder a = OrderOf(plain);
        PlannedOrder b = OrderOf(off);
        Assert.Equal(763000.0, b.VolumeUnits);
        Assert.Equal(a, b);
        Assert.Null(b.Brake);
        Assert.Equal(TakenLine(a), TakenLine(b));
        Assert.Equal(1, off.HistoryReads);
        Assert.Equal(plain.HistoryReads, off.HistoryReads);
        Assert.Equal(plain.Broker.QueryCount, off.Broker.QueryCount);
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void BrakeOn_TheHistoryIsReadOnceForTheBrakeAndTheAccountChecks()
    {
        Run on = Evaluate(Fixed, 203700.0);

        Assert.NotNull(OrderOf(on).Brake);
        Assert.Equal(1, on.HistoryReads);
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void LadderOn_FivePercentBelowThePeak_TheLadderPercentUnchangedNoNote()
    {
        Run braked = Evaluate(Fixed with { LadderEnabled = true }, 199500.0);
        Run off = Evaluate(Fixed with { LadderEnabled = true, DrawdownBrakePercent = 0.0 }, 199500.0);

        PlannedOrder order = OrderOf(braked);
        Assert.Equal(88000.0, order.VolumeUnits);
        Assert.Equal(176.0, order.RiskMoney, 6);
        Assert.Null(order.Brake);
        Assert.Equal(OrderOf(off), order);
        Assert.Equal(off.HistoryReads, braked.HistoryReads);
        Assert.Equal(off.Broker.QueryCount, braked.Broker.QueryCount);
        Assert.DoesNotContain("brake=", TakenLine(order), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void Recovery_FivePercentBelowThePeak_TheRecoveryAmountUnchangedNoNote()
    {
        Run braked = Evaluate(Fixed with { SizingPlan = SizingPlan.Recovery }, 199500.0);
        Run off = Evaluate(Fixed with { SizingPlan = SizingPlan.Recovery, DrawdownBrakePercent = 0.0 }, 199500.0);
        Run fixedBraked = Evaluate(Fixed, 199500.0);

        PlannedOrder order = OrderOf(braked);
        Assert.Null(order.Brake);
        Assert.Equal(OrderOf(off), order);
        Assert.NotEqual(OrderOf(fixedBraked).VolumeUnits, order.VolumeUnits);
        Assert.Equal(off.HistoryReads, braked.HistoryReads);
        Assert.Equal(off.Broker.QueryCount, braked.Broker.QueryCount);
        Assert.DoesNotContain("brake=", TakenLine(order), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ every later check on the braked amount

    [Fact]
    [Trait("Req", "RSK-019")]
    public void SanityCap_FullRiskAboveTheCap_BrakedRiskBelowIt()
    {
        RiskParameters capped = Fixed with { SanityCapEnabled = true, SanityCapPercent = 0.5 };

        Run full = Evaluate(capped, 205000.0);
        Run braked = Evaluate(capped, 203700.0);

        Assert.Equal(SkipReason.RiskAboveSanityCap, SkipOf(full).Reason);
        Assert.Equal(381000.0, OrderOf(braked).VolumeUnits);
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void DailyBudget_DayLossBeyondTheBudget_StillRefusedWithTheBrakedAmount()
    {
        // The second trade closed today (09:00 UTC, after 00:00 CET): the day started at 210,000.
        ClosedTradeInfo[] history =
        {
            Closed(1, ExecutionData.At(5, 12, 0), 10000.0, 210000.0),
            Closed(2, ExecutionData.At(14, 9, 0), -6300.0, 203700.0),
        };

        SkipRecord braked = SkipOf(Evaluate(Fixed, 203700.0, history));
        SkipRecord off = SkipOf(Evaluate(Fixed with { DrawdownBrakePercent = 0.0 }, 203700.0, history));

        Assert.Equal(SkipReason.DailyBudgetExceeded, braked.Reason);
        Assert.Equal(SkipReason.DailyBudgetExceeded, off.Reason);
        Assert.Equal(6300.0 + 762.0, braked.Measured, 6);
        Assert.Equal(6300.0 + 1526.0, off.Measured, 6);
        Assert.Equal(4000.0, braked.Limit, 6);
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void KillSwitch_SameSnapshot_SameDecisionWithAndWithoutTheBrake()
    {
        ClosedTradeInfo[] history = PeakThenBalance(193000.0);
        var snapshot = new RiskSnapshot(ExecutionData.Now, ExecutionData.Account(193000.0, 193000.0, 193000.0), Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), history, RiskData.EpochStart, new[] { ExecutionData.EurUsd() });

        KillSwitchDecision braked = new RiskEngine(Fixed).KillSwitch(snapshot);
        KillSwitchDecision off = new RiskEngine(Fixed with { DrawdownBrakePercent = 0.0 }).KillSwitch(snapshot);

        Assert.True(braked.Halt);
        Assert.Equal(off.Halt, braked.Halt);
        Assert.Equal(off.Peak, braked.Peak);
        Assert.Equal(off.DrawdownMoney, braked.DrawdownMoney);
        Assert.Equal(off.LimitMoney, braked.LimitMoney);
        Assert.Equal(210000.0, braked.Peak);
    }

    // ------------------------------------------------------------------ broker data

    [Fact]
    [Trait("Req", "RSK-019")]
    public void BrokerHistoryThrows_SkippedAsBrokerDataInvalid_NoOrder()
    {
        Run run = Evaluate(Fixed, 203700.0, historyOverride: _ => throw new InvalidOperationException("The broker history could not be read."));

        SkipRecord skip = SkipOf(run);
        Assert.Equal(SkipReason.BrokerDataInvalid, skip.Reason);
        Assert.Equal("The broker history could not be read.", skip.Detail);
        Assert.Empty(run.Broker.Requests);
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void BrokerHistoryInvalid_SkippedAsBrokerDataInvalid_NoOrder()
    {
        ClosedTradeInfo[] history = { Closed(1, ExecutionData.At(5, 12, 0), double.NaN, 210000.0) };

        Run run = Evaluate(Fixed, 203700.0, history);

        Assert.Equal(SkipReason.BrokerDataInvalid, SkipOf(run).Reason);
        Assert.Empty(run.Broker.Requests);
    }

    // ------------------------------------------------------------------ the journal note

    [Fact]
    [Trait("Req", "RSK-019")]
    public void Journal_BrakedTrade_TheTakeLineEndsWithTheNote()
    {
        PlannedOrder order = OrderOf(Evaluate(Fixed, 203700.0));

        const string expected = "2026-01-14T10:00:00Z signal symbol=EURUSD result=taken bias=Long zone=D-20260114-0400 zoneProximal=1.099 zoneDistal=1.098"
            + " fvgUpper=1.1005 fvgLower=1.0995 entry=1.1 stop=1.098 target=1.104 stopPips=20.0 volume=381000 spreadPips=1.0 costToRisk=0.050 news=Available skip=none"
            + " brake=on peak=210000 balance=203700 riskPercent=0.375";
        Assert.Equal(expected, TakenLine(order));
        Assert.Equal("e=1.10000;s=1.09800;t=1.10400;r=762.00;h=f681dff2", order.Comment);
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void Journal_NotBrakedTrade_TheTakeLineOfBeforeThisWorkPackage()
    {
        PlannedOrder order = OrderOf(Evaluate(Fixed, 205000.0));

        const string expected = "2026-01-14T10:00:00Z signal symbol=EURUSD result=taken bias=Long zone=D-20260114-0400 zoneProximal=1.099 zoneDistal=1.098"
            + " fvgUpper=1.1005 fvgLower=1.0995 entry=1.1 stop=1.098 target=1.104 stopPips=20.0 volume=768000 spreadPips=1.0 costToRisk=0.050 news=Available skip=none";
        Assert.Equal(expected, TakenLine(order));
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void Journal_SkippedSignal_NoNote()
    {
        Run run = Evaluate(Fixed with { SanityCapEnabled = true, SanityCapPercent = 0.1 }, 203700.0);
        SkipRecord skip = SkipOf(run);

        var sink = new RecordingSink();
        string line = OpsData.NewJournal(sink).Signal(SignalJournalEntry.Skipped(ExecutionData.Buy(), ExecutionData.Symbol, Bias.Long, null, skip, ExecutionData.EurUsd(), NewsStatus.Available));

        Assert.DoesNotContain("brake=", line, StringComparison.Ordinal);
    }

    /// <summary>One EURUSD instance's filters on a FakeBroker, with a counted (or replaced) history read.</summary>
    private sealed class Run
    {
        public Run(RiskParameters risk, double balance, IEnumerable<ClosedTradeInfo> history, Func<DateTime, IReadOnlyList<ClosedTradeInfo>>? historyOverride)
        {
            Broker = ExecutionHarness.DefaultBroker(account: ExecutionData.Account(balance, balance, balance));
            foreach (ClosedTradeInfo trade in history)
            {
                Broker.AddClosedTrade(trade);
            }

            var port = new SwitchableBroker(Broker)
            {
                History = since =>
                {
                    HistoryReads++;
                    return historyOverride != null ? historyOverride(since) : Broker.GetHistory(since);
                },
            };
            var news = new NewsService(new NewsParameters(), new[] { NewsTestData.ForexFactoryFeed() }, new MemoryStore(), new FakeTransport(NewsTestData.Failed()), true, ExecutionData.QuietNews);
            Filters = new PreTradeFilters(ExecutionData.ExecutionDefaults, ExecutionData.SignalDefaults, risk, port, news, Events);
        }

        public FakeBroker Broker { get; }

        public RecordingEvents Events { get; } = new();

        public PreTradeFilters Filters { get; }

        public int HistoryReads { get; private set; }

        public PreTradeResult? Result { get; set; }
    }
}
