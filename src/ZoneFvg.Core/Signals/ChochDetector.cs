using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;

namespace ZoneFvg.Core.Signals;

/// <summary>The target of a Choch signal and where it comes from (SIG-040).</summary>
/// <param name="Price">The target price.</param>
/// <param name="Source"><c>zone:&lt;id&gt;</c>, <c>swing:&lt;yyyyMMdd-HHmm of the swing bar&gt;</c> or <c>default</c>.</param>
public sealed record ChochTarget(double Price, string Source);

/// <summary>
/// The rules of the Choch entry model (spec 3.9): the change of character (SIG-037), the sweep
/// (SIG-038), the stop (SIG-039) and the target (SIG-040). All are pure functions of the closed
/// bars and zones they are given; they keep no state, so a replay reaches the same answers
/// (SIG-041). Long rules are described; short rules mirror them.
/// </summary>
/// <remarks>
/// The M15 swings at a bar b are the swings of <see cref="SwingDetector.Find"/> on the bars up to
/// and including b. Find reads only the last <see cref="SwingDetector.LookbackBars"/> plus
/// SwingStrength of them, so those bars are passed, and the answer does not depend on how many
/// older bars the caller still holds.
/// </remarks>
public static class ChochDetector
{
    /// <summary>The target source when no opposing level lies beyond the entry (SIG-040).</summary>
    public const string DefaultSource = "default";

