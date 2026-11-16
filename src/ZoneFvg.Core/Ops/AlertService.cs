using System;
using System.Collections.Generic;
using System.Linq;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Ops;

/// <summary>What <see cref="AlertService.Raise"/> did with an alert (OPS-004).</summary>
public enum AlertRaiseOutcome
{
    /// <summary>The alert waits in the queue for <see cref="AlertService.Pump"/>.</summary>
    Queued,

    /// <summary>An identical alert was raised less than the collapse window earlier; this one is not sent.</summary>
    Collapsed,

    /// <summary>No Telegram token and chat id are set; the alert was journaled only.</summary>
    Disabled,

    /// <summary>The alert could not be prepared; the error was journaled.</summary>
    Failed,
}

/// <summary>
/// Best-effort Telegram alerts (OPS-004, spec 11). <see cref="Raise"/> only queues;
/// <see cref="Pump"/>, called from the bot's timer, sends through <see cref="IHttpTransport"/>
/// to the Telegram Bot API <c>sendMessage</c> method. Neither method ever throws into the caller.
/// </summary>
/// <remarks>
/// <para>Text: plain, prefixed with <c>ZFVG &lt;instance&gt; &lt;kind&gt;: </c>, masked
/// (OPS-007), and cut to <see cref="OpsParameters.MaxAlertLength"/> characters, the last three
/// being <c>...</c> when cut.</para>
/// <para>Collapse: an alert with the same kind and text as one queued less than
/// <see cref="OpsParameters.CollapseWindowMinutes"/> earlier is counted and journaled, not queued.
/// The window runs from the alert that was queued, so a problem that repeats is sent again once
/// per window.</para>
/// <para>Queue: at most <see cref="OpsParameters.AlertQueueCapacity"/> alerts; when full the oldest
/// is dropped and counted. A pump sends at most <see cref="OpsParameters.AlertsPerPump"/>, oldest
/// first. A failed send (no success status, or the transport threw) keeps the alert at the head
/// and stops the pump; the next attempt waits <see cref="OpsParameters.BackoffFirstMinutes"/>
/// minute, doubled after each further failure up to <see cref="OpsParameters.BackoffMaxMinutes"/>.
/// A response with a 4xx status other than 429 means Telegram refuses that message for good: it is
/// removed, journaled and counted, and the pump goes on with the next one.</para>
/// <para>Journal: the URL contains the token and is never journaled. Each step writes a line of
/// category <c>alert</c> with the kind and the hash of the text (<see cref="JournalFormat.Hash8"/>).</para>
/// </remarks>
public sealed class AlertService
{
    /// <summary>Start of every Telegram request address; the token follows (OPS-004).</summary>
    public const string TelegramApiBase = "https://api.telegram.org/bot";

    private const string Ellipsis = "...";

    private readonly OpsParameters _parameters;
    private readonly string _instance;
    private readonly IHttpTransport _http;
    private readonly Journal _journal;
    private readonly SecretMasker _masker;
    private readonly List<QueuedAlert> _queue = new();
    private readonly Dictionary<string, DateTime> _recent = new(StringComparer.Ordinal);
    private int _consecutiveFailures;

