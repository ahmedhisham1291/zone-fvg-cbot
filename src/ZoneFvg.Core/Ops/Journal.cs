using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// The bot's journal (OPS-001, OPS-002, spec 11). Each typed method formats one line
/// <c>&lt;UTC time&gt; &lt;category&gt; key=value ...</c> with the keys in a fixed order, invariant
/// formatting and escaped values (<see cref="JournalFormat"/>), masks every secret
/// (<see cref="SecretMasker"/>, OPS-007) and writes it through <see cref="IJournalSink"/>.
/// </summary>
/// <remarks>
/// <para>No method throws because the sink failed: a write that throws is counted in
/// <see cref="WriteFailures"/> and its masked message kept in <see cref="LastWriteFailure"/>, so
/// a broken log can never stop trading logic. A null argument is a programming error and throws.</para>
/// <para>The journal also keeps per-CE(S)T-day counters of the lines it wrote (trades, net R,
/// skips by reason) for the daily summary (OPS-003). They live in memory only and start empty
/// after a restart; the most recent 8 days are kept.</para>
/// </remarks>
public sealed class Journal
{
    /// <summary>Days of counters kept (OPS-003).</summary>
    public const int CounterDays = 8;

    private readonly IJournalSink _sink;
    private readonly SecretMasker _masker;
    private readonly SortedDictionary<DateTime, JournalDayCounters> _days = new();

