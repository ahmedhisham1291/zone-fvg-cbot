using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Follow-up of the validation of WP37b (orchestrating session, 2026-11-23): a Choch signal meets
/// the money limits that stay as they are (spec 3.9, "What stays the same"): the size cap
/// MaxVolumeUnits and an account limit; the news check at the exact edge of the confirming bar;
/// and a daily zone line with a non-zero <c>no-choch</c> count. Data: <see cref="ChochExecutionData"/>
/// (EURUSD, now Wednesday 2026-01-14 10:00) and <see cref="ChochBotData"/>.
/// </summary>
public class ChochLimitScenarios
{
    private static readonly DateTime Now = ExecutionData.Now;

    private static (PreTradeFilters Filters, FakeBroker Broker, RecordingEvents Events) Filters(RiskParameters risk, SymbolInfo? symbol = null, DateTime? now = null)
    {
        FakeBroker broker = ExecutionHarness.DefaultBroker(symbol ?? ChochExecutionData.BuyQuote(), now);
        var news = new NewsService(new NewsParameters(), new[] { NewsTestData.ForexFactoryFeed() }, new MemoryStore(), new FakeTransport(NewsTestData.Failed()), true, ExecutionData.QuietNews);
        var events = new RecordingEvents();
        var filters = new PreTradeFilters(ExecutionData.ExecutionDefaults, ExecutionData.SignalDefaults, risk, broker, news, events, choch: ChochExecutionData.Defaults);
        return (filters, broker, events);
    }

    private static PreTradeResult Evaluate(PreTradeFilters filters, Signal signal)
    {
        return filters.Evaluate(signal, new PreTradeContext(ExecutionData.Symbol, BotState.Running, Array.Empty<Zone>(), false));
    }

