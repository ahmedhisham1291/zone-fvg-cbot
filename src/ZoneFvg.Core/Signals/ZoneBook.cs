using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Signals;

/// <summary>A zone in state Touched and the number of M15 bars closed since its touch bar (SIG-014).</summary>
/// <param name="Zone">The zone snapshot; its state is <see cref="ZoneState.Touched"/>.</param>
/// <param name="BarsSinceTouch">M15 bars closed since the touch bar: 0 on the touch bar itself, which is bar 0 of the confirmation window.</param>
public sealed record TouchedZone(Zone Zone, int BarsSinceTouch);

/// <summary>
/// Signal-layer state of the zones of one symbol (spec section 3.3): the supersede rule
/// (SIG-012), id collisions (SIG-013) and the lifecycle Active, Touched, Signalled, Used,
/// Invalid, Expired (SIG-014). It depends only on the zones and bars it is given, never on bias,
/// orders or positions (SIG-015).
/// </summary>
/// <remarks>
/// <para>Order of work in one M15 close: <see cref="OnM15Closed"/> (invalidation first, then
/// touch detection), then the caller's fair value gap detection with <see cref="MarkSignalled"/>
/// or <see cref="MarkUsed"/>, then <see cref="EndConfirmationWindows"/>.</para>
/// <para>A zone that reaches a terminal state (Used, Invalid or Expired) leaves the book at once
/// and is reported in the list of changes returned by the call that ended it. Its id stays known
/// so that a later detection with the same id is discarded.</para>
/// <para>Prices are compared with <see cref="PriceMath"/> and its default tolerance. Zones are
/// kept and returned in ordinal order of their ids, never in hash order.</para>
/// </remarks>
public sealed class ZoneBook
{
    private readonly int _maxZoneAgeBars;
    private readonly int _confirmWindowBars;
    private readonly SortedDictionary<string, Entry> _zones = new(StringComparer.Ordinal);

    // Used only to ask whether an id was ever added; it is never enumerated.
    private readonly HashSet<string> _knownIds = new(StringComparer.Ordinal);
    private readonly List<string> _diagnostics = new();

    /// <summary>Creates an empty book (SIG-012, SIG-014).</summary>
    /// <param name="parameters">Signal parameters; MaxZoneAgeBars and ConfirmWindowBars are used. They are validated first.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public ZoneBook(SignalParameters parameters)
        : this(parameters, parameters?.ConfirmWindowBars ?? 0)
    {
    }

