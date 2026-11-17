using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-014: the bot refuses to start when 00:00 CE(S)T of RiskEpochDate is after the server time,
/// or, in a backtest, when the balance differs from InitialCapital (100,000) by more than 0.01.
/// Server time Wednesday 2026-03-04 10:00 UTC; CET is UTC+1 in winter, CEST UTC+2 in summer.
/// </summary>
public class StartChecksTests
{
    private static readonly DateTime Now = new(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc);

    private static BotParameters WithEpoch(int year, int month, int day)
    {
        return RecoveryData.Parameters() with { Risk = RiskData.Parameters with { RiskEpochDate = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc) } };
    }

    private static AccountSnapshot Balance(double balance)
    {
        return ExecutionData.Account(balance, balance, balance);
    }

    [Fact]
    [Trait("Req", "RSK-014")]
    public void Check_EpochStartBeforeTheServerTime_Accepted()
    {
        // 2026-03-04 starts at 2026-03-03 23:00 UTC.
        Assert.Null(StartChecks.Check(WithEpoch(2026, 3, 4), Balance(100000.0), Now, false));
    }

    [Fact]
    [Trait("Req", "RSK-014")]
    public void Check_EpochStartAfterTheServerTime_RefusedNamingBothValues()
    {
        Assert.Equal(
            "RiskEpochDate 2026-03-05 starts at 2026-03-04 23:00 UTC, after the server time 2026-03-04 10:00 UTC",
            StartChecks.Check(WithEpoch(2026, 3, 5), Balance(100000.0), Now, false));
        Assert.NotNull(StartChecks.Check(WithEpoch(2026, 3, 5), Balance(100000.0), Now, true));
    }

    [Fact]
    [Trait("Req", "RSK-014")]
    public void Check_EpochStartExactlyAtTheServerTime_AcceptedAndOneTickEarlierRefused()
    {
        DateTime start = new(2026, 3, 4, 23, 0, 0, DateTimeKind.Utc);

        Assert.Null(StartChecks.Check(WithEpoch(2026, 3, 5), Balance(100000.0), start, false));
        Assert.Equal(
            "RiskEpochDate 2026-03-05 starts at 2026-03-04 23:00 UTC, after the server time 2026-03-04 22:59 UTC",
            StartChecks.Check(WithEpoch(2026, 3, 5), Balance(100000.0), start.AddTicks(-1), false));
    }

    [Fact]
    [Trait("Req", "RSK-014")]
    public void Check_SummerEpoch_StartsAt2200Utc()
    {
        DateTime start = new(2026, 6, 30, 22, 0, 0, DateTimeKind.Utc);

        Assert.Null(StartChecks.Check(WithEpoch(2026, 7, 1), Balance(100000.0), start, false));
        Assert.Equal(
            "RiskEpochDate 2026-07-01 starts at 2026-06-30 22:00 UTC, after the server time 2026-06-30 21:59 UTC",
            StartChecks.Check(WithEpoch(2026, 7, 1), Balance(100000.0), start.AddMinutes(-1.0), false));
    }

    [Theory]
    [Trait("Req", "RSK-014")]
    [InlineData(100000.0)]
    [InlineData(100000.01)]
    [InlineData(99999.99)]
    public void Check_BacktestBalanceWithinOneCent_Accepted(double balance)
    {
        Assert.Null(StartChecks.Check(WithEpoch(2026, 3, 4), Balance(balance), Now, true));
    }

    [Theory]
    [Trait("Req", "RSK-014")]
    [InlineData(100000.02, "100000.02")]
    [InlineData(99999.98, "99999.98")]
    [InlineData(10000.0, "10000.00")]
    public void Check_BacktestBalanceFurtherThanOneCent_RefusedNamingBothValues(double balance, string shown)
    {
        Assert.Equal(
            "backtest starting balance " + shown + " differs from InitialCapital 100000.00; set both to the same value",
            StartChecks.Check(WithEpoch(2026, 3, 4), Balance(balance), Now, true));
    }

    [Theory]
    [Trait("Req", "RSK-014")]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e20)]
    public void Check_BacktestBalanceNotComparable_Refused(double balance)
    {
        Assert.StartsWith("backtest starting balance ", StartChecks.Check(WithEpoch(2026, 3, 4), Balance(balance), Now, true), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-014")]
    public void Check_LiveBalanceDifferentFromInitialCapital_Accepted()
    {
        Assert.Null(StartChecks.Check(WithEpoch(2026, 3, 4), Balance(10000.0), Now, false));
    }

    [Fact]
    [Trait("Req", "RSK-014")]
    public void Check_NoAccountOrServerTimeNotUtc_Refused()
    {
        Assert.Equal("the account could not be read", StartChecks.Check(WithEpoch(2026, 3, 4), null, Now, false));
        Assert.Equal(
            "the server time 2026-03-04 10:00 is not a UTC time",
            StartChecks.Check(WithEpoch(2026, 3, 4), Balance(100000.0), DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), false));
    }

    [Fact]
    [Trait("Req", "RSK-014")]
    public void Check_FromTheBroker_ReadsTimeAndAccount()
    {
        var broker = new SwitchableBroker(BotHarness.NewBroker(Now, Balance(10000.0)));

        Assert.Null(StartChecks.Check(WithEpoch(2026, 3, 4), broker, false));
        Assert.Equal(
            "backtest starting balance 10000.00 differs from InitialCapital 100000.00; set both to the same value",
            StartChecks.Check(WithEpoch(2026, 3, 4), broker, true));
    }

    [Fact]
    [Trait("Req", "RSK-014")]
    public void Check_AccountReadThrows_RefusedWithTheMessage()
    {
        var broker = new ThrowingAccountBroker(BotHarness.NewBroker(Now));

        Assert.Equal(
            "the account or the server time could not be read: InvalidOperationException: account unavailable",
            StartChecks.Check(WithEpoch(2026, 3, 4), broker, false));
    }

    [Fact]
    [Trait("Req", "RSK-014")]
    public void Check_ServerTimeThrows_Refused()
    {
        var broker = new SwitchableBroker(BotHarness.NewBroker(Now)) { TimeFault = new InvalidOperationException("no server time") };

        Assert.Equal(
            "the account or the server time could not be read: InvalidOperationException: no server time",
            StartChecks.Check(WithEpoch(2026, 3, 4), broker, true));
    }

    [Fact]
    [Trait("Req", "RSK-014")]
    public void Check_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => StartChecks.Check(null!, Balance(100000.0), Now, false));
        Assert.Throws<ArgumentNullException>(() => StartChecks.Check(null!, BotHarness.NewBroker(Now), false));
        Assert.Throws<ArgumentNullException>(() => StartChecks.Check(WithEpoch(2026, 3, 4), (IBroker)null!, false));
    }

    /// <summary>A broker whose account cannot be read.</summary>
    private sealed class ThrowingAccountBroker : IBroker
    {
        private readonly FakeBroker _inner;

        public ThrowingAccountBroker(FakeBroker inner)
        {
            _inner = inner;
        }

        public DateTime ServerTimeUtc => _inner.ServerTimeUtc;

        public bool IsConnected => true;

        public AccountSnapshot GetAccount() => throw new InvalidOperationException("account unavailable");

        public SymbolInfo GetSymbol(string symbol) => _inner.GetSymbol(symbol);

        public IReadOnlyList<PositionInfo> GetPositions() => _inner.GetPositions();

        public IReadOnlyList<PendingOrderInfo> GetPendingOrders() => _inner.GetPendingOrders();

        public IReadOnlyList<ClosedTradeInfo> GetHistory(DateTime sinceUtc) => _inner.GetHistory(sinceUtc);

        public double EstimateMargin(string symbol, Side side, double volumeUnits) => _inner.EstimateMargin(symbol, side, volumeUnits);

        public OrderResult PlaceLimitOrder(LimitOrderRequest request) => _inner.PlaceLimitOrder(request);

        public OrderResult CancelOrder(long orderId) => _inner.CancelOrder(orderId);

        public OrderResult ModifyPositionProtection(long positionId, double? stopLoss, double? takeProfit) => _inner.ModifyPositionProtection(positionId, stopLoss, takeProfit);

        public OrderResult ClosePosition(long positionId) => _inner.ClosePosition(positionId);
    }
}
