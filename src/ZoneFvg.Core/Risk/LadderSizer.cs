using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// Ladder sizing, the owner's 4-part plan (RSK-013, spec 4.1): the risk percent of the next
/// entry as a pure function of the bot's entries since RiskEpochDate. The only field is the
/// validated parameter set; nothing is remembered between calls.
/// </summary>
/// <remarks>
/// <para>Entries are numbered 1, 2, 3 in the order they were placed. Entry t starts deal t of
/// size LadderDealRiskPercent x LadderGrowth ^ s(t), where s(t) is the number of consecutive
/// winning entries, newest close first, among the entries closed at or before the placement of
/// entry t. A win is a net result above zero; zero (break-even) or less ends the run. Entries
/// that are still pending or open do not count and do not end the run.</para>
/// <para>Deal t is split into four parts with weights w1 = LadderFirstWeight, w2 = w1/2,
/// w3 = (w1+w2)/2, w4 = (w1+w2+w3)/2, traded on entries t, t+1, t+2 and t+3. The risk percent of
/// entry t is w1 x deal t + w2 x deal t-1 + w3 x deal t-2 + w4 x deal t-3 (deals before entry 1
/// do not exist), capped at MaxRiskPercentPerTrade.</para>
/// <para>Order and ties: entries are ordered by placement time, then by key (ordinal); closed
/// entries are ranked newest first by close time, then by higher entry number. The growth power
/// is computed by repeated multiplication, so the result does not depend on the platform's
/// <c>Math.Pow</c>.</para>
/// </remarks>
public sealed class LadderSizer
{
    /// <summary>Number of parts a deal is split into, and so the number of entries a deal is traded on (RSK-013).</summary>
    public const int Parts = 4;

    private readonly RiskParameters _parameters;

    /// <summary>Creates a ladder sizer. The sizing values are validated here (RSK-013).</summary>
    /// <param name="parameters">The risk parameters; only RiskPercent and the ladder values are used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A sizing value is out of range.</exception>
    public LadderSizer(RiskParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.ValidateSizing();
        _parameters = parameters;
    }

    /// <summary>The part weights w1, w2, w3, w4 of a deal (RSK-013).</summary>
    public IReadOnlyList<double> PartWeights => Weights(_parameters.LadderFirstWeight);

    /// <summary>
    /// Size of a deal in percent of balance (RSK-013): LadderDealRiskPercent x LadderGrowth ^
    /// <paramref name="consecutiveWins"/>.
    /// </summary>
    /// <param name="consecutiveWins">The number of consecutive wins s, zero or more.</param>
    /// <returns>The deal size in percent; may be infinite for a very long run of wins.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="consecutiveWins"/> is negative.</exception>
    public double DealPercent(int consecutiveWins)
    {
        if (consecutiveWins < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(consecutiveWins), "The number of consecutive wins must not be negative.");
        }

        double deal = _parameters.LadderDealRiskPercent;
        for (int i = 0; i < consecutiveWins && double.IsFinite(deal); i++)
        {
            deal *= _parameters.LadderGrowth;
        }

