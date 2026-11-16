using Xunit;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-003: the daily summary, produced once per CE(S)T day, with trades, net R, balance, equity,
/// drawdown, distance to both FTMO floors, progress to the target, skips by reason, calendar
/// status, uptime and restarts.
/// </summary>
/// <remarks>
/// <para>Worked example. Started Wednesday 2026-01-14 10:00 UTC; the summary is due at the first
/// pump at or after 2026-01-14 23:00 UTC (00:00 CET on 15 January) and covers 14 January
/// (2026-01-13 23:00 to 2026-01-14 23:00 UTC). Risk defaults: InitialCapital 100,000, FTMO limits
/// 5% and 10%, epoch 1 January 2026; profit target 10%.</para>
/// <para>History from a balance of 100,000 at the epoch: bot trade 1004 closed 13 January 20:00
/// UTC (+200, previous CET day) gives 100,200; bot trade 1001 closed 14 January 12:00 (+993) gives
/// 101,193; bot trade 1002 closed 15:00 (-507) gives 100,686; a manual trade 1003 closed 16:00
/// (+100) gives 100,786. Equity 100,586 (an open position is 200 down).</para>
/// <para>Expected: trades 2 (1001 and 1002; 1003 is not a bot trade, 1004 is the day before), net
/// 993 - 507 = 486. Journaled R: 993 / 500 = 1.986 and -507 / 500 = -1.014, net 0.972 shown as
/// 0.97. Peak balance 101,193, drawdown 101,193 - 100,586 = 607, which is 0.5998% shown as 0.60%.
/// The new day started at 23:00 UTC with no trade since, so its start balance is 100,786: FTMO
/// daily floor 95,786, distance 100,586 - 95,786 = 4,800. Overall floor 90,000, distance 10,586.
/// Profit 786 of 10,000 = 7.86% shown as 7.9%. Uptime 10:00 to 23:05 = 13:05:00.</para>
/// </remarks>
public class DailySummaryTests
{
    private const string BotLabel = "ZFVG|v1|EURUSD|D-20260114-0400";
    private const string Comment = "e=1.10000;s=1.09800;t=1.10400;r=500.00;h=0123abcd";

    private static readonly DateTime Started = OpsData.T0;
    private static readonly DateTime SummaryTime = new(2026, 1, 14, 23, 5, 0, DateTimeKind.Utc);

    private static DateTime Utc(int month, int day, int hour, int minute = 0, int second = 0)
    {
        return new DateTime(2026, month, day, hour, minute, second, DateTimeKind.Utc);
    }

    private static ClosedTradeInfo Closed(long id, string label, DateTime closeUtc, double net, double balanceAfter)
    {
        return new ClosedTradeInfo(id, "EURUSD", label, closeUtc.AddHours(-1.0), closeUtc, net, -7.0, 0.0, balanceAfter);
    }

    private static IReadOnlyList<ClosedTradeInfo> History => new[]
    {
        Closed(1004, BotLabel, Utc(1, 13, 20), 200.0, 100200.0),
        Closed(1001, BotLabel, Utc(1, 14, 12), 993.0, 101193.0),
        Closed(1002, BotLabel, Utc(1, 14, 15), -507.0, 100686.0),
        Closed(1003, "manual", Utc(1, 14, 16), 100.0, 100786.0),
    };

    private static RiskSnapshot Snapshot(DateTime? historySince = null)
    {
        return new RiskSnapshot(
            SummaryTime,
            RiskData.Account(100786.0, 100586.0, 100000.0),
            Array.Empty<PositionInfo>(),
            Array.Empty<PendingOrderInfo>(),
            History,
            historySince ?? RiskData.EpochStart,
            RiskData.AllSymbols);
    }

    private static (DailySummary Summary, AlertSetup Alerts) Create(DateTime? started = null)
    {
        AlertSetup alerts = OpsData.Alerts();
        return (new DailySummary(OpsData.WithSecrets, RiskData.Parameters, alerts.Journal, alerts.Service, started ?? Started), alerts);
    }

