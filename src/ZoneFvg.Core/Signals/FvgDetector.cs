using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// Fair value gap detection on M15 bars (SIG-016) and the levels of the signal it confirms
/// (SIG-017). Both are pure functions of their inputs and hold no state.
/// </summary>
public static class FvgDetector
{
    /// <summary>
    /// Looks for a fair value gap that confirms a Touched zone at the close of the newest bar of
    /// <paramref name="m15Buffer"/> (SIG-016). With c3, c2, c1 the three newest bars (c1 newest), a
    /// demand zone is confirmed by a bullish gap when all of these hold:
    /// <list type="number">
    /// <item>low(c1) is above high(c3); the gap runs from high(c3) up to low(c1);</item>
    /// <item>c2 closes above its open;</item>
    /// <item>the gap height low(c1) - high(c3) is at least MinFvgAtr x <paramref name="atrM15"/>;</item>
    /// <item>c3, and so all three bars, opened at or after the zone's touch bar;</item>
    /// <item>c1 is at most ConfirmWindowBars bars after the touch bar
    /// (<paramref name="barsSinceTouch"/> is at most ConfirmWindowBars);</item>
    /// <item>the lower edge high(c3) is at most proximal + 0.5 x <paramref name="atrH4"/>.</item>
    /// </list>
    /// A supply zone is confirmed by the mirror image: high(c1) below low(c3), c2 closing below its
    /// open, height low(c3) - high(c1), and the upper edge low(c3) at least
    /// proximal - 0.5 x <paramref name="atrH4"/>. Prices are compared with <see cref="PriceMath"/>
    /// and its default tolerance: "above", "below" and "after" exclude the limit, "at least" and
    /// "at most" include it. That only the first gap after a touch is used follows from the caller
    /// consuming the zone on the first gap found.
    /// </summary>
    /// <param name="m15Buffer">Closed M15 bars, oldest first. The newest bar is c1, the bar that has just closed.</param>
    /// <param name="zone">The zone, in state Touched, with its touch bar open time.</param>
    /// <param name="barsSinceTouch">M15 bars closed since the touch bar: 0 on the touch bar itself (see <see cref="TouchedZone"/>).</param>
    /// <param name="atrM15">ATR(M15) to measure the gap height with. Must be finite and not negative.</param>
    /// <param name="atrH4">ATR(H4) to limit the distance from the zone with. Must be finite and not negative.</param>
    /// <param name="parameters">Signal parameters. They are validated first.</param>
    /// <returns>The gap, or null when the bars do not confirm the zone or fewer than three bars are given.</returns>
    /// <exception cref="ArgumentException">The zone has no touch bar.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter, an ATR or the bar count is out of range.</exception>
    public static Fvg? Find(IReadOnlyList<Bar> m15Buffer, Zone zone, int barsSinceTouch, double atrM15, double atrH4, SignalParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(m15Buffer);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        if (barsSinceTouch < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(barsSinceTouch), "The number of bars since the touch must not be negative.");
        }

        RequireAtr(atrM15, nameof(atrM15));
        RequireAtr(atrH4, nameof(atrH4));
        if (zone.TouchBarOpenUtc is null)
        {
            throw new ArgumentException("Only a touched zone can be confirmed by a fair value gap.", nameof(zone));
        }

        int count = m15Buffer.Count;
        if (count < 3 || barsSinceTouch > parameters.ConfirmWindowBars)
        {
            return null;
        }

        Bar c3 = m15Buffer[count - 3];
        Bar c2 = m15Buffer[count - 2];
        Bar c1 = m15Buffer[count - 1];
        if (c3.OpenTimeUtc < zone.TouchBarOpenUtc.Value)
        {
            return null;
        }

