using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Execution;

/// <summary>
/// Which bot orders and positions of its symbol an instance acts on when it cancels, closes,
/// modifies or time-exits (EXE-033, spec v0.18 row BA): an Overnight instance acts only on trades
/// whose label carries an Overnight id (<see cref="OvernightId"/>); an instance of any other mode
/// acts on every bot trade of its symbol except those, exactly as before the Overnight mode
/// existed. So the SessionRange and the Overnight instance on XAUUSD never touch each other's
/// order or position, and nothing changes for the other modes. Reads only the label.
/// </summary>
public static class InstanceScope
{
    /// <summary>
    /// True when the instance owns the bot trade with this label (EXE-033): for an Overnight
    /// instance when the label's id is an Overnight id, for every other instance when it is not
    /// (a label that cannot be read counts as not Overnight).
    /// </summary>
    /// <param name="overnightInstance">True for an instance in Overnight mode.</param>
    /// <param name="label">The label of the order or position.</param>
    /// <returns>True when the instance may act on the trade.</returns>
    public static bool Owns(bool overnightInstance, string? label)
    {
        bool overnightId = OrderLabel.TryParse(label, out string _, out string id) && OvernightId.TryTradingDate(id, out _);
        return overnightInstance == overnightId;
    }
}
