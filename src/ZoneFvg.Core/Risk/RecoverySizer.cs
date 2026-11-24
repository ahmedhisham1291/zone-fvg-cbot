using System;
using System.Collections.Generic;
using System.Linq;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// The Recovery sizing plan (RSK-016, RSK-017, spec 4.1.1, owner decisions Q, V and Y of
/// 2026-11-24): the risk money of the next trade of one symbol as a pure function of that
/// symbol's bot trades since RiskEpochDate, the placement time, the balance and RewardRisk. The
/// only field is the validated parameter set; nothing is remembered between calls.
/// </summary>
/// <remarks>
/// <para>R = min((L + RR x F) / RR, B x RecoveryMaxRiskPercent / 100), F being
/// RecoveryFirstRiskUsd, RR the instance's RewardRisk, B the balance and L the losing run. A raw
/// amount within 1e-9 of the cap, or above it, is the cap.</para>
/// <para>The losing run L: the closed trades with a close time at or before the placement time T
/// are taken in the order close time, then placement time, then key (ordinal); a win is a net
/// result above zero (1e-9 tolerance) and sets L to 0; every other closed trade adds the absolute
/// value of its net result. The sum runs in that fixed order, so the order of the broker's records
/// cannot change the last bits. Open and pending trades do not change L.</para>
/// <para>The trades are the ladder's entries (<see cref="LadderSizer.EntriesFromBroker"/>,
/// <see cref="LadderEntry"/>) built from the broker records of the instance's symbol only
/// (<see cref="EntriesFromBroker"/>); the ladder itself stays account-wide.</para>
/// </remarks>
public sealed class RecoverySizer
{
    private const string Prefix = "Recovery sizing (RSK-017): ";

    private readonly RiskParameters _parameters;

    /// <summary>Creates a Recovery sizer. The sizing values are validated here (RSK-018).</summary>
    /// <param name="parameters">The risk parameters; RecoveryFirstRiskUsd and RecoveryMaxRiskPercent are used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A sizing value is out of range.</exception>
    public RecoverySizer(RiskParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.ValidateSizing();
        _parameters = parameters;
    }

    /// <summary>
    /// Builds the trades of one symbol from broker data (RSK-017): the bot positions, pending
    /// orders and closed trades whose symbol equals <paramref name="symbol"/> (ordinal) are given
    /// to <see cref="LadderSizer.EntriesFromBroker"/>, which groups them by label, leaves out trades
    /// placed before <paramref name="epochStartUtc"/> and checks every record. Records of other
    /// symbols are never read further, so they cannot change the result.
    /// </summary>
    /// <param name="symbol">The instance's symbol.</param>
    /// <param name="positions">All open positions.</param>
    /// <param name="pendingOrders">All pending orders.</param>
    /// <param name="history">Closed trades at least since <paramref name="epochStartUtc"/>.</param>
    /// <param name="epochStartUtc">00:00 CE(S)T of RiskEpochDate, kind UTC.</param>
    /// <returns>The trades of the symbol, ordered by placement time and then label.</returns>
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
    /// The losing run L at the placement time <paramref name="placedUtc"/> (RSK-017): the sum of
    /// the absolute net results of the closed non-wins after the last win, in the order of the
    /// remarks.
    /// </summary>
    /// <param name="entries">The trades of the instance's symbol, in any order.</param>
    /// <param name="placedUtc">Placement time of the new trade, kind UTC: the broker's server time.</param>
    /// <returns>L, zero or more.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <exception cref="ArgumentException">A trade is null or invalid, two share a key, the time is not UTC, or a trade was placed or closed after it.</exception>
    public static double LosingRun(IReadOnlyList<LadderEntry> entries, DateTime placedUtc)
    {
        double run = 0.0;
        foreach (LadderEntry entry in ClosedInOrder(entries, placedUtc))
        {
            run = IsWin(entry) ? 0.0 : run + Math.Abs(entry.NetResult);
        }

        return run;
    }

    /// <summary>
    /// F, the risk money of the first trade of a losing run (RSK-016): RecoveryFirstRiskUsd. It is
    /// a function of the symbol's trades so that a rule that lets F depend on them (for example on
    /// the wins so far) changes only this method; today the trades are checked but do not change F.
    /// </summary>
    /// <param name="entries">The trades of the instance's symbol, in any order.</param>
    /// <param name="placedUtc">Placement time of the new trade, kind UTC.</param>
    /// <returns>F, greater than zero.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <exception cref="ArgumentException">The trades are invalid (see <see cref="LosingRun"/>).</exception>
    public double FirstAmount(IReadOnlyList<LadderEntry> entries, DateTime placedUtc)
    {
        ClosedInOrder(entries, placedUtc);
        return _parameters.RecoveryFirstRiskUsd;
    }

