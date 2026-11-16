using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// Replaces every occurrence of a secret with <see cref="Replacement"/> (OPS-007). Every journal
/// line, alert text and exception message written by the operations module passes through it.
/// Empty and null secrets are ignored. Secrets are compared ordinally and replaced longest first,
/// so a secret that contains another one is masked as a whole.
/// </summary>
public sealed class SecretMasker
{
    /// <summary>The text that stands in for a secret (OPS-007).</summary>
    public const string Replacement = "***";

    private readonly string[] _secrets;

    /// <summary>Creates a masker for the given secrets (OPS-007).</summary>
    /// <param name="secrets">The secret strings; null and empty entries are ignored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="secrets"/> is null.</exception>
    public SecretMasker(IEnumerable<string?> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        _secrets = secrets
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(s => s.Length)
            .ThenBy(s => s, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Number of distinct non-empty secrets masked (OPS-007).</summary>
    public int Count => _secrets.Length;

    /// <summary>
    /// A masker for the secrets of the operations parameters: the Telegram token, the Telegram
    /// chat id and the heartbeat address (OPS-007).
    /// </summary>
    /// <param name="parameters">The operations parameters.</param>
    /// <returns>The masker.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
    public static SecretMasker For(OpsParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return new SecretMasker(new[] { parameters.TelegramBotToken, parameters.TelegramChatId, parameters.HeartbeatUrl });
    }

    /// <summary>The text with every secret replaced by <see cref="Replacement"/> (OPS-007).</summary>
    /// <param name="text">The text, possibly null.</param>
    /// <returns>The masked text; empty for null.</returns>
    public string Apply(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string result = text;
        foreach (string secret in _secrets)
        {
            result = result.Replace(secret, Replacement, StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// The exception as <c>Type: message</c>, followed by <c> ---&gt; Type: message</c> for each
    /// inner exception, with every secret masked (OPS-007). The stack trace is not included.
    /// </summary>
    /// <param name="exception">The exception, possibly null.</param>
    /// <returns>The masked description; empty for null.</returns>
    public string Apply(Exception? exception)
    {
        var text = new StringBuilder();
        for (Exception? e = exception; e != null; e = e.InnerException)
        {
            if (text.Length > 0)
            {
                text.Append(" ---> ");
            }

            text.Append(e.GetType().Name).Append(": ").Append(e.Message);
        }

        return Apply(text.ToString());
    }
}
