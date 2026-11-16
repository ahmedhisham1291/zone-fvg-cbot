using Xunit;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-015: order expiry is the earlier of the normal expiry (EntryExpiryBars, 8 bars = 2 hours
/// after placement at 10:00, so 12:00) and the start of the next news window of the symbol
/// (30 minutes before a high-impact EUR or USD event). With fewer than 15 minutes left the signal
/// is skipped.
/// </summary>
public class Exe015ExpiryScenarios
{
    private static string News(string time, string currency = "USD")
    {
        return "2026-01-14 " + time + "," + currency + ",High,Event\n";
    }

    [Fact]
    [Trait("Req", "EXE-015")]
    public void NoWindowBeforeTheNormalExpiry_ExpiresAfterEightBars()
    {
        var h = new ExecutionHarness();

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        Assert.Equal(ExecutionData.At(14, 12, 0), order.ExpiryUtc);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-015")]
    [InlineData("USD")]
    [InlineData("EUR")]
    public void WindowBeforeTheNormalExpiry_ShortensTheExpiryToItsStart(string currency)
    {
        // Event at 11:30: window from 11:00.
        var h = new ExecutionHarness(news: News("11:30", currency));

        Assert.Equal(PlaceOutcome.Placed, h.EvaluateAndPlace(ExecutionData.Buy())!.Outcome);

        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(ExecutionData.At(14, 11, 0), order.ExpiryUtc);
        h.Broker.AdvanceTo(ExecutionData.At(14, 11, 0));
        Assert.Empty(h.Broker.GetPendingOrders());
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-015")]
    public void FourteenMinutesLeft_IsSkipped()
    {
        // Event at 10:44: window from 10:14, 14 minutes after 10:00.
        var h = new ExecutionHarness(news: News("10:44"));

        SkipRecord skip = h.SkipOf(ExecutionData.Buy());

        Assert.Equal(SkipReason.ExpiryTooShort, skip.Reason);
        Assert.Equal(14.0, skip.Measured, 9);
        Assert.Equal(15.0, skip.Limit);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-015")]
    public void FifteenMinutesLeft_IsPlacedWithThatExpiry()
    {
        // Event at 10:45: window from 10:15.
        var h = new ExecutionHarness(news: News("10:45"));

        PlannedOrder order = h.OrderOf(ExecutionData.Sell());

        Assert.Equal(ExecutionData.At(14, 10, 15), order.ExpiryUtc);
        Assert.Equal(PlaceOutcome.Placed, h.Orders.TryPlace(order).Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-015")]
    public void WindowOfAnotherCurrency_DoesNotShortenTheExpiry()
    {
        var h = new ExecutionHarness(news: News("10:45", "JPY"));

        Assert.Equal(ExecutionData.At(14, 12, 0), h.OrderOf(ExecutionData.Buy()).ExpiryUtc);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-015")]
    public void WindowAfterTheNormalExpiry_KeepsTheNormalExpiry()
    {
        // Event at 12:31: window from 12:01, after 12:00.
        var h = new ExecutionHarness(news: News("12:31"));

        Assert.Equal(ExecutionData.At(14, 12, 0), h.OrderOf(ExecutionData.Buy()).ExpiryUtc);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-015")]
    public void EntryExpiryBarsParameter_SetsTheNormalExpiry()
    {
        var h = new ExecutionHarness(execution: new ExecutionParameters { EntryExpiryBars = 4 });

        Assert.Equal(ExecutionData.At(14, 11, 0), h.OrderOf(ExecutionData.Buy()).ExpiryUtc);
        ScenarioAssert.Global(h.Broker);
    }
}
