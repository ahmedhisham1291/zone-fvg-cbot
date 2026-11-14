namespace ZoneFvg.Core.News;

/// <summary>Calendar status (NWS-008).</summary>
public enum NewsStatus
{
    /// <summary>At least one source has trusted data that covers the current time (NWS-008).</summary>
    Available,

    /// <summary>No source has trusted data that covers the current time. No entries are allowed (NWS-008).</summary>
    Unavailable,
}
