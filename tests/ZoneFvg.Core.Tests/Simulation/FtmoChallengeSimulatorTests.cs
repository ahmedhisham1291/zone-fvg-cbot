using Xunit;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Simulation;
using static ZoneFvg.Core.Tests.SimData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-004: replay trade sequences through the 2-Step rules (spec 15.2): profit target, daily
/// loss from the 00:00 CE(S)T balance with the intraday worst case of every trade open that day
/// at its stop, the static maximum loss, and four trading days, after the bot's own daily risk
/// budget and kill switch (spec 15.4), at a given risk per trade.
/// </summary>
/// <remarks>
/// Results are scaled to the chosen risk: a trade risks RiskPercent of the balance when it opens
/// (RSK-001) and its result is result_r times that. At its stop a trade loses that risk plus its
/// commission in R. Spec 15.2 and 15.4 say equity must stay above each floor, so equity exactly
/// at a floor is a breach. Initial capital 100,000.
/// </remarks>
public class FtmoChallengeSimulatorTests
{
    private static readonly FtmoChallengeParameters OnePercent = new() { InitialCapital = 100000.0, DailyBudgetPercent = 1.5, RiskPercent = 1.0 };

    private static DateTime DayStart(DateTime utc)
    {
        return CestClock.StartOfCestDayUtc(utc);
    }

    private static TradeRecord DayTrade(int day, double resultR)
    {
        // One trade a day in January 2026, 10:00 to 12:00 UTC.
        return Trade("EURUSD", Utc(2026, 1, day, 10, 0), Utc(2026, 1, day, 12, 0), resultR);
    }

