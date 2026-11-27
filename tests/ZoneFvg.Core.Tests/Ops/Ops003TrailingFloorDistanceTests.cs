using Xunit;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-003 with RSK-021 (owner answer of 2026-11-27 in pull request 111): the daily summary's
/// distance to FTMO's overall floor. Static mode: equity minus (InitialCapital - FtmoMaxLossPercent
/// of it), as before. TrailingBalance: equity minus (P - FtmoMaxLossPercent of InitialCapital), P the
/// guard's P; the static floor when P cannot be computed.
/// </summary>
/// <remarks>
/// The worked example of <see cref="DailySummaryTests"/>: InitialCapital 100,000, FtmoMaxLossPercent
/// 10, peak balance 101,193 since the epoch, equity 100,586. Static floor 90,000, distance 10,586;
/// trailing floor 91,193, distance 9,393.
/// </remarks>
public class Ops003TrailingFloorDistanceTests
{
    private static readonly DateTime SummaryTime = new(2026, 1, 14, 23, 5, 0, DateTimeKind.Utc);
    private static readonly DateTime DayStart = new(2026, 1, 14, 23, 0, 0, DateTimeKind.Utc);

    private static RiskParameters Trailing => RiskData.Parameters with { FtmoMaxLossMode = FtmoMaxLossMode.TrailingBalance };

    private static ClosedTradeInfo Closed(long id, DateTime closeUtc, double net, double balanceAfter)
    {
        return new ClosedTradeInfo(id, "EURUSD", "manual", closeUtc.AddHours(-1.0), closeUtc, net, 0.0, 0.0, balanceAfter);
    }

    private static RiskSnapshot Snapshot(DateTime? historySince = null, double balance = 100786.0, double equity = 100586.0, IReadOnlyList<ClosedTradeInfo>? history = null)
    {
        history ??= new[]
        {
            Closed(1, new DateTime(2026, 1, 14, 12, 0, 0, DateTimeKind.Utc), 1193.0, 101193.0),
            Closed(2, new DateTime(2026, 1, 14, 15, 0, 0, DateTimeKind.Utc), -407.0, 100786.0),
        };
        return new RiskSnapshot(SummaryTime, RiskData.Account(balance, equity, equity), Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), history, historySince ?? RiskData.EpochStart, RiskData.AllSymbols);
    }

    private static DailySummaryReport Build(RiskParameters risk, RiskSnapshot snapshot)
    {
        AlertSetup alerts = OpsData.Alerts();
        var summary = new DailySummary(OpsData.WithSecrets, risk, alerts.Journal, alerts.Service, OpsData.T0);
        return summary.Build(DayStart, new DailySummaryInput(snapshot, null, null, null));
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    [Trait("Req", "RSK-021")]
    public void Summary_Static_OverallDistanceFromTheStaticFloorAsBefore()
    {
        DailySummaryReport byDefault = Build(RiskData.Parameters, Snapshot());
        DailySummaryReport explicitStatic = Build(RiskData.Parameters with { FtmoMaxLossMode = FtmoMaxLossMode.Static }, Snapshot());

        Assert.Equal(10586.0, byDefault.OverallFloorDistance!.Value, 1e-6);
        Assert.Contains(", overall floor 10586.00\n", byDefault.Text, StringComparison.Ordinal);
        Assert.Equal(byDefault.Text, explicitStatic.Text);
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    [Trait("Req", "RSK-021")]
    public void Summary_Trailing_OverallDistanceFromPMinus10Percent()
    {
        DailySummaryReport report = Build(Trailing, Snapshot());

        Assert.Equal(9393.0, report.OverallFloorDistance!.Value, 1e-6);
        Assert.Contains(", overall floor 9393.00\n", report.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    [Trait("Req", "RSK-021")]
    public void Summary_TrailingHistoryIncomplete_StaticFloor()
    {
        DailySummaryReport report = Build(Trailing, Snapshot(historySince: SummaryTime));

        Assert.Equal(10586.0, report.OverallFloorDistance!.Value, 1e-6);
    }

    [Fact]
    [Trait("Req", "RSK-021")]
    public void FtmoOverallFloor_StaticTrailingAndFallback()
    {
        Assert.Equal(90000.0, new RiskEngine(RiskData.Parameters).FtmoOverallFloor(Snapshot()), 1e-6);
        Assert.Equal(91193.0, new RiskEngine(Trailing).FtmoOverallFloor(Snapshot()), 1e-6);

        // P below InitialCapital counts from InitialCapital; a peak that is not positive falls back to the static floor.
        Assert.Equal(90000.0, new RiskEngine(Trailing).FtmoOverallFloor(Snapshot(balance: 97000.0, equity: 97000.0, history: new[] { Closed(1, new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc), 2000.0, 97000.0) })), 1e-6);
        Assert.Equal(90000.0, new RiskEngine(Trailing).FtmoOverallFloor(Snapshot(balance: 1000.0, equity: 1000.0, history: new[] { Closed(1, new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc), 5000.0, -1.0) })), 1e-6);
        Assert.Throws<ArgumentNullException>(() => new RiskEngine(Trailing).FtmoOverallFloor(null!));
    }
}
