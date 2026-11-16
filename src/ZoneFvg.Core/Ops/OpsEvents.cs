using System;
using System.Collections.Generic;
using System.Linq;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// Connects the execution layer's hook (<see cref="IExecutionEvents"/>) and the news service's
/// results to the journal and the alert service (OPS-001, OPS-004, spec 11). Every skip record is
/// journaled (category <c>skip</c>) and counted for the daily summary; every execution alert is
/// raised as a Telegram alert at the broker's server time.
/// </summary>
public sealed class OpsEvents : IExecutionEvents
{
    private readonly Journal _journal;
    private readonly AlertService _alerts;
    private readonly IBroker _broker;

    /// <summary>Creates the hook (OPS-001, OPS-004).</summary>
    /// <param name="journal">The journal.</param>
    /// <param name="alerts">The alert service.</param>
    /// <param name="broker">The broker port; only its server time is read, as the time of an alert.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public OpsEvents(Journal journal, AlertService alerts, IBroker broker)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(broker);
        _journal = journal;
        _alerts = alerts;
        _broker = broker;
    }

    /// <summary>Journals and counts a skipped signal (OPS-001, EXE-014). Does not throw for a journal failure.</summary>
    /// <param name="skip">The skip record.</param>
    /// <exception cref="ArgumentNullException"><paramref name="skip"/> is null.</exception>
    public void Skipped(SkipRecord skip)
    {
        _journal.Skip(skip);
    }

    /// <summary>
    /// Raises an execution alert as a Telegram alert of the kind given by
    /// <see cref="AlertKinds.Telegram"/>, with the text <c>&lt;symbol&gt; &lt;kind&gt;: &lt;message&gt;</c>
    /// (OPS-004, RSK-002, spec 11). Never throws for an alert failure.
    /// </summary>
    /// <param name="alert">The alert.</param>
    /// <exception cref="ArgumentNullException"><paramref name="alert"/> is null.</exception>
    public void Alert(ExecutionAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        _alerts.Raise(alert.Kind, alert.Symbol + " " + alert.Kind + ": " + alert.Message, _broker.ServerTimeUtc);
    }

    /// <summary>
    /// Raises the alerts a news refresh calls for (OPS-004, NWS-005, NWS-008): a
    /// <see cref="AlertKind.CalendarUnavailable"/> alert when <see cref="NewsUpdate.AlertRaised"/>
    /// is set, and a <see cref="AlertKind.NewsFeedDown"/> alert per source whose download was
    /// rejected as malformed when <see cref="NewsUpdate.MalformedDownloadAlert"/> is set.
    /// </summary>
    /// <param name="update">The result of <see cref="NewsService.Refresh"/>.</param>
    /// <param name="atUtc">Server time of the refresh.</param>
    /// <returns>Alerts raised.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="update"/> is null.</exception>
    public int NewsUpdated(NewsUpdate update, DateTime atUtc)
    {
        ArgumentNullException.ThrowIfNull(update);
        int raised = 0;
        if (update.AlertRaised)
        {
            _alerts.Raise(AlertKind.CalendarUnavailable, "No trusted news data covers the current time; new entries are blocked.", atUtc);
            raised++;
        }

        if (update.MalformedDownloadAlert)
        {
            IEnumerable<NewsFeedReport> malformed = update.Reports.Where(r => r.Outcome == NewsFeedOutcome.DownloadMalformed);
            foreach (NewsFeedReport report in malformed)
            {
                _alerts.Raise(AlertKind.NewsFeedDown, "Download from " + report.Source + " rejected as malformed; earlier data kept. " + report.Detail, atUtc);
                raised++;
            }
        }

        return raised;
    }
}