    /// <summary>Creates a journal (OPS-001).</summary>
    /// <param name="sink">The journal port.</param>
    /// <param name="masker">The secret masker applied to every line (OPS-007).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public Journal(IJournalSink sink, SecretMasker masker)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(masker);
        _sink = sink;
        _masker = masker;
    }

    /// <summary>Number of writes for which the sink threw (OPS-001).</summary>
    public int WriteFailures { get; private set; }

    /// <summary>Masked description of the last exception thrown by the sink; empty when none (OPS-001, OPS-007).</summary>
    public string LastWriteFailure { get; private set; } = string.Empty;

    /// <summary>
    /// Journals one signal, taken or skipped, with the fields of spec 11 in this order: symbol,
    /// result (taken or skipped), bias, zone, zoneProximal, zoneDistal, fvgUpper, fvgLower,
    /// entry, stop, target, stopPips (1 decimal), volume (0 decimals), spreadPips (1 decimal),
    /// costToRisk (3 decimals), news, skip (reason or <c>none</c>) (OPS-001). For a breakout signal
    /// two more keys follow: kind (<c>breakout</c>) and level (OPS-010).
    /// </summary>
    /// <param name="entry">The signal fields.</param>
    /// <returns>The line written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    public string Signal(SignalJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var fields = new List<(string, string?)>
        {
            ("symbol", entry.Symbol),
            ("result", entry.Skip.HasValue ? "skipped" : "taken"),
            ("bias", entry.Bias.ToString()),
            ("zone", entry.ZoneId),
            ("zoneProximal", JournalFormat.Price(entry.ZoneProximal)),
            ("zoneDistal", JournalFormat.Price(entry.ZoneDistal)),
            ("fvgUpper", JournalFormat.Price(entry.FvgUpper)),
            ("fvgLower", JournalFormat.Price(entry.FvgLower)),
            ("entry", JournalFormat.Price(entry.Entry)),
            ("stop", JournalFormat.Price(entry.Stop)),
            ("target", JournalFormat.Price(entry.Target)),
            ("stopPips", JournalFormat.Fixed(entry.StopPips, 1)),
            ("volume", JournalFormat.Fixed(entry.VolumeUnits, 0)),
            ("spreadPips", JournalFormat.Fixed(entry.SpreadPips, 1)),
            ("costToRisk", JournalFormat.Fixed(entry.CostToRisk, 3)),
            ("news", entry.News.HasValue ? entry.News.Value.ToString() : JournalFormat.NotAvailable),
            ("skip", entry.Skip.HasValue ? entry.Skip.Value.ToString() : "none"),
        };

        // OPS-010: two more keys for a breakout signal only; a ZoneFvg line is unchanged.
        if (entry.Kind == SignalKind.Breakout)
        {
            fields.Add(("kind", "breakout"));
            fields.Add(("level", JournalFormat.Price(entry.Level)));
        }

        return Emit(entry.TimeUtc, "signal", fields);
    }

    /// <summary>
    /// Journals a skip record of the execution layer (EXE-014) with the keys symbol, zone,
    /// reason, measured, limit (4 decimals) and detail, and counts it for the daily summary
    /// (OPS-001, OPS-003).
    /// </summary>
    /// <param name="skip">The skip record.</param>
    /// <returns>The line written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="skip"/> is null.</exception>
    public string Skip(SkipRecord skip)
    {
        ArgumentNullException.ThrowIfNull(skip);
        JournalDayCounters? day = DayOf(skip.AtUtc);
        day?.AddSkip(skip.Reason);
        return Emit(skip.AtUtc, "skip", new (string, string?)[]
        {
            ("symbol", skip.Symbol),
            ("zone", skip.ZoneId),
            ("reason", skip.Reason.ToString()),
            ("measured", JournalFormat.Fixed(skip.Measured, 4)),
            ("limit", JournalFormat.Fixed(skip.Limit, 4)),
            ("detail", skip.Detail),
        });
    }

    /// <summary>
    /// Journals one closed trade with the fields of spec 11 in this order: symbol, position,
    /// label, side, requested, filled, slippagePips (1 decimal), exit, r (2 decimals), money,
    /// commission, swap (2 decimals each), duration, mae, mfe (2 decimals each), at the trade's
    /// close time, and counts it for the daily summary (OPS-002, OPS-003). A missing or invalid
    /// comment gives <c>r=NA</c>.
    /// </summary>
    /// <param name="entry">The trade fields.</param>
    /// <returns>The line written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> or its trade is null.</exception>
    public string Trade(TradeJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Trade, nameof(entry));
        ClosedTradeInfo trade = entry.Trade;
        double? r = entry.ResultR;
        DayOf(trade.CloseTimeUtc)?.AddTrade(r);
        return Emit(trade.CloseTimeUtc, "trade", new (string, string?)[]
        {
            ("symbol", trade.Symbol),
            ("position", JournalFormat.Integer(trade.PositionId)),
            ("label", trade.Label),
            ("side", entry.Side.HasValue ? entry.Side.Value.ToString() : JournalFormat.NotAvailable),
            ("requested", JournalFormat.Price(entry.RequestedPrice)),
            ("filled", JournalFormat.Price(entry.FilledPrice)),
            ("slippagePips", JournalFormat.Fixed(entry.SlippagePips, 1)),
            ("exit", entry.ExitReason.ToString()),
            ("r", JournalFormat.Fixed(r, 2)),
            ("money", JournalFormat.Fixed(trade.NetProfit, 2)),
            ("commission", JournalFormat.Fixed(trade.Commission, 2)),
            ("swap", JournalFormat.Fixed(trade.Swap, 2)),
            ("duration", JournalFormat.Duration(entry.Duration)),
            ("mae", JournalFormat.Fixed(entry.MaxAdverseR, 2)),
            ("mfe", JournalFormat.Fixed(entry.MaxFavourableR, 2)),
        });
    }

    /// <summary>
    /// Journals a recovery report (spec 8.3 step 10, spec 11) with the keys symbol, trigger,
    /// adopted, kept, cancelled, strangers, zones, downtime and state.
    /// </summary>
    /// <param name="entry">The report.</param>
    /// <returns>The line written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    public string Recovery(RecoveryJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var fields = new List<(string, string?)>
        {
            ("symbol", entry.Symbol),
            ("trigger", entry.Trigger),
            ("adopted", JournalFormat.Integer(entry.PositionsAdopted)),
            ("kept", JournalFormat.Integer(entry.OrdersKept)),
            ("cancelled", JournalFormat.Integer(entry.OrdersCancelled)),
            ("strangers", JournalFormat.Integer(entry.Strangers)),
            ("zones", JournalFormat.Integer(entry.ZonesActive)),
            ("downtime", JournalFormat.Duration(entry.Downtime)),
            ("state", entry.ResultState.ToString()),
        };
        if (entry.FailedRuns.HasValue)
        {
            // OPS-008: a failed run carries the number of failed runs since the last written one.
            fields.Add(("failed-runs", JournalFormat.Integer(entry.FailedRuns.Value)));
        }

        return Emit(entry.TimeUtc, "recovery", fields);
    }

    /// <summary>Journals a bot state change with the keys from, to and reason (spec 8.5, spec 11).</summary>
    /// <param name="atUtc">Server time of the change.</param>
    /// <param name="from">The state left.</param>
    /// <param name="to">The state entered.</param>
    /// <param name="reason">Why, in short text.</param>
    /// <returns>The line written.</returns>
    public string State(DateTime atUtc, BotState from, BotState to, string? reason)
    {
        return Emit(atUtc, "state", new (string, string?)[]
        {
            ("from", from.ToString()),
            ("to", to.ToString()),
            ("reason", reason ?? string.Empty),
        });
    }

    /// <summary>Journals an exception with the keys source and error; the message is masked (OPS-007, REC-011).</summary>
    /// <param name="atUtc">Server time.</param>
    /// <param name="source">Where it happened, for example <c>OnBarClosed</c>.</param>
    /// <param name="exception">The exception.</param>
    /// <returns>The line written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public string Error(DateTime atUtc, string? source, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Error(atUtc, source, _masker.Apply(exception));
    }

    /// <summary>
    /// Journals an exception with the keys source, error and left-out: the number of equal errors
    /// that were not journaled since the last line of their group (OPS-008); the message is masked
    /// (OPS-007, REC-011).
    /// </summary>
    /// <param name="atUtc">Server time.</param>
    /// <param name="source">Where it happened, for example <c>OnTimer</c>.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="leftOut">Equal errors left out since the group's last line.</param>
    /// <returns>The line written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public string Error(DateTime atUtc, string? source, Exception exception, int leftOut)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Emit(atUtc, "error", new (string, string?)[]
        {
            ("source", source ?? string.Empty),
            ("error", _masker.Apply(exception)),
            ("left-out", JournalFormat.Integer(leftOut)),
        });
    }

    /// <summary>Journals an error message with the keys source and error; the message is masked (OPS-007).</summary>
    /// <param name="atUtc">Server time.</param>
    /// <param name="source">Where it happened.</param>
    /// <param name="message">The message.</param>
    /// <returns>The line written.</returns>
    public string Error(DateTime atUtc, string? source, string? message)
    {
        return Emit(atUtc, "error", new (string, string?)[]
        {
            ("source", source ?? string.Empty),
            ("error", message ?? string.Empty),
        });
    }

    /// <summary>
    /// Journals a line of the operations module itself (alerts, heartbeat, summary, inactivity)
    /// with the given keys in the given order (OPS-003 to OPS-006). Values are masked and escaped.
    /// </summary>
    /// <param name="atUtc">Server time.</param>
    /// <param name="category">Category, a single word.</param>
    /// <param name="fields">Keys and values in the order to write.</param>
    /// <returns>The line written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="category"/> or <paramref name="fields"/> is null.</exception>
    public string Write(DateTime atUtc, string category, IReadOnlyList<(string Key, string? Value)> fields)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(fields);
        return Emit(atUtc, category, fields);
    }

    /// <summary>
    /// The counters of the CE(S)T day that starts at <paramref name="dayStartUtc"/> (OPS-003):
    /// lines written by <see cref="Trade"/> (by close time) and <see cref="Skip"/> (by decision
    /// time) in that day. Empty counters when nothing was journaled for that day since start.
    /// </summary>
    /// <param name="dayStartUtc">Start of the CE(S)T day (<see cref="CestClock.StartOfCestDayUtc"/>).</param>
    /// <returns>A copy of the counters.</returns>
    public JournalDayCounters CountersFor(DateTime dayStartUtc)
    {
        return _days.TryGetValue(dayStartUtc, out JournalDayCounters? day) ? day.Copy() : new JournalDayCounters(dayStartUtc);
    }

    private JournalDayCounters? DayOf(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
        {
            return null;
        }

        DateTime start = CestClock.StartOfCestDayUtc(utc);
        if (!_days.TryGetValue(start, out JournalDayCounters? day))
        {
            day = new JournalDayCounters(start);
            _days.Add(start, day);
            while (_days.Count > CounterDays)
            {
                _days.Remove(_days.Keys.First());
            }
        }

        return _days.TryGetValue(start, out JournalDayCounters? kept) ? kept : null;
    }

    private string Emit(DateTime atUtc, string category, IEnumerable<(string Key, string? Value)> fields)
    {
        var line = new StringBuilder();
        line.Append(JournalFormat.Time(atUtc)).Append(' ').Append(category);
        foreach ((string key, string? value) in fields)
        {
            line.Append(' ').Append(key).Append('=').Append(JournalFormat.Escape(value == null ? null : _masker.Apply(value)));
        }

        string text = _masker.Apply(line.ToString());
        try
        {
            _sink.Write(category, text);
        }
        catch (Exception exception)
        {
            // Counted and kept, never rethrown: a broken log must not stop trading logic (OPS-001).
            WriteFailures++;
            LastWriteFailure = _masker.Apply(exception);
        }

        return text;
    }
}