    private static TradeRecord[] FourOpenAtOnce()
    {
        // Tuesday 6 January: four trades open between 08:00 and 09:30 and all close at 16:00 at +1R.
        return new[]
        {
            Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 16, 0), 1.0),
            Trade("GBPUSD", Utc(2026, 1, 6, 8, 30), Utc(2026, 1, 6, 16, 0), 1.0),
            Trade("AUDUSD", Utc(2026, 1, 6, 9, 0), Utc(2026, 1, 6, 16, 0), 1.0),
            Trade("USDJPY", Utc(2026, 1, 6, 9, 30), Utc(2026, 1, 6, 16, 0), 1.0),
        };
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void RunPath_TargetReachedOnDayFour_PassesOnDayFourExactly()
    {
        // +1R a day at 1%: 101,000; 102,010; 103,030.10; 104,060.401. Target 4% = 104,000 is
        // first reached on day 4, which is also the fourth trading day.
        TradeRecord[] trades = { DayTrade(5, 1.0), DayTrade(6, 1.0), DayTrade(7, 1.0), DayTrade(8, 1.0), DayTrade(9, 1.0) };
        FtmoChallengeParameters parameters = OnePercent with { ProfitTargetPercent = 4.0 };

        ChallengePath path = FtmoChallengeSimulator.RunPath(trades, DayStart(trades[0].OpenTimeUtc), parameters);

        Assert.Equal(ChallengeOutcome.Passed, path.Outcome);
        Assert.Equal(4, path.CalendarDays);
        Assert.Equal(4, path.TradingDays);
        Assert.Equal(4, path.TradesTaken);
        Assert.Equal(104060.401, path.FinalBalance, 6);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void RunPath_TargetReachedOnDayThree_WaitsForTheFourthTradingDay()
    {
        // Target 3% = 103,000 is reached on day 3 (103,030.10) with three trading days. Day 4
        // trades +0.1R: 103,030.10 x 1.001 = 103,133.1301 and the challenge passes on day 4.
        TradeRecord[] trades = { DayTrade(5, 1.0), DayTrade(6, 1.0), DayTrade(7, 1.0), DayTrade(8, 0.1), DayTrade(9, 1.0) };
        FtmoChallengeParameters parameters = OnePercent with { ProfitTargetPercent = 3.0 };

        ChallengePath path = FtmoChallengeSimulator.RunPath(trades, DayStart(trades[0].OpenTimeUtc), parameters);

        Assert.Equal(ChallengeOutcome.Passed, path.Outcome);
        Assert.Equal(4, path.CalendarDays);
        Assert.Equal(4, path.TradingDays);
        Assert.Equal(103133.1301, path.FinalBalance, 6);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void RunPath_TargetLostAgainOnDayFour_DoesNotPass()
    {
        // As above, but day 4 loses 1R: 103,030.10 x 0.99 = 101,999.799, below the target, and
        // there is no further trade.
        TradeRecord[] trades = { DayTrade(5, 1.0), DayTrade(6, 1.0), DayTrade(7, 1.0), DayTrade(8, -1.0) };
        FtmoChallengeParameters parameters = OnePercent with { ProfitTargetPercent = 3.0 };

        ChallengePath path = FtmoChallengeSimulator.RunPath(trades, DayStart(trades[0].OpenTimeUtc), parameters);

        Assert.Equal(ChallengeOutcome.NotDecided, path.Outcome);
        Assert.Equal(4, path.TradingDays);
        Assert.Equal(101999.799, path.FinalBalance, 6);
    }

    [Theory]
    [Trait("Req", "SIM-004")]
    [InlineData(-0.5, true)]
    [InlineData(-1.5, false)]
    public void RunPath_TargetReachedWithATradeStillOpen_PassesOnlyIfStillAtTargetWhenFlat(double secondR, bool passes)
    {
        // Minimum one trading day, target 2% = 102,000. X (+3R = +3,000) closes at 10:00 while Y
        // is open; Y risks 1% of 100,000 (its balance at the open) and closes at 12:00:
        // -0.5R gives 102,500 (pass), -1.5R gives 101,500 (no pass).
        TradeRecord x = Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 10, 0), 3.0);
        TradeRecord y = Trade("USDJPY", Utc(2026, 1, 6, 9, 0), Utc(2026, 1, 6, 12, 0), secondR);
        FtmoChallengeParameters parameters = OnePercent with { ProfitTargetPercent = 2.0, MinTradingDays = 1, DailyBudgetPercent = 3.0 };

        ChallengePath path = FtmoChallengeSimulator.RunPath(new[] { x, y }, DayStart(x.OpenTimeUtc), parameters);

        Assert.Equal(passes ? ChallengeOutcome.Passed : ChallengeOutcome.NotDecided, path.Outcome);
        Assert.Equal(passes ? 102500.0 : 101500.0, path.FinalBalance, 6);
        Assert.Equal(Utc(2026, 1, 6, 12, 0), path.LastEventUtc);
    }

    [Theory]
    [Trait("Req", "SIM-004")]
    [InlineData(5.0, true)]
    [InlineData(4.99999, true)]
    [InlineData(5.00001, false)]
    public void RunPath_FourTradesOpenAtTheirStops_BreachesTheDailyFloorThroughFloatingLoss(double dailyLossPercent, bool breached)
    {
        // Risk 1.25% = 1,250 each; all four end at +1R, but while all are open the worst case is
        // 100,000 - 4 x 1,250 = 95,000. Floor = 100,000 - 5% = 95,000: equal is a breach.
        // 5.00001% puts the floor one cent lower (94,999.99), 4.99999% one cent higher.
        FtmoChallengeParameters parameters = new FtmoChallengeParameters { InitialCapital = 100000.0, RiskPercent = 1.25, DailyBudgetPercent = 10.0 } with { DailyLossPercent = dailyLossPercent };
        TradeRecord[] trades = FourOpenAtOnce();

        ChallengePath path = FtmoChallengeSimulator.RunPath(trades, DayStart(trades[0].OpenTimeUtc), parameters);

        Assert.Equal(breached ? ChallengeOutcome.DailyFloorBreached : ChallengeOutcome.NotDecided, path.Outcome);
        Assert.Equal(4, path.TradesTaken);
        Assert.Equal(1, path.CalendarDays);
        Assert.Equal(breached ? 100000.0 : 105000.0, path.FinalBalance, 6);
        Assert.Equal(breached ? Utc(2026, 1, 6, 9, 30) : Utc(2026, 1, 6, 16, 0), path.LastEventUtc);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void RunPath_BotDailyBudget_RefusesTheFourthTradeAndPreventsTheBreach()
    {
        // As above with the bot's budget at 3.75% (1.5% scaled to 1.25% risk, spec 15.5): the
        // third trade uses exactly 3,750 and is allowed; the fourth would use 5,000 and is
        // refused, and so is a fifth trade the same day. Three trades end at +1R: 103,750.
        TradeRecord[] four = FourOpenAtOnce();
        TradeRecord fifth = Trade("NZDUSD", Utc(2026, 1, 6, 17, 0), Utc(2026, 1, 6, 18, 0), 1.0);
        FtmoChallengeParameters parameters = new FtmoChallengeParameters { InitialCapital = 100000.0, RiskPercent = 1.25, DailyBudgetPercent = 3.75 };

        ChallengePath path = FtmoChallengeSimulator.RunPath(four.Append(fifth).ToArray(), DayStart(four[0].OpenTimeUtc), parameters);

        Assert.Equal(ChallengeOutcome.NotDecided, path.Outcome);
        Assert.Equal(3, path.TradesTaken);
        Assert.Equal(2, path.TradesRefusedByBudget);
        Assert.Equal(103750.0, path.FinalBalance, 6);
    }

    [Theory]
    [Trait("Req", "SIM-004")]
    [InlineData(10.0, true)]
    [InlineData(9.99999, true)]
    [InlineData(10.00001, false)]
    public void RunPath_OneTradeAtItsStopReachingTheStaticFloor_BreachesAtTheFloor(double maxLossPercent, bool breached)
    {
        // Risk 10%: the worst case is 100,000 - 10,000 = 90,000 = static floor, a breach. The
        // daily floor is 80,000 here, and the budget allows the whole risk.
        TradeRecord trade = Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 12, 0), 1.0);
        var parameters = new FtmoChallengeParameters { InitialCapital = 100000.0, RiskPercent = 10.0, DailyLossPercent = 20.0, DailyBudgetPercent = 100.0, MaxLossPercent = maxLossPercent };

        ChallengePath path = FtmoChallengeSimulator.RunPath(new[] { trade }, DayStart(trade.OpenTimeUtc), parameters);

        Assert.Equal(breached ? ChallengeOutcome.StaticFloorBreached : ChallengeOutcome.NotDecided, path.Outcome);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void RunPath_DailyLossesOverSixDays_BreachTheStaticFloorWhenTheSixthTradeOpens()
    {
        // -1R a day at 2% (budget 3% so the bot allows 2,000): 98,000; 96,040; 94,119.20;
        // 92,236.816; 90,392.07968. Day 6 opens with risk 1,807.84...: worst case 88,584.24 is
        // below 90,000. The daily floor that day is 85,392.08, so it is the static floor.
        TradeRecord[] trades = Enumerable.Range(5, 6).Select(day => DayTrade(day, -1.0)).ToArray();
        var parameters = new FtmoChallengeParameters { InitialCapital = 100000.0, RiskPercent = 2.0, DailyBudgetPercent = 3.0, KillSwitchPercent = 100.0 };

        ChallengePath path = FtmoChallengeSimulator.RunPath(trades, DayStart(trades[0].OpenTimeUtc), parameters);

        Assert.Equal(ChallengeOutcome.StaticFloorBreached, path.Outcome);
        Assert.Equal(6, path.CalendarDays);
        Assert.Equal(6, path.TradesTaken);
        Assert.Equal(90392.07968, path.FinalBalance, 6);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void RunPath_KillSwitchAtEightPercent_HaltsBeforeTheStaticFloor()
    {
        // Same losses with the kill switch at 8%: after day 4 the drawdown is 7.76%, after day 5
        // it is 9.61%, so the bot halts and the sixth trade is not taken.
        TradeRecord[] trades = Enumerable.Range(5, 6).Select(day => DayTrade(day, -1.0)).ToArray();
        var parameters = new FtmoChallengeParameters { InitialCapital = 100000.0, RiskPercent = 2.0, DailyBudgetPercent = 3.0 };

        ChallengePath path = FtmoChallengeSimulator.RunPath(trades, DayStart(trades[0].OpenTimeUtc), parameters);

        Assert.Equal(ChallengeOutcome.Halted, path.Outcome);
        Assert.Equal(5, path.TradesTaken);
        Assert.Equal(90392.07968, path.FinalBalance, 6);
    }

    [Theory]
    [Trait("Req", "SIM-004")]
    [InlineData(8.0, ChallengeOutcome.Halted)]
    [InlineData(7.99999, ChallengeOutcome.Halted)]
    [InlineData(8.00001, ChallengeOutcome.StaticFloorBreached)]
    public void RunPath_DrawdownExactlyAtTheKillSwitch_Halts(double killSwitchPercent, ChallengeOutcome expected)
    {
        // Risk 8%: day 1 loses 8,000, a drawdown of exactly 8%. If the bot does not halt, day 2
        // risks 7,360 and the worst case 84,640 is below the static floor.
        TradeRecord[] trades = { DayTrade(5, -1.0), DayTrade(6, 1.0) };
        var parameters = new FtmoChallengeParameters { InitialCapital = 100000.0, RiskPercent = 8.0, DailyLossPercent = 20.0, DailyBudgetPercent = 100.0, KillSwitchPercent = killSwitchPercent };

        ChallengePath path = FtmoChallengeSimulator.RunPath(trades, DayStart(trades[0].OpenTimeUtc), parameters);

        Assert.Equal(expected, path.Outcome);
    }

    [Theory]
    [Trait("Req", "SIM-004")]
    [InlineData(22, 59, ChallengeOutcome.DailyFloorBreached)]
    [InlineData(23, 0, ChallengeOutcome.NotDecided)]
    public void RunPath_OctoberSwitchDay_DayEndsAtTwentyThreeUtc(int hour, int minute, ChallengeOutcome expected)
    {
        // Sunday 25 October 2026 returns to winter time at 01:00 UTC, so its CE(S)T day runs from
        // 22:00 UTC on the 24th to 23:00 UTC on the 25th. A closes at 22:30 UTC with -4R at 1%:
        // 96,000, still on the 25th (M = 100,000). B and C then risk 960 each: worst case 94,080.
        // Before 23:00 that is below 95,000; from 23:00 M = 96,000 and the floor is 91,000.
        TradeRecord a = Trade("EURUSD", Utc(2026, 10, 25, 20, 0), Utc(2026, 10, 25, 22, 30), -4.0);
        TradeRecord b = Trade("GBPUSD", Utc(2026, 10, 25, hour, minute), Utc(2026, 10, 26, 10, 0), 1.0);
        TradeRecord c = Trade("AUDUSD", Utc(2026, 10, 25, hour, minute), Utc(2026, 10, 26, 10, 0), 1.0);
        FtmoChallengeParameters parameters = OnePercent with { DailyBudgetPercent = 100.0, KillSwitchPercent = 100.0 };

        ChallengePath path = FtmoChallengeSimulator.RunPath(new[] { a, b, c }, Utc(2026, 10, 24, 22, 0), parameters);

        Assert.Equal(expected, path.Outcome);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void RunPath_LossBeyondTheStopOnALaterDay_CountsTheActualLoss()
    {
        // Opened Monday at 1% (stop 1,000), closed Tuesday at -5.2R through a gap: -5,200. On
        // Tuesday M = 100,000 and the actual loss takes equity to 94,800, below 95,000.
        TradeRecord gap = Trade("EURUSD", Utc(2026, 1, 5, 10, 0), Utc(2026, 1, 6, 7, 0), -5.2);
        FtmoChallengeParameters parameters = OnePercent with { KillSwitchPercent = 100.0 };

        ChallengePath path = FtmoChallengeSimulator.RunPath(new[] { gap }, DayStart(gap.OpenTimeUtc), parameters);

        Assert.Equal(ChallengeOutcome.DailyFloorBreached, path.Outcome);
        Assert.Equal(2, path.CalendarDays);
        Assert.Equal(1, path.TradingDays);
        Assert.Equal(94800.0, path.FinalBalance, 6);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void RunPath_CommissionCountsAtTheStop()
    {
        // Logged commission -10 on risk 500 is -0.02R, so at 1.25% a stop costs 1,275. Four open
        // trades: 100,000 - 5,100 = 94,900, below the floor even with the floor 50 lower (4.95%).
        TradeRecord[] trades = FourOpenAtOnce().Select(t => Trade(t.Symbol, t.OpenTimeUtc, t.CloseTimeUtc, 1.0, commission: -10.0)).ToArray();
        var parameters = new FtmoChallengeParameters { InitialCapital = 100000.0, RiskPercent = 1.25, DailyBudgetPercent = 10.0, DailyLossPercent = 5.05 };

        ChallengePath path = FtmoChallengeSimulator.RunPath(trades, DayStart(trades[0].OpenTimeUtc), parameters);

        Assert.Equal(ChallengeOutcome.DailyFloorBreached, path.Outcome);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void RunPath_TradesBeforeTheStartDay_AreIgnored()
    {
        TradeRecord[] trades = { DayTrade(5, -1.0), DayTrade(6, 1.0), DayTrade(7, 1.0) };

        ChallengePath path = FtmoChallengeSimulator.RunPath(trades, DayStart(trades[1].OpenTimeUtc), OnePercent);

        Assert.Equal(2, path.TradesTaken);
        Assert.Equal(102010.0, path.FinalBalance, 6);
        Assert.Equal(2, path.CalendarDays);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void Simulate_FiveDaysSeed42_PassesExactlyFromTheFirstTwoStartDays()
    {
        // +1R each weekday 5 to 9 January at 1%, target 3%. Starting Monday or Tuesday passes on
        // calendar day 4 (four trading days); starting later leaves fewer than four trading days.
        // Start days drawn from xoshiro256** seed 42 over 5 days (independent implementation):
        // 2 2 4 3 1 4 4 2 3 0 4 3 0 2 3 0 1 1 0 3 -> 7 of 20 are day 0 or 1.
        TradeRecord[] trades = Enumerable.Range(5, 5).Select(day => DayTrade(day, 1.0)).ToArray();
        FtmoChallengeParameters parameters = OnePercent with { ProfitTargetPercent = 3.0, Runs = 20 };

        ChallengeSummary summary = FtmoChallengeSimulator.Simulate(trades, parameters, 42UL);

        Assert.Equal(20, summary.Runs);
        Assert.Equal(5, summary.StartDays);
        Assert.Equal(7, summary.Passed);
        Assert.Equal(13, summary.NotDecided);
        Assert.Equal(0, summary.DailyFloorBreaches + summary.StaticFloorBreaches + summary.Halted);
        Assert.Equal(0.35, summary.PassProbability, 12);
        Assert.Equal(0.0, summary.BreachProbability, 12);
        Assert.Equal(0.65, summary.NeitherProbability, 12);
        Assert.Equal(4, summary.MedianDaysToPass);
        Assert.Equal(4, summary.P90DaysToPass);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void Simulate_SameSeed_GivesTheSameProbabilities()
    {
        int seed = 314;
        TradeRecord[] trades = RandomDays(seed, 120);
        FtmoChallengeParameters parameters = OnePercent with { Runs = 2000 };

        ChallengeSummary first = FtmoChallengeSimulator.Simulate(trades, parameters, (ulong)seed);
        ChallengeSummary second = FtmoChallengeSimulator.Simulate(trades, parameters, (ulong)seed);

        Assert.Equal(first.Passed, second.Passed);
        Assert.Equal(first.DailyFloorBreaches, second.DailyFloorBreaches);
        Assert.Equal(first.StaticFloorBreaches, second.StaticFloorBreaches);
        Assert.Equal(first.Halted, second.Halted);
        Assert.Equal(first.MedianDaysToPass, second.MedianDaysToPass);
        Assert.Equal(1.0, first.PassProbability + first.BreachProbability + first.NeitherProbability, 12);
        Assert.Equal(parameters.Runs, first.Passed + first.DailyFloorBreaches + first.StaticFloorBreaches + first.Halted + first.NotDecided);
        Assert.True(first.Passed > 0, "seed " + seed);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void Grid_BothPhasesThreeRisks_ScalesTheBudgetWithTheRisk()
    {
        TradeRecord[] trades = RandomDays(2, 60);
        FtmoChallengeParameters parameters = new FtmoChallengeParameters { InitialCapital = 100000.0, DailyBudgetPercent = 1.5, Runs = 50 };

        IReadOnlyList<ChallengeSummary> grid = FtmoChallengeSimulator.Grid(trades, parameters, 9UL);
        string text = FtmoChallengeSimulator.ToMarkdown(grid, 9UL);

        Assert.Equal(new[] { 10.0, 10.0, 10.0, 5.0, 5.0, 5.0 }, grid.Select(s => s.Parameters.ProfitTargetPercent));
        Assert.Equal(new[] { 0.5, 0.75, 1.0, 0.5, 0.75, 1.0 }, grid.Select(s => s.Parameters.RiskPercent));
        Assert.Equal(new[] { 1.5, 2.25, 3.0, 1.5, 2.25, 3.0 }, grid.Select(s => s.Parameters.DailyBudgetPercent));
        Assert.All(grid, s => Assert.Equal(8.0, s.Parameters.KillSwitchPercent));
        Assert.Equal(FtmoChallengeSimulator.Simulate(trades, parameters with { RiskPercent = 0.75, DailyBudgetPercent = 2.25 }, 9UL).Passed, grid[1].Passed);
        Assert.Contains("past trade distributions may not repeat", text);
        Assert.Contains("Seed: 9", text);
        Assert.Contains("| Challenge | 0.5% |", text);
        Assert.Contains("| Verification | 1% |", text);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void ToMarkdown_HandCheckedSummary_PrintsProbabilitiesAndDays()
    {
        TradeRecord[] trades = Enumerable.Range(5, 5).Select(day => DayTrade(day, 1.0)).ToArray();
        ChallengeSummary summary = FtmoChallengeSimulator.Simulate(trades, OnePercent with { ProfitTargetPercent = 3.0, Runs = 20 }, 42UL);

        string text = FtmoChallengeSimulator.ToMarkdown(new[] { summary }, 42UL);

        // Target 3% is neither phase, so the row is labelled with the target.
        Assert.Contains("| Target 3% | 1% | 1.5% | 35.0% | 0.0% (0.0% / 0.0%) | 65.0% (0.0% / 65.0%) | 4 | 4 |", text);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void Simulate_NoPass_ReportsNoDaysToPass()
    {
        TradeRecord[] trades = { DayTrade(5, -1.0), DayTrade(6, -1.0) };

        ChallengeSummary summary = FtmoChallengeSimulator.Simulate(trades, OnePercent with { Runs = 10 }, 1UL);

        Assert.Equal(0, summary.Passed);
        Assert.Null(summary.MedianDaysToPass);
        Assert.Null(summary.P90DaysToPass);
        Assert.Contains("| n/a | n/a |", FtmoChallengeSimulator.ToMarkdown(new[] { summary }, 1UL));
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void InvalidInput_Throws()
    {
        TradeRecord[] trades = { DayTrade(5, 1.0) };

        Assert.Throws<ArgumentException>(() => FtmoChallengeSimulator.Simulate(Array.Empty<TradeRecord>(), OnePercent, 1UL));
        Assert.Throws<ArgumentNullException>(() => FtmoChallengeSimulator.Simulate(null!, OnePercent, 1UL));
        Assert.Throws<ArgumentNullException>(() => FtmoChallengeSimulator.Simulate(trades, null!, 1UL));
        Assert.Throws<ArgumentException>(() => FtmoChallengeSimulator.RunPath(trades, Utc(2026, 1, 5, 10, 0), OnePercent));
        Assert.Throws<ArgumentOutOfRangeException>(() => FtmoChallengeSimulator.Simulate(trades, OnePercent with { Runs = 0 }, 1UL));
        Assert.Throws<ArgumentOutOfRangeException>(() => FtmoChallengeSimulator.Simulate(trades, OnePercent with { InitialCapital = -1.0 }, 1UL));
        Assert.Throws<ArgumentOutOfRangeException>(() => FtmoChallengeSimulator.Simulate(trades, OnePercent with { RiskPercent = 0.0 }, 1UL));
        Assert.Throws<ArgumentOutOfRangeException>(() => FtmoChallengeSimulator.Simulate(trades, OnePercent with { ProfitTargetPercent = 101.0 }, 1UL));
        Assert.Throws<ArgumentOutOfRangeException>(() => FtmoChallengeSimulator.Simulate(trades, OnePercent with { DailyLossPercent = double.NaN }, 1UL));
        Assert.Throws<ArgumentOutOfRangeException>(() => FtmoChallengeSimulator.Simulate(trades, OnePercent with { MaxLossPercent = 0.0 }, 1UL));
        Assert.Throws<ArgumentOutOfRangeException>(() => FtmoChallengeSimulator.Simulate(trades, OnePercent with { MinTradingDays = -1 }, 1UL));
        Assert.Throws<ArgumentOutOfRangeException>(() => FtmoChallengeSimulator.Simulate(trades, OnePercent with { DailyBudgetPercent = 0.0 }, 1UL));
        Assert.Throws<ArgumentOutOfRangeException>(() => FtmoChallengeSimulator.Simulate(trades, OnePercent with { KillSwitchPercent = 0.0 }, 1UL));
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void FtmoChallengeParameters_Defaults_AreTheSpecValues()
    {
        var defaults = new FtmoChallengeParameters();

        Assert.Equal(200000.0, defaults.InitialCapital);
        Assert.Equal(0.5, defaults.RiskPercent);
        Assert.Equal(10.0, defaults.ProfitTargetPercent);
        Assert.Equal(5.0, defaults.DailyLossPercent);
        Assert.Equal(10.0, defaults.MaxLossPercent);
        Assert.Equal(4, defaults.MinTradingDays);
        Assert.Equal(2.0, defaults.DailyBudgetPercent);
        Assert.Equal(8.0, defaults.KillSwitchPercent);
        Assert.Equal(5000, defaults.Runs);
    }

    private static TradeRecord[] RandomDays(int seed, int days)
    {
        // One or two trades on each weekday from 5 January 2026: 40% +2R, 50% -1R, 10% +0.5R.
        var generator = new SeededGenerator(seed);
        var trades = new List<TradeRecord>();
        DateTime day = Utc(2026, 1, 5, 0, 0);
        for (int i = 0; i < days; i++, day = day.AddDays(1.0))
        {
            if (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday)
            {
                continue;
            }

            int count = 1 + generator.Next(2);
            for (int k = 0; k < count; k++)
            {
                int draw = generator.Next(10);
                double r = draw < 4 ? 2.0 : draw < 9 ? -1.0 : 0.5;
                DateTime open = day.AddHours(8 + (3 * k));
                trades.Add(Trade(k == 0 ? "EURUSD" : "GBPUSD", open, open.AddHours(2.0), r));
            }
        }

        return trades.ToArray();
    }
}