        return zone.Type == ZoneType.Demand
            ? Bullish(c3, c2, c1, zone.Proximal, atrM15, atrH4, parameters.MinFvgAtr)
            : Bearish(c3, c2, c1, zone.Proximal, atrM15, atrH4, parameters.MinFvgAtr);
    }

    /// <summary>
    /// Builds the signal of a zone confirmed by <paramref name="fvg"/> (SIG-017). For a buy:
    /// <list type="bullet">
    /// <item>Entry = low(c1) - EntryLevelPct / 100 x gap height, that is
    /// Upper - EntryLevelPct / 100 x (Upper - Lower);</item>
    /// <item>Stop = min(distal, extreme since touch) - StopBufferAtr x <paramref name="atrM15"/>;
    /// the extreme since touch already includes the touch bar;</item>
    /// <item>Target = Entry + RewardRisk x (Entry - Stop).</item>
    /// </list>
    /// A sell mirrors this: Entry = Lower + EntryLevelPct / 100 x (Upper - Lower), Stop =
    /// max(distal, extreme since touch) + buffer, Target = Entry - RewardRisk x (Stop - Entry).
    /// The confirmation time is the close of c1: its open time plus 15 minutes. The stop distance
    /// in price is |Entry - Stop|. Spread is not added here: it is not bar data.
    /// </summary>
    /// <param name="zone">The confirmed zone, with its extreme since touch.</param>
    /// <param name="fvg">The gap returned by <see cref="Find"/> for the zone. Its side must match the zone type.</param>
    /// <param name="atrM15">ATR(M15) for the stop buffer. Must be finite and not negative.</param>
    /// <param name="parameters">Signal parameters. They are validated first.</param>
    /// <param name="fromReplay">Copied to <see cref="Signal.FromReplay"/>.</param>
    /// <returns>The signal.</returns>
    /// <exception cref="ArgumentException">The zone has no extreme since touch, or the gap side does not match the zone type.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter or the ATR is out of range.</exception>
    public static Signal BuildSignal(Zone zone, Fvg fvg, double atrM15, SignalParameters parameters, bool fromReplay)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(fvg);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        RequireAtr(atrM15, nameof(atrM15));
        if (zone.ExtremeSinceTouch is null)
        {
            throw new ArgumentException("A signal needs the zone's extreme since the touch.", nameof(zone));
        }

        Side expected = zone.Type == ZoneType.Demand ? Side.Buy : Side.Sell;
        if (fvg.Side != expected)
        {
            throw new ArgumentException("The side of the gap does not match the zone type.", nameof(fvg));
        }

        double depth = parameters.EntryLevelPct / 100.0 * (fvg.Upper - fvg.Lower);
        double buffer = parameters.StopBufferAtr * atrM15;
        double extreme = zone.ExtremeSinceTouch.Value;
        double entry;
        double stop;
        double target;
        if (expected == Side.Buy)
        {
            entry = fvg.Upper - depth;
            stop = Math.Min(zone.Distal, extreme) - buffer;
            target = entry + (parameters.RewardRisk * (entry - stop));
        }
        else
        {
            entry = fvg.Lower + depth;
            stop = Math.Max(zone.Distal, extreme) + buffer;
            target = entry - (parameters.RewardRisk * (stop - entry));
        }

        DateTime confirmationCloseUtc = fvg.C1OpenUtc + BarIntakeOrder.Length(TimeFrame.M15);
        return new Signal(zone.Id, expected, entry, stop, target, fvg, confirmationCloseUtc, fromReplay);
    }

    private static Fvg? Bullish(Bar c3, Bar c2, Bar c1, double proximal, double atrM15, double atrH4, double minFvgAtr)
    {
        double lower = c3.High;
        double upper = c1.Low;
        bool gapExists = PriceMath.GreaterThan(upper, lower, PriceMath.DefaultTolerance);
        bool c2Bullish = PriceMath.GreaterThan(c2.Close, c2.Open, PriceMath.DefaultTolerance);
        bool highEnough = !PriceMath.LessThan(upper - lower, minFvgAtr * atrM15, PriceMath.DefaultTolerance);
        bool nearZone = !PriceMath.GreaterThan(lower, proximal + (0.5 * atrH4), PriceMath.DefaultTolerance);
        if (gapExists && c2Bullish && highEnough && nearZone)
        {
            return new Fvg(Side.Buy, upper, lower, c3.OpenTimeUtc, c2.OpenTimeUtc, c1.OpenTimeUtc);
        }

        return null;
    }

    private static Fvg? Bearish(Bar c3, Bar c2, Bar c1, double proximal, double atrM15, double atrH4, double minFvgAtr)
    {
        double upper = c3.Low;
        double lower = c1.High;
        bool gapExists = PriceMath.GreaterThan(upper, lower, PriceMath.DefaultTolerance);
        bool c2Bearish = PriceMath.LessThan(c2.Close, c2.Open, PriceMath.DefaultTolerance);
        bool highEnough = !PriceMath.LessThan(upper - lower, minFvgAtr * atrM15, PriceMath.DefaultTolerance);
        bool nearZone = !PriceMath.LessThan(upper, proximal - (0.5 * atrH4), PriceMath.DefaultTolerance);
        if (gapExists && c2Bearish && highEnough && nearZone)
        {
            return new Fvg(Side.Sell, upper, lower, c3.OpenTimeUtc, c2.OpenTimeUtc, c1.OpenTimeUtc);
        }

        return null;
    }

    private static void RequireAtr(double atr, string name)
    {
        if (!double.IsFinite(atr) || atr < 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "The ATR must be a finite number that is not negative.");
        }
    }
}
