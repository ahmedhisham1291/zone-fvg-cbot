using System.Text;
using Xunit;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Simulation;
using static ZoneFvg.Core.Tests.SimData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-013 in the challenge simulator: with <see cref="FtmoChallengeParameters.Ladder"/> set,
/// every simulated trade is sized with the same <see cref="LadderSizer"/> the bot uses, so its
/// risk equals <see cref="LadderSizer.NextRiskPercent"/> of the trades taken before it, each
/// counted as closed when it closed at or before the new trade's open.
/// </summary>
public class LadderSimulationTests
{
    private static readonly FtmoChallengeParameters WithLadder = new() { Ladder = new RiskParameters(), MinTradingDays = 100 };

    private static string Sample(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Sim", name), Encoding.UTF8);
    }

    /// <summary>
    /// The final balance the ladder gives when every trade is taken: each trade risks the ladder
    /// percent of the balance at its open (closed trades only), its result is result_r times that.
    /// </summary>
    private static double Reference(IReadOnlyList<TradeRecord> trades)
    {
        var sizer = new LadderSizer(new RiskParameters());
        var results = new double[trades.Count];
        for (int i = 0; i < trades.Count; i++)
        {
            double balance = 100000.0;
            var entries = new List<LadderEntry>();
            for (int j = 0; j < i; j++)
            {
                bool closed = trades[j].CloseTimeUtc <= trades[i].OpenTimeUtc;
                balance += closed ? results[j] : 0.0;
                entries.Add(new LadderEntry("T" + j.ToString(System.Globalization.CultureInfo.InvariantCulture), trades[j].OpenTimeUtc, closed ? trades[j].CloseTimeUtc : null, closed ? results[j] : 0.0));
            }

            results[i] = trades[i].ResultR * balance * sizer.NextRiskPercent(entries) / 100.0;
        }

        return 100000.0 + results.Sum();
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void RunPath_LadderSequentialTrades_RiskTheLadderPercent()
    {
        TradeRecord[] trades =
        {
            Trade("EURUSD", Utc(2026, 1, 5, 10, 0), Utc(2026, 1, 5, 12, 0), -1.0),
            Trade("EURUSD", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 12, 0), 1.0),
            Trade("EURUSD", Utc(2026, 1, 7, 10, 0), Utc(2026, 1, 7, 12, 0), 2.0),
            Trade("EURUSD", Utc(2026, 1, 8, 10, 0), Utc(2026, 1, 8, 12, 0), 0.0),
            Trade("EURUSD", Utc(2026, 1, 9, 10, 0), Utc(2026, 1, 9, 12, 0), 1.0),
        };

        ChallengePath path = FtmoChallengeSimulator.RunPath(trades, CestClock.StartOfCestDayUtc(trades[0].OpenTimeUtc), WithLadder);

        Assert.Equal(5, path.TradesTaken);
        Assert.Equal(Reference(trades), path.FinalBalance, 6);

        // By hand: entry 1 risks 0.08886% (-88.86); entry 2 risks 0.13329% of 99,911.14.
        double afterTwo = 99911.14 + (99911.14 * 0.13329 / 100.0);
        Assert.Equal(afterTwo, Reference(trades.Take(2).ToArray()), 6);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void RunPath_LadderOverlappingTrades_CountOnlyClosedTradesForTheRun()
    {
        TradeRecord[] trades =
        {
            Trade("EURUSD", Utc(2026, 1, 5, 8, 0), Utc(2026, 1, 5, 9, 0), 1.0),
            Trade("GBPUSD", Utc(2026, 1, 5, 10, 0), Utc(2026, 1, 5, 14, 0), 1.0),
            Trade("AUDUSD", Utc(2026, 1, 5, 11, 0), Utc(2026, 1, 5, 12, 0), -1.0),
            Trade("USDJPY", Utc(2026, 1, 5, 12, 0), Utc(2026, 1, 5, 16, 0), 2.0),
            Trade("EURUSD", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 12, 0), 1.0),
            Trade("GBPUSD", Utc(2026, 1, 6, 11, 0), Utc(2026, 1, 6, 13, 0), 1.0),
        };

        ChallengePath path = FtmoChallengeSimulator.RunPath(trades, CestClock.StartOfCestDayUtc(trades[0].OpenTimeUtc), WithLadder);

        Assert.Equal(6, path.TradesTaken);
        Assert.Equal(Reference(trades), path.FinalBalance, 6);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void RunPath_LadderDisabled_RisksTheFixedPercent()
    {
        TradeRecord[] trades =
        {
            Trade("EURUSD", Utc(2026, 1, 5, 10, 0), Utc(2026, 1, 5, 12, 0), 1.0),
            Trade("EURUSD", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 12, 0), 1.0),
        };
        FtmoChallengeParameters parameters = WithLadder with { Ladder = new RiskParameters { LadderEnabled = false }, RiskPercent = 1.0 };

        ChallengePath path = FtmoChallengeSimulator.RunPath(trades, CestClock.StartOfCestDayUtc(trades[0].OpenTimeUtc), parameters);

        Assert.Equal(102010.0, path.FinalBalance, 6);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Validate_InvalidLadderValues_AreRejected()
    {
        FtmoChallengeParameters parameters = WithLadder with { Ladder = new RiskParameters { LadderGrowth = 0.5 } };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("LadderGrowth", exception.ParamName);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void FtmoCommand_SizingLadder_RunsBothPhasesWithTheLadder()
    {
        var files = new Dictionary<string, string> { ["eur.csv"] = Sample("sample-eurusd.csv") };
        SimCommandResult ladder = SimCommandLine.Run(new[] { "ftmo", "--log", "eur.csv", "--seed", "3", "--runs", "50", "--sizing", "ladder" }, path => files[path]);
        SimCommandResult fixedRisk = SimCommandLine.Run(new[] { "ftmo", "--log", "eur.csv", "--seed", "3", "--runs", "50", "--sizing", "fixed" }, path => files[path]);
        SimCommandResult wrong = SimCommandLine.Run(new[] { "ftmo", "--log", "eur.csv", "--seed", "3", "--sizing", "martingale" }, path => files[path]);

        Assert.Equal(SimCommandLine.ExitOk, ladder.ExitCode);
        Assert.Contains("| Challenge | ladder |", ladder.Output);
        Assert.Contains("| Verification | ladder |", ladder.Output);
        Assert.DoesNotContain("| 0.75% |", ladder.Output);
        Assert.Equal(SimCommandLine.ExitOk, fixedRisk.ExitCode);
        Assert.Contains("| Challenge | 0.75% |", fixedRisk.Output);
        Assert.DoesNotContain("ladder |", fixedRisk.Output);
        Assert.Equal(SimCommandLine.ExitInvalid, wrong.ExitCode);
        Assert.Contains("--sizing", wrong.Error);
    }
}
