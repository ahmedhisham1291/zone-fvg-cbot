using System;
using System.Globalization;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.App;

/// <summary>
/// The seven cBot parameters of the Overnight mode (PLT-021, SIG-043, spec 3.10 and 7), turned into
/// <see cref="OvernightParameters"/> and checked with <see cref="OvernightParameters.Validate"/>.
/// The cBot names and the core names are the same, so a refusal names the parameter as cTrader
/// shows it. Pure: no clock, no culture other than the invariant one.
/// </summary>
public static class OvernightText
{
    private const string TimeFormat = @"hh\:mm";

    /// <summary>
    /// Parses the three New York times (each <c>HH:mm</c>, invariant culture) in the order
    /// NightEntryNy, NightLatestNy, NightExitNy, takes the four numbers as they are and checks the
    /// result with <see cref="OvernightParameters.Validate"/> (PLT-021).
    /// </summary>
    /// <param name="nightEntryNy">The text of NightEntryNy, for example <c>18:15</c>.</param>
    /// <param name="nightLatestNy">The text of NightLatestNy, for example <c>19:00</c>.</param>
    /// <param name="nightExitNy">The text of NightExitNy, for example <c>02:45</c>.</param>
    /// <param name="nightEntryBufferAtr">NightEntryBufferAtr.</param>
    /// <param name="nightStopAtr">NightStopAtr.</param>
    /// <param name="nightTargetR">NightTargetR.</param>
    /// <param name="nightOrderMinutes">NightOrderMinutes.</param>
    /// <returns>The checked parameters.</returns>
    /// <exception cref="ArgumentException">A text is not a time of day written as <c>HH:mm</c>, or a value is out of range; the parameter name of the exception is the cBot's.</exception>
    public static OvernightParameters Build(string? nightEntryNy, string? nightLatestNy, string? nightExitNy, double nightEntryBufferAtr, double nightStopAtr, double nightTargetR, int nightOrderMinutes)
    {
        var parameters = new OvernightParameters
        {
            NightEntryNy = TimeOfDay(nameof(OvernightParameters.NightEntryNy), nightEntryNy),
            NightLatestNy = TimeOfDay(nameof(OvernightParameters.NightLatestNy), nightLatestNy),
            NightExitNy = TimeOfDay(nameof(OvernightParameters.NightExitNy), nightExitNy),
            NightEntryBufferAtr = nightEntryBufferAtr,
            NightStopAtr = nightStopAtr,
            NightTargetR = nightTargetR,
            NightOrderMinutes = nightOrderMinutes,
        };
        parameters.Validate();
        return parameters;
    }

    private static TimeSpan TimeOfDay(string name, string? text)
    {
        if (TimeSpan.TryParseExact(text ?? string.Empty, TimeFormat, CultureInfo.InvariantCulture, TimeSpanStyles.None, out TimeSpan value))
        {
            return value;
        }

        throw new ArgumentException(name + " must be a New York time of day written as HH:mm, for example 18:15.", name);
    }
}
