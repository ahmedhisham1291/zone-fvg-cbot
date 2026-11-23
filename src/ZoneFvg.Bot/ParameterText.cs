using System;
using System.Globalization;

namespace ZoneFvg.Bot
{
    /// <summary>
    /// Converts the text parameters of the cBot (times, dates and names) into the values the core
    /// expects (PLT-006). Conversion only: every range and combination is checked by the core's
    /// <c>Validate()</c> methods. All parsing uses the invariant culture (AGENTS.md rule 7).
    /// </summary>
    internal static class ParameterText
    {
        /// <summary>Parses a UTC time of day written as <c>HH:mm</c>, for example <c>07:00</c> (PLT-006).</summary>
        /// <param name="name">Parameter name, for the error message.</param>
        /// <param name="text">The text.</param>
        /// <returns>The time of day.</returns>
        /// <exception cref="ArgumentException">The text is not a time of day in that form.</exception>
        public static TimeSpan TimeOfDay(string name, string text)
        {
            if (TimeSpan.TryParseExact(text ?? string.Empty, @"hh\:mm", CultureInfo.InvariantCulture, TimeSpanStyles.None, out TimeSpan value))
            {
                return value;
            }

            throw new ArgumentException(name + " must be a UTC time of day written as HH:mm, for example 07:00.", name);
        }

        /// <summary>
        /// Parses a calendar date written as <c>yyyy-MM-dd</c> into a date at 00:00 with kind UTC, the
        /// form <c>RiskParameters.RiskEpochDate</c> expects; the core reads it as 00:00 Prague time on
        /// that date (architecture section 10, PLT-006).
        /// </summary>
        /// <param name="name">Parameter name, for the error message.</param>
        /// <param name="text">The text.</param>
        /// <returns>The date with kind UTC.</returns>
        /// <exception cref="ArgumentException">The text is not a date in that form.</exception>
        public static DateTime Date(string name, string text)
        {
            if (DateTime.TryParseExact(text ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value))
            {
                return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
            }

            throw new ArgumentException(name + " must be a date written as yyyy-MM-dd, for example 2026-12-01.", name);
        }

        /// <summary>
        /// Parses an optional calendar date (PLT-014): an empty or whitespace text means "not
        /// configured" and gives null; any other text is parsed by <see cref="Date"/>, so an invalid
        /// text is still refused.
        /// </summary>
        /// <param name="name">Parameter name, for the error message.</param>
        /// <param name="text">The text.</param>
        /// <returns>Null for an empty text, otherwise the date with kind UTC.</returns>
        /// <exception cref="ArgumentException">The text is not empty and not a date in the form <c>yyyy-MM-dd</c>.</exception>
        public static DateTime? OptionalDate(string name, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return Date(name, text);
        }

        /// <summary>
        /// Parses the exact name of an enum value, case-sensitive. Numbers are not accepted, so a
        /// typing error cannot select an unintended value (PLT-006).
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="name">Parameter name, for the error message.</param>
        /// <param name="text">The text.</param>
        /// <returns>The enum value.</returns>
        /// <exception cref="ArgumentException">The text is not the name of a value.</exception>
        public static T EnumName<T>(string name, string text)
            where T : struct, Enum
        {
            foreach (string candidate in Enum.GetNames(typeof(T)))
            {
                if (string.Equals(candidate, text, StringComparison.Ordinal))
                {
                    return (T)Enum.Parse(typeof(T), candidate);
                }
            }

            throw new ArgumentException(name + " must be one of: " + string.Join(", ", Enum.GetNames(typeof(T))) + ".", name);
        }

        /// <summary>
        /// Checks a parameter that spec section 7 marks as fixed and that has only one supported
        /// value, for example DayResetZone = Europe/Prague (PLT-006).
        /// </summary>
        /// <param name="name">Parameter name, for the error message.</param>
        /// <param name="text">The text.</param>
        /// <param name="expected">The only supported value.</param>
        /// <exception cref="ArgumentException">The text is not the supported value.</exception>
        public static void Fixed(string name, string text, string expected)
        {
            if (!string.Equals(text, expected, StringComparison.Ordinal))
            {
                throw new ArgumentException(name + " is fixed; the only supported value is " + expected + ".", name);
            }
        }

        /// <summary>Returns the text unchanged, or an empty string for null (PLT-006).</summary>
        /// <param name="text">The text.</param>
        /// <returns>The text, never null.</returns>
        public static string Text(string? text)
        {
            return text ?? string.Empty;
        }
    }
}
