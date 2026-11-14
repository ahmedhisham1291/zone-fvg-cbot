namespace ZoneFvg.Core.News;

/// <summary>
/// Impact rating of a calendar event. Only <see cref="High"/> opens a news window (NWS-001).
/// </summary>
public enum NewsImpact
{
    /// <summary>High impact: opens a news window for the event's currency (NWS-001, NWS-002).</summary>
    High,

    /// <summary>Medium impact: no window (NWS-001).</summary>
    Medium,

    /// <summary>Low impact: no window (NWS-001).</summary>
    Low,

    /// <summary>A holiday: it has no clock time, so it creates no window and is reported (NWS-012).</summary>
    Holiday,

    /// <summary>A rating the parser does not recognise. Kept as non-high (NWS-003).</summary>
    Unknown,
}
