using System;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Ops;


/// <summary>Result of one <see cref="InactivityMonitor.Check"/> (OPS-006).</summary>
/// <param name="LastTradeUtc">Open time of the account's most recent trade seen by this monitor; the history start of the first check when no trade was ever found.</param>
/// <param name="LastTradeKnown">False when no trade was ever found in the history or the open positions.</param>
/// <param name="Inactive">Server time minus <paramref name="LastTradeUtc"/>.</param>
/// <param name="AlertRaised">True when this check raised the inactivity alert.</param>
public sealed record InactivityStatus(DateTime LastTradeUtc, bool LastTradeKnown, TimeSpan Inactive, bool AlertRaised);

/// <summary>
/// Inactivity warning (OPS-006, spec 11, spec 15.2): FTMO is reported to close an account after
/// 30 days without a trade. From the broker's history and open positions, the time since the
/// account's last opened trade (any label, bot or not) is measured in server time. One alert is
/// raised when it reaches <see cref="OpsParameters.InactivityAlertDays"/> days (exactly 21 x 24
/// hours counts) and again every <see cref="OpsParameters.InactivityRepeatDays"/> days after. A
/// newer trade starts the count again. The check does not depend on the bot state.
/// </summary>
/// <remarks>
/// When neither the history nor the positions hold a trade, the start of the history of the first
/// check is used as the last trade time: the account has been inactive at least that long (fail
/// towards warning). The reference time never moves backward and never drifts: it is replaced
/// only by a newer trade, and kept when the last trade leaves the history window, so each step is
/// raised once. Which alerts were raised is kept in memory only, so after a restart the current
/// step is raised once more.
/// </remarks>
public sealed class InactivityMonitor
{
    private readonly AlertService _alerts;
    private readonly Journal _journal;
    private DateTime? _lastTradeUtc;
    private bool _lastTradeKnown;
    private long _alertedStep = -1;

    /// <summary>Creates the monitor (OPS-006).</summary>
    /// <param name="alerts">The alert service.</param>
    /// <param name="journal">The journal.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public InactivityMonitor(AlertService alerts, Journal journal)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(journal);
        _alerts = alerts;
        _journal = journal;
    }

    /// <summary>
    /// Measures the inactivity and raises the alert when a new step is reached: step 0 at 21 days,
    /// step n at 21 + 3n days (OPS-006).
    /// </summary>
    /// <param name="serverTimeUtc">Server time, kind UTC.</param>
    /// <param name="history">Closed trades of the account from the broker.</param>
    /// <param name="historySinceUtc">Earliest close time from which <paramref name="history"/> is complete.</param>
    /// <param name="positions">Open positions of the account from the broker.</param>
    /// <returns>The measured inactivity and whether an alert was raised.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="history"/> or <paramref name="positions"/> is null.</exception>
    public InactivityStatus Check(DateTime serverTimeUtc, IEnumerable<ClosedTradeInfo> history, DateTime historySinceUtc, IEnumerable<PositionInfo> positions)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(positions);
        DateTime? latest = null;
        foreach (ClosedTradeInfo trade in history)
        {
            if (trade != null && (!latest.HasValue || trade.EntryTimeUtc > latest.Value))
            {
                latest = trade.EntryTimeUtc;
            }
        }

        foreach (PositionInfo position in positions)
        {
            if (position != null && (!latest.HasValue || position.OpenTimeUtc > latest.Value))
            {
                latest = position.OpenTimeUtc;
            }
        }

        DateTime last = UpdateReference(latest, historySinceUtc);
        bool known = _lastTradeKnown;
        TimeSpan inactive = serverTimeUtc - last;
        TimeSpan first = TimeSpan.FromDays(OpsParameters.InactivityAlertDays);
        if (inactive < first)
        {
            return new InactivityStatus(last, known, inactive, false);
        }

        long step = (inactive - first).Ticks / TimeSpan.FromDays(OpsParameters.InactivityRepeatDays).Ticks;
        if (step <= _alertedStep)
        {
            return new InactivityStatus(last, known, inactive, false);
        }

        _alertedStep = step;
        string days = inactive.TotalDays.ToString("F1", CultureInfo.InvariantCulture);
        string text = known
            ? "No trade opened for " + days + " days (last opened " + JournalFormat.Time(last) + "). FTMO may close an account after 30 days without a trade."
            : "No trade found since " + JournalFormat.Time(last) + " (" + days + " days). FTMO may close an account after 30 days without a trade.";
        _journal.Write(serverTimeUtc, "inactivity", new (string, string?)[]
        {
            ("days", days),
            ("last", JournalFormat.Time(last)),
            ("known", known ? "true" : "false"),
            ("step", JournalFormat.Integer(step)),
        });
        _alerts.Raise(AlertKind.Inactivity, text, serverTimeUtc);
        return new InactivityStatus(last, known, inactive, true);
    }

    /// <summary>
    /// Keeps the reference time of the last trade (OPS-006): a trade newer than the reference
    /// replaces it and starts the count again; otherwise the reference stays, also when the last
    /// trade has left the history window. Without any trade, the first history start is used and
    /// then kept, so the reference never drifts with the clock.
    /// </summary>
    private DateTime UpdateReference(DateTime? latest, DateTime historySinceUtc)
    {
        if (latest.HasValue && (!_lastTradeUtc.HasValue || latest.Value > _lastTradeUtc.Value))
        {
            _lastTradeUtc = latest.Value;
            _lastTradeKnown = true;
            _alertedStep = -1;
        }
        else if (!_lastTradeUtc.HasValue)
        {
            _lastTradeUtc = historySinceUtc;
            _lastTradeKnown = false;
            _alertedStep = -1;
        }

        return _lastTradeUtc.Value;
    }
}
