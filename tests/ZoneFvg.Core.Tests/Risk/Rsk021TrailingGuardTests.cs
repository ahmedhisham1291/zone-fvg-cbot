using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Recovery;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-021: the FTMO emergency guard on a trailing floor (spec v0.20 rows BH and BI, section 15.8).
/// With <see cref="FtmoMaxLossMode.TrailingBalance"/> the overall condition is equity at or below
/// P - (FtmoMaxLossPercent - FtmoGuardBufferPercent) / 100 x InitialCapital, with P the larger of
/// InitialCapital and the kill switch's peak balance since RiskEpochDate; when P cannot be computed
/// the static floor applies. The daily condition is unchanged.
/// </summary>
/// <remarks>
/// <para>Account of FTMO Instant: InitialCapital 100,000, FtmoDailyLossPercent 3, FtmoMaxLossPercent
/// 10, FtmoGuardBufferPercent 0.5, so the overall floor is P - 9,500 and the daily floor M - 2,500;
/// the static floor is 90,500. Every other value is that of <see cref="RiskData.Parameters"/>.</para>
/// <para>Server time <see cref="RiskData.Now"/> (Wednesday 2026-01-14 10:00 UTC); the CE(S)T day
/// began 2026-01-13 23:00 UTC; RiskEpochDate 1 January 2026 (2025-12-31 23:00 UTC). History from
/// the epoch start unless a test says otherwise.</para>
/// </remarks>
public class Rsk021TrailingGuardTests
{
    private const string Manual = "manual";

    private static RiskParameters Trailing => RiskData.Parameters with
    {
        FtmoMaxLossMode = FtmoMaxLossMode.TrailingBalance,
        FtmoDailyLossPercent = 3.0,
        FtmoMaxLossPercent = 10.0,
        FtmoGuardBufferPercent = 0.5,
    };

    private static RiskParameters StaticInstant => Trailing with { FtmoMaxLossMode = FtmoMaxLossMode.Static };

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    private static ClosedTradeInfo Closed(long id, DateTime closeUtc, double net, double balanceAfter)
    {
        return RiskData.Closed(id, "EURUSD", closeUtc.AddHours(-1.0), closeUtc, net, balanceAfter, Manual);
    }

    /// <summary>+6,000 on 5 January (peak 106,000), -8,000 on 12 January: balance 98,000, M = 98,000.</summary>
    private static ClosedTradeInfo[] PeakThenLoss => new[]
    {
        Closed(1, Utc(2026, 1, 5, 12), 6000.0, 106000.0),
        Closed(2, Utc(2026, 1, 12, 12), -8000.0, 98000.0),
    };

    private static RiskSnapshot Snapshot(double balance, double equity, IEnumerable<ClosedTradeInfo>? history = null)
    {
        return RiskData.Snapshot(RiskData.Account(balance, equity, equity), history: history);
    }

    private static EmergencyDecision Guard(RiskParameters parameters, RiskSnapshot snapshot)
    {
        return new RiskEngine(parameters).EmergencyGuard(snapshot);
    }

    // ------------------------------------------------------------------ the trailing floor

    [Theory]
    [Trait("Req", "RSK-021")]
    // No closed trade: P = max(100,000, balance at the epoch 92,000) = 100,000, floor 90,500. M = 92,000, so the daily
    // floor is 89,500 and only the overall condition can act. Exactly at the floor: act; one cent above: nothing.
    [InlineData(90500.0, EmergencyAction.CloseAllAndHalt)]
    [InlineData(90500.01, EmergencyAction.None)]
    public void EmergencyGuard_TrailingNoTrades_FloorIs90500(double equity, EmergencyAction expected)
    {
        EmergencyDecision decision = Guard(Trailing, Snapshot(92000.0, equity));

        Assert.Equal(expected, decision.Action);
        Assert.Equal(expected == EmergencyAction.CloseAllAndHalt, decision.OverallFloorReached);
        Assert.False(decision.DailyFloorReached);
        Assert.Equal(90500.0, decision.OverallFloor, 1e-6);
        Assert.Equal(89500.0, decision.DailyFloor, 1e-6);
        Assert.Equal(100000.0, decision.Peak);
        Assert.True(decision.TrailingFloorUsed);
        Assert.Equal(FtmoMaxLossMode.TrailingBalance, decision.MaxLossMode);
    }

