using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZoneFvg.Core.Risk;

/// <summary>Decision of a request or error counter (RSK-010).</summary>
public sealed class CircuitBreakerDecision
{
    internal CircuitBreakerDecision(bool halt, int lastHourCount, int todayCount, int hourLimit, int? dayLimit, string detail)
    {
        Halt = halt;
        LastHourCount = lastHourCount;
        TodayCount = todayCount;
        HourLimit = hourLimit;
        DayLimit = dayLimit;
        Detail = detail;
    }

    /// <summary>True when a limit is exceeded and the instance must halt (RSK-010).</summary>
    public bool Halt { get; }

    /// <summary>Events in the last hour: later than one hour before the evaluation time.</summary>
    public int LastHourCount { get; }

    /// <summary>Events at or after the start of the CE(S)T day of the evaluation time.</summary>
    public int TodayCount { get; }

    /// <summary>Most events allowed in one hour.</summary>
    public int HourLimit { get; }

    /// <summary>Most events allowed in one CE(S)T day, or null when there is no daily limit.</summary>
    public int? DayLimit { get; }

    /// <summary>Short invariant text naming the exceeded limit; empty when not halted.</summary>
    public string Detail { get; }
}

/// <summary>
/// Counts order requests of this instance (RSK-010): at most MaxRequestsPerHour (20) in a
/// sliding hour and MaxRequestsPerDay (100) in a CE(S)T day. Exceeding either returns Halt. This
/// in-memory counter is the one piece of mutable state RSK-011 allows in the risk module.
/// </summary>
/// <remarks>
/// Time comes from the caller (server time). A request exactly one hour old has left the hourly
/// window. If a server time earlier than one already seen is passed, the latest time seen is used,
/// so a clock that steps back cannot shrink a window.
/// </remarks>
public sealed class RequestCounter
{
    private readonly EventWindow _window;

    /// <summary>Creates a counter with the limits of the parameters (RSK-010).</summary>
    /// <param name="parameters">The risk parameters; validated here.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public RequestCounter(RiskParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        _window = new EventWindow("requests", parameters.MaxRequestsPerHour, parameters.MaxRequestsPerDay);
    }

    /// <summary>Records one order request at the server time and returns the decision (RSK-010).</summary>
    /// <param name="serverTimeUtc">Server time of the request; kind UTC.</param>
    /// <returns>The decision after counting this request.</returns>
    /// <exception cref="ArgumentException">The time is not UTC.</exception>
    public CircuitBreakerDecision Record(DateTime serverTimeUtc)
    {
        return _window.Record(serverTimeUtc);
    }

    /// <summary>Returns the decision at the server time without recording a request (RSK-010).</summary>
    /// <param name="serverTimeUtc">Server time; kind UTC.</param>
    /// <returns>The current decision.</returns>
    /// <exception cref="ArgumentException">The time is not UTC.</exception>
    public CircuitBreakerDecision Evaluate(DateTime serverTimeUtc)
    {
        return _window.Evaluate(serverTimeUtc);
    }
}

/// <summary>
/// Counts unhandled errors of this instance (RSK-010): at most MaxErrorsPerHour (5) in a sliding
/// hour. Exceeding it returns Halt. Same time rules as <see cref="RequestCounter"/>.
/// </summary>
public sealed class ErrorCounter
{
    private readonly EventWindow _window;

    /// <summary>Creates a counter with the limit of the parameters (RSK-010).</summary>
    /// <param name="parameters">The risk parameters; validated here.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public ErrorCounter(RiskParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        _window = new EventWindow("errors", parameters.MaxErrorsPerHour, null);
    }

    /// <summary>Records one unhandled error at the server time and returns the decision (RSK-010).</summary>
    /// <param name="serverTimeUtc">Server time of the error; kind UTC.</param>
    /// <returns>The decision after counting this error.</returns>
    /// <exception cref="ArgumentException">The time is not UTC.</exception>
    public CircuitBreakerDecision Record(DateTime serverTimeUtc)
    {
        return _window.Record(serverTimeUtc);
    }

    /// <summary>Returns the decision at the server time without recording an error (RSK-010).</summary>
    /// <param name="serverTimeUtc">Server time; kind UTC.</param>
    /// <returns>The current decision.</returns>
    /// <exception cref="ArgumentException">The time is not UTC.</exception>
    public CircuitBreakerDecision Evaluate(DateTime serverTimeUtc)
    {
        return _window.Evaluate(serverTimeUtc);
    }
}

/// <summary>Event times in a sliding hour and the current CE(S)T day (RSK-010).</summary>
internal sealed class EventWindow
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1.0);

    private readonly string _name;
    private readonly int _hourLimit;
    private readonly int? _dayLimit;
    private readonly List<DateTime> _times = new List<DateTime>();
    private DateTime? _latestUtc;

    internal EventWindow(string name, int hourLimit, int? dayLimit)
    {
        _name = name;
        _hourLimit = hourLimit;
        _dayLimit = dayLimit;
    }

    internal CircuitBreakerDecision Record(DateTime serverTimeUtc)
    {
        DateTime now = Advance(serverTimeUtc);
        _times.Add(serverTimeUtc);
        return Decide(now);
    }

    internal CircuitBreakerDecision Evaluate(DateTime serverTimeUtc)
    {
        return Decide(Advance(serverTimeUtc));
    }

    private DateTime Advance(DateTime serverTimeUtc)
    {
        if (serverTimeUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The server time must be a UTC time (kind UTC).", nameof(serverTimeUtc));
        }

        DateTime latest = _latestUtc.HasValue && _latestUtc.Value > serverTimeUtc ? _latestUtc.Value : serverTimeUtc;
        _latestUtc = latest;

        // Events that are outside both windows now can never count again, because the windows only move forward.
        DateTime hourStart = latest - Hour;
        DateTime dayStart = CestClock.StartOfCestDayUtc(latest);
        _times.RemoveAll(time => time <= hourStart && time < dayStart);
        return latest;
    }

    private CircuitBreakerDecision Decide(DateTime now)
    {
        DateTime hourStart = now - Hour;
        DateTime dayStart = CestClock.StartOfCestDayUtc(now);
        int lastHour = 0;
        int today = 0;
        foreach (DateTime time in _times)
        {
            lastHour += time > hourStart ? 1 : 0;
            today += time >= dayStart ? 1 : 0;
        }

        if (lastHour > _hourLimit)
        {
            return new CircuitBreakerDecision(true, lastHour, today, _hourLimit, _dayLimit, _name + " in the last hour " + Text(lastHour) + " above " + Text(_hourLimit));
        }

        if (_dayLimit.HasValue && today > _dayLimit.Value)
        {
            return new CircuitBreakerDecision(true, lastHour, today, _hourLimit, _dayLimit, _name + " today " + Text(today) + " above " + Text(_dayLimit.Value));
        }

        return new CircuitBreakerDecision(false, lastHour, today, _hourLimit, _dayLimit, string.Empty);
    }

    private static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
