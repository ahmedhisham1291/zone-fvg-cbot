using System;
using System.Globalization;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.App;

/// <summary>
/// The cBot texts of the Choch entry model and of the trading window (PLT-016, spec 3.9 and 7),
/// turned into the core's values, so that what can be tested without cTrader is tested in the core.
/// The adapter calls <see cref="Build"/> at start in every mode, <see cref="SessionStartUtc"/> and
/// <see cref="SessionEndUtc"/> for the two window texts, and <see cref="ForEveryModeCheck"/> before the
/// every-mode check of the breakout values. A refusal names each parameter as cTrader shows it.
/// Pure: no clock, no culture other than the invariant one.
/// </summary>
public static class ChochText
{
    /// <summary>The value of EntryModel for the gap entry of spec 3.4 and 3.5, the default (PLT-016).</summary>
    public const string Fvg = "Fvg";

    /// <summary>The value of EntryModel for the CHoCH entry of spec 3.9 (PLT-016).</summary>
    public const string Choch = "Choch";

    private const string TimeFormat = @"hh\:mm";

    private static readonly TimeSpan WholeDay = TimeSpan.FromHours(24.0);

    /// <summary>
    /// Reads EntryModel and the seven Choch values (PLT-016, SIG-035). In this order: EntryModel must
    /// be exactly <c>Fvg</c> or <c>Choch</c>; the seven values are checked with
    /// <see cref="ChochParameters.Validate"/> whatever the model and the mode; EntryModel Choch with
    /// an EntryMode other than ZoneFvg is refused naming both parameters.
    /// </summary>
    /// <param name="entryModel">The text of EntryModel.</param>
    /// <param name="mode">The entry mode the adapter read from EntryMode.</param>
    /// <param name="requireSweep">RequireSweep.</param>
    /// <param name="chochWindowBars">ChochWindowBars.</param>
    /// <param name="chochUseBias">ChochUseBias.</param>
    /// <param name="chochStopAtDistal">ChochStopAtDistal.</param>
    /// <param name="chochTargetBufferAtr">ChochTargetBufferAtr.</param>
    /// <param name="chochMinStopPips">ChochMinStopPips.</param>
    /// <param name="minRewardRisk">MinRewardRisk.</param>
    /// <returns>The checked Choch parameters with EntryModel Choch, null with EntryModel Fvg.</returns>
    /// <exception cref="ArgumentException">A text or a value is refused; the parameter name of the exception is the cBot's.</exception>
    public static ChochParameters? Build(string? entryModel, EntryMode mode, bool requireSweep, int chochWindowBars, bool chochUseBias, bool chochStopAtDistal, double chochTargetBufferAtr, double chochMinStopPips, double minRewardRisk)
    {
        bool choch = string.Equals(entryModel, Choch, StringComparison.Ordinal);
        if (!choch && !string.Equals(entryModel, Fvg, StringComparison.Ordinal))
        {
            throw new ArgumentException("EntryModel must be one of: Fvg, Choch.", "EntryModel");
        }

        var parameters = new ChochParameters
        {
            RequireSweep = requireSweep,
            ChochWindowBars = chochWindowBars,
            ChochUseBias = chochUseBias,
            ChochStopAtDistal = chochStopAtDistal,
            ChochTargetBufferAtr = chochTargetBufferAtr,
            ChochMinStopPips = chochMinStopPips,
            MinRewardRisk = minRewardRisk,
        };
        parameters.Validate();
        if (choch && mode != EntryMode.ZoneFvg)
        {
            throw new ArgumentException("EntryModel Choch needs EntryMode ZoneFvg, but EntryMode is " + mode + ".", "EntryModel");
        }

        return choch ? parameters : null;
    }

    /// <summary>Reads SessionStartUtc, a UTC time of day written as <c>HH:mm</c> from 00:00 to 23:59 (PLT-016, EXE-029).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The time of day.</returns>
    /// <exception cref="ArgumentException">The text is not such a time; 24:00 is refused.</exception>
    public static TimeSpan SessionStartUtc(string? text)
    {
        if (TimeSpan.TryParseExact(text ?? string.Empty, TimeFormat, CultureInfo.InvariantCulture, TimeSpanStyles.None, out TimeSpan value))
        {
            return value;
        }

        throw new ArgumentException("SessionStartUtc must be a UTC time of day written as HH:mm, for example 07:00.", "SessionStartUtc");
    }

    /// <summary>
    /// Reads SessionEndUtc, a UTC time of day written as <c>HH:mm</c>, or exactly <c>24:00</c>, the
    /// end of the whole day (PLT-016, EXE-029).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The time of day, or 24 hours for <c>24:00</c>.</returns>
    /// <exception cref="ArgumentException">The text is neither such a time nor <c>24:00</c>.</exception>
    public static TimeSpan SessionEndUtc(string? text)
    {
        if (string.Equals(text, "24:00", StringComparison.Ordinal))
        {
            return WholeDay;
        }

        if (TimeSpan.TryParseExact(text ?? string.Empty, TimeFormat, CultureInfo.InvariantCulture, TimeSpanStyles.None, out TimeSpan value))
        {
            return value;
        }

        throw new ArgumentException("SessionEndUtc must be a UTC time of day written as HH:mm, for example 17:00, or 24:00 for the end of the day.", "SessionEndUtc");
    }

    /// <summary>
    /// The breakout values as the adapter checks them in every mode (PLT-012, PLT-016, EXE-029): in
    /// Breakout mode unchanged, so a window end of 24:00 is refused there; in the other modes a
    /// window end of 24:00 (an all-day session, the setting of the Choch model) is checked as the
    /// last instant before 24:00, so that the all-day session does not refuse the breakout values
    /// that those modes do not use. Every other value is unchanged.
    /// </summary>
    /// <param name="breakout">The breakout values built from the cBot parameters and the session.</param>
    /// <param name="mode">The entry mode.</param>
    /// <returns>The values to validate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="breakout"/> is null.</exception>
    public static BreakoutParameters ForEveryModeCheck(BreakoutParameters breakout, EntryMode mode)
    {
        ArgumentNullException.ThrowIfNull(breakout);
        if (mode == EntryMode.Breakout || breakout.WindowEndUtc != WholeDay)
        {
            return breakout;
        }

        return breakout with { WindowEndUtc = WholeDay - TimeSpan.FromTicks(1) };
    }
}