        return deal;
    }

    /// <summary>
    /// The four parts of a deal in percent of balance, w1 to w4 times
    /// <see cref="DealPercent"/> (RSK-013).
    /// </summary>
    /// <param name="consecutiveWins">The number of consecutive wins s of the deal.</param>
    /// <returns>Part 1 to part 4.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="consecutiveWins"/> is negative.</exception>
    public IReadOnlyList<double> DealParts(int consecutiveWins)
    {
        double deal = DealPercent(consecutiveWins);
        return Weights(_parameters.LadderFirstWeight).Select(weight => deal * weight).ToArray();
    }

    /// <summary>
    /// The risk percent of an entry from the deals that fall on it (RSK-013): the deal the entry
    /// starts first (weight w1), then the deals of the one, two and three entries before it
    /// (weights w2, w3, w4). Capped at MaxRiskPercentPerTrade.
    /// </summary>
    /// <param name="dealsNewestFirst">One to four deal sizes in percent, the entry's own deal first.</param>
    /// <returns>The capped risk percent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dealsNewestFirst"/> is null.</exception>
    /// <exception cref="ArgumentException">There are no deals or more than four, or a deal is not a positive number.</exception>
    public double RiskPercentFromDeals(IReadOnlyList<double> dealsNewestFirst)
    {
        ArgumentNullException.ThrowIfNull(dealsNewestFirst);
        if (dealsNewestFirst.Count < 1 || dealsNewestFirst.Count > Parts)
        {
            throw new ArgumentException("An entry carries one to four deals.", nameof(dealsNewestFirst));
        }

        double[] weights = Weights(_parameters.LadderFirstWeight);
        double sum = 0.0;
        for (int k = 0; k < dealsNewestFirst.Count; k++)
        {
            double deal = dealsNewestFirst[k];
            if (double.IsNaN(deal) || deal <= 0.0)
            {
                throw new ArgumentException("Deal " + Text(k + 1) + " must be a positive number.", nameof(dealsNewestFirst));
            }

            sum += deal * weights[k];
        }

        return Math.Min(sum, _parameters.MaxRiskPercentPerTrade);
    }

    /// <summary>
    /// The risk percent of the next entry (RSK-013, spec 4.1). With LadderEnabled false it is the
    /// fixed RiskPercent (RSK-001). The entries are checked in either case.
    /// </summary>
    /// <param name="entries">Every bot entry since RiskEpochDate that filled or is pending, in any order.</param>
    /// <returns>The risk percent, greater than zero and at most MaxRiskPercentPerTrade when the ladder is on.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <exception cref="ArgumentException">An entry is null or invalid, or two entries share a key.</exception>
    public double NextRiskPercent(IReadOnlyList<LadderEntry> entries)
    {
        LadderEntry[] ordered = Ordered(entries);
        if (!_parameters.LadderEnabled)
        {
            return _parameters.RiskPercent;
        }

        var deals = new List<double>(Parts) { DealPercent(Wins(ordered, null)) };
        for (int k = 1; k < Parts && k <= ordered.Length; k++)
        {
            deals.Add(DealPercent(Wins(ordered, ordered[ordered.Length - k].PlacedUtc)));
        }

        return RiskPercentFromDeals(deals);
    }

    /// <summary>
    /// The number of consecutive winning entries s (RSK-013): among the entries closed at or
    /// before <paramref name="placedUtc"/> (all closed entries when null), newest close first,
    /// how many in a row have a net result above zero. Break-even ends the run; pending and open
    /// entries are skipped.
    /// </summary>
    /// <param name="entries">The entries, in any order.</param>
    /// <param name="placedUtc">Placement time of the entry whose deal is sized, or null for the next entry.</param>
    /// <returns>The number of consecutive wins.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <exception cref="ArgumentException">An entry is null or invalid, or two entries share a key.</exception>
    public static int ConsecutiveWins(IReadOnlyList<LadderEntry> entries, DateTime? placedUtc)
    {
        return Wins(Ordered(entries), placedUtc);
    }

    /// <summary>
    /// Builds the ladder entries from broker data (RSK-013). Every bot position, pending order
    /// and closed trade with the same label is one entry: a partial fill and its pending
    /// remainder, and the partial closes of a position, belong together. The placement time is
    /// the earliest of the order's creation time, the position's open time and the closed
    /// trades' entry times; the broker reports no order time for a filled entry, so for those it
    /// is the fill time. An entry is closed when nothing of it is open or pending; its result is
    /// the sum of its closed trades and its close time the latest of them. Entries placed before
    /// <paramref name="epochStartUtc"/> are left out. Cancelled or expired orders are not in the
    /// broker data and so are never counted.
    /// </summary>
    /// <param name="positions">All open positions; non-bot labels are ignored.</param>
    /// <param name="pendingOrders">All pending orders; non-bot labels are ignored.</param>
    /// <param name="history">Closed trades at least since <paramref name="epochStartUtc"/>; non-bot labels are ignored.</param>
    /// <param name="epochStartUtc">00:00 CE(S)T of RiskEpochDate, kind UTC.</param>
    /// <returns>The entries, ordered by placement time and then label.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">A record is null, a time is not UTC or a result is not finite.</exception>
    public static IReadOnlyList<LadderEntry> EntriesFromBroker(
        IEnumerable<PositionInfo> positions,
        IEnumerable<PendingOrderInfo> pendingOrders,
        IEnumerable<ClosedTradeInfo> history,
        DateTime epochStartUtc)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(pendingOrders);
        ArgumentNullException.ThrowIfNull(history);
        RequireUtc(epochStartUtc, "epoch start");
        var parts = new List<BrokerPart>();
        foreach (PositionInfo position in positions)
        {
            Require(position != null, "a position is null");
            AddPart(parts, new BrokerPart(position!.Label, position.OpenTimeUtc, null), "position open time");
        }

        foreach (PendingOrderInfo order in pendingOrders)
        {
            Require(order != null, "a pending order is null");
            AddPart(parts, new BrokerPart(order!.Label, order.CreatedTimeUtc, null), "order creation time");
        }

        foreach (ClosedTradeInfo trade in history)
        {
            Require(trade != null, "a closed trade is null");
            if (!RiskEngine.IsBotLabel(trade!.Label))
            {
                continue;
            }

            RequireUtc(trade.CloseTimeUtc, "close time of a closed trade");
            Require(double.IsFinite(trade.NetProfit), "the net result of a closed trade is not a finite number");
            AddPart(parts, new BrokerPart(trade.Label, trade.EntryTimeUtc, trade), "entry time of a closed trade");
        }

        LadderEntry[] entries = parts
            .GroupBy(part => part.Label, StringComparer.Ordinal)
            .Select(group => ToEntry(group.Key, group.ToArray()))
            .Where(entry => entry.PlacedUtc >= epochStartUtc)
            .ToArray();
        return Ordered(entries);
    }

    private static void AddPart(List<BrokerPart> parts, BrokerPart part, string timeName)
    {
        if (RiskEngine.IsBotLabel(part.Label))
        {
            RequireUtc(part.TimeUtc, timeName);
            parts.Add(part);
        }
    }

    private static LadderEntry ToEntry(string label, BrokerPart[] parts)
    {
        DateTime placed = parts.Min(part => part.TimeUtc);
        if (parts.Any(part => part.Closed == null))
        {
            // A position or a pending order of the entry is still there: the entry is not closed.
            return new LadderEntry(label, placed, null, 0.0);
        }

        // Fixed summation order, so the broker's order of the records cannot change the last bits.
        ClosedTradeInfo[] closed = parts
            .Select(part => part.Closed!)
            .OrderBy(trade => trade.CloseTimeUtc)
            .ThenBy(trade => trade.PositionId)
            .ThenBy(trade => trade.NetProfit)
            .ToArray();
        double net = 0.0;
        foreach (ClosedTradeInfo trade in closed)
        {
            net += trade.NetProfit;
        }

        return new LadderEntry(label, placed, closed[closed.Length - 1].CloseTimeUtc, net);
    }

    private static int Wins(LadderEntry[] ordered, DateTime? placedUtc)
    {
        var closed = new List<int>();
        for (int i = 0; i < ordered.Length; i++)
        {
            DateTime? close = ordered[i].ClosedUtc;
            if (close.HasValue && (!placedUtc.HasValue || close.Value <= placedUtc.Value))
            {
                closed.Add(i);
            }
        }

        // Newest close first; at the same close time the later entry first. Indices are unique, so the order is total.
        closed.Sort((a, b) =>
        {
            int result = ordered[b].ClosedUtc!.Value.CompareTo(ordered[a].ClosedUtc!.Value);
            return result != 0 ? result : b.CompareTo(a);
        });

        int wins = 0;
        foreach (int index in closed)
        {
            if (!PriceMath.GreaterThan(ordered[index].NetResult, 0.0, PriceMath.DefaultTolerance))
            {
                break;
            }

            wins++;
        }

        return wins;
    }

    private static LadderEntry[] Ordered(IReadOnlyList<LadderEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (LadderEntry entry in entries)
        {
            Require(entry != null, "an entry is null");
            Require(!string.IsNullOrEmpty(entry!.Key), "an entry has no key");
            Require(keys.Add(entry.Key), "two entries have the key " + entry.Key);
            RequireUtc(entry.PlacedUtc, "placement time of " + entry.Key);
            if (entry.ClosedUtc.HasValue)
            {
                RequireUtc(entry.ClosedUtc.Value, "close time of " + entry.Key);
                Require(entry.ClosedUtc.Value >= entry.PlacedUtc, "entry " + entry.Key + " closed before it was placed");
            }

            Require(double.IsFinite(entry.NetResult), "the result of " + entry.Key + " is not a finite number");
        }

        return entries
            .OrderBy(entry => entry.PlacedUtc)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static double[] Weights(double firstWeight)
    {
        double w1 = firstWeight;
        double w2 = w1 / 2.0;
        double w3 = (w1 + w2) / 2.0;
        double w4 = (w1 + w2 + w3) / 2.0;
        return new[] { w1, w2, w3, w4 };
    }

    private static void RequireUtc(DateTime value, string name)
    {
        Require(value.Kind == DateTimeKind.Utc, "the " + name + " is not UTC");
    }

    private static void Require(bool condition, string problem)
    {
        if (!condition)
        {
            throw new ArgumentException("Ladder sizing (RSK-013): " + problem + ".");
        }
    }

    private static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>One bot position, pending order or closed trade, by label (RSK-013). Lives for one call only.</summary>
    private sealed record BrokerPart(string Label, DateTime TimeUtc, ClosedTradeInfo? Closed);
}