    [Theory]
    [Trait("Req", "RSK-021")]
    // Peak 106,000 after closed trades: floor 96,500. M = 98,000, daily floor 95,500, below the overall floor.
    [InlineData(96500.0, EmergencyAction.CloseAllAndHalt)]
    [InlineData(96500.01, EmergencyAction.None)]
    public void EmergencyGuard_TrailingPeak106000_FloorIs96500(double equity, EmergencyAction expected)
    {
        EmergencyDecision decision = Guard(Trailing, Snapshot(98000.0, equity, PeakThenLoss));

        Assert.Equal(expected, decision.Action);
        Assert.Equal(expected == EmergencyAction.CloseAllAndHalt, decision.OverallFloorReached);
        Assert.False(decision.DailyFloorReached);
        Assert.Equal(96500.0, decision.OverallFloor, 1e-6);
        Assert.Equal(95500.0, decision.DailyFloor, 1e-6);
        Assert.Equal(106000.0, decision.Peak);
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    public void EmergencyGuard_TrailingPeakOnlyBeforeTheEpoch_DoesNotCount()
    {
        // History from 1 December: 110,000 on 20 December, back to 100,000 on 28 December, both before the epoch start;
        // no trade since, balance 92,000 now. Only trades since the epoch count: P = 100,000 and the floor 90,500, not
        // 100,500. M = 92,000, daily floor 89,500.
        ClosedTradeInfo[] history =
        {
            Closed(1, Utc(2025, 12, 20, 12), 10000.0, 110000.0),
            Closed(2, Utc(2025, 12, 28, 12), -10000.0, 100000.0),
        };
        RiskSnapshot snapshot = new(RiskData.Now, RiskData.Account(92000.0, 90500.01, 90500.01), Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), history, Utc(2025, 12, 1, 0), RiskData.AllSymbols);

        EmergencyDecision decision = Guard(Trailing, snapshot);

        Assert.Equal(100000.0, decision.Peak);
        Assert.Equal(90500.0, decision.OverallFloor, 1e-6);
        Assert.Equal(EmergencyAction.None, decision.Action);
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    public void EmergencyGuard_TrailingPeakBelowInitialCapital_FloorFromInitialCapital()
    {
        // Balance 95,000 at the epoch, +2,000 on 5 January: the kill switch's peak is 97,000, P = 100,000, floor 90,500.
        ClosedTradeInfo[] history = { Closed(1, Utc(2026, 1, 5, 12), 2000.0, 97000.0) };
        RiskSnapshot snapshot = Snapshot(97000.0, 90500.0, history);

        EmergencyDecision decision = Guard(Trailing, snapshot);

        Assert.Equal(97000.0, new RiskEngine(Trailing).KillSwitch(snapshot).Peak);
        Assert.Equal(100000.0, decision.Peak);
        Assert.Equal(90500.0, decision.OverallFloor, 1e-6);
        Assert.Equal(EmergencyAction.CloseAllAndHalt, decision.Action);
        Assert.True(decision.OverallFloorReached);
    }

    [Theory]
    [Trait("Req", "RSK-021")]
    // +6,000 to 106,000 on 5 January, then a withdrawal of 9,500 with no closed trade: balance 96,500. P stays 106,000,
    // so the floor stays 96,500 and equity at the balance is at the floor. M = 96,500, daily floor 94,000.
    [InlineData(96500.0, EmergencyAction.CloseAllAndHalt)]
    [InlineData(96500.01, EmergencyAction.None)]
    public void EmergencyGuard_TrailingAfterAWithdrawal_KeepsThePeak(double equity, EmergencyAction expected)
    {
        ClosedTradeInfo[] history = { Closed(1, Utc(2026, 1, 5, 12), 6000.0, 106000.0) };

        EmergencyDecision decision = Guard(Trailing, Snapshot(96500.0, equity, history));

        Assert.Equal(106000.0, decision.Peak);
        Assert.Equal(96500.0, decision.OverallFloor, 1e-6);
        Assert.Equal(94000.0, decision.DailyFloor, 1e-6);
        Assert.Equal(expected, decision.Action);
    }

    [Theory]
    [Trait("Req", "RSK-021")]
    // Daily condition in trailing mode: M = 100,000, M - (3 - 0.5)% x 100,000 = 97,500. Overall floor 90,500.
    [InlineData(97500.0, EmergencyAction.CloseAllAndHalt)]
    [InlineData(97500.01, EmergencyAction.None)]
    public void EmergencyGuard_TrailingDailyCondition_IsMMinus2500(double equity, EmergencyAction expected)
    {
        EmergencyDecision decision = Guard(Trailing, Snapshot(100000.0, equity));

        Assert.Equal(97500.0, decision.DailyFloor, 1e-6);
        Assert.Equal(expected, decision.Action);
        Assert.Equal(expected == EmergencyAction.CloseAllAndHalt, decision.DailyFloorReached);
        Assert.False(decision.OverallFloorReached);
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    public void EmergencyGuard_TrailingFloorIsNeverBelowTheStaticFloor()
    {
        foreach (ClosedTradeInfo[] history in new[] { Array.Empty<ClosedTradeInfo>(), PeakThenLoss, new[] { Closed(1, Utc(2026, 1, 5, 12), -5000.0, 95000.0) } })
        {
            double balance = history.Length == 0 ? 100000.0 : history[^1].BalanceAfterClose;
            RiskSnapshot snapshot = Snapshot(balance, balance, history);

            Assert.True(Guard(Trailing, snapshot).OverallFloor >= Guard(StaticInstant, snapshot).OverallFloor);
        }
    }

    // ------------------------------------------------------------------ P not computable: the static floor

    [Theory]
    [Trait("Req", "RSK-021")]
    [InlineData(90500.0, EmergencyAction.CloseAllAndHalt)]
    [InlineData(90500.01, EmergencyAction.None)]
    public void EmergencyGuard_TrailingHistoryIncomplete_StaticFloor90500(double equity, EmergencyAction expected)
    {
        // The history starts at the server time: neither M nor P can be computed, although the trades would give 106,000.
        RiskSnapshot snapshot = RiskData.SnapshotAt(RiskData.Now, RiskData.Now, RiskData.Account(98000.0, equity, equity), PeakThenLoss);

        EmergencyDecision decision = Guard(Trailing, snapshot);

        Assert.Equal(expected, decision.Action);
        Assert.Equal(90500.0, decision.OverallFloor, 1e-6);
        Assert.True(double.IsNaN(decision.Peak));
        Assert.False(decision.TrailingFloorUsed);
        Assert.False(decision.DayStartBalanceKnown);
        Assert.Equal(FtmoMaxLossMode.TrailingBalance, decision.MaxLossMode);
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    public void EmergencyGuard_TrailingPeakNotPositive_StaticFloor()
    {
        // Broken history: the balance at the epoch is 1,000 - 5,000 = -4,000 and the only balance after a close is -1,
        // so the peak is not positive and P is not computable. The static floor 90,500 applies (fail closed).
        ClosedTradeInfo[] history = { Closed(1, Utc(2026, 1, 5, 12), 5000.0, -1.0) };

        EmergencyDecision decision = Guard(Trailing, Snapshot(1000.0, 1000.0, history));

        Assert.True(double.IsNaN(decision.Peak));
        Assert.False(decision.TrailingFloorUsed);
        Assert.Equal(90500.0, decision.OverallFloor, 1e-6);
        Assert.Equal(EmergencyAction.CloseAllAndHalt, decision.Action);
    }

    [Theory]
    [Trait("Req", "RSK-021")]
    [InlineData(90500.0, EmergencyAction.CloseAllAndHalt)]
    [InlineData(90500.01, EmergencyAction.None)]
    public void EmergencyGuardFromEquity_Trailing_StaticFloor90500(double equity, EmergencyAction expected)
    {
        EmergencyDecision decision = new RiskEngine(Trailing).EmergencyGuardFromEquity(equity);

        Assert.Equal(expected, decision.Action);
        Assert.Equal(90500.0, decision.OverallFloor, 1e-6);
        Assert.True(double.IsNaN(decision.Peak));
        Assert.False(decision.TrailingFloorUsed);
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    public void AccountGuard_TrailingHistoryReadFails_StaticFloorAndTheTextSaysSo()
    {
        FakeBroker inner = BotHarness.NewBroker(RiskData.Now, ExecutionData.Account(98000.0, 90500.0, 90500.0));
        var broker = new SwitchableBroker(inner) { History = _ => throw new InvalidOperationException("history unavailable") };

        GuardStatus status = new AccountGuard(RecoveryData.Symbol, Trailing, broker).Evaluate(RiskData.Now);

        Assert.True(status.EmergencyFires);
        Assert.Equal(90500.0, status.Emergency!.OverallFloor, 1e-6);
        Assert.False(status.Emergency.TrailingFloorUsed);
        Assert.StartsWith("emergency=CLOSE-ALL equity 90500 daily floor unknown overall floor 90500 (trailing, peak unknown: static floor); kill-switch=unknown", status.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    public void AccountGuard_TrailingHistoryNull_StaticFloor()
    {
        FakeBroker inner = BotHarness.NewBroker(RiskData.Now, ExecutionData.Account(98000.0, 95000.0, 95000.0));
        var broker = new SwitchableBroker(inner) { History = _ => null! };

        GuardStatus status = new AccountGuard(RecoveryData.Symbol, Trailing, broker).Evaluate(RiskData.Now);

        Assert.False(status.EmergencyFires);
        Assert.Equal(90500.0, status.Emergency!.OverallFloor, 1e-6);
        Assert.False(status.EntriesAllowed);
    }

    // ------------------------------------------------------------------ the journal text of a decision

    [Fact]
    [Trait("Req", "RSK-021")]
    public void GuardText_Trailing_NamesThePeakAndTheFloor()
    {
        FakeBroker broker = BotHarness.NewBroker(RiskData.Now, ExecutionData.Account(98000.0, 96500.0, 96500.0));
        foreach (ClosedTradeInfo trade in PeakThenLoss)
        {
            broker.AddClosedTrade(trade);
        }

        GuardStatus status = new AccountGuard(RecoveryData.Symbol, Trailing, broker).Evaluate(RiskData.Now);

        Assert.True(status.EmergencyFires);
        Assert.StartsWith("emergency=CLOSE-ALL equity 96500 daily floor 95500 overall floor 96500 (trailing, peak 106000); kill-switch=HALT peak 106000 ", status.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    [Trait("Req", "RSK-009")]
    public void GuardText_Static_ByteForByteAsBefore()
    {
        // The text pinned by the REC-010 report test, with the mode written out and with the core default.
        const string Expected = "emergency=ok equity 100000 daily floor 96000 overall floor 91000; kill-switch=ok peak 100000 drawdown 0 limit 8000;"
            + " daily-budget=ok used 0 limit 1500; data=ok";
        FakeBroker broker = BotHarness.NewBroker(RiskData.Now);

        string explicitStatic = new AccountGuard(RecoveryData.Symbol, RiskData.Parameters with { FtmoMaxLossMode = FtmoMaxLossMode.Static }, broker).Evaluate(RiskData.Now).Text;
        string byDefault = new AccountGuard(RecoveryData.Symbol, RiskData.Parameters, broker).Evaluate(RiskData.Now).Text;

        Assert.Equal(Expected, explicitStatic);
        Assert.Equal(Expected, byDefault);
        Assert.Equal(FtmoMaxLossMode.Static, new RiskParameters().FtmoMaxLossMode);
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    [Trait("Req", "RSK-009")]
    public void EmergencyGuard_Static_SameDecisionsAsBefore()
    {
        // Static mode with the 2-Step values: every value of RSK-009 as before, and no peak.
        RiskSnapshot snapshot = Snapshot(98000.0, 96500.0, PeakThenLoss);
        EmergencyDecision byDefault = RiskData.Engine.EmergencyGuard(snapshot);
        EmergencyDecision explicitStatic = Guard(RiskData.Parameters with { FtmoMaxLossMode = FtmoMaxLossMode.Static }, snapshot);

        foreach (EmergencyDecision decision in new[] { byDefault, explicitStatic })
        {
            Assert.Equal(91000.0, decision.OverallFloor, 1e-6);
            Assert.Equal(94000.0, decision.DailyFloor, 1e-6);
            Assert.Equal(EmergencyAction.None, decision.Action);
            Assert.True(double.IsNaN(decision.Peak));
            Assert.False(decision.TrailingFloorUsed);
            Assert.Equal(FtmoMaxLossMode.Static, decision.MaxLossMode);
        }

        // With the Instant values in static mode the floor is the static 90,500, whatever the peak.
        Assert.Equal(90500.0, Guard(StaticInstant, snapshot).OverallFloor, 1e-6);
    }

    // ------------------------------------------------------------------ the kill switch and the brake are unchanged

    private static IEnumerable<(double Balance, double Equity, ClosedTradeInfo[] History)> Histories()
    {
        yield return (100000.0, 99000.0, Array.Empty<ClosedTradeInfo>());
        yield return (98000.0, 97000.0, PeakThenLoss);
        yield return (96500.0, 96500.0, new[] { Closed(1, Utc(2026, 1, 5, 12), 6000.0, 106000.0) });
        yield return (97000.0, 90000.0, new[] { Closed(1, Utc(2026, 1, 5, 12), 2000.0, 97000.0) });
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    [Trait("Req", "RSK-008")]
    [Trait("Req", "RSK-019")]
    public void KillSwitchAndBrake_TrailingMode_DecideExactlyAsStatic()
    {
        RiskEngine staticEngine = new(RiskData.Parameters);
        RiskEngine trailingEngine = new(Trailing);
        foreach ((double balance, double equity, ClosedTradeInfo[] history) in Histories())
        {
            RiskSnapshot snapshot = Snapshot(balance, equity, history);

            KillSwitchDecision before = staticEngine.KillSwitch(snapshot);
            KillSwitchDecision after = trailingEngine.KillSwitch(snapshot);

            Assert.Equal(before.Halt, after.Halt);
            Assert.Equal(before.Peak, after.Peak);
            Assert.Equal(before.DrawdownMoney, after.DrawdownMoney);
            Assert.Equal(before.LimitMoney, after.LimitMoney);
            Assert.Equal(staticEngine.PeakBalance(snapshot), trailingEngine.PeakBalance(snapshot));
            Assert.Equal(DrawdownBrake.IsBraked(staticEngine.PeakBalance(snapshot), balance, 3.0), DrawdownBrake.IsBraked(trailingEngine.PeakBalance(snapshot), balance, 3.0));
            Assert.Equal(staticEngine.DailyBudgetExhausted(snapshot).Used, trailingEngine.DailyBudgetExhausted(snapshot).Used);
        }
    }

    // ------------------------------------------------------------------ validation

    [Fact]
    [Trait("Req", "RSK-021")]
    public void Validate_UndefinedMode_RefusedNamingFtmoMaxLossMode()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => (Trailing with { FtmoMaxLossMode = (FtmoMaxLossMode)2 }).Validate());

        Assert.Equal("FtmoMaxLossMode", exception.ParamName);
        Assert.StartsWith("FtmoMaxLossMode must be Static or TrailingBalance but was 2", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "RSK-021")]
    [InlineData("trailingbalance")]
    [InlineData("static")]
    [InlineData("TrailingBalance ")]
    [InlineData("Trailing")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData(null)]
    public void FtmoMaxLossModeText_OtherText_RefusedNamingIt(string? text)
    {
        var exception = Assert.Throws<ArgumentException>(() => FtmoInstantText.MaxLossMode(text));

        Assert.Equal("FtmoMaxLossMode", exception.ParamName);
        Assert.StartsWith("FtmoMaxLossMode must be one of: Static, TrailingBalance.", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "RSK-021")]
    [InlineData("Static", FtmoMaxLossMode.Static)]
    [InlineData("TrailingBalance", FtmoMaxLossMode.TrailingBalance)]
    public void FtmoMaxLossModeText_TheTwoNames_AreRead(string text, FtmoMaxLossMode expected)
    {
        Assert.Equal(expected, FtmoInstantText.MaxLossMode(text));
    }

    [Theory]
    [Trait("Req", "RSK-021")]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(3.0)]
    [InlineData(3.01)]
    [InlineData(double.NaN)]
    public void Validate_TrailingBufferNotAbove0OrNotBelowTheDailyLimit_RefusedNamingIt(double buffer)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => (Trailing with { FtmoGuardBufferPercent = buffer }).Validate());

        Assert.Equal("FtmoGuardBufferPercent", exception.ParamName);
    }

    [Theory]
    [Trait("Req", "RSK-021")]
    [InlineData(0.5)]
    [InlineData(2.99)]
    public void Validate_TrailingBufferInRange_Accepted(double buffer)
    {
        Assert.Null(Record.Exception(() => (Trailing with { FtmoGuardBufferPercent = buffer }).Validate()));
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    public void Validate_TrailingBufferZero_TheMessageNamesTheRule()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => (Trailing with { FtmoGuardBufferPercent = 0.0 }).Validate());

        Assert.StartsWith("FtmoGuardBufferPercent must be greater than 0 and below FtmoDailyLossPercent with FtmoMaxLossMode TrailingBalance but was 0", exception.Message, StringComparison.Ordinal);
    }
}
