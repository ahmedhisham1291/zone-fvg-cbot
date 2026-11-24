using System;
using System.Collections.Generic;
using System.Linq;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// The Recovery sizing plan with step columns (RSK-016, RSK-017, spec 4.1.1, owner decisions V, Y
/// and Z of 2026-11-24): the risk money of the next entry of one symbol as a pure function of that
/// symbol's bot entries since RiskEpochDate, the placement time, the balance and RewardRisk. The only field is the validated parameter set; the columns are rebuilt
/// from the full entry list at every call.
/// </summary>
/// <remarks>
/// <para>Base amounts: m1 = 1, mk = (m1 + ... + m(k-1) + RR) / RR for k = 2 to 8, M = m1 + ... + m8,
/// F0 = InitialCapital x RecoveryRunLossPercent / 100 / M and Bp = mp x F0, each sum in that fixed
/// order. The state rule is <see cref="RecoveryState"/>.</para>
/// <para>The entries are the ladder's entries (<see cref="LadderSizer.EntriesFromBroker"/>,
/// <see cref="LadderEntry"/>) of the instance's symbol only (<see cref="EntriesFromBroker"/>), and
/// they are replayed in the ladder's event order; the ladder itself stays account-wide.</para>
/// <para>The freeze of a past entry is judged against the fixed reference InitialCapital x
/// RecoveryMaxRiskPercent / 100 (orchestrating session, 2026-11-24), so the replay needs no past
/// balance and no other symbol's trade; the current balance only caps the new entry's R.</para>
/// </remarks>
public sealed class RecoverySizer
{
    /// <summary>Number of positions of a losing run and of step columns C to J (RSK-016).</summary>
    public const int Positions = 8;

    private const string Prefix = "Recovery sizing (RSK-017): ";

    private readonly RiskParameters _parameters;

    /// <summary>Creates a Recovery sizer. The sizing values are validated here (RSK-018).</summary>
    /// <param name="parameters">The risk parameters; InitialCapital, RecoveryRunLossPercent, RecoveryMaxRiskPercent and LadderGrowth are used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A sizing value is out of range.</exception>
    public RecoverySizer(RiskParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.ValidateSizing();
        _parameters = parameters;
    }

    /// <summary>
    /// The eight base multipliers m1 to m8 for the divisor <paramref name="rewardRisk"/> (RSK-016):
    /// m1 = 1, mk = (m1 + ... + m(k-1) + RR) / RR.
    /// </summary>
    /// <param name="rewardRisk">The instance's RewardRisk.</param>
    /// <returns>m1 to m8.</returns>
    /// <exception cref="ArgumentException"><paramref name="rewardRisk"/> is not a finite number greater than zero.</exception>
    public static IReadOnlyList<double> Multipliers(double rewardRisk)
    {
        Require(double.IsFinite(rewardRisk) && rewardRisk > 0.0, "RewardRisk is not a finite number greater than zero");
        var m = new double[Positions];
        double sum = 0.0;
        for (int k = 0; k < Positions; k++)
        {
            m[k] = k == 0 ? 1.0 : (sum + rewardRisk) / rewardRisk;
            sum += m[k];
        }

        return m;
    }

    /// <summary>
    /// The eight base amounts B1 to B8 in account currency (RSK-016): mp x InitialCapital x
    /// RecoveryRunLossPercent / 100 / M. They sum to RecoveryRunLossPercent of InitialCapital.
    /// </summary>
    /// <param name="rewardRisk">The instance's RewardRisk.</param>
    /// <returns>B1 to B8.</returns>
    /// <exception cref="ArgumentException">RewardRisk or InitialCapital is not a finite number greater than zero.</exception>
    public IReadOnlyList<double> BaseAmounts(double rewardRisk)
    {
        IReadOnlyList<double> m = Multipliers(rewardRisk);
        double capital = _parameters.InitialCapital;
        Require(double.IsFinite(capital) && capital > 0.0, "InitialCapital is not a finite number greater than zero");
        double total = 0.0;
        foreach (double multiplier in m)
        {
            total += multiplier;
        }

        double first = capital * _parameters.RecoveryRunLossPercent / 100.0 / total;
        return m.Select(multiplier => multiplier * first).ToArray();
    }

    /// <summary>The state of one symbol before its first entry: all columns 0, not frozen (RSK-016), also used by the simulator (SIM-006).</summary>
    /// <param name="rewardRisk">The instance's RewardRisk.</param>
    /// <returns>The start state.</returns>
    /// <exception cref="ArgumentException">RewardRisk or InitialCapital is not a finite number greater than zero.</exception>
    public RecoveryState StartState(double rewardRisk)
    {
        double freezeLimit = _parameters.InitialCapital * _parameters.RecoveryMaxRiskPercent / 100.0;
        return RecoveryState.Start(BaseAmounts(rewardRisk).ToArray(), _parameters.LadderGrowth, _parameters.RecoveryMaxRiskPercent, freezeLimit);
    }

