using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Simulation;
using static ZoneFvg.Core.Tests.SimData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-002: merge per-symbol trade logs by time, replay the account-level caps of RSK-004
/// (open risk at most 1.5% of initial capital), RSK-005 (at most two trades on the same side of
/// one currency) and RSK-006 (M - W at most 1.5% of initial capital, then blocked until the next
/// CE(S)T day), and report the combined equity curve, the maximum drawdown and how often each cap
/// would have applied.
/// </summary>
/// <remarks>
/// A trade is checked when it opens, against the trades accepted before it that are still open.
/// A refused trade is left out of the combined list, the equity curve and later checks. The caps
/// are checked in the order of the risk engine (RSK-004, RSK-005, RSK-006) and the first that
/// refuses is counted. W counts each trade at its stop: -risk_money + commission (architecture
/// section 10: commission counts, swap does not). Initial capital 100,000.
/// </remarks>
public class PortfolioCombinerTests
{
    // The account of before spec v0.12, with both caps on (owner authorisation of 2026-11-24, WP38b).
    private static readonly PortfolioParameters Defaults = new() { InitialCapital = 100000.0, DailyLossLimitPercent = 1.5, OpenRiskCapEnabled = true, CurrencyCapEnabled = true };

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Combine_TwoOverlappingLogs_CountsCapsAndBuildsTheCurveOnTheWorkedExample()
    {
        // Tuesday 6 January 2026, risk 500 each, no commission.
        // 08:00 a1 EURUSD buy  accepted (nothing open).
        // 09:00 a2 EURUSD buy  open {a1}: risk 1,000; EUR long 2, USD short 2; M - W = 1,000. Accepted.
        // 10:00 b1 GBPUSD sell open {a1,a2}: risk 1,500 = cap; GBP short 1, USD long 1;
        //       M - W = 1,500 = budget. Accepted (both limits exactly reached).
        // 10:30 b2 GBPUSD sell open {a1,a2,b1}: risk 2,000 > 1,500. Skipped by RSK-004.
        // 11:00 b1 closes -500 (99,500). 12:00 a1 closes +1,000 (100,500).
        // 13:00 a3 EURUSD buy  open {a2}: risk 1,000; M - W = 100,000 - (100,500 - 1,000) = 500. Accepted.
        // 14:00 a3 closes -500 (100,000).
        // 14:15 a4 EURUSD buy  open {a2}: accepted as a3.
        // 14:30 b3 GBPUSD buy  open {a2,a4}: risk 1,500; USD short 3 > 2. Skipped by RSK-005.
        // 15:00 a2 closes -500 (99,500). 18:00 a4 closes +1,000 (100,500).
        // Curve: 99,500; 100,500; 100,000; 99,500; 100,500. Largest fall: 100,500 to 99,500 =
        // 1,000, which is 1,000 / 100,500 = 0.99502...% of the peak.
        TradeRecord a1 = Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 12, 0), 2.0);
        TradeRecord a2 = Trade("EURUSD", Utc(2026, 1, 6, 9, 0), Utc(2026, 1, 6, 15, 0), -1.0);
        TradeRecord a3 = Trade("EURUSD", Utc(2026, 1, 6, 13, 0), Utc(2026, 1, 6, 14, 0), -1.0);
        TradeRecord a4 = Trade("EURUSD", Utc(2026, 1, 6, 14, 15), Utc(2026, 1, 6, 18, 0), 2.0);
        TradeRecord b1 = Trade("GBPUSD", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 11, 0), -1.0, side: Side.Sell);
        TradeRecord b2 = Trade("GBPUSD", Utc(2026, 1, 6, 10, 30), Utc(2026, 1, 6, 16, 0), 2.0, side: Side.Sell);
        TradeRecord b3 = Trade("GBPUSD", Utc(2026, 1, 6, 14, 30), Utc(2026, 1, 6, 17, 0), -1.0);

        PortfolioResult result = PortfolioCombiner.Combine(new[] { Log(a1, a2, a3, a4), Log(b1, b2, b3) }, Defaults);

        Assert.Equal(7, result.InputTradeCount);
        Assert.Equal(new[] { b1, a1, a3, a2, a4 }, result.Trades);
        Assert.Equal(new[] { 99500.0, 100500.0, 100000.0, 99500.0, 100500.0 }, result.EquityCurve.Select(p => p.Balance));
        Assert.Equal(new[] { b1.CloseTimeUtc, a1.CloseTimeUtc, a3.CloseTimeUtc, a2.CloseTimeUtc, a4.CloseTimeUtc }, result.EquityCurve.Select(p => p.TimeUtc));
        Assert.Equal(2, result.Skipped.Count);
        Assert.Same(b2, result.Skipped[0].Trade);
        Assert.Equal(PortfolioCap.OpenRisk, result.Skipped[0].Cap);
        Assert.Same(b3, result.Skipped[1].Trade);
        Assert.Equal(PortfolioCap.CurrencyExposure, result.Skipped[1].Cap);
        Assert.Equal(1, result.SkippedCount(PortfolioCap.OpenRisk));
        Assert.Equal(1, result.SkippedCount(PortfolioCap.CurrencyExposure));
        Assert.Equal(0, result.SkippedCount(PortfolioCap.DailyBudget));
        Assert.Equal(100500.0, result.FinalBalance);
        Assert.Equal(1000.0, result.MaxDrawdownMoney, 9);
        Assert.Equal(100.0 * 1000.0 / 100500.0, result.MaxDrawdownPercent, 12);
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Combine_DailyBudgetExhausted_BlocksUntilTheNextCestMidnight()
    {
        // Wednesday 7 January 2026 (winter): the next CE(S)T day starts at 23:00 UTC.
        // Risk 500 and commission -10 each: at its stop a trade costs 510.
        // c1 and c2 close at -510 each: balance 98,980, M = 100,000.
        // 12:00 c3: W = 98,980 - 510 = 98,470; M - W = 1,530 > 1,500. Skipped, day blocked.
        // 14:00 c4 risks only 400 (M - W would be 1,430) but the day is blocked. Skipped.
        // 22:59 c5: still the same CE(S)T day. Skipped.
        // 23:00 c6: new day, M = 98,980, M - W = 510. Accepted.
        TradeRecord c1 = Trade("EURUSD", Utc(2026, 1, 7, 8, 0), Utc(2026, 1, 7, 9, 0), -1.02, commission: -10.0);
        TradeRecord c2 = Trade("USDJPY", Utc(2026, 1, 7, 10, 0), Utc(2026, 1, 7, 11, 0), -1.02, commission: -10.0);
        TradeRecord c3 = Trade("AUDNZD", Utc(2026, 1, 7, 12, 0), Utc(2026, 1, 7, 13, 0), 2.0, commission: -10.0);
        TradeRecord c4 = Trade("EURGBP", Utc(2026, 1, 7, 14, 0), Utc(2026, 1, 7, 15, 0), 2.0, riskMoney: 400.0, commission: -10.0);
        TradeRecord c5 = Trade("EURGBP", Utc(2026, 1, 7, 22, 59), Utc(2026, 1, 7, 23, 30), 2.0, commission: -10.0);
        TradeRecord c6 = Trade("AUDNZD", Utc(2026, 1, 7, 23, 0), Utc(2026, 1, 8, 1, 0), 2.0, commission: -10.0);

        PortfolioResult result = PortfolioCombiner.Combine(new[] { Log(c1, c2, c3, c4, c5, c6) }, Defaults);

        Assert.Equal(new[] { c1, c2, c6 }, result.Trades);
        Assert.Equal(new[] { c3, c4, c5 }, result.Skipped.Select(s => s.Trade));
        Assert.All(result.Skipped, s => Assert.Equal(PortfolioCap.DailyBudget, s.Cap));
        Assert.Equal(3, result.SkippedCount(PortfolioCap.DailyBudget));
        Assert.Equal(98980.0 + 1000.0, result.FinalBalance, 9);
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Combine_DailyBlockOnTheMarchSwitchDay_EndsAtTwentyTwoUtc()
    {
        // Sunday 29 March 2026 switches to summer time at 01:00 UTC, so the CE(S)T day of the
        // 29th ends at 22:00 UTC. Two -1.02R losses with commission, then a refused trade.
        TradeRecord d1 = Trade("EURUSD", Utc(2026, 3, 29, 2, 0), Utc(2026, 3, 29, 3, 0), -1.02, commission: -10.0);
        TradeRecord d2 = Trade("USDJPY", Utc(2026, 3, 29, 4, 0), Utc(2026, 3, 29, 5, 0), -1.02, commission: -10.0);
        TradeRecord d3 = Trade("AUDNZD", Utc(2026, 3, 29, 6, 0), Utc(2026, 3, 29, 7, 0), 1.0, commission: -10.0);
        TradeRecord d4 = Trade("EURGBP", Utc(2026, 3, 29, 21, 59), Utc(2026, 3, 29, 23, 0), 1.0, riskMoney: 100.0, commission: -10.0);
        TradeRecord d5 = Trade("EURGBP", Utc(2026, 3, 29, 22, 0), Utc(2026, 3, 29, 23, 0), 1.0, commission: -10.0);

        PortfolioResult result = PortfolioCombiner.Combine(new[] { Log(d1, d2, d3, d4, d5) }, Defaults);

        Assert.Equal(new[] { d1, d2, d5 }, result.Trades);
        Assert.Equal(new[] { d3, d4 }, result.Skipped.Select(s => s.Trade));
    }

    [Theory]
    [Trait("Req", "SIM-002")]
    [InlineData(499.99, true)]
    [InlineData(500.0, true)]
    [InlineData(500.01, false)]
    public void Combine_OpenRiskAroundTheCap_IsAllowedUpToExactlyTheCap(double thirdRisk, bool accepted)
    {
        // Three trades on different currencies, no commission: 500 + 500 + third against 1,500.
        // M - W is the same sum, so RSK-006 is at its limit too but RSK-004 is checked first.
        TradeRecord first = Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 20, 0), 1.0);
        TradeRecord second = Trade("AUDJPY", Utc(2026, 1, 6, 9, 0), Utc(2026, 1, 6, 20, 0), 1.0);
        TradeRecord third = Trade("GBPCHF", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 20, 0), 1.0, riskMoney: thirdRisk);

        PortfolioResult result = PortfolioCombiner.Combine(new[] { Log(first, second, third) }, Defaults);

        Assert.Equal(accepted ? 3 : 2, result.Trades.Count);
        Assert.Equal(accepted ? 0 : 1, result.SkippedCount(PortfolioCap.OpenRisk));
        Assert.Equal(0, result.SkippedCount(PortfolioCap.DailyBudget));
    }

    [Theory]
    [Trait("Req", "SIM-002")]
    [InlineData(499.99, true)]
    [InlineData(500.0, true)]
    [InlineData(500.01, false)]
    public void Combine_DailyBudgetAroundTheLimit_IsAllowedUpToExactlyTheLimit(double newRisk, bool accepted)
    {
        // A loss of -1,000 (2R through a gap) closed today: M = 100,000, balance 99,000.
        // New trade: M - W = 1,000 + newRisk against 1,500; open risk is only newRisk.
        TradeRecord gap = Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 9, 0), -2.0);
        TradeRecord next = Trade("USDJPY", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 11, 0), 1.0, riskMoney: newRisk);

        PortfolioResult result = PortfolioCombiner.Combine(new[] { Log(gap), Log(next) }, Defaults);

        Assert.Equal(accepted ? 2 : 1, result.Trades.Count);
        Assert.Equal(accepted ? 0 : 1, result.SkippedCount(PortfolioCap.DailyBudget));
        Assert.Equal(0, result.SkippedCount(PortfolioCap.OpenRisk));
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Combine_LossesOfThePreviousDay_DoNotCountInTodaysBudget()
    {
        // -1,000 closed at 22:59 UTC on 6 January (CE(S)T day of the 6th). Next CE(S)T day starts
        // 23:00 UTC: M = 99,000 and a 1,000-risk trade at 23:00 uses only 1,000 of the budget. At
        // 22:59, still the old day, a 500.01-risk trade would use 1,000 + 500.01 = 1,500.01.
        TradeRecord gap = Trade("EURUSD", Utc(2026, 1, 6, 20, 0), Utc(2026, 1, 6, 22, 59), -2.0);
        TradeRecord sameDay = Trade("USDJPY", Utc(2026, 1, 6, 22, 59), Utc(2026, 1, 6, 23, 30), 1.0, riskMoney: 500.01);
        TradeRecord nextDay = Trade("AUDNZD", Utc(2026, 1, 6, 23, 0), Utc(2026, 1, 7, 1, 0), 1.0, riskMoney: 1000.0);

        PortfolioResult result = PortfolioCombiner.Combine(new[] { Log(gap, sameDay, nextDay) }, Defaults);

        Assert.Equal(new[] { gap, nextDay }, result.Trades);
        Assert.Same(sameDay, Assert.Single(result.Skipped).Trade);
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Combine_CloseAtTheSameInstantAsAnOpen_IsProcessedFirst()
    {
        // Risk 600 each: three open would be 1,800 > 1,500. The first trade closes at 10:00 with
        // +1,200, exactly when the third opens, so only 1,200 is open and the budget is unused.
        TradeRecord first = Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 10, 0), 2.0, riskMoney: 600.0);
        TradeRecord second = Trade("USDJPY", Utc(2026, 1, 6, 9, 0), Utc(2026, 1, 6, 12, 0), 1.0, riskMoney: 600.0);
        TradeRecord third = Trade("AUDNZD", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 11, 0), 1.0, riskMoney: 600.0);

        PortfolioResult result = PortfolioCombiner.Combine(new[] { Log(first, second, third) }, Defaults);

        Assert.Equal(3, result.Trades.Count);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Combine_SellsCountAsLongTheQuoteCurrency_ThirdUsdLongIsSkipped()
    {
        // EURUSD sell: EUR short, USD long. Two of them plus a USDJPY buy (USD long) is three USD long.
        // Risk 400 each keeps open risk (1,200) and M - W (1,200) inside their limits.
        TradeRecord first = Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 20, 0), 1.0, riskMoney: 400.0, side: Side.Sell);
        TradeRecord second = Trade("EURUSD", Utc(2026, 1, 6, 9, 0), Utc(2026, 1, 6, 20, 0), 1.0, riskMoney: 400.0, side: Side.Sell);
        TradeRecord third = Trade("USDJPY", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 20, 0), 1.0, riskMoney: 400.0);
        TradeRecord fourth = Trade("USDJPY", Utc(2026, 1, 6, 11, 0), Utc(2026, 1, 6, 20, 0), 1.0, riskMoney: 400.0, side: Side.Sell);

        PortfolioResult result = PortfolioCombiner.Combine(new[] { Log(first, second, third, fourth) }, Defaults);

        Assert.Equal(new[] { first, second, fourth }, result.Trades);
        Assert.Equal(PortfolioCap.CurrencyExposure, Assert.Single(result.Skipped).Cap);
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Combine_SimultaneousOpens_DoNotDependOnTheOrderOfTheLogs()
    {
        // Two trades open at 10:00 after two others: the tie is broken by symbol, so AUDJPY is
        // checked before NZDCHF whichever log comes first. The second of them exceeds 1,500.
        TradeRecord first = Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 20, 0), 1.0);
        TradeRecord second = Trade("GBPCAD", Utc(2026, 1, 6, 9, 0), Utc(2026, 1, 6, 20, 0), 1.0);
        TradeRecord audJpy = Trade("AUDJPY", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 20, 0), 1.0);
        TradeRecord nzdChf = Trade("NZDCHF", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 20, 0), 1.0);

        PortfolioResult forward = PortfolioCombiner.Combine(new[] { Log(first, second), Log(audJpy), Log(nzdChf) }, Defaults);
        PortfolioResult backward = PortfolioCombiner.Combine(new[] { Log(nzdChf), Log(audJpy), Log(second, first) }, Defaults);

        Assert.Same(nzdChf, Assert.Single(forward.Skipped).Trade);
        Assert.Same(nzdChf, Assert.Single(backward.Skipped).Trade);
        Assert.Equal(forward.Trades, backward.Trades);
        Assert.Equal(forward.ToMarkdown(), backward.ToMarkdown());
        Assert.Equal(forward.ToEquityCsv(), backward.ToEquityCsv());
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Combine_ParametersAreUsed_ASmallerCapRefusesTheSecondTrade()
    {
        var tight = new PortfolioParameters { InitialCapital = 50000.0, MaxOpenRiskPercent = 1.5, OpenRiskCapEnabled = true, MaxPositionsPerCurrency = 1, CurrencyCapEnabled = true, DailyLossLimitPercent = 3.0 };
        TradeRecord first = Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 20, 0), 1.0);
        TradeRecord second = Trade("EURJPY", Utc(2026, 1, 6, 9, 0), Utc(2026, 1, 6, 20, 0), 1.0, riskMoney: 200.0);
        TradeRecord third = Trade("GBPJPY", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 20, 0), 1.0, riskMoney: 300.0);

        // Cap 750: second makes 700 but EUR long would be 2 > 1; third makes 800 > 750.
        PortfolioResult result = PortfolioCombiner.Combine(new[] { Log(first, second, third) }, tight);

        Assert.Equal(new[] { first }, result.Trades);
        Assert.Equal(PortfolioCap.CurrencyExposure, result.Skipped[0].Cap);
        Assert.Equal(PortfolioCap.OpenRisk, result.Skipped[1].Cap);
        Assert.Equal(50000.0 + 500.0, result.FinalBalance);
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Combine_NoTrades_IsAnEmptyPortfolioAtInitialCapital()
    {
        PortfolioResult result = PortfolioCombiner.Combine(new[] { Log() }, Defaults);

        Assert.Equal(0, result.InputTradeCount);
        Assert.Empty(result.Trades);
        Assert.Empty(result.EquityCurve);
        Assert.Equal(100000.0, result.FinalBalance);
        Assert.Equal(0.0, result.MaxDrawdownMoney);
        Assert.Equal(0.0, result.MaxDrawdownPercent);
        Assert.Equal("time_utc,balance\n", result.ToEquityCsv());
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void ToMarkdownAndEquityCsv_WorkedValues_AreWrittenInvariantly()
    {
        TradeRecord win = Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 9, 0), 2.0);
        TradeRecord loss = Trade("USDJPY", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 11, 30), -1.0);

        PortfolioResult result = PortfolioCombiner.Combine(new[] { Log(win), Log(loss) }, Defaults);
        string markdown = result.ToMarkdown();

        Assert.Equal("time_utc,balance\n2026-01-06T09:00:00Z,101000\n2026-01-06T11:30:00Z,100500\n", result.ToEquityCsv());
        Assert.Contains("past trade distributions may not repeat", markdown);
        Assert.Contains("| Trades in the logs | 2 |", markdown);
        Assert.Contains("| Trades kept | 2 |", markdown);
        Assert.Contains("| Skipped by open risk cap (RSK-004) | 0 |", markdown);
        Assert.Contains("| Skipped by currency exposure cap (RSK-005) | 0 |", markdown);
        Assert.Contains("| Skipped by daily risk budget (RSK-006) | 0 |", markdown);
        Assert.Contains("| Final balance | 100500.00 |", markdown);
        Assert.Contains("| Maximum drawdown | 500.00 (0.50%) |", markdown);
        Assert.Contains("| EURUSD | 1 | 1000.00 |", markdown);
        Assert.Contains("| USDJPY | 1 | -500.00 |", markdown);
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Combine_InvalidInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PortfolioCombiner.Combine(null!, Defaults));
        Assert.Throws<ArgumentNullException>(() => PortfolioCombiner.Combine(new[] { Log() }, null!));
        Assert.Throws<ArgumentException>(() => PortfolioCombiner.Combine(new IReadOnlyList<TradeRecord>[] { null! }, Defaults));
        Assert.Throws<ArgumentOutOfRangeException>(() => PortfolioCombiner.Combine(new[] { Log() }, Defaults with { InitialCapital = 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PortfolioCombiner.Combine(new[] { Log() }, Defaults with { MaxOpenRiskPercent = 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PortfolioCombiner.Combine(new[] { Log() }, Defaults with { MaxPositionsPerCurrency = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PortfolioCombiner.Combine(new[] { Log() }, Defaults with { DailyLossLimitPercent = double.NaN }));
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void PortfolioParameters_Defaults_AreThoseOfTheRiskEngine()
    {
        var defaults = new PortfolioParameters();

        Assert.Equal(200000.0, defaults.InitialCapital);
        Assert.Equal(1.5, defaults.MaxOpenRiskPercent);
        Assert.False(defaults.OpenRiskCapEnabled);
        Assert.Equal(2, defaults.MaxPositionsPerCurrency);
        Assert.False(defaults.CurrencyCapEnabled);
        Assert.Equal(2.0, defaults.DailyLossLimitPercent);
    }
}
