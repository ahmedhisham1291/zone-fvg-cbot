using System;
using System.Globalization;
using System.Text;

namespace ZoneFvg.Core.Execution.Ports;

/// <summary>
/// Turns a key of the key-value port into a key the platform's local storage accepts (NWS-006,
/// NWS-007). cTrader allows only Latin letters, digits and inner spaces in a storage key, and
/// rejects anything else with an exception, so the port's keys (such as <c>journal.trades</c>)
/// are stored under an encoded form. The encoding is one-to-one: ASCII letters and digits other
/// than <c>Q</c> stay as they are, and every other character, <c>Q</c> included, becomes
/// <c>Q</c> followed by its code as two upper-case hexadecimal digits. The result contains only
/// letters and digits, so it never starts or ends with a space.
/// </summary>
public static class StorageKey
{
    private const char Escape = 'Q';

    /// <summary>Returns the platform key for a port key (NWS-006, NWS-007).</summary>
    /// <param name="key">The port key: not empty, ASCII characters only.</param>
    /// <returns>The encoded key, made only of Latin letters and digits.</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    /// <exception cref="ArgumentException">The key is empty or has a character outside ASCII.</exception>
    public static string Encode(string key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (key.Length == 0)
        {
            throw new ArgumentException("A storage key must not be empty.", nameof(key));
        }

        var builder = new StringBuilder(key.Length * 2);
        foreach (char c in key)
        {
            if (c > '\u007f')
            {
                throw new ArgumentException("A storage key must be ASCII.", nameof(key));
            }

            if (IsKept(c))
            {
                builder.Append(c);
            }
            else
            {
                builder.Append(Escape);
                builder.Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static bool IsKept(char c)
    {
        bool letterOrDigit = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
        return letterOrDigit && c != Escape;
    }
}
