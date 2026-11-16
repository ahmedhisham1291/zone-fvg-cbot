using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// What the caller supplies for a daily summary (OPS-003). Every value may be null when it is not
/// known; it is then reported as <c>NA</c>.
/// </summary>
/// <param name="Snapshot">Broker data read at the summary time, with history back to the start of the summarised day; null when it could not be built.</param>
/// <param name="Calendar">News calendar status.</param>
/// <param name="Uptime">Time since this process started.</param>
/// <param name="Restarts">Restarts counted by the caller.</param>
public sealed record DailySummaryInput(RiskSnapshot? Snapshot, NewsStatus? Calendar, TimeSpan? Uptime, int? Restarts);

/// <summary>
/// The values of one daily summary (OPS-003, spec 11, spec 15.4). Money is in the account
/// currency. A null value was not known.
/// </summary>
/// <param name="DayStartUtc">Start of the summarised CE(S)T day, kind UTC.</param>
/// <param name="Date">The summarised CE(S)T date as <c>yyyy-MM-dd</c>.</param>
/// <param name="Trades">Bot trades (distinct positions with the bot label) closed in the day, from broker history.</param>
/// <param name="NetMoney">Net result of those trades.</param>
/// <param name="NetR">Sum of the results in R journaled for the day.</param>
/// <param name="TradesWithR">Trades of the day whose result in R was journaled.</param>
/// <param name="Balance">Balance at the summary time.</param>
/// <param name="Equity">Equity at the summary time.</param>
/// <param name="Drawdown">Drawdown from the peak balance of the kill switch (RSK-008).</param>
/// <param name="DrawdownPercent">Drawdown in percent of that peak.</param>
/// <param name="DailyFloorDistance">Equity minus FTMO's daily floor: the 00:00 CE(S)T balance minus FtmoDailyLossPercent of InitialCapital.</param>
/// <param name="OverallFloorDistance">Equity minus FTMO's overall floor: InitialCapital minus FtmoMaxLossPercent of it.</param>
/// <param name="Profit">Balance minus InitialCapital.</param>
/// <param name="TargetMoney">ProfitTargetPercent of InitialCapital.</param>
/// <param name="TargetProgressPercent">Profit in percent of the target.</param>
/// <param name="Skips">Skips of the day by reason, as journaled.</param>
/// <param name="Calendar">News calendar status.</param>
/// <param name="Uptime">Process uptime.</param>
/// <param name="Restarts">Restarts.</param>
public sealed record DailySummaryReport(
    DateTime DayStartUtc,
    string Date,
    int? Trades,
    double? NetMoney,
    double NetR,
    int TradesWithR,
    double? Balance,
    double? Equity,
    double? Drawdown,
    double? DrawdownPercent,
    double? DailyFloorDistance,
    double? OverallFloorDistance,
    double? Profit,
    double TargetMoney,
    double? TargetProgressPercent,
    IReadOnlyList<KeyValuePair<SkipReason, int>> Skips,
    NewsStatus? Calendar,
    TimeSpan? Uptime,
    int? Restarts)
{
    /// <summary>The plain-text message sent to Telegram (OPS-003). One value per line; unknown values read NA.</summary>
    public string Text
    {
        get
        {
            var text = new StringBuilder();
            text.Append("Daily summary ").Append(Date).Append(" (CE(S)T day)\n");
            text.Append("Trades ").Append(Trades.HasValue ? JournalFormat.Integer(Trades.Value) : JournalFormat.NotAvailable)
                .Append(", net ").Append(JournalFormat.Fixed(NetMoney, 2))
                .Append(", net R ").Append(JournalFormat.Fixed(NetR, 2))
                .Append(" (R known for ").Append(JournalFormat.Integer(TradesWithR)).Append(")\n");
            text.Append("Balance ").Append(JournalFormat.Fixed(Balance, 2)).Append(", equity ").Append(JournalFormat.Fixed(Equity, 2)).Append('\n');
            text.Append("Drawdown ").Append(JournalFormat.Fixed(Drawdown, 2)).Append(" (").Append(JournalFormat.Fixed(DrawdownPercent, 2)).Append("%)\n");
            text.Append("Room to FTMO daily floor ").Append(JournalFormat.Fixed(DailyFloorDistance, 2))
                .Append(", overall floor ").Append(JournalFormat.Fixed(OverallFloorDistance, 2)).Append('\n');
            text.Append("Target progress ").Append(JournalFormat.Fixed(TargetProgressPercent, 1)).Append("% (")
                .Append(JournalFormat.Fixed(Profit, 2)).Append(" of ").Append(JournalFormat.Fixed(TargetMoney, 2)).Append(")\n");
            text.Append("Skips ").Append(SkipText()).Append('\n');
            text.Append("Calendar ").Append(Calendar.HasValue ? Calendar.Value.ToString() : JournalFormat.NotAvailable).Append('\n');
            text.Append("Uptime ").Append(JournalFormat.Duration(Uptime))
                .Append(", restarts ").Append(Restarts.HasValue ? JournalFormat.Integer(Restarts.Value) : JournalFormat.NotAvailable);
            return text.ToString();
        }
    }

    /// <summary>The skips as <c>Reason count, Reason count</c>, or <c>none</c>.</summary>
    /// <returns>The text.</returns>
    public string SkipText()
    {
        return Skips.Count == 0
            ? "none"
            : string.Join(", ", Skips.Select(s => s.Key + " " + JournalFormat.Integer(s.Value)));
    }
}

