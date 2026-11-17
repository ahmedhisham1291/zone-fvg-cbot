using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-017: entry, stop and target are rounded half away from zero to a whole multiple of the
/// broker's tick size and written in the comment with the broker's digits; the read-back accepts
/// half a tick; an unusable grid skips the signal as BrokerDataInvalid before anything is sent.
/// </summary>
/// <remarks>
/// The gold-like grid (pip 0.01, digits 2, tick 0.01, pip value 0.01 USD per unit, volume step 1)
/// is set on the harness's EURUSD symbol record, because the harness trades one fixed symbol; only
/// the grid matters here. Buy: entry 2650.125 -> 2650.13, stop 2641.994 -> 2641.99, target
/// 2666.40; stop 8.14 = 814 pips; volume 500 / (814 x 0.01) = 61.4, rounded down to 61; risk
/// money 61 x 814 x 0.01 = 496.54. Spread 0.01 is 1 pip.
/// </remarks>
public class Exe017PriceGridScenarios
{
    private static SymbolInfo GoldGrid => ExecutionData.EurUsd(spread: 0.01) with
    {
        PipSize = 0.01,
        PipValuePerUnit = 0.01,
        VolumeMinUnits = 1.0,
        VolumeStepUnits = 1.0,
        Digits = 2,
        TickSize = 0.01,
    };

    private static Signal GoldBuy => ExecutionData.Buy(entry: 2650.125, stop: 2641.994, target: 2666.4, atrH4: 10.0, gapUpper: 2650.6, gapLower: 2649.6);

    [Theory]
    [Trait("Req", "EXE-017")]
    [InlineData(1.0834550, 0.00001, 1.08346)]
    [InlineData(1.0834549, 0.00001, 1.08345)]
    [InlineData(-1.0834550, 0.00001, -1.08346)]
    [InlineData(150.1235, 0.001, 150.124)]
    [InlineData(2650.125, 0.01, 2650.13)]
    [InlineData(2641.994, 0.01, 2641.99)]
    [InlineData(100.024, 0.05, 100.00)]
    [InlineData(100.025, 0.05, 100.05)]
    [InlineData(100.074, 0.05, 100.05)]
    [InlineData(1.1021000000000001, 0.00001, 1.1021)]
    public void RoundToTick_RoundsHalfAwayFromZeroToAWholeTick(double price, double tick, double expected)
    {
        Assert.Equal(expected, PriceGrid.RoundToTick(price, tick));
    }

