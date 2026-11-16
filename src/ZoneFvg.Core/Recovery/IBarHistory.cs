using System.Collections.Generic;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Recovery;

/// <summary>
/// Closed-bar history port (WP07, spec 8.3 step 1). The cTrader adapter implements it (WP08);
/// tests use an in-memory version. It never returns a forming bar.
/// </summary>
public interface IBarHistory
{
    /// <summary>
    /// The most recent closed bars of the timeframe, oldest first, at most <paramref name="count"/>
    /// of them; fewer when the platform has fewer loaded (REC-003). Open times are UTC.
    /// </summary>
    /// <param name="timeFrame">D1, H4 or M15.</param>
    /// <param name="count">Largest number of bars wanted.</param>
    /// <returns>The closed bars, oldest first.</returns>
    IReadOnlyList<Bar> ClosedBars(TimeFrame timeFrame, int count);

    /// <summary>
    /// Asks the platform to load older history so that at least <paramref name="count"/> closed
    /// bars of the timeframe become available, and to reload missing bars (REC-003). Recovery
    /// calls it at most once per run and timeframe, then reads <see cref="ClosedBars"/> again.
    /// </summary>
    /// <param name="timeFrame">D1, H4 or M15.</param>
    /// <param name="count">Number of closed bars wanted.</param>
    void RequestMore(TimeFrame timeFrame, int count);
}