/// <summary>
/// Produces the daily summary once per CE(S)T day (OPS-003, spec 11): at the first
/// <see cref="Pump"/> in a CE(S)T day later than the day of the last summary (or of the start),
/// it summarises the day that has just ended, journals it (category <c>summary</c>) and raises it
/// as a <see cref="OpsAlertKind.DailySummary"/> alert. No summary is produced in the day the
/// service starts. Never throws.
/// </summary>
public sealed class DailySummary
{
    private readonly OpsParameters _ops;
    private readonly RiskParameters _risk;
    private readonly RiskEngine _engine;
    private readonly Journal _journal;
    private readonly AlertService _alerts;

    /// <summary>Creates the service (OPS-003). The parameters are validated here.</summary>
    /// <param name="ops">Operations parameters (profit target).</param>
    /// <param name="risk">Risk parameters (InitialCapital and the FTMO limits).</param>
    /// <param name="journal">The journal, with its day counters.</param>
    /// <param name="alerts">The alert service.</param>
    /// <param name="startedUtc">Server time at start, kind UTC: no summary is produced in this CE(S)T day.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    /// <exception cref="ArgumentException"><paramref name="startedUtc"/> is not UTC.</exception>
    public DailySummary(OpsParameters ops, RiskParameters risk, Journal journal, AlertService alerts, DateTime startedUtc)
    {
        ArgumentNullException.ThrowIfNull(ops);
        ArgumentNullException.ThrowIfNull(risk);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(alerts);
        ops.Validate();
        _ops = ops;
        _risk = risk;
        _engine = new RiskEngine(risk);
        _journal = journal;
        _alerts = alerts;
        LastDayStartUtc = CestClock.StartOfCestDayUtc(startedUtc);
    }

    /// <summary>Start of the CE(S)T day of the last summary, or of the start (OPS-003).</summary>
    public DateTime LastDayStartUtc { get; private set; }

    /// <summary>True when server time is in a later CE(S)T day than <see cref="LastDayStartUtc"/> (OPS-003).</summary>
    /// <param name="serverTimeUtc">Server time, kind UTC.</param>
    /// <returns>True when a summary is due.</returns>
    /// <exception cref="ArgumentException"><paramref name="serverTimeUtc"/> is not UTC.</exception>
    public bool IsDue(DateTime serverTimeUtc)
    {
        return CestClock.StartOfCestDayUtc(serverTimeUtc) > LastDayStartUtc;
    }

    /// <summary>
    /// Produces the summary when due (OPS-003). The input is read only then. If reading the input
    /// throws, the error is journaled and the summary is produced with unknown values.
    /// </summary>
    /// <param name="serverTimeUtc">Server time, kind UTC.</param>
    /// <param name="input">Supplies the broker snapshot and the other values.</param>
    /// <returns>The report, or null when no summary was due or it failed (journaled).</returns>
    public DailySummaryReport? Pump(DateTime serverTimeUtc, Func<DailySummaryInput> input)
    {
        try
        {
            if (input == null || !IsDue(serverTimeUtc))
            {
                return null;
            }

            DateTime dayStart = CestClock.StartOfCestDayUtc(serverTimeUtc);
            LastDayStartUtc = dayStart;
            DailySummaryInput values;
            try
            {
                values = input();
            }
            catch (Exception exception)
            {
                // Reported as unknown values; journaled, so not silent (OPS-003).
                _journal.Error(serverTimeUtc, "DailySummary.input", exception);
                values = new DailySummaryInput(null, null, null, null);
            }

            DailySummaryReport report = Build(dayStart, values ?? new DailySummaryInput(null, null, null, null));
            Publish(serverTimeUtc, report);
            return report;
        }
        catch (Exception exception)
        {
            // Never thrown into trading logic; journaled, so not silent (OPS-003).
            _journal.Error(serverTimeUtc, "DailySummary.Pump", exception);
            return null;
        }
    }