    private static void JournalTheDay(Journal journal)
    {
        foreach (ClosedTradeInfo trade in History.Where(t => t.PositionId == 1001 || t.PositionId == 1002))
        {
            journal.Trade(new TradeJournalEntry(trade, Comment, ZoneFvg.Core.Domain.Side.Buy, 1.1, 0.0001, TradeExitReason.Unknown, null, null));
        }

        journal.Skip(new SkipRecord("EURUSD", "Z1", SkipReason.NewsWindow, 0.0, 0.0, "x", Utc(1, 14, 9)));
        journal.Skip(new SkipRecord("EURUSD", "Z2", SkipReason.SpreadTooWide, 1.6, 1.5, "x", Utc(1, 14, 10)));
        journal.Skip(new SkipRecord("EURUSD", "Z3", SkipReason.SpreadTooWide, 1.7, 1.5, "x", Utc(1, 14, 22, 59, 59)));
        journal.Skip(new SkipRecord("EURUSD", "Z4", SkipReason.SpreadTooWide, 1.7, 1.5, "x", Utc(1, 14, 23)));
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    public void Pump_WorkedSnapshot_SummarisesTheDayThatEnded()
    {
        (DailySummary summary, AlertSetup alerts) = Create();
        JournalTheDay(alerts.Journal);

        DailySummaryReport? report = summary.Pump(SummaryTime, () => new DailySummaryInput(Snapshot(), NewsStatus.Available, SummaryTime - Started, 0));

        Assert.NotNull(report);
        Assert.Equal(
            "Daily summary 2026-01-14 (CE(S)T day)\n"
            + "Trades 2, net 486.00, net R 0.97 (R known for 2)\n"
            + "Balance 100786.00, equity 100586.00\n"
            + "Drawdown 607.00 (0.60%)\n"
            + "Room to FTMO daily floor 4800.00, overall floor 10586.00\n"
            + "Target progress 7.9% (786.00 of 10000.00)\n"
            + "Skips SpreadTooWide 2, NewsWindow 1\n"
            + "Calendar Available\n"
            + "Uptime 13:05:00, restarts 0",
            report!.Text);
        Assert.Equal(
            "2026-01-14T23:05:00Z summary date=2026-01-14 trades=2 netMoney=486.00 netR=0.97 tradesWithR=2 balance=100786.00 equity=100586.00"
            + " drawdown=607.00 drawdownPercent=0.60 dailyFloorDistance=4800.00 overallFloorDistance=10586.00 targetProgressPercent=7.9"
            + " skips=SpreadTooWide\\s2,\\sNewsWindow\\s1 calendar=Available uptime=13:05:00 restarts=0",
            alerts.Sink.Of("summary").Single());
        Assert.Equal("ZFVG EURUSD DailySummary: " + report.Text, alerts.Service.PendingTexts.Single());
        Assert.Equal(new DateTime(2026, 1, 13, 23, 0, 0, DateTimeKind.Utc), report.DayStartUtc);
        Assert.Equal(1.986 - 1.014, report.NetR, 9);
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    public void Pump_SameDay_ProducesNothingAndDoesNotReadInput()
    {
        (DailySummary summary, AlertSetup alerts) = Create();
        int reads = 0;
        DailySummaryInput Input()
        {
            reads++;
            return new DailySummaryInput(null, null, null, null);
        }

        Assert.Null(summary.Pump(Started, Input));
        Assert.Null(summary.Pump(Utc(1, 14, 22, 59, 59), Input));
        Assert.NotNull(summary.Pump(Utc(1, 14, 23), Input));
        Assert.Null(summary.Pump(Utc(1, 14, 23, 0, 1), Input));
        Assert.Null(summary.Pump(Utc(1, 15, 22, 59, 59), Input));
        Assert.NotNull(summary.Pump(Utc(1, 15, 23), Input));

        Assert.Equal(2, reads);
        Assert.Equal(2, alerts.Sink.Of("summary").Count);
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    public void Pump_SpringDaylightSavingChange_OncePerCestDay()
    {
        // Summer time starts 2026-03-29 01:00 UTC. 00:00 CET on 29 March is 2026-03-28 23:00 UTC;
        // 00:00 CEST on 30 March is 2026-03-29 22:00 UTC (a 23-hour day).
        (DailySummary summary, _) = Create(Utc(3, 28, 10));
        var dates = new List<string>();
        foreach (DateTime t in new[] { Utc(3, 28, 22, 59, 59), Utc(3, 28, 23), Utc(3, 28, 23, 0, 1), Utc(3, 29, 21, 59, 59), Utc(3, 29, 22), Utc(3, 29, 22, 0, 1), Utc(3, 29, 23) })
        {
            DailySummaryReport? report = summary.Pump(t, () => new DailySummaryInput(null, null, null, null));
            if (report != null)
            {
                dates.Add(JournalFormat.Time(t) + " " + report.Date);
            }
        }

        Assert.Equal(new[] { "2026-03-28T23:00:00Z 2026-03-28", "2026-03-29T22:00:00Z 2026-03-29" }, dates);
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    public void Pump_AutumnDaylightSavingChange_OncePerCestDay()
    {
        // Summer time ends 2026-10-25 01:00 UTC. 00:00 CEST on 25 October is 2026-10-24 22:00 UTC;
        // 00:00 CET on 26 October is 2026-10-25 23:00 UTC (a 25-hour day).
        (DailySummary summary, _) = Create(Utc(10, 24, 10));
        var dates = new List<string>();
        foreach (DateTime t in new[] { Utc(10, 24, 21, 59, 59), Utc(10, 24, 22), Utc(10, 24, 23), Utc(10, 25, 22), Utc(10, 25, 22, 59, 59), Utc(10, 25, 23), Utc(10, 25, 23, 0, 1) })
        {
            DailySummaryReport? report = summary.Pump(t, () => new DailySummaryInput(null, null, null, null));
            if (report != null)
            {
                dates.Add(JournalFormat.Time(t) + " " + report.Date);
            }
        }

        Assert.Equal(new[] { "2026-10-24T22:00:00Z 2026-10-24", "2026-10-25T23:00:00Z 2026-10-25" }, dates);
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    public void Pump_AfterSeveralDaysAway_SummarisesTheLastDayOnce()
    {
        (DailySummary summary, _) = Create();

        DailySummaryReport? report = summary.Pump(Utc(1, 18, 9), () => new DailySummaryInput(null, null, null, null));

        Assert.Equal("2026-01-17", report!.Date);
        Assert.Null(summary.Pump(Utc(1, 18, 10), () => new DailySummaryInput(null, null, null, null)));
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    public void Pump_NoData_ReportsNaForEveryUnknownValue()
    {
        (DailySummary summary, _) = Create();

        DailySummaryReport report = summary.Pump(SummaryTime, () => new DailySummaryInput(null, null, null, null))!;

        Assert.Equal(
            "Daily summary 2026-01-14 (CE(S)T day)\n"
            + "Trades NA, net NA, net R 0.00 (R known for 0)\n"
            + "Balance NA, equity NA\n"
            + "Drawdown NA (NA%)\n"
            + "Room to FTMO daily floor NA, overall floor NA\n"
            + "Target progress NA% (NA of 10000.00)\n"
            + "Skips none\n"
            + "Calendar NA\n"
            + "Uptime NA, restarts NA",
            report.Text);
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    public void Pump_HistoryTooShort_ReportsAccountValuesAndNaForHistoryValues()
    {
        (DailySummary summary, _) = Create();

        DailySummaryReport report = summary.Pump(SummaryTime, () => new DailySummaryInput(Snapshot(Utc(1, 14, 23, 1)), NewsStatus.Unavailable, TimeSpan.FromHours(1.0), 3))!;

        Assert.Null(report.Trades);
        Assert.Null(report.Drawdown);
        Assert.Null(report.DailyFloorDistance);
        Assert.Equal(10586.0, report.OverallFloorDistance!.Value, 6);
        Assert.Equal(100786.0, report.Balance!.Value, 6);
        Assert.Contains("Calendar Unavailable\nUptime 01:00:00, restarts 3", report.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    public void Pump_InputThrows_JournalsErrorAndStillSummarises()
    {
        (DailySummary summary, AlertSetup alerts) = Create();

        DailySummaryReport? report = summary.Pump(SummaryTime, () => throw new InvalidOperationException("broker gone " + OpsData.Token));

        Assert.NotNull(report);
        Assert.Null(report!.Balance);
        Assert.Equal(
            "2026-01-14T23:05:00Z error source=DailySummary.input error=InvalidOperationException:\\sbroker\\sgone\\s***",
            alerts.Sink.Of("error").Single());
        Assert.Single(alerts.Service.PendingTexts);
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    public void Pump_NullInputOrTimeNotUtc_NeverThrows()
    {
        (DailySummary summary, AlertSetup alerts) = Create();

        Assert.Null(summary.Pump(SummaryTime, null!));
        Assert.Null(summary.Pump(DateTime.SpecifyKind(SummaryTime, DateTimeKind.Unspecified), () => new DailySummaryInput(null, null, null, null)));
        Assert.Single(alerts.Sink.Of("error"));
        Assert.NotNull(summary.Pump(SummaryTime, () => new DailySummaryInput(null, null, null, null)));
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    public void Build_VerificationTarget_UsesFivePercent()
    {
        AlertSetup alerts = OpsData.Alerts();
        var summary = new DailySummary(OpsData.WithSecrets with { ProfitTargetPercent = 5.0 }, RiskData.Parameters, alerts.Journal, alerts.Service, Started);

        DailySummaryReport report = summary.Build(Utc(1, 14, 23), new DailySummaryInput(Snapshot(), null, null, null));

        // Profit 786 of 5,000 = 15.72%.
        Assert.Equal(5000.0, report.TargetMoney, 6);
        Assert.Equal(15.72, report.TargetProgressPercent!.Value, 9);
        Assert.Throws<ArgumentNullException>(() => summary.Build(Utc(1, 14, 23), null!));
    }

    [Fact]
    [Trait("Req", "OPS-003")]
    public void Constructor_NullOrInvalidArguments_Throw()
    {
        AlertSetup a = OpsData.Alerts();

        Assert.Throws<ArgumentNullException>(() => new DailySummary(null!, RiskData.Parameters, a.Journal, a.Service, Started));
        Assert.Throws<ArgumentNullException>(() => new DailySummary(OpsData.WithSecrets, null!, a.Journal, a.Service, Started));
        Assert.Throws<ArgumentNullException>(() => new DailySummary(OpsData.WithSecrets, RiskData.Parameters, null!, a.Service, Started));
        Assert.Throws<ArgumentNullException>(() => new DailySummary(OpsData.WithSecrets, RiskData.Parameters, a.Journal, null!, Started));
        Assert.Throws<ArgumentException>(() => new DailySummary(OpsData.WithSecrets, RiskData.Parameters, a.Journal, a.Service, DateTime.SpecifyKind(Started, DateTimeKind.Local)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DailySummary(OpsData.WithSecrets, new RiskParameters(), a.Journal, a.Service, Started));
    }
}