/// <summary>Counters of one CE(S)T day kept by <see cref="Journal"/> for the daily summary (OPS-003).</summary>
public sealed class JournalDayCounters
{
    private readonly SortedDictionary<SkipReason, int> _skips;

    internal JournalDayCounters(DateTime dayStartUtc)
        : this(dayStartUtc, 0, 0, 0.0, new SortedDictionary<SkipReason, int>())
    {
    }

    private JournalDayCounters(DateTime dayStartUtc, int trades, int tradesWithR, double netR, SortedDictionary<SkipReason, int> skips)
    {
        DayStartUtc = dayStartUtc;
        Trades = trades;
        TradesWithR = tradesWithR;
        NetR = netR;
        _skips = skips;
    }

    /// <summary>Start of the CE(S)T day, kind UTC.</summary>
    public DateTime DayStartUtc { get; }

    /// <summary>Trade lines journaled for the day.</summary>
    public int Trades { get; private set; }

    /// <summary>Trade lines of the day whose result in R was known.</summary>
    public int TradesWithR { get; private set; }

    /// <summary>Sum of the known results in R of the day.</summary>
    public double NetR { get; private set; }

    /// <summary>Skip lines journaled for the day.</summary>
    public int Skips => _skips.Values.Sum();

    /// <summary>Skips of the day by reason, in the order of the <see cref="SkipReason"/> values.</summary>
    public IReadOnlyList<KeyValuePair<SkipReason, int>> SkipsByReason => _skips.ToArray();

    internal void AddTrade(double? r)
    {
        Trades++;
        if (r.HasValue && double.IsFinite(r.Value))
        {
            TradesWithR++;
            NetR += r.Value;
        }
    }

    internal void AddSkip(SkipReason reason)
    {
        _skips.TryGetValue(reason, out int count);
        _skips[reason] = count + 1;
    }

    internal JournalDayCounters Copy()
    {
        return new JournalDayCounters(DayStartUtc, Trades, TradesWithR, NetR, new SortedDictionary<SkipReason, int>(_skips));
    }
}