    /// <summary>
    /// Builds the summary of the CE(S)T day before the one starting at <paramref name="currentDayStartUtc"/>
    /// (OPS-003). Trades and net money come from the snapshot's history (bot label, closed in that
    /// day, distinct positions); net R and skips from the journal's counters of that day; balance,
    /// equity, drawdown and floor distances from the snapshot at its server time.
    /// </summary>
    /// <param name="currentDayStartUtc">Start of the current CE(S)T day, kind UTC.</param>
    /// <param name="input">The input values.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    public DailySummaryReport Build(DateTime currentDayStartUtc, DailySummaryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        DateTime dayStart = CestClock.StartOfCestDayUtc(currentDayStartUtc.AddTicks(-1));
        string date = (dayStart + CestClock.UtcOffset(dayStart)).ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture);
        JournalDayCounters counters = _journal.CountersFor(dayStart);
        double capital = _risk.InitialCapital;
        double target = capital * _ops.ProfitTargetPercent / 100.0;
        RiskSnapshot? s = input.Snapshot;

        (int? trades, double? net) = s != null && s.HistorySinceUtc <= dayStart ? TradesOf(s, dayStart, currentDayStartUtc) : (null, null);
        double? drawdown = null;
        double? drawdownPercent = null;
        double? dailyDistance = null;
        if (s != null)
        {
            RiskSnapshot snapshot = s;
            KillSwitchDecision? kill = Try(() => _engine.KillSwitch(snapshot));
            if (kill != null && kill.Peak > 0.0)
            {
                drawdown = kill.DrawdownMoney;
                drawdownPercent = kill.DrawdownMoney / kill.Peak * 100.0;
            }

            double? dayStartBalance = Try<double?>(() => _engine.DayStartBalance(snapshot));
            if (dayStartBalance.HasValue)
            {
                dailyDistance = s.Account.Equity - (dayStartBalance.Value - (_risk.FtmoDailyLossPercent * capital / 100.0));
            }
        }

        double? overallDistance = s == null ? null : s.Account.Equity - (capital - (_risk.FtmoMaxLossPercent * capital / 100.0));
        double? profit = s == null ? null : s.Account.Balance - capital;
        return new DailySummaryReport(
            dayStart,
            date,
            trades,
            net,
            counters.NetR,
            counters.TradesWithR,
            s?.Account.Balance,
            s?.Account.Equity,
            drawdown,
            drawdownPercent,
            dailyDistance,
            overallDistance,
            profit,
            target,
            profit.HasValue ? profit.Value / target * 100.0 : (double?)null,
            counters.SkipsByReason,
            input.Calendar,
            input.Uptime,
            input.Restarts);
    }

    private static (int? Trades, double? Net) TradesOf(RiskSnapshot snapshot, DateTime fromUtc, DateTime toUtc)
    {
        var positions = new SortedSet<long>();
        double net = 0.0;
        foreach (ClosedTradeInfo trade in snapshot.History)
        {
            if (RiskEngine.IsBotLabel(trade.Label) && trade.CloseTimeUtc >= fromUtc && trade.CloseTimeUtc < toUtc)
            {
                positions.Add(trade.PositionId);
                net += trade.NetProfit;
            }
        }

        return (positions.Count, net);
    }

    private static T? Try<T>(Func<T> compute)
    {
        try
        {
            return compute();
        }
        catch (ArgumentException)
        {
            // History too short for this value: reported as NA (OPS-003).
            return default;
        }
        catch (InvalidOperationException)
        {
            // Symbol data missing for this value: reported as NA (OPS-003).
            return default;
        }
    }

    private void Publish(DateTime now, DailySummaryReport r)
    {
        _journal.Write(now, "summary", new (string, string?)[]
        {
            ("date", r.Date),
            ("trades", r.Trades.HasValue ? JournalFormat.Integer(r.Trades.Value) : JournalFormat.NotAvailable),
            ("netMoney", JournalFormat.Fixed(r.NetMoney, 2)),
            ("netR", JournalFormat.Fixed(r.NetR, 2)),
            ("tradesWithR", JournalFormat.Integer(r.TradesWithR)),
            ("balance", JournalFormat.Fixed(r.Balance, 2)),
            ("equity", JournalFormat.Fixed(r.Equity, 2)),
            ("drawdown", JournalFormat.Fixed(r.Drawdown, 2)),
            ("drawdownPercent", JournalFormat.Fixed(r.DrawdownPercent, 2)),
            ("dailyFloorDistance", JournalFormat.Fixed(r.DailyFloorDistance, 2)),
            ("overallFloorDistance", JournalFormat.Fixed(r.OverallFloorDistance, 2)),
            ("targetProgressPercent", JournalFormat.Fixed(r.TargetProgressPercent, 1)),
            ("skips", r.SkipText()),
            ("calendar", r.Calendar.HasValue ? r.Calendar.Value.ToString() : JournalFormat.NotAvailable),
            ("uptime", JournalFormat.Duration(r.Uptime)),
            ("restarts", r.Restarts.HasValue ? JournalFormat.Integer(r.Restarts.Value) : JournalFormat.NotAvailable),
        });
        _alerts.Raise(OpsAlertKind.DailySummary, r.Text, now);
    }
}