    /// <summary>
    /// Creates an empty book whose confirmation window is <paramref name="confirmWindowBars"/> M15
    /// bars instead of ConfirmWindowBars (SIG-036: ChochWindowBars in the Choch model). Everything
    /// else is as <see cref="ZoneBook(SignalParameters)"/>.
    /// </summary>
    /// <param name="parameters">Signal parameters; MaxZoneAgeBars is used. They are validated first.</param>
    /// <param name="confirmWindowBars">The confirmation window in M15 bars after the touch bar. Must be from 1 to <see cref="SignalParameters.MaxBarCount"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter or the window is out of range.</exception>
    public ZoneBook(SignalParameters parameters, int confirmWindowBars)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        if (confirmWindowBars < 1 || confirmWindowBars > SignalParameters.MaxBarCount)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmWindowBars), "The confirmation window must be between 1 and " + Invariant.Text(SignalParameters.MaxBarCount) + " bars.");
        }

        _maxZoneAgeBars = parameters.MaxZoneAgeBars;
        _confirmWindowBars = confirmWindowBars;
    }

    /// <summary>The non-terminal zones (Active or Touched), ordered by id with ordinal comparison (SIG-014).</summary>
    public IReadOnlyList<Zone> Zones
    {
        get
        {
            var zones = new List<Zone>(_zones.Count);
            foreach (Entry entry in _zones.Values)
            {
                zones.Add(entry.Zone);
            }

            return zones.AsReadOnly();
        }
    }

    /// <summary>
    /// The zones currently in state Touched with the number of M15 bars closed since their touch
    /// bar, ordered by id with ordinal comparison (SIG-014). The fair value gap search (WP03) reads
    /// this list.
    /// </summary>
    public IReadOnlyList<TouchedZone> TouchedZones
    {
        get
        {
            var touched = new List<TouchedZone>();
            foreach (Entry entry in _zones.Values)
            {
                if (entry.Zone.State == ZoneState.Touched)
                {
                    touched.Add(new TouchedZone(entry.Zone, entry.BarsSinceTouch));
                }
            }

            return touched.AsReadOnly();
        }
    }

    /// <summary>Messages about detections that were discarded because their id was already used (SIG-013), oldest first.</summary>
    internal IReadOnlyList<string> Diagnostics => _diagnostics.AsReadOnly();

    /// <summary>
    /// Adds a newly detected zone (SIG-012, SIG-013). If a zone with the same id was ever added,
    /// the new zone is discarded, a diagnostic is recorded and nothing else changes. Otherwise
    /// every zone already in the book that has the same type, is still Active and whose price
    /// range intersects the new zone's range by more than 50% of its own (the older zone's)
    /// height is removed, and the new zone is added. Zones in any other state are never removed
    /// by this rule.
    /// </summary>
    /// <param name="zone">The new zone: Active, without touch data, proximal strictly on the trade side of distal.</param>
    /// <returns>True when the zone was added; false when it was discarded as an id collision.</returns>
    /// <exception cref="ArgumentException">The zone is not a fresh, well-formed zone.</exception>
    public bool Add(Zone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        RequireFresh(zone);

        if (_knownIds.Contains(zone.Id))
        {
            _diagnostics.Add("Zone " + zone.Id + " was detected again; the later detection was discarded.");
            return false;
        }

        var superseded = new List<string>();
        foreach (KeyValuePair<string, Entry> pair in _zones)
        {
            Zone older = pair.Value.Zone;
            if (older.Type == zone.Type && older.State == ZoneState.Active && OverlapsMoreThanHalf(older, zone))
            {
                superseded.Add(pair.Key);
            }
        }

        foreach (string id in superseded)
        {
            _zones.Remove(id);
        }

        _zones.Add(zone.Id, new Entry(zone));
        _knownIds.Add(zone.Id);
        return true;
    }

    /// <summary>
    /// Ages the zones by one closed H4 bar (SIG-014). Only bars that opened at or after a zone's
    /// activation time count for that zone. When MaxZoneAgeBars such bars have closed, the zone
    /// becomes Expired, whatever its state, and leaves the book.
    /// </summary>
    /// <param name="bar">The H4 bar that has just closed.</param>
    /// <returns>The zones that expired, as final snapshots, ordered by id.</returns>
    public IReadOnlyList<Zone> OnH4Closed(Bar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);

        var changed = new List<Zone>();
        foreach (Entry entry in _zones.Values)
        {
            if (bar.OpenTimeUtc < entry.Zone.ActivationTimeUtc)
            {
                continue;
            }

            entry.H4BarsSinceActivation++;
            if (entry.H4BarsSinceActivation >= _maxZoneAgeBars)
            {
                entry.Zone = entry.Zone with { State = ZoneState.Expired };
                changed.Add(entry.Zone);
            }
        }

        RemoveAll(changed);
        return changed.AsReadOnly();
    }

    /// <summary>
    /// Applies one closed M15 bar to every zone whose activation time is at or before the bar's
    /// open time (SIG-014). For each such zone, in id order:
    /// <list type="number">
    /// <item>A Touched zone counts the bar (BarsSinceTouch + 1) and extends its extreme: the lowest
    /// low (demand) or highest high (supply) since the touch bar.</item>
    /// <item>Invalidation: a close below the distal line (demand) or above it (supply) makes the
    /// zone Invalid, and it leaves the book.</item>
    /// <item>Touch: an Active zone whose bar low is at or below the proximal line (demand), or
    /// whose bar high is at or above it (supply), becomes Touched. The bar is the touch bar,
    /// bar 0 of the confirmation window, and its low (demand) or high (supply) is the first
    /// extreme. Because invalidation is checked first, a bar that touches and closes beyond the
    /// distal line makes the zone Invalid, not Touched.</item>
    /// </list>
    /// The confirmation window is closed separately by <see cref="EndConfirmationWindows"/>.
    /// </summary>
    /// <param name="bar">The M15 bar that has just closed.</param>
    /// <returns>The zones whose state changed, as new snapshots, ordered by id. Extreme updates alone are not listed.</returns>
    public IReadOnlyList<Zone> OnM15Closed(Bar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);

        var changed = new List<Zone>();
        foreach (Entry entry in _zones.Values)
        {
            if (bar.OpenTimeUtc < entry.Zone.ActivationTimeUtc)
            {
                continue;
            }

            if (entry.Zone.State == ZoneState.Touched)
            {
                entry.BarsSinceTouch++;
                entry.Zone = entry.Zone with { ExtremeSinceTouch = MoreExtreme(entry.Zone, bar) };
            }

            if (ClosesBeyondDistal(entry.Zone, bar))
            {
                entry.Zone = entry.Zone with { State = ZoneState.Invalid };
                changed.Add(entry.Zone);
                continue;
            }

            if (entry.Zone.State == ZoneState.Active && Touches(entry.Zone, bar))
            {
                entry.Zone = entry.Zone with
                {
                    State = ZoneState.Touched,
                    TouchBarOpenUtc = bar.OpenTimeUtc,
                    ExtremeSinceTouch = Extreme(entry.Zone.Type, bar),
                };
                entry.BarsSinceTouch = 0;
                changed.Add(entry.Zone);
            }
        }

        RemoveAll(changed);
        return changed.AsReadOnly();
    }

    /// <summary>
    /// Ends the confirmation windows (SIG-014): every zone still Touched after ConfirmWindowBars
    /// M15 bars have closed since its touch bar becomes Used and leaves the book. Call it after
    /// <see cref="OnM15Closed"/> and after the fair value gap search of the same bar, so that the
    /// last bar of the window can still confirm a signal.
    /// </summary>
    /// <returns>The zones that became Used, as final snapshots, ordered by id.</returns>
    public IReadOnlyList<Zone> EndConfirmationWindows()
    {
        var changed = new List<Zone>();
        foreach (Entry entry in _zones.Values)
        {
            if (entry.Zone.State == ZoneState.Touched && entry.BarsSinceTouch >= _confirmWindowBars)
            {
                entry.Zone = entry.Zone with { State = ZoneState.Used };
                changed.Add(entry.Zone);
            }
        }

        RemoveAll(changed);
        return changed.AsReadOnly();
    }

    /// <summary>
    /// Records that a Touched zone produced a signal (SIG-014): the zone becomes Signalled and
    /// then immediately Used, and leaves the book.
    /// </summary>
    /// <param name="zoneId">Id of a zone in state Touched.</param>
    /// <returns>Two snapshots: the zone in state Signalled, then in state Used.</returns>
    /// <exception cref="InvalidOperationException">No zone with this id is in the book, or it is not Touched.</exception>
    public IReadOnlyList<Zone> MarkSignalled(string zoneId)
    {
        Entry entry = TouchedEntry(zoneId);
        Zone signalled = entry.Zone with { State = ZoneState.Signalled };
        Zone used = entry.Zone with { State = ZoneState.Used };
        _zones.Remove(zoneId);
        return Array.AsReadOnly(new[] { signalled, used });
    }

    /// <summary>
    /// Moves a Touched zone to Used without a signal (SIG-014), for example when a valid fair value
    /// gap appears while the bias does not allow the zone to trade (SIG-018). The zone leaves the book.
    /// </summary>
    /// <param name="zoneId">Id of a zone in state Touched.</param>
    /// <returns>One snapshot: the zone in state Used.</returns>
    /// <exception cref="InvalidOperationException">No zone with this id is in the book, or it is not Touched.</exception>
    public IReadOnlyList<Zone> MarkUsed(string zoneId)
    {
        Entry entry = TouchedEntry(zoneId);
        Zone used = entry.Zone with { State = ZoneState.Used };
        _zones.Remove(zoneId);
        return Array.AsReadOnly(new[] { used });
    }

    private static void RequireFresh(Zone zone)
    {
        if (string.IsNullOrEmpty(zone.Id))
        {
            throw new ArgumentException("A zone needs an id.", nameof(zone));
        }

        if (zone.State != ZoneState.Active || zone.TouchBarOpenUtc is not null || zone.ExtremeSinceTouch is not null)
        {
            throw new ArgumentException("Only a new, untouched Active zone can be added.", nameof(zone));
        }

        if (!ZoneDetector.IsOnTradeSide(zone.Type, zone.Proximal, zone.Distal))
        {
            throw new ArgumentException("The proximal line must be strictly on the trade side of the distal line.", nameof(zone));
        }
    }

    private static bool OverlapsMoreThanHalf(Zone older, Zone newer)
    {
        double olderLow = Math.Min(older.Proximal, older.Distal);
        double olderHigh = Math.Max(older.Proximal, older.Distal);
        double newerLow = Math.Min(newer.Proximal, newer.Distal);
        double newerHigh = Math.Max(newer.Proximal, newer.Distal);
        double intersection = Math.Min(olderHigh, newerHigh) - Math.Max(olderLow, newerLow);
        return PriceMath.GreaterThan(intersection, 0.5 * (olderHigh - olderLow), PriceMath.DefaultTolerance);
    }

    private static bool ClosesBeyondDistal(Zone zone, Bar bar)
    {
        return zone.Type == ZoneType.Demand
            ? PriceMath.LessThan(bar.Close, zone.Distal, PriceMath.DefaultTolerance)
            : PriceMath.GreaterThan(bar.Close, zone.Distal, PriceMath.DefaultTolerance);
    }

    private static bool Touches(Zone zone, Bar bar)
    {
        return zone.Type == ZoneType.Demand
            ? !PriceMath.GreaterThan(bar.Low, zone.Proximal, PriceMath.DefaultTolerance)
            : !PriceMath.LessThan(bar.High, zone.Proximal, PriceMath.DefaultTolerance);
    }

    private static double Extreme(ZoneType type, Bar bar)
    {
        return type == ZoneType.Demand ? bar.Low : bar.High;
    }

    private static double MoreExtreme(Zone zone, Bar bar)
    {
        double current = zone.ExtremeSinceTouch ?? Extreme(zone.Type, bar);
        return zone.Type == ZoneType.Demand ? Math.Min(current, bar.Low) : Math.Max(current, bar.High);
    }

    private Entry TouchedEntry(string zoneId)
    {
        ArgumentNullException.ThrowIfNull(zoneId);
        if (!_zones.TryGetValue(zoneId, out Entry? entry))
        {
            throw new InvalidOperationException("No zone with id " + zoneId + " is in the book.");
        }

        if (entry.Zone.State != ZoneState.Touched)
        {
            throw new InvalidOperationException("Zone " + zoneId + " is not Touched.");
        }

        return entry;
    }

    private void RemoveAll(List<Zone> ended)
    {
        foreach (Zone zone in ended)
        {
            if (zone.State is ZoneState.Invalid or ZoneState.Used or ZoneState.Expired)
            {
                _zones.Remove(zone.Id);
            }
        }
    }

    private sealed class Entry
    {
        public Entry(Zone zone)
        {
            Zone = zone;
        }

        public Zone Zone { get; set; }

        public int BarsSinceTouch { get; set; }

        public int H4BarsSinceActivation { get; set; }
    }
}
