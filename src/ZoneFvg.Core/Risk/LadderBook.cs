using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// The ladder's step columns and open deals replayed event by event (RSK-013). Placements and
/// closes must be given in event order: by time, and at the same instant every close of an entry
/// placed earlier comes before the placements, which come in entry order; a close at the instant
/// of its own placement comes right after that placement. <see cref="LadderSizer"/> rebuilds a
/// book from the broker's entries at every call, and the simulator feeds one book as its path
/// runs, so both use the same rule. A book lives for one call or one simulated path only. The
/// columns start at 0 and are never reset inside a book: there is no monthly reset, and a new
/// RiskEpochDate resets them because its book holds no earlier entry (RSK-013 clause 5).
/// </summary>
internal sealed class LadderBook
{
    /// <summary>Number of step columns C, D, E, F (RSK-013 clause 2).</summary>
    internal const int ColumnCount = 4;

    private readonly LadderSizer _sizer;
    private readonly double[] _weights;
    private readonly double _cap;
    private readonly int[] _columns = new int[ColumnCount];
    private readonly List<BookEntry> _entries = new();
    private DateTime _last = DateTime.MinValue;
    private bool _frozen;
    private int _sinceWin;
    private int _firstLiveDeal;

    internal LadderBook(LadderSizer sizer, double cap)
    {
        _sizer = sizer;
        _cap = cap;
        _weights = new double[LadderSizer.Parts];
        IReadOnlyList<double> weights = sizer.PartWeights;
        for (int k = 0; k < LadderSizer.Parts; k++)
        {
            _weights[k] = weights[k];
        }
    }

    /// <summary>Number of entries placed so far.</summary>
    internal int Count => _entries.Count;

    /// <summary>The sizing an entry placed at <paramref name="placedUtc"/> would get. Changes nothing but the time of the last event.</summary>
    internal LadderDecision Preview(DateTime placedUtc)
    {
        Advance(placedUtc);
        int position = _sinceWin + 1;
        int step = position <= ColumnCount ? _columns[position - 1] : 0;
        double raw = _sizer.DealPercent(step) * _weights[0];
        int t = _entries.Count;
        for (int k = 1; k < LadderSizer.Parts; k++)
        {
            int j = t - k;
            if (j >= 0 && j >= _firstLiveDeal)
            {
                raw += _entries[j].Deal * _weights[k];
            }
        }

        return new LadderDecision(Math.Min(raw, _cap), raw, position, step, _columns, _frozen);
    }

    /// <summary>Places the next entry at <paramref name="placedUtc"/> and returns its sizing (RSK-013 clauses 1, 2, 3, 5).</summary>
    internal LadderDecision Place(DateTime placedUtc)
    {
        LadderDecision decision = Preview(placedUtc);
        _entries.Add(new BookEntry(decision.Position, _sizer.DealPercent(decision.Step)));
        _sinceWin++;
        // An infinite raw percent (an endless run of raises) is above the cap; PriceMath compares finite values only.
        if (double.IsPositiveInfinity(decision.RawPercent) || PriceMath.GreaterThan(decision.RawPercent, _cap, PriceMath.DefaultTolerance))
        {
            // Clause 5: wins that close after this placement raise no step; only a new RiskEpochDate (a new book) lifts it.
            _frozen = true;
        }

        return decision;
    }

    /// <summary>
    /// Closes entry <paramref name="index"/> (0-based, in placement order) at
    /// <paramref name="closedUtc"/>. A win (net above zero) raises a column unless frozen, ends
    /// every deal placed before it and starts the positions again (RSK-013 clauses 1, 3, 4).
    /// </summary>
    internal void Close(int index, DateTime closedUtc, double net)
    {
        if (index < 0 || index >= _entries.Count)
        {
            throw new InvalidOperationException("Ladder sizing (RSK-013): a close of an entry that was not placed.");
        }

        Advance(closedUtc);
        if (!PriceMath.GreaterThan(net, 0.0, PriceMath.DefaultTolerance))
        {
            return;
        }

        if (!_frozen)
        {
            int column = Math.Min(_entries[index].Position, ColumnCount) - 1;
            while (column > 0 && _columns[column - 1] == 0)
            {
                column--;
            }

            _columns[column]++;
        }

        _firstLiveDeal = _entries.Count;
        _sinceWin = 0;
    }

    private void Advance(DateTime timeUtc)
    {
        if (timeUtc < _last)
        {
            throw new InvalidOperationException("Ladder sizing (RSK-013): events must come in time order.");
        }

        _last = timeUtc;
    }

    /// <summary>A placed entry: its position and the size of its deal in percent.</summary>
    private sealed record BookEntry(int Position, double Deal);
}
