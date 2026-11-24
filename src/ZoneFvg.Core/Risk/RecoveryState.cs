using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// The Recovery state of one symbol between two events (RSK-016, spec 4.1.1, owner decision Z of
/// 2026-11-24): the step columns C to J, the entries placed since the latest win or reset, the
/// non-wins in a row and whether the steps are frozen. Immutable: <see cref="Place"/> and
/// <see cref="Close"/> return a new state. Used by <see cref="RecoverySizer"/> on broker data and by
/// the challenge simulator on simulated trades (SIM-006).
/// </summary>
/// <remarks>
/// <para>Position of a new entry: 1 plus the entries placed since the latest win or reset, at most
/// 8 (a ninth uses position 8). raw = Bp x LadderGrowth ^ step(p) by repeated multiplication;
/// R = raw, or the cap (balance x RecoveryMaxRiskPercent / 100) when raw is above it beyond the
/// 1e-9 tolerance of <see cref="PriceMath"/>. An entry whose raw amount is above the fixed freeze
/// limit InitialCapital x RecoveryMaxRiskPercent / 100 freezes the steps; no balance decides the
/// freeze, so each symbol's columns depend on its own entries only.</para>
/// <para>A win (net above zero, 1e-9 tolerance) raises the column of its position unless frozen,
/// moving one column back while the column before it is 0 (C is always allowed), and starts the
/// positions again. The eighth non-win in a row (break-even included) resets all columns to 0,
/// lifts the freeze and starts the positions again.</para>
/// </remarks>
public sealed class RecoveryState
{
    private readonly double[] _baseAmounts;
    private readonly double _growth;
    private readonly double _maxPercent;
    private readonly double _freezeLimit;
    private readonly int[] _columns;
    private readonly int _placedSince;
    private readonly int _nonWins;
    private readonly bool _frozen;
    private readonly DateTime _last;

    private RecoveryState(double[] baseAmounts, double growth, double maxPercent, double freezeLimit, int[] columns, int placedSince, int nonWins, bool frozen, DateTime last)
    {
        _baseAmounts = baseAmounts;
        _growth = growth;
        _maxPercent = maxPercent;
        _freezeLimit = freezeLimit;
        _columns = columns;
        _placedSince = placedSince;
        _nonWins = nonWins;
        _frozen = frozen;
        _last = last;
    }

    /// <summary>The columns C to J (RSK-016).</summary>
    public IReadOnlyList<int> Columns => (int[])_columns.Clone();

    /// <summary>True while wins raise no step (RSK-016, freeze).</summary>
    public bool Frozen => _frozen;

    /// <summary>Non-win closes in a row since the latest win or reset (RSK-016).</summary>
    public int NonWinsInARow => _nonWins;

    /// <summary>
    /// The sizing a new entry would get now with this balance (RSK-016); the state is not changed.
    /// </summary>
    /// <param name="balance">The balance at the placement, a finite number greater than zero.</param>
    /// <returns>Position, step, raw amount, R, cap, columns and the freeze after the placement.</returns>
    /// <exception cref="ArgumentException">The balance is not a finite number greater than zero.</exception>
    public RecoveryDecision Preview(double balance)
    {
        if (!double.IsFinite(balance) || balance <= 0.0)
        {
            throw new ArgumentException("Recovery sizing (RSK-017): the balance is not a finite number greater than zero.", nameof(balance));
        }

        int position = Math.Min(_placedSince + 1, RecoverySizer.Positions);
        int step = _columns[position - 1];
        double raw = _baseAmounts[position - 1];
        for (int i = 0; i < step && double.IsFinite(raw); i++)
        {
            raw *= _growth;
        }

        double cap = balance * _maxPercent / 100.0;
        bool above = !double.IsFinite(raw) || PriceMath.GreaterThan(raw, cap, PriceMath.DefaultTolerance);

        // The freeze is judged against the fixed reference RecoveryMaxRiskPercent x InitialCapital (orchestrating
        // session, 2026-11-24), never a balance, so no other symbol's result can change this symbol's columns.
        bool freezes = !double.IsFinite(raw) || PriceMath.GreaterThan(raw, _freezeLimit, PriceMath.DefaultTolerance);
        return new RecoveryDecision(above ? cap : raw, raw, cap, position, step, above, _frozen || freezes, Columns);
    }

    /// <summary>Places an entry at <paramref name="placedUtc"/> (RSK-016).</summary>
    /// <param name="placedUtc">The placement time, not before the last event.</param>
    /// <param name="balance">The balance at the placement.</param>
    /// <param name="decision">The sizing of the entry.</param>
    /// <returns>The state after the placement.</returns>
    /// <exception cref="ArgumentException">The balance is not a finite number greater than zero.</exception>
    /// <exception cref="InvalidOperationException">The time is before the last event.</exception>
    public RecoveryState Place(DateTime placedUtc, double balance, out RecoveryDecision decision)
    {
        RequireOrder(placedUtc);
        decision = Preview(balance);
        return new RecoveryState(_baseAmounts, _growth, _maxPercent, _freezeLimit, _columns, _placedSince + 1, _nonWins, decision.StepsFrozen, placedUtc);
    }

