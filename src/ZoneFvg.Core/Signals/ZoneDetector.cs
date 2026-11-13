using System;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;

namespace ZoneFvg.Core.Signals;

/// <summary>Result of one zone detection at an H4 close (SIG-008 to SIG-011, SIG-013).</summary>
public enum ZoneDetectionOutcome
{
    /// <summary>A new zone was created.</summary>
    ZoneCreated,

    /// <summary>No impulse leg ends at the just-closed bar (SIG-007).</summary>
    NoImpulseLeg,

    /// <summary>The leg does not break structure, or no retained swing exists (SIG-008).</summary>
    NoBreakOfStructure,

    /// <summary>The proximal line is not strictly on the trade side of the distal line (SIG-010).</summary>
    DegenerateZone,

    /// <summary>The zone height is outside MinZoneAtr to MaxZoneAtr x ATR(H4) (SIG-011).</summary>
    WidthOutOfRange,
}

/// <summary>Outcome of <see cref="ZoneDetector.Detect"/>: zero or one new zone and the reason (SIG-008 to SIG-013).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Zone">The new zone when <paramref name="Outcome"/> is <see cref="ZoneDetectionOutcome.ZoneCreated"/>; otherwise null.</param>
public sealed record ZoneDetection(ZoneDetectionOutcome Outcome, Zone? Zone);

/// <summary>
/// Detects supply and demand zones at each H4 close (spec section 3.2). It is a pure function of
/// its inputs, holds no state and knows nothing about zones found earlier: the supersede rule
/// (SIG-012) and the lifecycle (SIG-014) belong to <see cref="ZoneBook"/>.
/// </summary>
public static class ZoneDetector
{
    /// <summary>Format of the time part of a zone id (SIG-013), applied with the invariant culture.</summary>
    public const string IdTimeFormat = "yyyyMMdd-HHmm";

    /// <summary>
    /// Looks for a new zone created by the bar that has just closed, the newest bar of
    /// <paramref name="h4Bars"/>. The rules are applied in this order: impulse leg (SIG-007),
    /// break of structure (SIG-008), base (SIG-009), edges (SIG-010), width (SIG-011), activation
    /// and id (SIG-013).
    /// <list type="bullet">
    /// <item>SIG-008: with RequireBos, a bullish leg needs its final close above the most recent
    /// swing high whose bar opened before the leg's first bar (a bearish leg: below the most
    /// recent such swing low). Without such a swing there is no zone.</item>
    /// <item>SIG-009: the base starts with the bar immediately before the leg, which is always
    /// taken, and continues backward, up to MaxBaseBars bars, while each bar's range
    /// (high - low) is at most 1.0 x the leg's ATR. The collection stops at the first bar that is
    /// wider, so when the bar immediately before the leg is itself wider the base is that bar
    /// alone.</item>
    /// <item>SIG-010: demand distal = lowest low of the base bars and the first leg bar, proximal
    /// = highest body top of the base bars; supply distal = highest high, proximal = lowest body
    /// bottom. A zone whose proximal is not strictly on the trade side of its distal is
    /// discarded.</item>
    /// <item>SIG-011: |proximal - distal| must lie between MinZoneAtr and MaxZoneAtr x the leg's
    /// ATR, both limits included.</item>
    /// <item>SIG-013: the zone activates at the close time of the leg's last bar (its open time
    /// plus four hours), which is the open time of the following H4 bar when the bars are
    /// contiguous. The id is "D-" or "S-" followed by the UTC open time of the oldest base bar in
    /// the format <see cref="IdTimeFormat"/>.</item>
    /// </list>
    /// Prices are compared with <see cref="PriceMath"/> and its default tolerance.
    /// </summary>
    /// <param name="h4Bars">Closed H4 bars, oldest first. The last bar is the just-closed bar.</param>
    /// <param name="swings">The swings confirmed as of the just-closed bar, as returned by <see cref="SwingDetector.Find"/> for the same bars.</param>
    /// <param name="parameters">Signal parameters. They are validated first.</param>
    /// <returns>The outcome, with the new zone when one was created.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public static ZoneDetection Detect(IReadOnlyList<Bar> h4Bars, IReadOnlyList<Swing> swings, SignalParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(h4Bars);
        ArgumentNullException.ThrowIfNull(swings);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();

        ImpulseLeg? leg = ImpulseLeg.Find(h4Bars, parameters);
        if (leg is null)
        {
            return new ZoneDetection(ZoneDetectionOutcome.NoImpulseLeg, null);
        }

        if (parameters.RequireBos && !BreaksStructure(h4Bars, swings, leg))
        {
            return new ZoneDetection(ZoneDetectionOutcome.NoBreakOfStructure, null);
        }

        int baseFirst = OldestBaseIndex(h4Bars, leg, parameters.MaxBaseBars);
        double proximal = Proximal(h4Bars, leg.Type, baseFirst, leg.FirstIndex - 1);
        double distal = Distal(h4Bars, leg.Type, baseFirst, leg.FirstIndex);
        if (!IsOnTradeSide(leg.Type, proximal, distal))
        {
            return new ZoneDetection(ZoneDetectionOutcome.DegenerateZone, null);
        }