    /// <summary>Creates the service. The parameters are validated here (OPS-004).</summary>
    /// <param name="parameters">The operations parameters with the token and chat id.</param>
    /// <param name="instance">Name of this bot instance in the alert text, for example the symbol.</param>
    /// <param name="http">The HTTP port.</param>
    /// <param name="journal">The journal.</param>
    /// <param name="masker">The secret masker (OPS-007).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public AlertService(OpsParameters parameters, string instance, IHttpTransport http, Journal journal, SecretMasker masker)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(masker);
        parameters.Validate();
        _parameters = parameters;
        _instance = instance;
        _http = http;
        _journal = journal;
        _masker = masker;
    }

    /// <summary>True when a token and a chat id are set, so alerts are sent (OPS-004).</summary>
    public bool Enabled => _parameters.AlertsEnabled;

    /// <summary>Alerts waiting to be sent.</summary>
    public int QueueLength => _queue.Count;

    /// <summary>Alerts sent with success.</summary>
    public int Sent { get; private set; }

    /// <summary>Failed send attempts.</summary>
    public int FailedAttempts { get; private set; }

    /// <summary>Alerts dropped because the queue was full.</summary>
    public int Dropped { get; private set; }

    /// <summary>Alerts not queued because an identical one was raised within the collapse window.</summary>
    public int Collapsed { get; private set; }

    /// <summary>Alerts removed because Telegram refused them for good (4xx status other than 429).</summary>
    public int Refused { get; private set; }

    /// <summary>Server time before which no send is attempted, or null when there is no back-off.</summary>
    public DateTime? NextAttemptUtc { get; private set; }

    /// <summary>The texts waiting in the queue, oldest first (OPS-004). For reports and tests.</summary>
    public IReadOnlyList<string> PendingTexts => _queue.Select(q => q.Text).ToArray();

    /// <summary>
    /// The text sent for an alert (OPS-004, OPS-007): <c>ZFVG &lt;instance&gt; &lt;kind&gt;: &lt;text&gt;</c>,
    /// masked, then cut to <see cref="OpsParameters.MaxAlertLength"/> characters with <c>...</c> at the end when cut.
    /// </summary>
    /// <param name="kind">The alert kind.</param>
    /// <param name="text">The alert text, possibly null.</param>
    /// <returns>The message text.</returns>
    public string Format(OpsAlertKind kind, string? text)
    {
        string message = _masker.Apply("ZFVG " + _instance + " " + kind + ": " + (text ?? string.Empty));
        if (message.Length > OpsParameters.MaxAlertLength)
        {
            int cut = OpsParameters.MaxAlertLength - Ellipsis.Length;
            if (char.IsHighSurrogate(message[cut - 1]))
            {
                // Never split a surrogate pair; the request address could not be built from half of one.
                cut--;
            }

            message = message.Substring(0, cut) + Ellipsis;
        }

        return message;
    }

    /// <summary>
    /// Queues an alert (OPS-004). Never throws and never calls the transport. With alerts disabled
    /// the alert is journaled only; an identical alert within the collapse window is counted and
    /// journaled only; with a full queue the oldest alert is dropped.
    /// </summary>
    /// <param name="kind">The alert kind.</param>
    /// <param name="text">The alert text, possibly null.</param>
    /// <param name="atUtc">Server time, kind UTC.</param>
    /// <returns>What happened to the alert.</returns>
    public AlertRaiseOutcome Raise(OpsAlertKind kind, string? text, DateTime atUtc)
    {
        try
        {
            return RaiseCore(kind, text, atUtc);
        }
        catch (Exception exception)
        {
            // Never thrown into trading logic (OPS-004); journaled, so not silent.
            _journal.Error(atUtc, "AlertService.Raise", exception);
            return AlertRaiseOutcome.Failed;
        }
    }

    /// <summary>
    /// Sends queued alerts, at most <see cref="OpsParameters.AlertsPerPump"/>, unless alerts are
    /// disabled or a back-off is running (OPS-004). Never throws; a failure keeps the alert queued.
    /// </summary>
    /// <param name="serverTimeUtc">Server time, kind UTC.</param>
    /// <returns>Alerts sent with success in this call.</returns>
    public int Pump(DateTime serverTimeUtc)
    {
        try
        {
            return PumpCore(serverTimeUtc);
        }
        catch (Exception exception)
        {
            // Never thrown into trading logic (OPS-004); journaled, so not silent.
            _journal.Error(serverTimeUtc, "AlertService.Pump", exception);
            return 0;
        }
    }

    private AlertRaiseOutcome RaiseCore(OpsAlertKind kind, string? text, DateTime atUtc)
    {
        string message = Format(kind, text);
        string hash = JournalFormat.Hash8(message);
        if (!Enabled)
        {
            Log(atUtc, "disabled", kind, hash);
            return AlertRaiseOutcome.Disabled;
        }

        string key = kind + "\n" + message;
        TimeSpan window = TimeSpan.FromMinutes(OpsParameters.CollapseWindowMinutes);
        if (_recent.TryGetValue(key, out DateTime last) && atUtc >= last && atUtc - last < window)
        {
            Collapsed++;
            Log(atUtc, "collapsed", kind, hash);
            return AlertRaiseOutcome.Collapsed;
        }

        foreach (string old in _recent.Where(p => !(atUtc >= p.Value && atUtc - p.Value < window)).Select(p => p.Key).ToArray())
        {
            _recent.Remove(old);
        }

        string url = TelegramApiBase + _parameters.TelegramBotToken + "/sendMessage?chat_id="
            + Uri.EscapeDataString(_parameters.TelegramChatId) + "&text=" + Uri.EscapeDataString(message);
        _recent[key] = atUtc;
        if (_queue.Count >= OpsParameters.AlertQueueCapacity)
        {
            QueuedAlert oldest = _queue[0];
            _queue.RemoveAt(0);
            Dropped++;
            Log(atUtc, "dropped", oldest.Kind, oldest.Hash);
        }

        _queue.Add(new QueuedAlert(kind, message, hash, url));
        Log(atUtc, "queued", kind, hash);
        return AlertRaiseOutcome.Queued;
    }

    private int PumpCore(DateTime now)
    {
        if (!Enabled || (NextAttemptUtc.HasValue && now < NextAttemptUtc.Value))
        {
            return 0;
        }

        int sent = 0;
        int attempts = 0;
        while (attempts < OpsParameters.AlertsPerPump && _queue.Count > 0)
        {
            attempts++;
            QueuedAlert alert = _queue[0];
            HttpResult? result = null;
            string error = string.Empty;
            try
            {
                result = _http.Get(alert.Url, OpsParameters.HttpTimeoutSeconds);
            }
            catch (Exception exception)
            {
                // The port must not throw; if it does, it counts as a failed send (OPS-004).
                error = _masker.Apply(exception);
            }

            if (result != null && result.Ok)
            {
                _queue.RemoveAt(0);
                Sent++;
                sent++;
                _consecutiveFailures = 0;
                NextAttemptUtc = null;
                Log(now, "sent", alert.Kind, alert.Hash);
                continue;
            }

            int status = result?.Status ?? 0;
            if (status >= 400 && status < 500 && status != 429)
            {
                _queue.RemoveAt(0);
                Refused++;
                Log(now, "refused", alert.Kind, alert.Hash, ("status", JournalFormat.Integer(status)));
                continue;
            }

            Fail(now, alert, status, error);
            break;
        }

        return sent;
    }

    private void Fail(DateTime now, QueuedAlert alert, int status, string error)
    {
        FailedAttempts++;
        _consecutiveFailures++;
        int minutes = OpsParameters.BackoffFirstMinutes;
        for (int i = 1; i < _consecutiveFailures && minutes < OpsParameters.BackoffMaxMinutes; i++)
        {
            minutes *= 2;
        }

        minutes = Math.Min(minutes, OpsParameters.BackoffMaxMinutes);
        NextAttemptUtc = now.AddMinutes(minutes);
        Log(
            now,
            "failed",
            alert.Kind,
            alert.Hash,
            ("status", JournalFormat.Integer(status)),
            ("failures", JournalFormat.Integer(_consecutiveFailures)),
            ("retryAt", JournalFormat.Time(NextAttemptUtc.Value)),
            ("error", error));
    }

    private void Log(DateTime at, string what, OpsAlertKind kind, string hash, params (string Key, string? Value)[] extra)
    {
        var fields = new List<(string Key, string? Value)>
        {
            ("event", what),
            ("kind", kind.ToString()),
            ("hash", hash),
        };
        fields.AddRange(extra);
        fields.Add(("queue", JournalFormat.Integer(_queue.Count)));
        _journal.Write(at, "alert", fields);
    }

    private sealed record QueuedAlert(OpsAlertKind Kind, string Text, string Hash, string Url);
}