    /// <summary>
    /// The most recent confirmed swing of the kind (latest bar open time) among the swings found on
    /// <paramref name="bars"/>[0..<paramref name="endIndex"/>] (SIG-037, SIG-038).
    /// </summary>
    /// <param name="bars">Closed bars, oldest first.</param>
    /// <param name="endIndex">Index of the newest bar to read. Bars after it are not read.</param>
    /// <param name="strength">SwingStrength, at least 1.</param>
    /// <param name="kind">High or low.</param>
    /// <returns>The swing, or null when there is none.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The end index is outside the bars, or the strength is below 1.</exception>
    public static Swing? MostRecentSwing(IReadOnlyList<Bar> bars, int endIndex, int strength, SwingKind kind)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (endIndex < 0 || endIndex >= bars.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(endIndex), "The end index is outside the closed bars.");
        }

        int length = Math.Min(endIndex + 1, SwingDetector.LookbackBars + Math.Max(strength, 0));
        IReadOnlyList<Swing> swings = SwingDetector.Find(new BarWindow(bars, endIndex + 1 - length, length), strength);
        for (int i = swings.Count - 1; i >= 0; i--)
        {
            if (swings[i].Kind == kind)
            {
                return swings[i];
            }
        }

        return null;
    }

    /// <summary>
    /// The change of character at the newest bar c1 of <paramref name="m15"/> (SIG-037). For a
    /// demand zone let H be the most recent confirmed M15 swing high on the bars up to and
    /// including c1. c1 confirms when H exists, no bar after H's bar and before c1 closed above H's
    /// price, and c1 closes above it. Above is strict, with <see cref="PriceMath"/> and its default
    /// tolerance. When H was already closed above, nothing confirms (no older swing is tried). A
    /// supply zone mirrors this with the most recent swing low and closes below it.
    /// </summary>
    /// <param name="m15">Closed M15 bars, oldest first; the newest is c1.</param>
    /// <param name="type">The zone type: demand looks for a break up, supply for a break down.</param>
    /// <param name="strength">SwingStrength, at least 1.</param>
    /// <returns>The broken swing H, or null when c1 confirms nothing.</returns>
    public static Swing? BrokenSwing(IReadOnlyList<Bar> m15, ZoneType type, int strength)
    {
        ArgumentNullException.ThrowIfNull(m15);
        if (m15.Count == 0)
        {
            return null;
        }

        int c1 = m15.Count - 1;
        SwingKind kind = type == ZoneType.Demand ? SwingKind.High : SwingKind.Low;
        Swing? level = MostRecentSwing(m15, c1, strength, kind);
        if (level is null)
        {
            return null;
        }

        for (int i = IndexOf(m15, level.BarOpenTimeUtc, c1) + 1; i < c1; i++)
        {
            if (Beyond(m15[i].Close, level.Price, kind))
            {
                return null;
            }
        }

        return Beyond(m15[c1].Close, level.Price, kind) ? level : null;
    }

    /// <summary>
    /// Whether a bar from the touch bar to c1, the newest bar of <paramref name="m15"/>, both
    /// included, swept (SIG-038): for a demand zone a bar b sweeps when its low is strictly below
    /// the price of the most recent confirmed M15 swing low on the bars up to and including b. A
    /// supply zone mirrors this with swing highs and highs strictly above.
    /// </summary>
    /// <param name="m15">Closed M15 bars, oldest first; the newest is c1. They must include the touch bar.</param>
    /// <param name="type">The zone type.</param>
    /// <param name="touchBarOpenUtc">Open time of the zone's touch bar.</param>
    /// <param name="strength">SwingStrength, at least 1.</param>
    /// <returns>True when at least one bar swept.</returns>
    /// <exception cref="ArgumentException">The touch bar is not among the bars.</exception>
    public static bool Swept(IReadOnlyList<Bar> m15, ZoneType type, DateTime touchBarOpenUtc, int strength)
    {
        ArgumentNullException.ThrowIfNull(m15);
        int c1 = m15.Count - 1;
        int touch = c1 < 0 ? -1 : IndexOf(m15, touchBarOpenUtc, c1);
        if (touch < 0)
        {
            throw new ArgumentException("The touch bar is not among the closed bars.", nameof(touchBarOpenUtc));
        }

        SwingKind kind = type == ZoneType.Demand ? SwingKind.Low : SwingKind.High;
        for (int b = touch; b <= c1; b++)
        {
            Swing? swing = MostRecentSwing(m15, b, strength, kind);
            double extreme = kind == SwingKind.Low ? m15[b].Low : m15[b].High;
            if (swing is not null && Beyond(extreme, swing.Price, kind))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The stop of a Choch signal (SIG-039). For a buy, X is the lowest low from the touch bar to
    /// c1 (the zone's extreme since the touch) or, with ChochStopAtDistal, the lower of it and the
    /// distal line; the stop is X - StopBufferAtr x <paramref name="atrM15"/>. A sell mirrors this
    /// with the highest high, the higher of it and the distal line, and the buffer added.
    /// </summary>
    /// <param name="zone">The touched zone with its extreme since the touch.</param>
    /// <param name="atrM15">ATR(M15) at the close of c1.</param>
    /// <param name="parameters">Signal parameters: StopBufferAtr.</param>
    /// <param name="choch">Choch parameters: ChochStopAtDistal.</param>
    /// <returns>The stop price.</returns>
    /// <exception cref="ArgumentException">The zone has no extreme since the touch.</exception>
    public static double Stop(Zone zone, double atrM15, SignalParameters parameters, ChochParameters choch)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(choch);
        if (zone.ExtremeSinceTouch is null)
        {
            throw new ArgumentException("A Choch stop needs the zone's extreme since the touch.", nameof(zone));
        }

        double extreme = zone.ExtremeSinceTouch.Value;
        double buffer = parameters.StopBufferAtr * atrM15;
        if (zone.Type == ZoneType.Demand)
        {
            return (choch.ChochStopAtDistal ? Math.Min(extreme, zone.Distal) : extreme) - buffer;
        }

        return (choch.ChochStopAtDistal ? Math.Max(extreme, zone.Distal) : extreme) + buffer;
    }

    /// <summary>
    /// The target of a Choch signal (SIG-040). For a buy the opposing levels are the proximal lines
    /// of the supply zones in <paramref name="zones"/> and the prices of the confirmed H4 swing
    /// highs on <paramref name="h4"/> that no later H4 bar has closed above (orchestrating session's
    /// answer of 2026-11-23: a broken level is no longer resistance), each only when strictly above
    /// the entry. The target is the lowest of them minus ChochTargetBufferAtr x
    /// <paramref name="atrH4"/>, with the source <c>zone:&lt;id&gt;</c> or
    /// <c>swing:&lt;yyyyMMdd-HHmm&gt;</c>; among equal levels a zone wins over a swing, the lower
    /// zone id over a higher one and the older swing over a newer one. Without an opposing level
    /// the target is entry + RewardRisk x (entry - stop), source <c>default</c>. A sell mirrors
    /// this with demand zones, swing lows not closed below, the highest level below the entry and
    /// the buffer added. The reward the target gives is not checked here (EXE-028).
    /// </summary>
    /// <param name="side">The side of the signal.</param>
    /// <param name="entry">The entry, the broken swing level.</param>
    /// <param name="stop">The stop of <see cref="Stop"/>.</param>
    /// <param name="zones">The non-terminal zones of the book, in id order.</param>
    /// <param name="h4">Closed H4 bars, oldest first.</param>
    /// <param name="atrH4">ATR(H4) at the close of c1.</param>
    /// <param name="parameters">Signal parameters: SwingStrength and RewardRisk.</param>
    /// <param name="choch">Choch parameters: ChochTargetBufferAtr.</param>
    /// <returns>The target and its source.</returns>
    public static ChochTarget Target(Side side, double entry, double stop, IReadOnlyList<Zone> zones, IReadOnlyList<Bar> h4, double atrH4, SignalParameters parameters, ChochParameters choch)
    {
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(h4);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(choch);
        bool buy = side == Side.Buy;
        ZoneType opposing = buy ? ZoneType.Supply : ZoneType.Demand;
        double? best = null;
        string source = DefaultSource;
        foreach (Zone zone in zones)
        {
            if (zone.Type == opposing && Nearer(zone.Proximal, best, entry, buy))
            {
                best = zone.Proximal;
                source = "zone:" + zone.Id;
            }
        }

        foreach (Swing swing in UnbrokenSwings(h4, parameters.SwingStrength, buy ? SwingKind.High : SwingKind.Low))
        {
            if (Nearer(swing.Price, best, entry, buy))
            {
                best = swing.Price;
                source = "swing:" + swing.BarOpenTimeUtc.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
            }
        }

        double buffer = choch.ChochTargetBufferAtr * atrH4;
        if (best is not null)
        {
            return new ChochTarget(buy ? best.Value - buffer : best.Value + buffer, source);
        }

        double reward = parameters.RewardRisk * Math.Abs(entry - stop);
        return new ChochTarget(buy ? entry + reward : entry - reward, DefaultSource);
    }

    // SIG-040: a level counts when it lies strictly beyond the entry and strictly nearer than the best so far,
    // so the first of equal levels stays (zones are offered before swings).
    private static bool Nearer(double level, double? best, double entry, bool buy)
    {
        if (buy)
        {
            return PriceMath.GreaterThan(level, entry, PriceMath.DefaultTolerance) && (best is null || PriceMath.LessThan(level, best.Value, PriceMath.DefaultTolerance));
        }

        return PriceMath.LessThan(level, entry, PriceMath.DefaultTolerance) && (best is null || PriceMath.GreaterThan(level, best.Value, PriceMath.DefaultTolerance));
    }

    // SIG-040 (answer of 2026-11-23): the confirmed H4 swings of the kind that no later H4 bar has closed beyond, oldest first.
    private static List<Swing> UnbrokenSwings(IReadOnlyList<Bar> h4, int strength, SwingKind kind)
    {
        var unbroken = new List<Swing>();
        if (h4.Count == 0)
        {
            return unbroken;
        }

        int last = h4.Count - 1;
        foreach (Swing swing in SwingDetector.Find(h4, strength))
        {
            if (swing.Kind != kind)
            {
                continue;
            }

            bool broken = false;
            for (int i = IndexOf(h4, swing.BarOpenTimeUtc, last) + 1; i <= last && !broken; i++)
            {
                broken = Beyond(h4[i].Close, swing.Price, kind);
            }

            if (!broken)
            {
                unbroken.Add(swing);
            }
        }

        return unbroken;
    }

    // Above a swing high or below a swing low, strictly, with the default tolerance.
    private static bool Beyond(double price, double level, SwingKind kind)
    {
        return kind == SwingKind.High
            ? PriceMath.GreaterThan(price, level, PriceMath.DefaultTolerance)
            : PriceMath.LessThan(price, level, PriceMath.DefaultTolerance);
    }

    // Index of the bar that opens at the time, searched backward from `from`; -1 when there is none.
    private static int IndexOf(IReadOnlyList<Bar> bars, DateTime openTimeUtc, int from)
    {
        for (int i = from; i >= 0; i--)
        {
            if (bars[i].OpenTimeUtc == openTimeUtc)
            {
                return i;
            }

            if (bars[i].OpenTimeUtc < openTimeUtc)
            {
                return -1;
            }
        }

        return -1;
    }

    /// <summary>A read-only view of <c>count</c> consecutive bars of a list, from <c>start</c> on.</summary>
    private sealed class BarWindow : IReadOnlyList<Bar>
    {
        private readonly IReadOnlyList<Bar> _bars;
        private readonly int _start;

        public BarWindow(IReadOnlyList<Bar> bars, int start, int count)
        {
            _bars = bars;
            _start = start;
            Count = count;
        }

        public int Count { get; }

        public Bar this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), "The index is outside the window.");
                }

                return _bars[_start + index];
            }
        }

        public IEnumerator<Bar> GetEnumerator()
        {
            for (int index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