    /// <summary>
    /// Builds the entries of one symbol from broker data (RSK-017): the bot positions, pending
    /// orders and closed trades whose symbol equals <paramref name="symbol"/> (ordinal) are given
    /// to <see cref="LadderSizer.EntriesFromBroker"/>, which groups them by label, leaves out entries
    /// placed before <paramref name="epochStartUtc"/> and checks every record. Records of other
    /// symbols are never read further, so they cannot change a position or a column.
    /// </summary>
    /// <param name="symbol">The instance's symbol.</param>
    /// <param name="positions">All open positions.</param>
    /// <param name="pendingOrders">All pending orders.</param>
    /// <param name="history">Closed trades at least since <paramref name="epochStartUtc"/>.</param>
    /// <param name="epochStartUtc">00:00 CE(S)T of RiskEpochDate, kind UTC.</param>
    /// <returns>The entries of the symbol, ordered by placement time and then label.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The symbol is empty, a record is null, or a record of the symbol has a time that is not UTC or a result that is not finite.</exception>
    public static IReadOnlyList<LadderEntry> EntriesFromBroker(
        string symbol,
        IEnumerable<PositionInfo> positions,
        IEnumerable<PendingOrderInfo> pendingOrders,
        IEnumerable<ClosedTradeInfo> history,
        DateTime epochStartUtc)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(pendingOrders);
        ArgumentNullException.ThrowIfNull(history);
        Require(symbol.Length > 0, "the symbol is empty");

        // A null record is kept, so that the ladder's reader refuses it (fail closed).
        return Relabel(() => LadderSizer.EntriesFromBroker(
            positions.Where(p => p == null || Same(p.Symbol, symbol)).ToArray(),
            pendingOrders.Where(o => o == null || Same(o.Symbol, symbol)).ToArray(),
            history.Where(t => t == null || Same(t.Symbol, symbol)).ToArray(),
            epochStartUtc));
    }

    /// <summary>
    /// The Recovery sizing of the next entry of the symbol, placed at <paramref name="placedUtc"/>
    /// (RSK-016, RSK-017): its position, step, raw amount, R, cap, the columns C to J and the
    /// freeze. The state is rebuilt from <paramref name="entries"/> alone, so the same broker data
    /// always gives the same result.
    /// </summary>
    /// <param name="entries">The entries of the instance's symbol (<see cref="EntriesFromBroker"/>), in any order.</param>
    /// <param name="placedUtc">Placement time of the next entry, kind UTC: the broker's server time.</param>
    /// <param name="balance">The account balance from the broker port at the placement.</param>
    /// <param name="rewardRisk">The instance's RewardRisk (owner decision Y).</param>
    /// <returns>The sizing of the next entry.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">An entry is null or invalid, two entries share a key, a time is not UTC, an entry was placed or closed after <paramref name="placedUtc"/>, or the balance, RewardRisk or InitialCapital is not a finite number greater than zero.</exception>
    public RecoveryDecision Next(IReadOnlyList<LadderEntry> entries, DateTime placedUtc, double balance, double rewardRisk)
    {
        LadderEntry[] ordered = Relabel(() => LadderSizer.Ordered(entries));
        Require(placedUtc.Kind == DateTimeKind.Utc, "the placement time of the new entry is not UTC");
        foreach (LadderEntry entry in ordered)
        {
            // Broker data cannot hold an event after the server time; such data is not trusted (fail closed).
            Require(entry.PlacedUtc <= placedUtc, "entry " + entry.Key + " was placed after the new entry");
            Require(!entry.ClosedUtc.HasValue || entry.ClosedUtc.Value <= placedUtc, "entry " + entry.Key + " closed after the new entry was placed");
        }

        Require(double.IsFinite(balance) && balance > 0.0, "the balance is not a finite number greater than zero");
        RecoveryState state = StartState(rewardRisk);
        var positions = new int[ordered.Length];
        foreach (LadderSizer.BookEvent e in LadderSizer.SortedEvents(ordered))
        {
            if (e.IsClose)
            {
                state = state.Close(positions[e.Index], e.TimeUtc, ordered[e.Index].NetResult);
            }
            else
            {
                // The balance only caps R of a past entry, which is not used; the freeze does not depend on it.
                state = state.Place(e.TimeUtc, balance, out RecoveryDecision placed);
                positions[e.Index] = placed.Position;
            }
        }

        return state.Preview(balance);
    }

    private static bool Same(string? a, string b)
    {
        return string.Equals(a, b, StringComparison.Ordinal);
    }

    private static T Relabel<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (ArgumentException exception) when (exception is not ArgumentNullException)
        {
            // The ladder's reader names RSK-013; the refusal here is the Recovery plan's (fail closed, not swallowed).
            throw new ArgumentException(Prefix + exception.Message, exception);
        }
    }

    private static void Require(bool condition, string problem)
    {
        if (!condition)
        {
            throw new ArgumentException(Prefix + problem + ".");
        }
    }
}
