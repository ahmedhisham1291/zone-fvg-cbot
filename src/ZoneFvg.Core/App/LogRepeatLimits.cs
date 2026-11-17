using System;
using System.Collections.Generic;

namespace ZoneFvg.Core.App;

/// <summary>
/// Repeat limits for the Log (OPS-008, owner acceptance of 2026-11-17), so that a lasting
/// condition cannot fill the cTrader Log. Fixed values, not parameters. It decides only whether a
/// journal line (and, for errors, its alert) is written; no trading decision reads it. All times
/// are server time. Kept in memory for one <see cref="BotCore"/>.
/// </summary>
internal sealed class LogRepeatLimits
{
    /// <summary>Least time between two written lines of a recovery that keeps failing for missing or stale data.</summary>
    internal static readonly TimeSpan RecoveryInterval = TimeSpan.FromHours(24.0);

    /// <summary>Least time between two written lines of the same error (handler, exception type and message).</summary>
    internal static readonly TimeSpan ErrorInterval = TimeSpan.FromMinutes(60.0);

    /// <summary>Least time between two written risk-unavailable lines with the same text.</summary>
    internal static readonly TimeSpan RiskInterval = TimeSpan.FromMinutes(60.0);

    // Error groups by key; ordinal order, never hash order. Groups with nothing left out are dropped once their interval has passed.
    private readonly SortedDictionary<string, ErrorGroup> _errors = new SortedDictionary<string, ErrorGroup>(StringComparer.Ordinal);

    private DateTime? _recoveryWrittenUtc;
    private int _failedRuns;
    private string? _riskText;
    private DateTime _riskWrittenUtc;

    /// <summary>Starts the recovery limit again: after a start, a reconnect or a completed recovery (OPS-008 (1)).</summary>
    internal void ResetRecovery()
    {
        _recoveryWrittenUtc = null;
        _failedRuns = 0;
    }

    /// <summary>
    /// Counts one failed recovery run (OPS-008 (1)). Returns the number of failed runs since the
    /// last written one, this run included, when this run is to be written: the first after a
    /// reset, then at most once per <see cref="RecoveryInterval"/>. Returns null when it is left out.
    /// </summary>
    /// <param name="nowUtc">Server time of the run.</param>
    /// <returns>The failed runs to write, or null.</returns>
    internal int? FailedRecovery(DateTime nowUtc)
    {
        _failedRuns++;
        if (_recoveryWrittenUtc.HasValue && nowUtc - _recoveryWrittenUtc.Value < RecoveryInterval)
        {
            return null;
        }

        int runs = _failedRuns;
        _failedRuns = 0;
        _recoveryWrittenUtc = nowUtc;
        return runs;
    }

    /// <summary>
    /// Decides whether an error is journaled and alerted (OPS-008 (2)). An error equal to the last
    /// written error of its group (same handler, exception type and message) less than
    /// <see cref="ErrorInterval"/> earlier is left out and counted. Returns the number left out
    /// since the group's last written line when this error is to be written, or null when it is
    /// left out.
    /// </summary>
    /// <param name="handler">The handler that caught it.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="nowUtc">Server time.</param>
    /// <returns>The number left out before this one, or null when this one is left out.</returns>
    internal int? Error(string handler, Exception exception, DateTime nowUtc)
    {
        string key = handler + "\n" + exception.GetType().FullName + "\n" + exception.Message;
        if (_errors.TryGetValue(key, out ErrorGroup? group) && nowUtc - group.WrittenUtc < ErrorInterval)
        {
            group.LeftOut++;
            return null;
        }

        int leftOut = group?.LeftOut ?? 0;
        Forget(nowUtc);
        _errors[key] = new ErrorGroup(nowUtc);
        return leftOut;
    }

    /// <summary>
    /// Decides whether a risk-unavailable line is written (OPS-008 (3)): when its text differs from
    /// the last one written, or when <see cref="RiskInterval"/> has passed since that line.
    /// </summary>
    /// <param name="text">The text of the line.</param>
    /// <param name="nowUtc">Server time.</param>
    /// <returns>True when the line is to be written.</returns>
    internal bool RiskUnavailable(string text, DateTime nowUtc)
    {
        if (string.Equals(text, _riskText, StringComparison.Ordinal) && nowUtc - _riskWrittenUtc < RiskInterval)
        {
            return false;
        }

        _riskText = text;
        _riskWrittenUtc = nowUtc;
        return true;
    }

    private void Forget(DateTime nowUtc)
    {
        var old = new List<string>();
        foreach (KeyValuePair<string, ErrorGroup> pair in _errors)
        {
            if (pair.Value.LeftOut == 0 && nowUtc - pair.Value.WrittenUtc >= ErrorInterval)
            {
                old.Add(pair.Key);
            }
        }

        foreach (string key in old)
        {
            _errors.Remove(key);
        }
    }

    private sealed class ErrorGroup
    {
        internal ErrorGroup(DateTime writtenUtc)
        {
            WrittenUtc = writtenUtc;
        }

        internal DateTime WrittenUtc { get; }

        internal int LeftOut { get; set; }
    }
}
