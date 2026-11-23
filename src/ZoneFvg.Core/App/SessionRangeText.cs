using System;
using System.Globalization;
using System.Text.RegularExpressions;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.App;

/// <summary>
/// The six cBot parameters of the SessionRange mode (PLT-015, spec 3.8 and 7) turned into
/// <see cref="SessionRangeParameters"/> and checked with <see cref="SessionRangeParameters.Validate"/>.
/// The adapter calls <see cref="Build"/> at start in every mode. A refusal names each parameter as
/// cTrader shows it: RangeStartNy, RangeEndNy, RangeLastEntryNy, RangeExitNy, RangeMinWidthAtr and
/// RangeStopAtr. Pure: no clock, no culture other than the invariant one.
/// </summary>
public static class SessionRangeText
{
    private const string TimeFormat = @"hh\:mm";

    // The four core names that differ from the cBot's, as whole words. "RangeStartNy" and
    // "RangeEndNy" are the same in both, and "RangeLastEntryNy" or "RangeExitNy" do not match.
    private static readonly Regex CoreNames = new(@"\b(LastEntryNy|ExitNy|MinWidthAtr|StopAtrMult)\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses the four New York times (each <c>HH:mm</c>, invariant culture), takes the two numbers
    /// as they are, and checks the result with <see cref="SessionRangeParameters.Validate"/> (PLT-015).
    /// The time texts are parsed in the order RangeStartNy, RangeEndNy, RangeLastEntryNy,
    /// RangeExitNy; the values are then checked in the order of <see cref="SessionRangeParameters.Validate"/>.
    /// </summary>
    /// <param name="rangeStartNy">The text of RangeStartNy, for example <c>19:00</c>.</param>
    /// <param name="rangeEndNy">The text of RangeEndNy, for example <c>03:00</c>.</param>
    /// <param name="rangeLastEntryNy">The text of RangeLastEntryNy, for example <c>12:00</c>.</param>
    /// <param name="rangeExitNy">The text of RangeExitNy, for example <c>16:30</c>.</param>
    /// <param name="rangeMinWidthAtr">The value of RangeMinWidthAtr.</param>
    /// <param name="rangeStopAtr">The value of RangeStopAtr.</param>
    /// <returns>The checked parameters.</returns>
    /// <exception cref="ArgumentException">
    /// A text is not a time of day written as <c>HH:mm</c>, or a value is out of range. The
    /// parameter name of the exception and every parameter name in its message are the cBot's.
    /// </exception>
    public static SessionRangeParameters Build(string? rangeStartNy, string? rangeEndNy, string? rangeLastEntryNy, string? rangeExitNy, double rangeMinWidthAtr, double rangeStopAtr)
    {
        var parameters = new SessionRangeParameters
        {
            RangeStartNy = TimeOfDay("RangeStartNy", rangeStartNy),
            RangeEndNy = TimeOfDay("RangeEndNy", rangeEndNy),
            LastEntryNy = TimeOfDay("RangeLastEntryNy", rangeLastEntryNy),
            ExitNy = TimeOfDay("RangeExitNy", rangeExitNy),
            MinWidthAtr = rangeMinWidthAtr,
            StopAtrMult = rangeStopAtr,
        };

        try
        {
            parameters.Validate();
        }
        catch (ArgumentException exception)
        {
            // Not swallowed: the same refusal is thrown again with the cBot's names.
            string coreName = exception.ParamName ?? string.Empty;
            throw new ArgumentException(CbotText(exception.Message, coreName), CbotName(coreName), exception);
        }

        return parameters;
    }

    /// <summary>
    /// The name cTrader shows for a property of <see cref="SessionRangeParameters"/> (PLT-015):
    /// LastEntryNy is RangeLastEntryNy, ExitNy is RangeExitNy, MinWidthAtr is RangeMinWidthAtr,
    /// StopAtrMult is RangeStopAtr; every other name is returned unchanged.
    /// </summary>
    /// <param name="coreName">The property name in the core.</param>
    /// <returns>The cBot parameter name.</returns>
    public static string CbotName(string coreName)
    {
        return coreName switch
        {
            nameof(SessionRangeParameters.LastEntryNy) => "RangeLastEntryNy",
            nameof(SessionRangeParameters.ExitNy) => "RangeExitNy",
            nameof(SessionRangeParameters.MinWidthAtr) => "RangeMinWidthAtr",
            nameof(SessionRangeParameters.StopAtrMult) => "RangeStopAtr",
            _ => coreName,
        };
    }

    private static TimeSpan TimeOfDay(string name, string? text)
    {
        if (TimeSpan.TryParseExact(text ?? string.Empty, TimeFormat, CultureInfo.InvariantCulture, TimeSpanStyles.None, out TimeSpan value))
        {
            return value;
        }

        throw new ArgumentException(name + " must be a New York time of day written as HH:mm, for example 19:00.", name);
    }

    private static string CbotText(string message, string coreName)
    {
        // ArgumentException.Message ends with " (Parameter 'name')" when a name is set; the new
        // exception adds the same ending with the cBot's name.
        string ending = " (Parameter '" + coreName + "')";
        string text = coreName.Length > 0 && message.EndsWith(ending, StringComparison.Ordinal)
            ? message.Substring(0, message.Length - ending.Length)
            : message;
        return CoreNames.Replace(text, match => CbotName(match.Value));
    }
}
