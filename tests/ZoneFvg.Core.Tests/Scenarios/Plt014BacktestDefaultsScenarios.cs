using System.Globalization;
using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.News;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// PLT-014 end to end through <see cref="BotCore"/>: parameters built the way the adapter builds
/// them, with <see cref="BacktestDefaults"/>, an empty RiskEpochDate and NewsEnabled = true. Start
/// at Wednesday 2026-03-04 08:00 UTC (09:00 CET), so the default epoch is 2026-03-04. The reference
/// signal of <see cref="RecoveryData"/> confirms at 08:45.
/// </summary>
public class Plt014BacktestDefaultsScenarios
{
    private static readonly DateTime Start = RecoveryData.Activation;

    /// <summary>The parameters as the adapter builds them from an empty RiskEpochDate, NewsEnabled = true and the given history.</summary>
    private static BotParameters Defaults(string history)
    {
        bool filterOn = BacktestDefaults.NewsFilterOn(true, true, EmbeddedHistoryProvider.CountEvents(history));
        DateTime epoch = BacktestDefaults.RiskEpoch(null, true, Start);
        return RecoveryData.Parameters(filterOn, history) with { Risk = RiskData.Parameters with { RiskEpochDate = epoch } };
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void Backtest_EmptyHistoryAndDefaults_StartLineShowsNewsDisabledAndTheFirstDayAsRiskEpoch()
    {
        var h = new BotHarness();
        h.Start(Start, Defaults(string.Empty));

        string line = Assert.Single(h.Journal.Lines("start"));

        Assert.Contains(" news=disabled ", line, StringComparison.Ordinal);
        Assert.Contains(" backtest=yes ", line, StringComparison.Ordinal);
        Assert.Contains(" riskEpoch=2026-03-04 ", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void Backtest_EmptyHistoryAndDefaults_TheSignalIsPlaced()
    {
        var h = new BotHarness();
        BotCore core = h.Start(Start, Defaults(string.Empty));
        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void Backtest_EmptyHistoryWithTheFilterLeftOn_BlocksAsBefore()
    {
        // What the defaults prevent: NewsEnabled passed unchanged with an empty history blocks every entry.
        var h = new BotHarness();
        BotCore core = h.Start(Start, Defaults(string.Empty) with { News = new NewsParameters { NewsEnabled = true } });
        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(BotState.Blocked, core.State);
        Assert.Equal(0, h.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void Backtest_HistoryWithEvents_NewsEnabledWorksAsBefore()
    {
        var h = new BotHarness();
        BotCore core = h.Start(Start, Defaults(RecoveryData.News));
        h.FeedTo(RecoveryData.Confirmation);

        Assert.Contains(" news=enabled ", Assert.Single(h.Journal.Lines("start")), StringComparison.Ordinal);
        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(1, h.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void Backtest_HistoryWithEventsThatDoNotCoverTheRun_StillBlocks()
    {
        // One event in January 2025: the filter stays on and the calendar is unavailable in March 2026.
        var h = new BotHarness();
        BotCore core = h.Start(Start, Defaults("2025-01-08 13:30,USD,High,CPI m/m\n"));
        h.FeedTo(RecoveryData.Confirmation);

        Assert.Contains(" news=enabled ", Assert.Single(h.Journal.Lines("start")), StringComparison.Ordinal);
        Assert.Equal(BotState.Blocked, core.State);
        Assert.Equal(0, h.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void Backtest_MalformedHistory_FilterStaysOnAndBlocks()
    {
        // NWS-010: a malformed history is rejected and the calendar is unavailable; it is not a history without events.
        var h = new BotHarness();
        BotCore core = h.Start(Start, Defaults("2026-03-06 13:30,USD,Severe,Non-Farm Employment Change\n"));
        h.FeedTo(RecoveryData.Confirmation);

        Assert.Contains(" news=enabled ", Assert.Single(h.Journal.Lines("start")), StringComparison.Ordinal);
        Assert.Equal(BotState.Blocked, core.State);
        Assert.Equal(0, h.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void Live_NewsEnabledWithoutACalendar_StillBlocks()
    {
        // Live the embedded history is empty and the feed fails: the filter stays on and blocks.
        var h = new BotHarness(isBacktest: false);
        BotParameters parameters = RecoveryData.Parameters(BacktestDefaults.NewsFilterOn(true, false, EmbeddedHistoryProvider.CountEvents(string.Empty)), string.Empty);
        BotCore core = h.Start(Start, parameters);
        h.FeedTo(RecoveryData.Confirmation);

        Assert.Contains(" news=enabled ", Assert.Single(h.Journal.Lines("start")), StringComparison.Ordinal);
        Assert.Equal(BotState.Blocked, core.State);
        Assert.Equal(0, h.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void StartLine_UnderTheThaiCulture_ShowsTheGregorianRiskEpoch()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            var h = new BotHarness();
            h.Start(Start, Defaults(string.Empty));

            Assert.Contains(" riskEpoch=2026-03-04 ", Assert.Single(h.Journal.Lines("start")), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