    [Theory]
    [Trait("Req", "EXE-017")]
    [InlineData(double.NaN, 0.01)]
    [InlineData(double.PositiveInfinity, 0.01)]
    [InlineData(1e16, 0.01)]
    [InlineData(1.0, 0.0)]
    [InlineData(1.0, 1e-12)]
    [InlineData(1.0, double.NaN)]
    [InlineData(1.0, 1e16)]
    public void RoundToTick_OutOfRange_Throws(double price, double tick)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PriceGrid.RoundToTick(price, tick));
    }

    [Theory]
    [Trait("Req", "EXE-017")]
    [InlineData(5, 0.00001, true)]
    [InlineData(3, 0.001, true)]
    [InlineData(2, 0.01, true)]
    [InlineData(2, 0.05, true)]
    [InlineData(0, 1.0, true)]
    [InlineData(10, 1e-10, true)]
    [InlineData(-1, 0.00001, false)]
    [InlineData(11, 0.00001, false)]
    [InlineData(5, 0.0, false)]
    [InlineData(2, -0.01, false)]
    [InlineData(2, double.NaN, false)]
    [InlineData(2, double.PositiveInfinity, false)]
    [InlineData(2, 0.001, false)]
    [InlineData(2, 0.015, false)]
    public void IsUsableGrid_AcceptsOnlyDigitsZeroToTenAndAWholeMultipleOfTheStep(int digits, double tick, bool usable)
    {
        Assert.Equal(usable, PriceGrid.IsUsableGrid(digits, tick));
    }

    [Fact]
    [Trait("Req", "EXE-017")]
    public void GoldGrid_LevelsAreSentOnTheTickAndWrittenWithTheBrokersDigits()
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(GoldGrid));

        PlaceResult? result = h.EvaluateAndPlace(GoldBuy);

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        LimitOrderRequest request = Assert.Single(h.Broker.Requests).Order!;
        Assert.Equal(2650.13, request.EntryPrice);
        Assert.Equal(2641.99, request.StopLossPrice);
        Assert.Equal(2666.4, request.TakeProfitPrice);
        Assert.Equal(61.0, request.VolumeUnits);
        Assert.StartsWith("e=2650.13;s=2641.99;t=2666.40;r=496.54;h=", request.Comment, StringComparison.Ordinal);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-017")]
    public void ForexGrid_PlannedOrderCarriesTheBrokersDigitsAndTick()
    {
        var h = new ExecutionHarness();

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        Assert.Equal(5, order.PriceDigits);
        Assert.Equal(0.00001, order.TickSize);
    }

    [Theory]
    [Trait("Req", "EXE-017")]
    [InlineData(2641.99, PlaceOutcome.Placed)]
    [InlineData(2641.995, PlaceOutcome.Placed)]
    [InlineData(2641.985, PlaceOutcome.Placed)]
    [InlineData(2641.996, PlaceOutcome.MismatchCancelled)]
    [InlineData(2642.00, PlaceOutcome.MismatchCancelled)]
    public void GoldReadBack_AcceptsHalfATick(double brokerStop, PlaceOutcome expected)
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(GoldGrid));
        PlannedOrder order = h.OrderOf(GoldBuy);
        var orders = new OrderManager(new StopShiftingBroker(h.Broker, brokerStop), h.Counter, h.Events);

        PlaceResult result = orders.TryPlace(order);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(expected == PlaceOutcome.Placed ? 0 : 1, h.Events.AlertsOf(AlertKind.OrderMismatch));
        Assert.Equal(expected == PlaceOutcome.Placed ? 0 : 1, h.Broker.Count(BrokerCall.Cancel));
    }

    [Theory]
    [Trait("Req", "EXE-017")]
    [InlineData(-1, 0.00001)]
    [InlineData(11, 0.00001)]
    [InlineData(5, 0.0)]
    [InlineData(2, -0.01)]
    [InlineData(2, double.NaN)]
    [InlineData(2, double.PositiveInfinity)]
    [InlineData(2, 0.001)]
    public void UnusableGrid_IsSkippedAsBrokerDataInvalidAndNothingIsSent(int digits, double tick)
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.EurUsd() with { Digits = digits, TickSize = tick }));

        SkipRecord skip = h.SkipOf(ExecutionData.Buy());
        h.EvaluateAndPlace(ExecutionData.Buy());

        Assert.Equal(SkipReason.BrokerDataInvalid, skip.Reason);
        Assert.StartsWith("price grid of EURUSD is not usable: digits ", skip.Detail, StringComparison.Ordinal);
        Assert.Equal(0, h.Broker.Count(BrokerCall.Place));
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "EXE-017")]
    public void SymbolRecordWithoutGrid_IsSkippedAsBrokerDataInvalid()
    {
        var bare = new SymbolInfo("EURUSD", "EUR", "USD", 0.0001, 0.0001, 1000.0, 1000.0, 10000000.0, 0.0001, true, 0.0);
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(bare));

        Assert.Equal(SkipReason.BrokerDataInvalid, h.SkipOf(ExecutionData.Buy()).Reason);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    /// <summary>A broker that stores every placed order with a fixed stop loss, to test the read-back.</summary>
    private sealed class StopShiftingBroker : IBroker
    {
        private readonly FakeBroker _inner;
        private readonly double _stop;

        public StopShiftingBroker(FakeBroker inner, double stop)
        {
            _inner = inner;
            _stop = stop;
        }

        public DateTime ServerTimeUtc => _inner.ServerTimeUtc;

        public bool IsConnected => _inner.IsConnected;

        public AccountSnapshot GetAccount() => _inner.GetAccount();

        public SymbolInfo GetSymbol(string symbol) => _inner.GetSymbol(symbol);

        public IReadOnlyList<PositionInfo> GetPositions() => _inner.GetPositions();

        public IReadOnlyList<PendingOrderInfo> GetPendingOrders() => _inner.GetPendingOrders();

        public IReadOnlyList<ClosedTradeInfo> GetHistory(DateTime sinceUtc) => _inner.GetHistory(sinceUtc);

        public double EstimateMargin(string symbol, Side side, double volumeUnits) => _inner.EstimateMargin(symbol, side, volumeUnits);

        public OrderResult PlaceLimitOrder(LimitOrderRequest request) => _inner.PlaceLimitOrder(request with { StopLossPrice = _stop });

        public OrderResult CancelOrder(long orderId) => _inner.CancelOrder(orderId);

        public OrderResult ModifyPositionProtection(long positionId, double? stopLoss, double? takeProfit) => _inner.ModifyPositionProtection(positionId, stopLoss, takeProfit);

        public OrderResult ClosePosition(long positionId) => _inner.ClosePosition(positionId);
    }
}
