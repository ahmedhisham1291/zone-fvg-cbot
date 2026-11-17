using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// The ladder's step columns and live deals after some placements and closes (RSK-013). Immutable
/// (RSK-011): <see cref="Place"/> and <see cref="Close"/> return a new state. Events must be given
/// in event order: by time, and at the same instant every close of an entry placed earlier comes
/// before the placements, which come in entry order; a close at the instant of its own placement
/// comes right after that placement. <see cref="LadderSizer"/> replays the broker's entries into a
/// state at every call, and the simulator carries one state through a path, so both use the same
/// rule. The columns start at 0 and are never reset: there is no monthly reset, and a new
/// RiskEpochDate resets them because entries before it are not replayed (RSK-013 clause 5).
/// </summary>
internal sealed class LadderState
{
    /// <summary>Number of step columns C, D, E, F (RSK-013 clause 2).</summary>
    internal const int ColumnCount = 4;

    private readonly LadderSizer _sizer;
    private readonly double _cap;

    // Never written after construction; a change makes a new array.
    private readonly int[] _columns;

    // Deals of the last three entries, newest first, as long as no win has ended them; 0 when none.
    private readonly double _deal1;
    private readonly double _deal2;
    private readonly double _deal3;
    private readonly bool _frozen;
    private readonly int _sinceWin;
    private readonly DateTime _last;

    private LadderState(LadderSizer sizer, double cap, int[] columns, double deal1, double deal2, double deal3, bool frozen, int sinceWin, DateTime last)
    {
        _sizer = sizer;
        _cap = cap;
        _columns = columns;
        _deal1 = deal1;
        _deal2 = deal2;
        _deal3 = deal3;
        _frozen = frozen;
        _sinceWin = sinceWin;
        _last = last;
    }

    /// <summary>The state before the first entry: all columns 0, no deals, not frozen.</summary>
    internal static LadderState Start(LadderSizer sizer, double cap)
    {
        return new LadderState(sizer, cap, new int[ColumnCount], 0.0, 0.0, 0.0, false, 0, DateTime.MinValue);
    }

    /// <summary>The sizing an entry placed at <paramref name="placedUtc"/> would get (RSK-013 clauses 1, 2, 3).</summary>
    internal LadderDecision Preview(DateTime placedUtc)
    {
        RequireOrder(placedUtc);
        int position = _sinceWin + 1;
        int step = position <= ColumnCount ? _columns[position - 1] : 0;
        IReadOnlyList<double> w = _sizer.PartWeights;
        double raw = (_sizer.DealPercent(step) * w[0]) + (_deal1 * w[1]) + (_deal2 * w[2]) + (_deal3 * w[3]);
        return new LadderDecision(Math.Min(raw, _cap), raw, position, step, _columns, _frozen);
    }

    /// <summary>Places the next entry at <paramref name="placedUtc"/> (RSK-013 clauses 1, 2, 3, 5).</summary>
    /// <param name="placedUtc">The placement time, not before the last event.</param>
    /// <param name="decision">The sizing of the entry; its position is needed to close it.</param>
    /// <returns>The state after the placement.</returns>
    internal LadderState Place(DateTime placedUtc, out LadderDecision decision)
    {
        decision = Preview(placedUtc);

        // Clause 5: once a raw percent is above the cap, wins raise no step. An infinite raw percent (an
        // endless run of raises) is above it; PriceMath compares finite values only.
        bool frozen = _frozen || double.IsPositiveInfinity(decision.RawPercent) || PriceMath.GreaterThan(decision.RawPercent, _cap, PriceMath.DefaultTolerance);
        return new LadderState(_sizer, _cap, _columns, _sizer.DealPercent(decision.Step), _deal1, _deal2, frozen, _sinceWin + 1, placedUtc);
    }

    /// <summary>
    /// Closes an entry at <paramref name="closedUtc"/>. A win (net above zero, 1e-9 tolerance)
    /// raises the column of <paramref name="position"/> unless frozen, ends every live deal and
    /// starts the positions again (RSK-013 clauses 1, 3, 4).
    /// </summary>
    /// <param name="position">The position the entry had when it was placed, 1 or more.</param>
    /// <param name="closedUtc">The close time, not before the last event.</param>
    /// <param name="net">The entry's net result.</param>
    /// <returns>The state after the close.</returns>
    internal LadderState Close(int position, DateTime closedUtc, double net)
    {
        if (position < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Ladder sizing (RSK-013): a position starts at 1.");
        }

        RequireOrder(closedUtc);
        if (!PriceMath.GreaterThan(net, 0.0, PriceMath.DefaultTolerance))
        {
            return new LadderState(_sizer, _cap, _columns, _deal1, _deal2, _deal3, _frozen, _sinceWin, closedUtc);
        }

        int[] columns = _columns;
        if (!_frozen)
        {
            int column = Math.Min(position, ColumnCount) - 1;
            while (column > 0 && _columns[column - 1] == 0)
            {
                column--;
            }

            columns = (int[])_columns.Clone();
            columns[column]++;
        }

        return new LadderState(_sizer, _cap, columns, 0.0, 0.0, 0.0, _frozen, 0, closedUtc);
    }

    private void RequireOrder(DateTime timeUtc)
    {
        if (timeUtc < _last)
        {
            throw new InvalidOperationException("Ladder sizing (RSK-013): events must come in time order.");
        }
    }
}
