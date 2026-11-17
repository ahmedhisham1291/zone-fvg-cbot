using System;
using System.Globalization;

namespace ZoneFvg.Core.Execution;

/// <summary>The plan stored in an order comment (EXE-008, EXE-011).</summary>
/// <param name="Entry">Planned entry price.</param>
/// <param name="Stop">Planned stop-loss price, spread included for a sell (EXE-007).</param>
/// <param name="Target">Planned take-profit price.</param>
/// <param name="RiskMoney">Money at risk at the stop with the placed volume, in the account currency.</param>
/// <param name="ParameterHash">The 8-character parameter hash of the bot that placed the order.</param>
public sealed record CommentPlan(double Entry, double Stop, double Target, double RiskMoney, string ParameterHash);

/// <summary>
/// The order comment <c>e=&lt;entry&gt;;s=&lt;stop&gt;;t=&lt;target&gt;;r=&lt;riskMoney&gt;;h=&lt;hash8&gt;</c>
/// (EXE-008, docs/architecture.md section 7). Prices are written with the symbol's digits, the
/// risk money with two decimals, all in invariant formatting. The comment is the trade's memory at
/// the broker: a position without a stop loss gets its stop back from it (EXE-011).
/// </summary>
public static class OrderComment
{
    /// <summary>Longest comment built or accepted, in characters (EXE-008).</summary>
    public const int MaxLength = 100;

    private static readonly string[] Keys = { "e", "s", "t", "r", "h" };

    /// <summary>Builds the comment of a plan (EXE-008).</summary>
    /// <param name="plan">The plan; prices finite and greater than zero, risk money finite and not negative, hash 8 lowercase hex characters.</param>
    /// <param name="digits">Price digits of the symbol, 0 to 10 (<see cref="Ports.SymbolInfo.Digits"/>, EXE-017).</param>
    /// <returns>The comment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is null.</exception>
    /// <exception cref="ArgumentException">A value is out of range, or the comment would be longer than <see cref="MaxLength"/>.</exception>
    public static string Build(CommentPlan plan, int digits)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (digits < 0 || digits > 10)
        {
            throw new ArgumentException("The digits must be between 0 and 10.", nameof(digits));
        }

        if (!IsPrice(plan.Entry) || !IsPrice(plan.Stop) || !IsPrice(plan.Target) || !double.IsFinite(plan.RiskMoney) || plan.RiskMoney < 0.0 || !IsHash(plan.ParameterHash))
        {
            throw new ArgumentException("The plan has a value that cannot be written to a comment.", nameof(plan));
        }

        string format = "F" + digits.ToString(CultureInfo.InvariantCulture);
        string comment = "e=" + plan.Entry.ToString(format, CultureInfo.InvariantCulture)
            + ";s=" + plan.Stop.ToString(format, CultureInfo.InvariantCulture)
            + ";t=" + plan.Target.ToString(format, CultureInfo.InvariantCulture)
            + ";r=" + plan.RiskMoney.ToString("F2", CultureInfo.InvariantCulture)
            + ";h=" + plan.ParameterHash;
        if (comment.Length > MaxLength)
        {
            throw new ArgumentException("The comment would be longer than 100 characters.", nameof(plan));
        }

        return comment;
    }

    /// <summary>
    /// Reads a comment built by <see cref="Build"/> (EXE-008, EXE-011). Strict: exactly the five
    /// fields in order, plain decimal numbers without sign or exponent, prices greater than zero,
    /// and a hash of 8 lowercase hex characters.
    /// </summary>
    /// <param name="comment">The comment, possibly null.</param>
    /// <returns>The plan, or null when the comment is not a valid plan comment.</returns>
    public static CommentPlan? TryParse(string? comment)
    {
        if (comment == null || comment.Length > MaxLength)
        {
            return null;
        }

        string[] fields = comment.Split(';');
        if (fields.Length != Keys.Length)
        {
            return null;
        }

        var values = new string[Keys.Length];
        for (int i = 0; i < Keys.Length; i++)
        {
            string prefix = Keys[i] + "=";
            if (!fields[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }

            values[i] = fields[i].Substring(prefix.Length);
        }

        double? entry = Number(values[0]);
        double? stop = Number(values[1]);
        double? target = Number(values[2]);
        double? risk = Number(values[3]);
        if (entry is not > 0.0 || stop is not > 0.0 || target is not > 0.0 || risk is null || !IsHash(values[4]))
        {
            return null;
        }

        return new CommentPlan(entry.Value, stop.Value, target.Value, risk.Value, values[4]);
    }

    private static double? Number(string text)
    {
        if (text.Length == 0 || !double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        {
            return null;
        }

        return value;
    }

    private static bool IsPrice(double value)
    {
        return double.IsFinite(value) && value > 0.0;
    }

    private static bool IsHash(string? hash)
    {
        if (hash == null || hash.Length != ParameterHash.Length)
        {
            return false;
        }

        foreach (char c in hash)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