        double height = Math.Abs(proximal - distal);
        bool tooNarrow = PriceMath.LessThan(height, parameters.MinZoneAtr * leg.Atr, PriceMath.DefaultTolerance);
        bool tooWide = PriceMath.GreaterThan(height, parameters.MaxZoneAtr * leg.Atr, PriceMath.DefaultTolerance);
        if (tooNarrow || tooWide)
        {
            return new ZoneDetection(ZoneDetectionOutcome.WidthOutOfRange, null);
        }

        DateTime activation = h4Bars[leg.LastIndex].OpenTimeUtc.AddHours(4.0);
        string id = ZoneId(leg.Type, h4Bars[baseFirst].OpenTimeUtc);
        var zone = new Zone(id, leg.Type, proximal, distal, activation, leg.Atr, ZoneState.Active, null, null);
        return new ZoneDetection(ZoneDetectionOutcome.ZoneCreated, zone);
    }

    /// <summary>Builds a zone id (SIG-013): "D-" or "S-" and the time in <see cref="IdTimeFormat"/>, invariant culture.</summary>
    /// <param name="type">Zone type.</param>
    /// <param name="firstBaseBarOpenUtc">UTC open time of the oldest base bar.</param>
    /// <returns>The id, for example "D-20261112-1600".</returns>
    public static string ZoneId(ZoneType type, DateTime firstBaseBarOpenUtc)
    {
        string prefix = type == ZoneType.Demand ? "D-" : "S-";
        return prefix + firstBaseBarOpenUtc.ToString(IdTimeFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>True when the proximal line is strictly on the trade side of the distal line (SIG-010).</summary>
    internal static bool IsOnTradeSide(ZoneType type, double proximal, double distal)
    {
        return type == ZoneType.Demand
            ? PriceMath.GreaterThan(proximal, distal, PriceMath.DefaultTolerance)
            : PriceMath.LessThan(proximal, distal, PriceMath.DefaultTolerance);
    }

    private static bool BreaksStructure(IReadOnlyList<Bar> bars, IReadOnlyList<Swing> swings, ImpulseLeg leg)
    {
        SwingKind kind = leg.Type == ZoneType.Demand ? SwingKind.High : SwingKind.Low;
        DateTime legStart = bars[leg.FirstIndex].OpenTimeUtc;
        Swing? latest = null;
        foreach (Swing swing in swings)
        {
            bool eligible = swing.Kind == kind && swing.BarOpenTimeUtc < legStart;
            if (eligible && (latest is null || swing.BarOpenTimeUtc > latest.BarOpenTimeUtc))
            {
                latest = swing;
            }
        }

        if (latest is null)
        {
            return false;
        }

        double close = bars[leg.LastIndex].Close;
        return leg.Type == ZoneType.Demand
            ? PriceMath.GreaterThan(close, latest.Price, PriceMath.DefaultTolerance)
            : PriceMath.LessThan(close, latest.Price, PriceMath.DefaultTolerance);
    }

    private static int OldestBaseIndex(IReadOnlyList<Bar> bars, ImpulseLeg leg, int maxBaseBars)
    {
        // The leg has an ATR, which needs bars before its first bar, so index FirstIndex - 1 exists.
        int oldest = leg.FirstIndex - 1;
        if (IsWide(bars[oldest], leg.Atr))
        {
            return oldest;
        }

        while (leg.FirstIndex - oldest < maxBaseBars && oldest > 0 && !IsWide(bars[oldest - 1], leg.Atr))
        {
            oldest--;
        }

        return oldest;
    }

    private static bool IsWide(Bar bar, double atr)
    {
        return PriceMath.GreaterThan(bar.High - bar.Low, atr, PriceMath.DefaultTolerance);
    }

    private static double Proximal(IReadOnlyList<Bar> bars, ZoneType type, int first, int last)
    {
        double proximal = BodyEdge(bars[first], type);
        for (int i = first + 1; i <= last; i++)
        {
            double edge = BodyEdge(bars[i], type);
            proximal = type == ZoneType.Demand ? Math.Max(proximal, edge) : Math.Min(proximal, edge);
        }

        return proximal;
    }

    private static double Distal(IReadOnlyList<Bar> bars, ZoneType type, int first, int last)
    {
        double distal = type == ZoneType.Demand ? bars[first].Low : bars[first].High;
        for (int i = first + 1; i <= last; i++)
        {
            distal = type == ZoneType.Demand ? Math.Min(distal, bars[i].Low) : Math.Max(distal, bars[i].High);
        }

        return distal;
    }

    private static double BodyEdge(Bar bar, ZoneType type)
    {
        return type == ZoneType.Demand ? Math.Max(bar.Open, bar.Close) : Math.Min(bar.Open, bar.Close);
    }
}