    [Fact]
    [Trait("Req", "EXE-028")]
    public void FivePipStopAtTheDefaultRisk_AboveMaxVolumeUnits_VolumeAboveCapWithAnAlert()
    {
        // 0.5% of 100,000 = 500 USD over 5 pips x 0.0001 USD per unit = 1,000,000 units, above 500,000.
        (PreTradeFilters filters, _, RecordingEvents events) = Filters(RiskData.Parameters);

        PreTradeResult result = Evaluate(filters, ChochExecutionData.Buy(stop: 1.0995, target: 1.101));

        Assert.False(result.IsAllowed);
        Assert.Equal(SkipReason.VolumeAboveCap, result.Skip!.Reason);
        Assert.Equal(500000.0, result.Skip.Limit);
        Assert.Equal(1, events.AlertsOf(AlertKind.SanityCap));
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void SymbolAtItsEntryLimitForTheDay_SymbolEntryLimitReached()
    {
        // One bot entry on EURUSD earlier today, and at most one entry per symbol per day.
        (PreTradeFilters filters, FakeBroker broker, _) = Filters(ChochExecutionData.Risk with { MaxEntriesPerSymbolPerDay = 1 });
        broker.AddClosedTrade(new ClosedTradeInfo(901, ExecutionData.Symbol, ExecutionData.Label("D-20260113-2000"), ExecutionData.At(14, 8, 0), ExecutionData.At(14, 9, 0), 0.0, 0.0, 0.0, 100000.0));

        PreTradeResult result = Evaluate(filters, ChochExecutionData.Buy());

        Assert.False(result.IsAllowed);
        Assert.Equal(SkipReason.SymbolEntryLimitReached, result.Skip!.Reason);
    }

    [Fact]
    [Trait("Req", "NWS-015")]
    public void WindowStartingAtTheBarClose_NotTheChochBar_NowInsideTheWindow()
    {
        // Event 10:30: the window starts at 10:00:00, the close of the bar 09:45 to 10:00, which the bar excludes.
        // Now (10:00) is inside the window, so the skip is NewsWindow (NWS-009), not NewsChochBar.
        var news = new NewsService(new NewsParameters(), new[] { NewsTestData.ForexFactoryFeed() }, new MemoryStore(), new FakeTransport(NewsTestData.Failed()), true, "2026-01-14 10:30,USD,High,Event\n");
        var filters = new PreTradeFilters(ExecutionData.ExecutionDefaults, ExecutionData.SignalDefaults, ChochExecutionData.Risk, ExecutionHarness.DefaultBroker(ChochExecutionData.BuyQuote()), news, new RecordingEvents(), choch: ChochExecutionData.Defaults);

        Assert.Equal(NewsBlockReason.InsideWindow, news.CheckBreakoutEntry(ExecutionData.Symbol, Now, Now).Reason);
        Assert.Equal(SkipReason.NewsWindow, Evaluate(filters, ChochExecutionData.Buy()).Skip!.Reason);
    }

    [Fact]
    [Trait("Req", "NWS-015")]
    public void WindowStartingOneSecondAfterTheBarClose_TheBarPasses()
    {
        // The bar 09:44:59 to 09:59:59 ends one second before the window that starts at 10:00:00, and now
        // (09:59:59) is outside it: the news checks pass, and the order would live only one second before the
        // window, so the signal is skipped as ExpiryTooShort (EXE-015), not for news.
        DateTime close = Now.AddSeconds(-1.0);
        var broker = ExecutionHarness.DefaultBroker(ChochExecutionData.BuyQuote(), close);
        var news = new NewsService(new NewsParameters(), new[] { NewsTestData.ForexFactoryFeed() }, new MemoryStore(), new FakeTransport(NewsTestData.Failed()), true, "2026-01-14 10:30,USD,High,Event\n");
        var events = new RecordingEvents();
        var filters = new PreTradeFilters(ExecutionData.ExecutionDefaults, ExecutionData.SignalDefaults, ChochExecutionData.Risk, broker, news, events, choch: ChochExecutionData.Defaults);

        Assert.Equal(NewsBlockReason.None, news.CheckBreakoutEntry(ExecutionData.Symbol, close, close).Reason);
        PreTradeResult result = Evaluate(filters, ChochExecutionData.Buy(closeUtc: close));

        Assert.Equal(SkipReason.ExpiryTooShort, result.Skip!.Reason);
    }

    [Fact]
    [Trait("Req", "OPS-011")]
    public void ZoneWithoutAChangeOfCharacter_DailyLineCountsOneNoChoch()
    {
        // Tail A to m8 (touch at m7), then flat bars (100.0, 100.05, 99.95, 100.0): no close above 101.0, so the
        // window ends at bar 32 (17:45 to 18:00) with NoChoch.
        DateTime nextDay = new(2026, 3, 4, 23, 0, 0, DateTimeKind.Utc);
        BarSet reference = ChochBotData.Bars();
        BarBuilder m15 = new BarBuilder(TimeFrame.M15)
            .At(ChochBotData.Activation.AddMinutes(-15.0 * 2400))
            .Repeat(2400, (b, _) => b.Ohlc(100.6, 100.65, 100.55, 100.6));
        foreach ((double open, double high, double low, double close) in ChochScenarios.TailA.Take(9))
        {
            m15.Ohlc(open, high, low, close);
        }

        m15.Repeat(520, (b, _) => b.Ohlc(100.0, 100.05, 99.95, 100.0));
        var h = new BotHarness(new BarSet(reference.D1, reference.H4, m15.Build()), ChochBotData.Broker(ChochBotData.Start));
        h.Start(ChochBotData.Start, ChochBotData.Parameters());

        h.FeedTo(nextDay);

        Assert.Equal(
            new[] { JournalFormat.Time(nextDay) + " zones day=2026-03-04 created=1 signalled=0 no-choch=1 bias=0 invalid=0 expired=0 superseded=0" },
            h.Journal.Lines("zones"));
        Assert.Empty(h.Journal.Lines("signal"));
        Assert.Empty(h.Broker.Requests);
    }
}
