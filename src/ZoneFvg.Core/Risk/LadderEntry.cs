using System;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// One bot entry as ladder sizing sees it (RSK-013): when it was placed, whether and when it
/// closed, and its net result. A data record; <see cref="LadderSizer"/> checks the values.
/// </summary>
/// <param name="Key">Identity of the entry, unique in a list: the order label for broker data.</param>
/// <param name="PlacedUtc">Time the entry was placed, kind UTC. Entries are numbered in this order.</param>
/// <param name="ClosedUtc">Time the entry closed completely, kind UTC, or null while it is pending or open.</param>
/// <param name="NetResult">Net result in the account currency, commission and swap included. Used only when closed.</param>
public sealed record LadderEntry(string Key, DateTime PlacedUtc, DateTime? ClosedUtc, double NetResult)
{
    /// <summary>Identity of the entry, unique in a list: the order label for broker data.</summary>
    public string Key { get; init; } = Key;

    /// <summary>Time the entry was placed, kind UTC. Entries are numbered in this order.</summary>
    public DateTime PlacedUtc { get; init; } = PlacedUtc;

    /// <summary>Time the entry closed completely, kind UTC, or null while it is pending or open.</summary>
    public DateTime? ClosedUtc { get; init; } = ClosedUtc;

    /// <summary>Net result in the account currency, commission and swap included. Used only when closed.</summary>
    public double NetResult { get; init; } = NetResult;
}
