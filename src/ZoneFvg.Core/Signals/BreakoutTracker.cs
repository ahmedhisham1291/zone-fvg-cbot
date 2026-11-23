using System;
using System.Globalization;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The signal logic of the Breakout mode (spec sections 3.1 and 3.7, SIG-022 to SIG-025, SIG-027 to
/// SIG-029). It holds one reference, the last closed D1 bar, and checks each closed M15 bar of the
/// reference's day against it. It reads no clock, keeps no bar history and computes no bias or EMA:
/// the bias, the D1 EMA values and the ATR values are passed in.
/// </summary>
public sealed class BreakoutTracker
{
    private const string IdTimeFormat = "yyyyMMdd-HHmm";

    // SIG-027: a reference serves one day of M15 bars, counted from the first bar of its day.
    private static readonly TimeSpan ReferenceLife = TimeSpan.FromHours(24.0);

    private readonly BreakoutParameters _parameters;
    private readonly double _rewardRisk;

    // SIG-027: open time of the first M15 bar received at or after the close of the current reference bar;
    // null until such a bar arrives, and emptied when a D1 bar gives a new reference.
    private DateTime? _dayStartUtc;

    // SIG-028: the fast and slow D1 EMA of the reference bar, each null when unknown; replaced with the reference.
    private double? _fastEma;
    private double? _slowEma;

    /// <summary>Creates a tracker with no reference (SIG-022, SIG-024).</summary>
    /// <param name="parameters">The breakout parameters. They are validated first.</param>
    /// <param name="rewardRisk">Target distance in multiples of the stop distance (SIG-024). Must be finite and greater than zero.</param>
    /// <exception cref="ArgumentNullException">The parameters are null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter or the reward-to-risk ratio is out of range.</exception>
    public BreakoutTracker(BreakoutParameters parameters, double rewardRisk)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        if (!double.IsFinite(rewardRisk) || rewardRisk <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(rewardRisk), "rewardRisk must be a finite number greater than zero but was " + rewardRisk.ToString("R", CultureInfo.InvariantCulture) + ".");
        }

        _parameters = parameters;
        _rewardRisk = rewardRisk;
    }

    /// <summary>The reference day (SIG-022): null until the first D1 bar has closed.</summary>
    public BreakoutReference? Reference { get; private set; }

    /// <summary>
    /// A D1 bar has closed (SIG-022): it becomes the new Armed reference, with its open time, high
    /// and low, and replaces the old reference whether or not that one confirmed. The first M15 bar
    /// of the old reference's day is forgotten (SIG-027). No EMA values come with it, so with the
    /// trend filter on this reference confirms nothing (SIG-028, fail closed).
    /// </summary>
    /// <param name="bar">The closed D1 bar. It must open after the current reference's bar.</param>
    /// <exception cref="ArgumentNullException">The bar is null.</exception>
    /// <exception cref="InvalidOperationException">The bar does not open after the current reference's bar; nothing is changed.</exception>
    public void OnD1Closed(Bar bar)
    {
        OnD1Closed(bar, null, null);
    }

    /// <summary>
    /// A D1 bar has closed (SIG-022, SIG-028): as <see cref="OnD1Closed(Bar)"/>, and the fast
    /// D1 EMA (period BiasEmaPeriod) and the slow D1 EMA (period TrendSlowEmaPeriod) of this bar are
    /// kept with the new reference. With the trend filter on, a value that is null or not finite
    /// lets the reference confirm nothing (fail closed); with the filter off they are not read.
    /// </summary>
    /// <param name="bar">The closed D1 bar. It must open after the current reference's bar.</param>
    /// <param name="fastEma">EMA(BiasEmaPeriod) of the D1 closes up to this bar, or null.</param>
    /// <param name="slowEma">EMA(TrendSlowEmaPeriod) of the D1 closes up to this bar, or null.</param>
    /// <exception cref="ArgumentNullException">The bar is null.</exception>
    /// <exception cref="InvalidOperationException">The bar does not open after the current reference's bar; nothing is changed.</exception>
    public void OnD1Closed(Bar bar, double? fastEma, double? slowEma)
    {
        ArgumentNullException.ThrowIfNull(bar);
        if (Reference is not null && bar.OpenTimeUtc <= Reference.DayOpenUtc)
        {
            throw new InvalidOperationException("A D1 bar opening at " + Invariant.Minute(bar.OpenTimeUtc) + " does not open after the reference bar of " + Invariant.Minute(Reference.DayOpenUtc) + ".");
        }

        Reference = new BreakoutReference(bar.OpenTimeUtc, bar.High, bar.Low, true);
        _dayStartUtc = null;
        _fastEma = fastEma;
        _slowEma = slowEma;
    }

    /// <summary>
    /// An M15 bar has closed (SIG-022 to SIG-025, SIG-027 to SIG-029). While a reference exists, the
    /// first bar that opens at or after the reference bar's close is noted as the first bar of its
    /// day, whatever else the call brings (SIG-027). Returns null and changes nothing else when there
    /// is no reference, the reference is Done, the bar opens 24 hours or more after the first bar of
    /// the reference's day (SIG-027), or the bar is not checked (SIG-022). A checked bar whose close
    /// time of day is at or after LastConfirmUtc makes the reference Done and returns null
    /// (SIG-029). Otherwise null is returned and nothing else changes when the bias is Neutral, the
    /// close does not confirm (SIG-023), or the trend filter is on and the fast D1 EMA is not
    /// strictly above the slow one for a buy, or strictly below it for a sell, with PriceMath and its
    /// default tolerance, or either value is missing (SIG-028). On a confirmation the reference becomes Done first;
    /// then, when an ATR value is missing, not finite or not greater than zero, or a computed level
    /// is not finite or not greater than zero, null is returned (fail closed). Otherwise the setup
    /// of SIG-024 with the id of SIG-025 is returned.
    /// </summary>
    /// <param name="bar">The closed M15 bar c1.</param>
    /// <param name="bias">The D1 bias at the close of the bar (SIG-005).</param>
    /// <param name="atrM15">The latest ATR(M15) at the close of the bar, or null.</param>
    /// <param name="atrH4">The latest ATR(H4) at the close of the bar, or null.</param>
    /// <returns>The breakout setup, or null.</returns>
    /// <exception cref="ArgumentNullException">The bar is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The close time of the reference bar or of the M15 bar is beyond the largest date; nothing is changed.</exception>
    public BreakoutSetup? OnM15Closed(Bar bar, Bias bias, double? atrM15, double? atrH4)
    {
        ArgumentNullException.ThrowIfNull(bar);
        BreakoutReference? reference = Reference;
        if (reference is null)
        {
            return null;
        }

        // SIG-027, before any other return: a difference of times, so that no time beyond the largest date is computed.
        if (!_dayStartUtc.HasValue && bar.OpenTimeUtc - reference.DayOpenUtc >= BarIntakeOrder.Length(TimeFrame.D1))
        {
            _dayStartUtc = bar.OpenTimeUtc;
        }

        if (!reference.Armed || IsPastTheReferenceDay(bar) || !IsChecked(bar, reference))
        {
            return null;
        }

        // SIG-029: a checked bar at or after the latest confirmation time finishes the reference for the day.
        if (BarIntakeOrder.CloseTimeUtc(TimeFrame.M15, bar).TimeOfDay >= _parameters.LastConfirmUtc)
        {
            Reference = reference with { Armed = false };
            return null;
        }

        Side side;
        if (bias == Bias.Long && PriceMath.GreaterThan(bar.Close, reference.High, PriceMath.DefaultTolerance))
        {
            side = Side.Buy;
        }
        else if (bias == Bias.Short && PriceMath.LessThan(bar.Close, reference.Low, PriceMath.DefaultTolerance))
        {
            side = Side.Sell;
        }
        else
        {
            return null;
        }

        // SIG-028: against the trend of the D1 EMAs the bar confirms nothing and the reference stays Armed.
        if (_parameters.TrendSlowEmaPeriod > 0 && !TrendAllows(side))
        {
            return null;
        }

        Reference = reference with { Armed = false };
        if (!IsPositiveFinite(atrM15) || !IsPositiveFinite(atrH4))
        {
            return null;
        }

        return Levels(bar, reference, side, atrM15.GetValueOrDefault(), atrH4.GetValueOrDefault());
    }

    private bool TrendAllows(Side side)
    {
        if (!IsFinite(_fastEma) || !IsFinite(_slowEma))
        {
            return false;
        }

        double fast = _fastEma.GetValueOrDefault();
        double slow = _slowEma.GetValueOrDefault();
        return side == Side.Buy
            ? PriceMath.GreaterThan(fast, slow, PriceMath.DefaultTolerance)
            : PriceMath.LessThan(fast, slow, PriceMath.DefaultTolerance);
    }

    private static bool IsFinite(double? value)
    {
        return value.HasValue && double.IsFinite(value.Value);
    }

    private static bool IsPositiveFinite(double? value)
    {
        return value.HasValue && double.IsFinite(value.Value) && value.Value > 0.0;
    }

    // SIG-027: the bar opens 24 hours or more after the first bar of the reference's day.
    private bool IsPastTheReferenceDay(Bar bar)
    {
        return _dayStartUtc.HasValue && bar.OpenTimeUtc - _dayStartUtc.Value >= ReferenceLife;
    }

    private bool IsChecked(Bar bar, BreakoutReference reference)
    {
        DateTime referenceCloseUtc = reference.DayOpenUtc + BarIntakeOrder.Length(TimeFrame.D1);
        TimeSpan closeTimeOfDay = BarIntakeOrder.CloseTimeUtc(TimeFrame.M15, bar).TimeOfDay;
        return bar.OpenTimeUtc >= referenceCloseUtc
            && closeTimeOfDay >= _parameters.WindowStartUtc
            && closeTimeOfDay < _parameters.WindowEndUtc;
    }

    private BreakoutSetup? Levels(Bar bar, BreakoutReference reference, Side side, double atrM15, double atrH4)
    {
        double entry;
        double stop;
        double target;
        double level;
        string prefix;
        if (side == Side.Buy)
        {
            entry = bar.High + (_parameters.EntryBufferAtr * atrM15);
            stop = entry - (_parameters.StopAtrMult * atrH4);
            target = entry + (_rewardRisk * (entry - stop));
            level = reference.High;
            prefix = "BH-";
        }
        else
        {
            entry = bar.Low - (_parameters.EntryBufferAtr * atrM15);
            stop = entry + (_parameters.StopAtrMult * atrH4);
            target = entry - (_rewardRisk * (stop - entry));
            level = reference.Low;
            prefix = "BL-";
        }

        if (!IsPositiveFinite(entry) || !IsPositiveFinite(stop) || !IsPositiveFinite(target))
        {
            return null;
        }

        string id = prefix + reference.DayOpenUtc.ToString(IdTimeFormat, CultureInfo.InvariantCulture);
        DateTime confirmationCloseUtc = BarIntakeOrder.CloseTimeUtc(TimeFrame.M15, bar);
        return new BreakoutSetup(id, side, entry, stop, target, level, confirmationCloseUtc, atrH4);
    }
}
