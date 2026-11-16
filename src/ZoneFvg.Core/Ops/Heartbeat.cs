using System;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// The dead-man's switch (OPS-005, spec 11): calls HeartbeatUrl through
/// <see cref="IHttpTransport"/> when <see cref="OpsParameters.HeartbeatIntervalMinutes"/> minutes
/// of server time have passed since the last call, in every bot state. The first call happens at
/// the first <see cref="Pump"/>. A failed call is journaled (address masked, OPS-007) and never
/// affects the caller: <see cref="Pump"/> never throws. A failed call is not repeated before the
/// interval has passed again. With an empty HeartbeatUrl nothing is called.
/// </summary>
public sealed class Heartbeat
{
    private readonly OpsParameters _parameters;
    private readonly IHttpTransport _http;
    private readonly Journal _journal;
    private readonly SecretMasker _masker;

    /// <summary>Creates the heartbeat. The parameters are validated here (OPS-005).</summary>
    /// <param name="parameters">The operations parameters with HeartbeatUrl.</param>
    /// <param name="http">The HTTP port.</param>
    /// <param name="journal">The journal.</param>
    /// <param name="masker">The secret masker (OPS-007).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public Heartbeat(OpsParameters parameters, IHttpTransport http, Journal journal, SecretMasker masker)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(masker);
        parameters.Validate();
        _parameters = parameters;
        _http = http;
        _journal = journal;
        _masker = masker;
    }

    /// <summary>Server time of the last call, or null before the first (OPS-005).</summary>
    public DateTime? LastCallUtc { get; private set; }

    /// <summary>Calls made (OPS-005).</summary>
    public int Calls { get; private set; }

    /// <summary>Calls that failed (OPS-005).</summary>
    public int Failures { get; private set; }

    /// <summary>
    /// Calls the heartbeat address when it is due: no call yet, the interval has passed since the
    /// last call (exactly the interval counts), or server time is before the last call (OPS-005).
    /// </summary>
    /// <param name="serverTimeUtc">Server time, kind UTC.</param>
    /// <param name="state">Current bot state; journaled with a failure, never a reason to skip the call.</param>
    /// <returns>True when a call was made in this pump.</returns>
    public bool Pump(DateTime serverTimeUtc, BotState state)
    {
        try
        {
            return PumpCore(serverTimeUtc, state);
        }
        catch (Exception exception)
        {
            // Never affects trading (OPS-005); journaled, so not silent.
            _journal.Error(serverTimeUtc, "Heartbeat.Pump", exception);
            return false;
        }
    }

    private bool PumpCore(DateTime now, BotState state)
    {
        if (!_parameters.HeartbeatEnabled)
        {
            return false;
        }

        if (LastCallUtc.HasValue && now >= LastCallUtc.Value && now - LastCallUtc.Value < TimeSpan.FromMinutes(OpsParameters.HeartbeatIntervalMinutes))
        {
            return false;
        }

        LastCallUtc = now;
        Calls++;
        HttpResult? result = null;
        string error = string.Empty;
        try
        {
            result = _http.Get(_parameters.HeartbeatUrl, OpsParameters.HttpTimeoutSeconds);
        }
        catch (Exception exception)
        {
            // The port must not throw; if it does, it counts as a failed call (OPS-005).
            error = _masker.Apply(exception);
        }

        if (result == null || !result.Ok)
        {
            Failures++;
            _journal.Write(now, "heartbeat", new (string, string?)[]
            {
                ("event", "failed"),
                ("status", JournalFormat.Integer(result?.Status ?? 0)),
                ("state", state.ToString()),
                ("failures", JournalFormat.Integer(Failures)),
                ("error", error),
            });
        }

        return true;
    }
}
