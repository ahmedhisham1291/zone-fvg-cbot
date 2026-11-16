using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Recovery;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The account guards read from the broker port: kill switch (RSK-008), daily budget (RSK-006)
/// and the emergency guard on bad broker data (architecture section 10). Now is Wednesday
/// 2026-03-04 10:00 UTC (11:00 CET); the CE(S)T day began 2026-03-03 23:00 UTC; RiskEpochDate is
/// 2026-01-01.
/// </summary>
public class AccountGuardTests
{
    private static readonly DateTime Now = RecoveryData.At(4, 10, 0);

    private static ClosedTradeInfo Closed(DateTime closeUtc, double net, double balanceAfter, string label = RecoveryData.Label)
    {
        return new ClosedTradeInfo(9001, RecoveryData.Symbol, label, closeUtc.AddHours(-2.0), closeUtc, net, 0.0, 0.0, balanceAfter);
    }

    private static AccountGuard Guard(IBroker broker)
    {
        return new AccountGuard(RecoveryData.Symbol, RiskData.Parameters, broker);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void Evaluate_CleanAccount_AllowsEntries()
    {
        FakeBroker broker = BotHarness.NewBroker(Now);

        GuardStatus status = Guard(broker).Evaluate(Now);

        Assert.True(status.EntriesAllowed);
        Assert.False(status.EmergencyFires);
        Assert.False(status.KillSwitchFires);
        Assert.False(status.BudgetExhausted);
        Assert.Null(status.DataProblem);
        Assert.Equal(Now, status.AtUtc);
        Assert.Equal(100000.0, status.KillSwitch!.Peak);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void Evaluate_DrawdownOfExactly8Percent_FiresTheKillSwitch()
    {
        // A loss of 8,000 on Monday: balance and equity 92,000. Balance at the epoch = 92,000 + 8,000 = 100,000 = peak.
        // (100,000 - 92,000) / 100,000 = 8%: halt. M = 92,000, daily floor 88,000; overall floor 91,000: no emergency.
        FakeBroker broker = BotHarness.NewBroker(Now, ExecutionData.Account(92000.0, 92000.0, 92000.0));
        broker.AddClosedTrade(Closed(RecoveryData.At(2, 15, 0), -8000.0, 92000.0));

        GuardStatus status = Guard(broker).Evaluate(Now);

        Assert.True(status.KillSwitchFires);
        Assert.False(status.EmergencyFires);
        Assert.Equal(8000.0, status.KillSwitch!.DrawdownMoney);
        Assert.Equal(8000.0, status.KillSwitch.LimitMoney);
        Assert.True(status.EntriesAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void Evaluate_DrawdownOneCentBelow8Percent_DoesNotFire()
    {
        FakeBroker broker = BotHarness.NewBroker(Now, ExecutionData.Account(92000.0, 92000.01, 92000.0));
        broker.AddClosedTrade(Closed(RecoveryData.At(2, 15, 0), -8000.0, 92000.0));

        Assert.False(Guard(broker).Evaluate(Now).KillSwitchFires);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void Evaluate_SameBrokerDataInANewGuard_GivesTheSameDecision()
    {
        // Recomputing after a restart yields the same decision (RSK-008): nothing is remembered.
        FakeBroker broker = BotHarness.NewBroker(Now, ExecutionData.Account(92000.0, 92000.0, 92000.0));
        broker.AddClosedTrade(Closed(RecoveryData.At(2, 15, 0), -8000.0, 92000.0));

        GuardStatus first = Guard(broker).Evaluate(Now);
        GuardStatus second = Guard(broker).Evaluate(Now);

        Assert.Equal(first.Text, second.Text);
        Assert.True(second.KillSwitchFires);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void Evaluate_WorstCaseLossBeyondTheBudget_IsExhausted()
    {
        // Lost 1,000 today: balance 99,000, M = 100,000. A bot position risks 600 at its stop (60,000 x 100 pips x 0.0001).
        // W = 99,000 - 600 = 98,400; M - W = 1,600 > 1,500.
        FakeBroker broker = BotHarness.NewBroker(Now, ExecutionData.Account(99000.0, 99000.0, 99000.0));
        broker.AddClosedTrade(Closed(RecoveryData.At(4, 8, 0), -1000.0, 99000.0));
        broker.AddPosition(RecoveryData.Symbol, Side.Buy, 60000.0, 100.0, 99.0, 102.0, RecoveryData.Label, RecoveryData.Comment());

        GuardStatus status = Guard(broker).Evaluate(Now);

        Assert.True(status.BudgetExhausted);
        Assert.Equal(1600.0, status.Budget!.Used, 6);
        Assert.Equal(1500.0, status.Budget.Limit);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void Evaluate_WorstCaseLossExactlyAtTheBudget_IsNotExhausted()
    {
        // Lost 900 today; the position risks 600: M - W = 1,500, exactly the budget.
        FakeBroker broker = BotHarness.NewBroker(Now, ExecutionData.Account(99100.0, 99100.0, 99100.0));
        broker.AddClosedTrade(Closed(RecoveryData.At(4, 8, 0), -900.0, 99100.0));
        broker.AddPosition(RecoveryData.Symbol, Side.Buy, 60000.0, 100.0, 99.0, 102.0, RecoveryData.Label, RecoveryData.Comment());

        Assert.False(Guard(broker).Evaluate(Now).BudgetExhausted);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void Evaluate_HistoryThatCannotBeRead_RunsTheGuardFromEquityAndRefusesEntries()
    {
        // Equity 90,500 is at or below the static floor 91,000: CloseAllAndHalt even without history.
        FakeBroker inner = BotHarness.NewBroker(Now, ExecutionData.Account(95000.0, 90500.0, 90000.0));
        var broker = new SwitchableBroker(inner) { History = _ => throw new InvalidOperationException("history unavailable") };

        GuardStatus status = Guard(broker).Evaluate(Now);

        Assert.True(status.EmergencyFires);
        Assert.False(status.Emergency!.DayStartBalanceKnown);
        Assert.Null(status.KillSwitch);
        Assert.Null(status.Budget);
        Assert.False(status.EntriesAllowed);
        Assert.Equal("InvalidOperationException: history unavailable", status.DataProblem);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void Evaluate_HistoryWithAValueThatIsNotFinite_RefusesEntriesButKeepsTheGuard()
    {
        FakeBroker inner = BotHarness.NewBroker(Now, ExecutionData.Account(100000.0, 99000.0, 99000.0));
        var broken = new ClosedTradeInfo(1, RecoveryData.Symbol, RecoveryData.Label, Now.AddHours(-3.0), Now.AddHours(-2.0), double.NaN, 0.0, 0.0, 100000.0);
        var broker = new SwitchableBroker(inner) { History = _ => new[] { broken } };

        GuardStatus status = Guard(broker).Evaluate(Now);

        Assert.False(status.EmergencyFires);
        Assert.NotNull(status.Emergency);
        Assert.False(status.EntriesAllowed);
        Assert.StartsWith("ArgumentException: ", status.DataProblem, StringComparison.Ordinal);
        Assert.Contains("kill-switch=unknown", status.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void Evaluate_EquityThatIsNotFinite_HasNoGuardAndRefusesEntries()
    {
        FakeBroker broker = BotHarness.NewBroker(Now, ExecutionData.Account(100000.0, double.NaN, 100000.0));

        GuardStatus status = Guard(broker).Evaluate(Now);

        Assert.Null(status.Emergency);
        Assert.False(status.EmergencyFires);
        Assert.False(status.EntriesAllowed);
        Assert.Contains("emergency=unknown", status.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void Evaluate_BotOrderOnASymbolWithoutData_RefusesEntries()
    {
        FakeBroker broker = BotHarness.NewBroker(Now);
        broker.AddOrder("GBPUSD", Side.Buy, 1000.0, 1.25, 1.24, 1.27, Now.AddHours(1.0), "ZFVG|v1|GBPUSD|D-1", "x");

        GuardStatus status = Guard(broker).Evaluate(Now);

        Assert.False(status.EntriesAllowed);
        Assert.StartsWith("ArgumentException: Unknown symbol GBPUSD", status.DataProblem, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void Evaluate_ProgrammingError_IsThrownToTheEventBoundary()
    {
        var broker = new SwitchableBroker(BotHarness.NewBroker(Now)) { PositionsFault = new NullReferenceException("bug") };

        Assert.Throws<NullReferenceException>(() => Guard(broker).Evaluate(Now));
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void HistoryStartUtc_IsTheEarlierOfTheEpochAndTheDayStart()
    {
        // Epoch 2026-01-01 00:00 CET = 2025-12-31 23:00 UTC, earlier than the day start 2026-03-03 23:00 UTC.
        Assert.Equal(new DateTime(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc), AccountGuard.HistoryStartUtc(RiskData.Parameters, Now));

        // With an epoch of today, the day start is the earlier (they are equal).
        RiskParameters today = RiskData.Parameters with { RiskEpochDate = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc) };
        Assert.Equal(new DateTime(2026, 3, 3, 23, 0, 0, DateTimeKind.Utc), AccountGuard.HistoryStartUtc(today, Now));
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void Constructor_InvalidArguments_Throw()
    {
        FakeBroker broker = BotHarness.NewBroker(Now);

        Assert.Throws<ArgumentException>(() => new AccountGuard(string.Empty, RiskData.Parameters, broker));
        Assert.Throws<ArgumentNullException>(() => new AccountGuard(RecoveryData.Symbol, null!, broker));
        Assert.Throws<ArgumentNullException>(() => new AccountGuard(RecoveryData.Symbol, RiskData.Parameters, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AccountGuard(RecoveryData.Symbol, new RiskParameters(), broker));
    }
}
