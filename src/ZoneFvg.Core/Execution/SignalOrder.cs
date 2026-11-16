using System;
using System.Collections.Generic;
using System.Linq;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Execution;

/// <summary>
/// Order in which the signals of one bar are evaluated (WP06 decision): by zone id, ordinal, so
/// the smaller zone id is evaluated first. Once its order is placed the symbol is busy and the
/// next signal is skipped with <see cref="SkipReason.SymbolBusy"/> (EXE-002).
/// </summary>
public static class SignalOrder
{
    /// <summary>The signals ordered by zone id, ordinal; equal ids keep their input order (EXE-002).</summary>
    /// <param name="signals">The signals of one bar.</param>
    /// <returns>A new list in evaluation order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="signals"/> is null.</exception>
    public static IReadOnlyList<Signal> ForEvaluation(IEnumerable<Signal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        return signals.OrderBy(signal => signal.ZoneId, StringComparer.Ordinal).ToList().AsReadOnly();
    }
}