    /// <summary>Closes an entry of <paramref name="position"/> at <paramref name="closedUtc"/> with its net result (RSK-016).</summary>
    /// <param name="position">The position the entry had when it was placed, 1 to 8.</param>
    /// <param name="closedUtc">The close time, not before the last event.</param>
    /// <param name="net">The entry's net result.</param>
    /// <returns>The state after the close.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is not 1 to 8.</exception>
    /// <exception cref="InvalidOperationException">The time is before the last event.</exception>
    public RecoveryState Close(int position, DateTime closedUtc, double net)
    {
        if (position < 1 || position > RecoverySizer.Positions)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Recovery sizing (RSK-016): a position is 1 to 8.");
        }

        RequireOrder(closedUtc);
        if (PriceMath.GreaterThan(net, 0.0, PriceMath.DefaultTolerance))
        {
            int[] columns = _columns;
            if (!_frozen)
            {
                int column = position - 1;
                while (column > 0 && _columns[column - 1] == 0)
                {
                    column--;
                }

                columns = (int[])_columns.Clone();
                columns[column]++;
            }

            return new RecoveryState(_baseAmounts, _growth, _maxPercent, _freezeLimit, columns, 0, 0, _frozen, closedUtc);
        }

        if (_nonWins + 1 >= RecoverySizer.Positions)
        {
            // The eighth non-win in a row: all columns back to 0, the freeze lifted, position 1 next.
            return new RecoveryState(_baseAmounts, _growth, _maxPercent, _freezeLimit, new int[RecoverySizer.Positions], 0, 0, false, closedUtc);
        }

        return new RecoveryState(_baseAmounts, _growth, _maxPercent, _freezeLimit, _columns, _placedSince, _nonWins + 1, _frozen, closedUtc);
    }

    internal static RecoveryState Start(double[] baseAmounts, double growth, double maxPercent, double freezeLimit)
    {
        return new RecoveryState(baseAmounts, growth, maxPercent, freezeLimit, new int[RecoverySizer.Positions], 0, 0, false, DateTime.MinValue);
    }

    private void RequireOrder(DateTime timeUtc)
    {
        if (timeUtc < _last)
        {
            throw new InvalidOperationException("Recovery sizing (RSK-016): events must come in time order.");
        }
    }
}

/// <summary>
/// The Recovery sizing of one new entry (RSK-016), with the values it came from, so that tests and
/// a journal can see them.
/// </summary>
/// <param name="RiskMoney">R in the account currency: the raw amount, or the cap when the raw amount is above it.</param>
/// <param name="RawAmount">Bp x LadderGrowth ^ step.</param>
/// <param name="CapAmount">Balance x RecoveryMaxRiskPercent / 100.</param>
/// <param name="Position">The position p, 1 to 8.</param>
/// <param name="Step">The step of the column of p.</param>
/// <param name="Capped">True when the raw amount is above the cap (1e-9 tolerance).</param>
/// <param name="StepsFrozen">True when wins raise no step after this placement: frozen before, or the raw amount is above InitialCapital x RecoveryMaxRiskPercent / 100.</param>
/// <param name="Columns">The columns C to J at the placement.</param>
public sealed record RecoveryDecision(double RiskMoney, double RawAmount, double CapAmount, int Position, int Step, bool Capped, bool StepsFrozen, IReadOnlyList<int> Columns)
{
    /// <summary>R in the account currency (RSK-016).</summary>
    public double RiskMoney { get; init; } = RiskMoney;

    /// <summary>Bp x LadderGrowth ^ step (RSK-016).</summary>
    public double RawAmount { get; init; } = RawAmount;

    /// <summary>Balance x RecoveryMaxRiskPercent / 100 (RSK-016).</summary>
    public double CapAmount { get; init; } = CapAmount;

    /// <summary>The position p, 1 to 8 (RSK-017).</summary>
    public int Position { get; init; } = Position;

    /// <summary>The step of the column of p (RSK-016).</summary>
    public int Step { get; init; } = Step;

    /// <summary>True when the raw amount is above the cap (RSK-016).</summary>
    public bool Capped { get; init; } = Capped;

    /// <summary>True when wins raise no step after this placement (RSK-016).</summary>
    public bool StepsFrozen { get; init; } = StepsFrozen;

    /// <summary>The columns C to J at the placement (RSK-016).</summary>
    public IReadOnlyList<int> Columns { get; init; } = Columns;
}
