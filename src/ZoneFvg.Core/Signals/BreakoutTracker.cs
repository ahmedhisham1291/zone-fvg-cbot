using System;
using System.Globalization;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The signal logic of the Breakout mode (spec section 3.7, SIG-022 to SIG-025). It holds one
/// reference, the last closed D1 bar, and checks each closed M15 bar against it. It reads no
/// clock, keeps no bar history and computes no bias: the bias and the ATR values are passed in.
/// </summary>
public sealed class BreakoutTracker
{
    private const string IdTimeFormat = "yyyyMMdd-HHmm";

    private readonly BreakoutParameters _parameters;
    private readonly double _rewardRisk;

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
    /// and low, and replaces the old reference whether or not that one confirmed.
    /// </summary>
    /// <param name="bar">The closed D1 bar. It must open after the current reference's bar.</param>
    /// <exception cref="ArgumentNullException">The bar is null.</exception>
    /// <exception cref="InvalidOperationException">The bar does not open after the current reference's bar; nothing is changed.</exception>
    public void OnD1Closed(Bar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        if (Reference is not null && bar.OpenTimeUtc <= Reference.DayOpenUtc)
        {
            throw new InvalidOperationException("A D1 bar opening at " + Invariant.Minute(bar.OpenTimeUtc) + " does not open after the reference bar of " + Invariant.Minute(Reference.DayOpenUtc) + ".");
        }

        Reference = new BreakoutReference(bar.OpenTimeUtc, bar.High, bar.Low, true);
    }

    /// <summary>
    /// An M15 bar has closed (SIG-022 to SIG-025). Returns null and changes nothing when there is
    /// no reference, the reference is Done, the bar is not checked (SIG-022), the bias is Neutral,
    /// or the close does not confirm (SIG-023). On a confirmation the reference becomes Done first;
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
    public BreakoutSetup? OnM15Closed(Bar bar, Bias bias, double? atrM15, double? atrH4)
    {
        ArgumentNullException.ThrowIfNull(bar);
        BreakoutReference? reference = Reference;
        if (reference is null || !reference.Armed || !IsChecked(bar, reference))
        {
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

        Reference = reference with { Armed = false };
        if (!IsPositiveFinite(atrM15) || !IsPositiveFinite(atrH4))
        {
            return null;
        }

        return Levels(bar, reference, side, atrM15.GetValueOrDefault(), atrH4.GetValueOrDefault());
    }

    private static bool IsPositiveFinite(double? value)
    {
        return value.HasValue && double.IsFinite(value.Value) && value.Value > 0.0;
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
