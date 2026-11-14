namespace ZoneFvg.Core.News;

/// <summary>Behaviour when the calendar is unavailable (spec 6.3, NWS-008).</summary>
public enum NewsFailMode
{
    /// <summary>No new entries until a feed returns; an alert is raised once per outage (NWS-008).</summary>
    Block,
}
