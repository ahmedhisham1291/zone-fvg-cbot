using System;

namespace ZoneFvg.Core.News;

/// <summary>
/// A news window of one symbol (NWS-001): from NewsBeforeMin before a high-impact event to
/// NewsAfterMin after it, both ends included. Overlapping or touching windows are merged.
/// </summary>
/// <param name="StartUtc">First instant inside the window, UTC.</param>
/// <param name="EndUtc">Last instant inside the window, UTC.</param>
public sealed record NewsWindow(DateTime StartUtc, DateTime EndUtc)
{
    /// <summary>True when the instant is inside the window, both ends included (NWS-001).</summary>
    /// <param name="utc">The instant.</param>
    /// <returns>True when <paramref name="utc"/> is at or after the start and at or before the end.</returns>
    public bool Contains(DateTime utc)
    {
        return utc >= StartUtc && utc <= EndUtc;
    }
}
