using System;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// The operations module of one bot instance, wired together (OPS-001 to OPS-007): masker,
/// journal, alert service, heartbeat, daily summary, inactivity monitor and the execution hook.
/// <c>BotCore</c> (WP07) creates one and calls <see cref="OnTimer"/> from its timer.
/// </summary>
public sealed class Operations
{
    /// <summary>Creates and wires the module. The parameters are validated here.</summary>
    /// <param name="parameters">Operations parameters.</param>
    /// <param name="risk">Risk parameters, for the daily summary.</param>
    /// <param name="instance">Name of this instance in alert texts, for example the symbol.</param>
    /// <param name="broker">The broker port, for the time of execution alerts.</param>
    /// <param name="http">The HTTP port for Telegram and the heartbeat.</param>
    /// <param name="sink">The journal port.</param>
    /// <param name="startedUtc">Server time at start, kind UTC.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    /// <exception cref="ArgumentException"><paramref name="startedUtc"/> is not UTC.</exception>
    public Operations(OpsParameters parameters, RiskParameters risk, string instance, IBroker broker, IHttpTransport http, IJournalSink sink, DateTime startedUtc)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        Masker = SecretMasker.For(parameters);
        Journal = new Journal(sink, Masker);
        Alerts = new AlertService(parameters, instance, http, Journal, Masker);
        Heartbeat = new Heartbeat(parameters, http, Journal, Masker);
        Summary = new DailySummary(parameters, risk, Journal, Alerts, startedUtc);
        Inactivity = new InactivityMonitor(Alerts, Journal);
        Events = new OpsEvents(Journal, Alerts, broker);
    }

    /// <summary>The secret masker (OPS-007).</summary>
    public SecretMasker Masker { get; }

    /// <summary>The journal (OPS-001, OPS-002).</summary>
    public Journal Journal { get; }

    /// <summary>The Telegram alert service (OPS-004).</summary>
    public AlertService Alerts { get; }

    /// <summary>The heartbeat (OPS-005).</summary>
    public Heartbeat Heartbeat { get; }

    /// <summary>The daily summary (OPS-003).</summary>
    public DailySummary Summary { get; }

    /// <summary>The inactivity monitor (OPS-006).</summary>
    public InactivityMonitor Inactivity { get; }

    /// <summary>The execution hook to pass to the execution layer (OPS-001, OPS-004).</summary>
    public OpsEvents Events { get; }

    /// <summary>
    /// The timer duties of the module, in every bot state: the heartbeat first, so a slow alert
    /// send cannot delay it (OPS-005), then the alert queue (OPS-004). Never throws. The daily
    /// summary and the inactivity check need broker data and are called by <c>BotCore</c> before
    /// this, so that their alerts go out in the same pump.
    /// </summary>
    /// <param name="serverTimeUtc">Server time, kind UTC.</param>
    /// <param name="state">Current bot state.</param>
    public void OnTimer(DateTime serverTimeUtc, BotState state)
    {
        Heartbeat.Pump(serverTimeUtc, state);
        Alerts.Pump(serverTimeUtc);
    }
}
