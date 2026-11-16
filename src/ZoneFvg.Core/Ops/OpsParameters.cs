using System;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// Operations parameters of spec section 7 (HeartbeatUrl, TelegramBotToken, TelegramChatId), the
/// profit target of the current FTMO phase for the daily summary (OPS-003), and the fixed values
/// of the operations module (OPS-003 to OPS-007). Instances are immutable; use a <c>with</c>
/// expression to derive a changed copy.
/// </summary>
/// <remarks>
/// The token, the chat id and the heartbeat address are secrets (OPS-007). They are optional:
/// with an empty token and chat id the alert service sends nothing but still journals, and with
/// an empty address the heartbeat does nothing. <see cref="Validate"/> never puts a value of
/// these three into its message.
/// </remarks>
public sealed record OpsParameters
{
    /// <summary>Minutes between two heartbeat calls (OPS-005, spec 11).</summary>
    public const int HeartbeatIntervalMinutes = 5;

    /// <summary>Days without an opened trade after which the inactivity alert is raised (OPS-006, spec 11).</summary>
    public const int InactivityAlertDays = 21;

    /// <summary>Days between repeated inactivity alerts after the first one (OPS-006, WP09 prompt).</summary>
    public const int InactivityRepeatDays = 3;

    /// <summary>Longest alert text sent to Telegram, in characters (OPS-004, WP09 prompt).</summary>
    public const int MaxAlertLength = 1000;

    /// <summary>Longest wait for one heartbeat or Telegram request, in seconds (OPS-004, OPS-005).</summary>
    public const int HttpTimeoutSeconds = 5;

    /// <summary>Most alerts waiting to be sent; when full, the oldest is dropped (OPS-004).</summary>
    public const int AlertQueueCapacity = 50;

    /// <summary>Most alerts sent in one <see cref="AlertService.Pump"/> call (OPS-004).</summary>
    public const int AlertsPerPump = 3;

    /// <summary>Wait after the first failed send, in minutes; doubled after each further failure (OPS-004).</summary>
    public const int BackoffFirstMinutes = 1;

    /// <summary>Longest wait between two send attempts, in minutes (OPS-004).</summary>
    public const int BackoffMaxMinutes = 30;

    /// <summary>An alert identical to one raised less than this many minutes earlier is collapsed (OPS-004).</summary>
    public const int CollapseWindowMinutes = 10;

    /// <summary>Address of the external heartbeat monitor, or empty for none (OPS-005, spec 7). A secret (OPS-007).</summary>
    public string HeartbeatUrl { get; init; } = string.Empty;

    /// <summary>Telegram bot token, or empty for no alerts (OPS-004, spec 7). A secret (OPS-007).</summary>
    public string TelegramBotToken { get; init; } = string.Empty;

    /// <summary>Telegram chat id, or empty for no alerts (OPS-004, spec 7). Treated as a secret (OPS-007).</summary>
    public string TelegramChatId { get; init; } = string.Empty;

    /// <summary>
    /// Profit target of the current FTMO phase in percent of InitialCapital, for the progress line
    /// of the daily summary (OPS-003, spec 15.2): 10 in the Challenge, 5 in the Verification.
    /// Default 10.
    /// </summary>
    public double ProfitTargetPercent { get; init; } = 10.0;

    /// <summary>True when both the Telegram token and the chat id are set, so alerts are sent (OPS-004).</summary>
    public bool AlertsEnabled => !string.IsNullOrEmpty(TelegramBotToken) && !string.IsNullOrEmpty(TelegramChatId);

    /// <summary>True when a heartbeat address is set (OPS-005).</summary>
    public bool HeartbeatEnabled => !string.IsNullOrEmpty(HeartbeatUrl);

    /// <summary>
    /// Checks every value and throws for the first one that is out of range (OPS-004, OPS-005,
    /// OPS-007). The token and chat id are both empty or both set; the token holds only letters,
    /// digits, ':', '_' and '-'; the chat id only letters, digits, '@', '_' and '-'; the heartbeat
    /// address is empty or an absolute http or https address without spaces; the profit target
    /// is greater than 0 and at most 100. A message never contains the value of a secret.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range. The exception's parameter name is the name of the parameter.</exception>
    public void Validate()
    {
        if (HeartbeatUrl == null || (HeartbeatUrl.Length > 0 && !IsHttpAddress(HeartbeatUrl)))
        {
            throw Invalid(nameof(HeartbeatUrl), "must be empty or an absolute http or https address without spaces");
        }

        if (TelegramBotToken == null || !OnlyChars(TelegramBotToken, ":_-"))
        {
            throw Invalid(nameof(TelegramBotToken), "may contain only letters, digits, ':', '_' and '-'");
        }

        if (TelegramChatId == null || !OnlyChars(TelegramChatId, "@_-"))
        {
            throw Invalid(nameof(TelegramChatId), "may contain only letters, digits, '@', '_' and '-'");
        }

        if ((TelegramBotToken.Length == 0) != (TelegramChatId.Length == 0))
        {
            throw Invalid(TelegramBotToken.Length == 0 ? nameof(TelegramBotToken) : nameof(TelegramChatId), "must be set together with the other Telegram parameter, or both left empty");
        }

        if (!double.IsFinite(ProfitTargetPercent) || ProfitTargetPercent <= 0.0 || ProfitTargetPercent > 100.0)
        {
            throw Invalid(nameof(ProfitTargetPercent), "must be greater than 0 and at most 100");
        }
    }

    private static bool IsHttpAddress(string value)
    {
        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return false;
            }
        }

        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    private static bool OnlyChars(string value, string extra)
    {
        foreach (char c in value)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || extra.IndexOf(c) >= 0;
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static ArgumentOutOfRangeException Invalid(string name, string rule)
    {
        return new ArgumentOutOfRangeException(name, name + " " + rule + ".");
    }
}
