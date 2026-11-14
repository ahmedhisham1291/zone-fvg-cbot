using System;
using System.Globalization;

namespace ZoneFvg.Core.News;

/// <summary>
/// The news parameters of spec section 7 with their documented defaults. Instances are
/// immutable; use a <c>with</c> expression to derive a changed copy. <see cref="Validate"/> is
/// called at start; a value it rejects stops the bot with the message of the exception
/// (NWS-004, NWS-011).
/// </summary>
public sealed record NewsParameters
{
    /// <summary>The only accepted value of <see cref="NewsSources"/> (NWS-004, owner decision of 2026-11-14).</summary>
    public const string ForexFactorySources = "ForexFactory";

    /// <summary>Smallest time between two fetches of one source, in minutes, across restarts (NWS-006). Fixed by the spec.</summary>
    public const int MinFetchIntervalMinutes = 60;

    /// <summary>
    /// News filter on or off (spec 7). Default true. Carried for the execution layer, which
    /// decides whether to consult the filter; this module does not read it.
    /// </summary>
    public bool NewsEnabled { get; init; } = true;

    /// <summary>Minutes a news window starts before its event (NWS-001). Default 30.</summary>
    public int NewsBeforeMin { get; init; } = 30;

    /// <summary>Minutes a news window ends after its event (NWS-001). Default 30.</summary>
    public int NewsAfterMin { get; init; } = 30;

    /// <summary>Behaviour without a calendar (NWS-008). Default and only value: Block.</summary>
    public NewsFailMode NewsFailMode { get; init; } = NewsFailMode.Block;

    /// <summary>
    /// Feeds used (NWS-004). Default ForexFactory, which is the only accepted value until a second
    /// feed is added (owner decision of 2026-11-14).
    /// </summary>
    public string NewsSources { get; init; } = ForexFactorySources;

    /// <summary>Longest time, in hours, that data without a successful refresh is trusted (NWS-007). Default 48.</summary>
    public int NewsMaxCacheHours { get; init; } = 48;

    /// <summary>Feed refresh interval in hours (NWS-006). Default 4.</summary>
    public int NewsRefreshHours { get; init; } = 4;

    /// <summary>
    /// Manual blackout list: semicolon-separated <c>yyyy-MM-dd HH:mm CUR</c> entries in UTC
    /// (NWS-011). Default empty.
    /// </summary>
    public string ExtraBlackouts { get; init; } = string.Empty;

    /// <summary>
    /// Checks every value and throws for the first one that is not accepted, in the order the
    /// parameters are declared. The message names the parameter and quotes the rejected value.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A number or the fail mode is out of range. The parameter name is the name of the parameter.</exception>
    /// <exception cref="ArgumentException">NewsSources is not ForexFactory (NWS-004), or an ExtraBlackouts entry is malformed (NWS-011). The parameter name is the name of the parameter.</exception>
    public void Validate()
    {
        RequireRange(NewsBeforeMin, 1, 1440, nameof(NewsBeforeMin));
        RequireRange(NewsAfterMin, 1, 1440, nameof(NewsAfterMin));
        if (NewsFailMode != NewsFailMode.Block)
        {
            throw new ArgumentOutOfRangeException(nameof(NewsFailMode), "NewsFailMode must be Block but was " + NewsFailMode.ToString() + ".");
        }

        if (!string.Equals(NewsSources, ForexFactorySources, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "NewsSources must be \"" + ForexFactorySources + "\": the news filter runs on Forex Factory alone (owner decision of 2026-11-14), but was \""
                + (NewsSources ?? "null") + "\".",
                nameof(NewsSources));
        }

        RequireRange(NewsMaxCacheHours, 1, 168, nameof(NewsMaxCacheHours));
        RequireRange(NewsRefreshHours, 1, NewsMaxCacheHours, nameof(NewsRefreshHours));
        ExtraBlackoutsResult blackouts = ExtraBlackoutsParser.Parse(ExtraBlackouts);
        if (!blackouts.IsValid)
        {
            throw new ArgumentException(blackouts.Error, nameof(ExtraBlackouts));
        }
    }

    private static void RequireRange(int value, int min, int max, string name)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(
                name,
                name + " must be from " + min.ToString(CultureInfo.InvariantCulture) + " to " + max.ToString(CultureInfo.InvariantCulture)
                + " but was " + value.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }
}
