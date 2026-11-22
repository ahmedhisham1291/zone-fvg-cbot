using System.Globalization;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-022: a stop order is sent only for a signal of Kind Breakout whose planned order has entry
/// type Stop, a limit order only for Kind ZoneFvg with entry type Limit. Any other combination,
/// and an undefined kind or entry type, sends nothing and makes no broker query: one skip record
/// InvalidSignal with "entry type &lt;EntryType&gt; does not fit signal kind &lt;Kind&gt;" at the
/// broker's server time, and the result Abandoned with the same detail. The planned orders are
/// built by hand from valid ones of <see cref="ExecutionData"/>.
/// </summary>
public class Exe022EntryTypeScenarios
{
    private static PlannedOrder ValidBreakout(ExecutionHarness h)
    {
        PlannedOrder order = h.OrderOf(ExecutionData.BreakoutBuy());
        Assert.Equal(EntryOrderType.Stop, order.EntryType);
        Assert.Equal(SignalKind.Breakout, order.Signal.Kind);
        return order;
    }

    private static PlannedOrder ValidZoneFvg(ExecutionHarness h)
    {
        PlannedOrder order = h.OrderOf(ExecutionData.Buy());
        Assert.Equal(EntryOrderType.Limit, order.EntryType);
        Assert.Equal(SignalKind.ZoneFvg, order.Signal.Kind);
        return order;
    }

    private static PlannedOrder Build(ExecutionHarness h, int kind, int entryType)
    {
        PlannedOrder valid = (SignalKind)kind == SignalKind.ZoneFvg ? ValidZoneFvg(h) : ValidBreakout(h);
        return valid with { Signal = valid.Signal with { Kind = (SignalKind)kind }, EntryType = (EntryOrderType)entryType };
    }

    private static void AssertRefused(ExecutionHarness h, PlannedOrder order, string detail)
    {
        // The skip record carries the broker's server time, one minute after the planning time.
        DateTime now = ExecutionData.Now.AddMinutes(1.0);
        h.Broker.AdvanceTo(now);
        int queries = h.Broker.QueryCount;

        PlaceResult result = h.Orders.TryPlace(order);

        Assert.Equal(PlaceOutcome.Abandoned, result.Outcome);
        Assert.Equal(detail, result.Detail);
        Assert.Equal(order.Label, result.Label);
        Assert.Null(result.OrderId);
        Assert.Equal(queries, h.Broker.QueryCount);
        Assert.Empty(h.Broker.Requests);
        Assert.Equal(0, h.Broker.Count(BrokerCall.Place));
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Empty(h.Broker.GetPositions());
        SkipRecord skip = Assert.Single(h.Events.Skips);
        Assert.Equal(SkipReason.InvalidSignal, skip.Reason);
        Assert.Equal(detail, skip.Detail);
        Assert.Equal(order.Signal.ZoneId, skip.ZoneId);
        Assert.Equal(ExecutionData.Symbol, skip.Symbol);
        Assert.Equal(now, skip.AtUtc);
        Assert.Empty(h.Events.Alerts);
        Assert.Equal(0, h.Orders.UnresolvedCount);
        Assert.False(h.Orders.HasUnresolved(ExecutionData.Symbol));
    }

    [Fact]
    [Trait("Req", "EXE-022")]
    public void TryPlace_BreakoutSignalWithEntryTypeLimit_NothingSentInvalidSignalAbandoned()
    {
        ExecutionHarness h = ExecutionData.BreakoutHarness();
        PlannedOrder order = ValidBreakout(h) with { EntryType = EntryOrderType.Limit };

        AssertRefused(h, order, "entry type Limit does not fit signal kind Breakout");
    }

    [Fact]
    [Trait("Req", "EXE-022")]
    public void TryPlace_ZoneFvgSignalWithEntryTypeStop_NothingSentInvalidSignalAbandoned()
    {
        var h = new ExecutionHarness();
        PlannedOrder order = ValidZoneFvg(h) with { EntryType = EntryOrderType.Stop };

        AssertRefused(h, order, "entry type Stop does not fit signal kind ZoneFvg");
    }

    [Theory]
    [Trait("Req", "EXE-022")]
    [InlineData(1, 99, "entry type 99 does not fit signal kind Breakout")]
    [InlineData(0, 99, "entry type 99 does not fit signal kind ZoneFvg")]
    [InlineData(99, 1, "entry type Stop does not fit signal kind 99")]
    [InlineData(99, 0, "entry type Limit does not fit signal kind 99")]
    [InlineData(-1, 0, "entry type Limit does not fit signal kind -1")]
    [InlineData(0, -1, "entry type -1 does not fit signal kind ZoneFvg")]
    public void TryPlace_UndefinedKindOrEntryType_NothingSentInvalidSignalWithTheNumber(int kind, int entryType, string detail)
    {
        ExecutionHarness h = ExecutionData.BreakoutHarness();
        PlannedOrder order = Build(h, kind, entryType);

        AssertRefused(h, order, detail);
    }

    [Theory]
    [Trait("Req", "EXE-022")]
    [InlineData("th-TH")]
    [InlineData("comma")]
    public void TryPlace_RefusedUnderAnotherCulture_DetailIsTheInvariantText(string culture)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo other = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        other.NumberFormat.NumberDecimalSeparator = ",";
        other.NumberFormat.NegativeSign = "~";
        try
        {
            CultureInfo.CurrentCulture = culture == "comma" ? other : new CultureInfo(culture);
            ExecutionHarness h = ExecutionData.BreakoutHarness();
            PlannedOrder order = Build(h, -1, 99);

            AssertRefused(h, order, "entry type 99 does not fit signal kind -1");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Req", "EXE-022")]
    public void TryPlace_WrongEntryTypeWithAFaultScripted_TheFaultIsNotUsedAndNothingIsUnresolved()
    {
        // No request reaches the broker, so the scripted unclear outcome is still queued and the
        // placement is not kept for a resend.
        ExecutionHarness h = ExecutionData.BreakoutHarness();
        PlannedOrder order = ValidBreakout(h) with { EntryType = (EntryOrderType)99 };
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");

        PlaceResult result = h.Orders.TryPlace(order);

        Assert.Equal(PlaceOutcome.Abandoned, result.Outcome);
        Assert.Empty(h.Broker.Requests);
        Assert.Equal(1, h.Broker.PendingFaults(BrokerCall.Place));
        Assert.Equal(0, h.Orders.UnresolvedCount);
        Assert.Empty(h.Orders.ResolvePending(Array.Empty<Bar>()));
    }

    [Fact]
    [Trait("Req", "EXE-022")]
    public void TryPlace_BreakoutWithStop_SendsOneStopOrder()
    {
        ExecutionHarness h = ExecutionData.BreakoutHarness();
        PlannedOrder order = ValidBreakout(h);

        PlaceResult result = h.Orders.TryPlace(order);

        Assert.Equal(PlaceOutcome.Placed, result.Outcome);
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Null(request.Order);
        Assert.Equal(order.ToStopRequest(), request.StopOrder);
        Assert.Empty(h.Events.Skips);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-022")]
    public void TryPlace_ZoneFvgWithLimit_SendsOneLimitOrder()
    {
        var h = new ExecutionHarness();
        PlannedOrder order = ValidZoneFvg(h);

        PlaceResult result = h.Orders.TryPlace(order);

        Assert.Equal(PlaceOutcome.Placed, result.Outcome);
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Null(request.StopOrder);
        Assert.Equal(order.ToRequest(), request.Order);
        Assert.Empty(h.Events.Skips);
        ScenarioAssert.Global(h.Broker);
    }
}
