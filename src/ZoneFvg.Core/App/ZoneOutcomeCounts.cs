using System;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.App;

/// <summary>
/// Zone outcome counts per CE(S)T day of the bar's close time, for bars fed live (OPS-009,
/// journal only). Kept in memory; a restart starts a new count. No trading decision reads it.
/// </summary>
internal sealed class ZoneOutcomeCounts
{
    // Journal keys in the order of the line: created, then one per ZoneEndReason in enum order up to Superseded.
    private static readonly string[] FvgKeys = { "created", "signalled", "no-fvg", "bias", "invalid", "expired", "superseded" };

    // OPS-011: the Choch model names the window end no-choch, in the same place.
    private static readonly string[] ChochKeys = { "created", "signalled", "no-choch", "bias", "invalid", "expired", "superseded" };

    private readonly string[] _keys;

    // Counts by CE(S)T day start (UTC); SortedDictionary so days are taken in time order.
    private readonly SortedDictionary<DateTime, int[]> _days = new SortedDictionary<DateTime, int[]>();

    /// <summary>Creates the counts of the Fvg model (OPS-009).</summary>
    internal ZoneOutcomeCounts()
        : this(false)
    {
    }

    /// <summary>Creates the counts of the Fvg model or, with <paramref name="choch"/>, of the Choch model (OPS-009, OPS-011).</summary>
    /// <param name="choch">True in the Choch model: the window end is named <c>no-choch</c>.</param>
    internal ZoneOutcomeCounts(bool choch)
    {
        _keys = choch ? ChochKeys : FvgKeys;
    }

    /// <summary>Adds what one live bar did to the zones to the CE(S)T day of its close time (OPS-009).</summary>
    /// <param name="closeUtc">Close time of the bar, kind UTC.</param>
    /// <param name="activity">The engine's report for the bar.</param>
    internal void Add(DateTime closeUtc, ZoneActivity activity)
    {
        if (activity.Created == 0 && activity.Ended.Count == 0)
        {
            return;
        }

        DateTime day = CestClock.StartOfCestDayUtc(closeUtc);
        if (!_days.TryGetValue(day, out int[]? counts))
        {
            counts = new int[_keys.Length];
            _days.Add(day, counts);
        }

        counts[0] += activity.Created;
        foreach (ZoneEnd end in activity.Ended)
        {
            counts[1 + Slot(end.Reason)]++;
        }
    }

    // SIG-037: NoChoch, the window end of the Choch model, shares the slot of NoFvg, the window end of the
    // Fvg model; an instance runs one model, so the line of the Fvg model keeps its keys and values.
    private static int Slot(ZoneEndReason reason)
    {
        return reason == ZoneEndReason.NoChoch ? (int)ZoneEndReason.NoFvg : (int)reason;
    }

    /// <summary>
    /// Removes and returns the journal fields of every counted CE(S)T day before the day of
    /// <paramref name="closeUtc"/> (OPS-009), oldest first: <c>day=&lt;yyyy-MM-dd&gt;</c> and the
    /// seven counts. Days whose counts are all 0 are never stored, so they give no line.
    /// </summary>
    /// <param name="closeUtc">Close time of the current live M15 bar, kind UTC.</param>
    /// <returns>The fields of one line per earlier day.</returns>
    internal IReadOnlyList<(string Key, string? Value)[]> TakeDaysBefore(DateTime closeUtc)
    {
        DateTime today = CestClock.StartOfCestDayUtc(closeUtc);
        var lines = new List<(string Key, string? Value)[]>();
        var done = new List<DateTime>();
        foreach (KeyValuePair<DateTime, int[]> pair in _days)
        {
            if (pair.Key >= today)
            {
                break;
            }

            var fields = new (string Key, string? Value)[_keys.Length + 1];
            // Noon UTC after the day start lies inside the same CE(S)T date.
            fields[0] = ("day", pair.Key.AddHours(12.0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            for (int i = 0; i < _keys.Length; i++)
            {
                fields[i + 1] = (_keys[i], pair.Value[i].ToString(CultureInfo.InvariantCulture));
            }

            lines.Add(fields);
            done.Add(pair.Key);
        }

        foreach (DateTime day in done)
        {
            _days.Remove(day);
        }

        return lines;
    }
}