    /// <summary>
    /// The risk money of the new trade of the instance's symbol (RSK-016): R = min((L + RR x F) /
    /// RR, B x RecoveryMaxRiskPercent / 100), with L from <see cref="LosingRun"/> and F from
    /// <see cref="FirstAmount"/>.
    /// </summary>
    /// <param name="entries">The trades of the instance's symbol (<see cref="EntriesFromBroker"/>), in any order.</param>
    /// <param name="placedUtc">Placement time of the new trade, kind UTC.</param>
    /// <param name="balance">The account balance from the broker port at the placement.</param>
    /// <param name="rewardRisk">The instance's RewardRisk, the divisor RR (owner decision Y).</param>
    /// <returns>R with L, the raw amount, the cap and whether the cap applied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <exception cref="ArgumentException">The trades are invalid (see <see cref="LosingRun"/>), or the balance or RewardRisk is not a finite number greater than zero.</exception>
    public RecoveryAmount Next(IReadOnlyList<LadderEntry> entries, DateTime placedUtc, double balance, double rewardRisk)
    {
        double run = LosingRun(entries, placedUtc);
        double first = FirstAmount(entries, placedUtc);
        Require(double.IsFinite(balance) && balance > 0.0, "the balance is not a finite number greater than zero");
        Require(double.IsFinite(rewardRisk) && rewardRisk > 0.0, "RewardRisk is not a finite number greater than zero");
        double raw = (run + (rewardRisk * first)) / rewardRisk;
        double cap = balance * _parameters.RecoveryMaxRiskPercent / 100.0;
        bool capped = !PriceMath.LessThan(raw, cap, PriceMath.DefaultTolerance);
        return new RecoveryAmount(capped ? cap : raw, run, raw, cap, capped);
    }

    /// <summary>
    /// The checked closed trades with a close time at or before <paramref name="placedUtc"/>, in
    /// the order close time, then placement time, then key (ordinal) (RSK-017). Fails closed on
    /// invalid trades and on any trade placed or closed after <paramref name="placedUtc"/>.
    /// </summary>
    private static LadderEntry[] ClosedInOrder(IReadOnlyList<LadderEntry> entries, DateTime placedUtc)
    {
        LadderEntry[] checkedEntries = Relabel(() => LadderSizer.Ordered(entries));
        Require(placedUtc.Kind == DateTimeKind.Utc, "the placement time of the new trade is not UTC");
        foreach (LadderEntry entry in checkedEntries)
        {
            // Broker data cannot hold an event after the server time; such data is not trusted (fail closed).
            Require(entry.PlacedUtc <= placedUtc, "trade " + entry.Key + " was placed after the new trade");
            Require(!entry.ClosedUtc.HasValue || entry.ClosedUtc.Value <= placedUtc, "trade " + entry.Key + " closed after the new trade was placed");
        }

        return checkedEntries
            .Where(entry => entry.ClosedUtc.HasValue)
            .OrderBy(entry => entry.ClosedUtc!.Value)
            .ThenBy(entry => entry.PlacedUtc)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsWin(LadderEntry entry)
    {
        return PriceMath.GreaterThan(entry.NetResult, 0.0, PriceMath.DefaultTolerance);
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

/// <summary>
/// The amount of the Recovery plan for one new trade (RSK-016), with the values it came from, so
/// that tests and a journal can see them.
/// </summary>
/// <param name="RiskMoney">R, the risk money of the trade in the account currency: <paramref name="CapAmount"/> when <paramref name="Capped"/>, else <paramref name="RawAmount"/>.</param>
/// <param name="LosingRun">L, the money lost in the current losing run of the symbol (RSK-017).</param>
/// <param name="RawAmount">(L + RR x F) / RR, before the cap.</param>
/// <param name="CapAmount">Balance x RecoveryMaxRiskPercent / 100.</param>
/// <param name="Capped">True when the raw amount is at or above the cap (1e-9 tolerance).</param>
public sealed record RecoveryAmount(double RiskMoney, double LosingRun, double RawAmount, double CapAmount, bool Capped)
{
    /// <summary>R, the risk money of the trade in the account currency (RSK-016).</summary>
    public double RiskMoney { get; init; } = RiskMoney;

    /// <summary>L, the money lost in the current losing run of the symbol (RSK-017).</summary>
    public double LosingRun { get; init; } = LosingRun;

    /// <summary>(L + RR x F) / RR, before the cap (RSK-016).</summary>
    public double RawAmount { get; init; } = RawAmount;

    /// <summary>Balance x RecoveryMaxRiskPercent / 100 (RSK-016).</summary>
    public double CapAmount { get; init; } = CapAmount;

    /// <summary>True when the raw amount is at or above the cap, 1e-9 tolerance (RSK-016).</summary>
    public bool Capped { get; init; } = Capped;
}
