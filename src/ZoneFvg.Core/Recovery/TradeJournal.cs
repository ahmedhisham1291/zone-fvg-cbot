using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Recovery;

/// <summary>What is known of a position when it opened, kept so its closed trade can be journaled in R (REC-008, OPS-002).</summary>
/// <param name="Side">Direction.</param>
/// <param name="EntryPrice">Fill price.</param>
/// <param name="Comment">Order comment holding the plan (EXE-008).</param>
public sealed record OpenedTradePlan(Side Side, double EntryPrice, string Comment);

/// <summary>Receives each bot trade event exactly once (REC-008): the bot journals and alerts it (WP09).</summary>
public interface ITradeEvents
{
    /// <summary>A bot position of the symbol is open and was not reported before.</summary>
    /// <param name="position">The position.</param>
    /// <param name="atUtc">Server time, kind UTC.</param>
    void Opened(PositionInfo position, DateTime atUtc);

    /// <summary>A bot position of the symbol has closed and was not reported before.</summary>
    /// <param name="trade">The closed trade; partial closes of the position are summed into one record.</param>
    /// <param name="plan">What was known when the position opened, or null when it opened and closed while the bot was away.</param>
    /// <param name="atUtc">Server time, kind UTC.</param>
    void Closed(ClosedTradeInfo trade, OpenedTradePlan? plan, DateTime atUtc);
}

/// <summary>Result of one <see cref="TradeJournal.Record"/> call (REC-008).</summary>
/// <param name="Written">Trade events reported by this call.</param>
/// <param name="Known">Trade events (openings and closings) known to be reported, within the history window.</param>
/// <param name="Ignored">Stored entries that could not be read and were ignored.</param>
public sealed record TradeJournalResult(int Written, int Known, IReadOnlyList<string> Ignored);

/// <summary>
/// Reports the bot trades of the symbol from the broker, exactly once per trade event, keyed by
/// position id (REC-008): one opening for each bot position that is open, and one closing for
/// each bot position that has closed. Runs in recovery (spec 8.3 step 7) and on every trade event,
/// so trades that opened or closed while the bot was away and trades closed while it runs go
/// through the same code.
/// </summary>
/// <remarks>
/// <para>The broker is the source of the trades. Which position ids were already reported is kept
/// under <see cref="StoreKey"/> in the key-value store as a comma-separated list such as
/// <c>c1001,o1001,o1004</c>; entries of positions that are neither open nor in the history window
/// are dropped. For an open position its side, fill price and comment are kept under
/// <see cref="PlanKeyPrefix"/> plus its id until it closes, so the closing can be journaled in R
/// after a restart (OPS-002). A trade that opened and closed while the bot was away has no
/// plan.</para>
/// <para>Partial closes of one position are summed into one closing, reported when the position
/// is no longer open.</para>
/// </remarks>
public sealed class TradeJournal
{
    /// <summary>Key-value store key of the reported position ids (REC-008).</summary>
    public const string StoreKey = "journal.trades";

    /// <summary>Prefix of the key-value store keys that keep the plan of an open position (OPS-002).</summary>
    public const string PlanKeyPrefix = "journal.plan.";

    private const char PlanSeparator = '\n';

    private readonly string _symbol;
    private readonly RiskParameters _risk;
    private readonly IBroker _broker;
    private readonly IKeyValueStore _store;
    private readonly ITradeEvents _events;

    /// <summary>Creates the trade journal of one symbol (REC-008).</summary>
    /// <param name="symbol">Symbol of this instance.</param>
    /// <param name="risk">Risk parameters; RiskEpochDate decides how far back history is read.</param>
    /// <param name="broker">Broker port.</param>
    /// <param name="store">Key-value store for the reported ids and plans.</param>
    /// <param name="events">Receiver of the trade events.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The symbol is empty.</exception>
    public TradeJournal(string symbol, RiskParameters risk, IBroker broker, IKeyValueStore store, ITradeEvents events)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            throw new ArgumentException("The symbol must not be empty.", nameof(symbol));
        }

        _symbol = symbol;
        _risk = risk ?? throw new ArgumentNullException(nameof(risk));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>
    /// Reports every bot trade event of the symbol not yet reported (REC-008): positions open now
    /// and positions closed since the earlier of 00:00 CE(S)T today and RiskEpochDate.
    /// </summary>
    /// <param name="nowUtc">Server time, kind UTC.</param>
    /// <returns>The events reported, the events known and the stored entries ignored.</returns>
    public TradeJournalResult Record(DateTime nowUtc)
    {
        DateTime since = AccountGuard.HistoryStartUtc(_risk, nowUtc);
        List<PositionInfo> open = _broker.GetPositions()
            .Where(p => IsBotOn(p.Label, p.Symbol))
            .OrderBy(p => p.Id)
            .ToList();
        var openIds = new SortedSet<long>(open.Select(p => p.Id));
        List<IGrouping<long, ClosedTradeInfo>> closed = _broker.GetHistory(since)
            .Where(t => IsBotOn(t.Label, t.Symbol) && t.CloseTimeUtc >= since)
            .GroupBy(t => t.PositionId)
            .OrderBy(g => g.Key)
            .ToList();

        string? stored = _store.Get(StoreKey);
        var ignored = new List<string>();
        SortedSet<string> keys = Load(stored, ignored);
        int written = 0;
        foreach (PositionInfo position in open)
        {
            if (keys.Add("o" + Text(position.Id)))
            {
                _store.Set(PlanKeyPrefix + Text(position.Id), WritePlan(position));
                _events.Opened(position, nowUtc);
                written++;
            }
        }

        foreach (IGrouping<long, ClosedTradeInfo> trade in closed.Where(g => !openIds.Contains(g.Key)))
        {
            if (keys.Add("c" + Text(trade.Key)))
            {
                string planKey = PlanKeyPrefix + Text(trade.Key);
                OpenedTradePlan? plan = ReadPlan(_store.Get(planKey));
                _events.Closed(Sum(trade.OrderBy(t => t.CloseTimeUtc).ToList()), plan, nowUtc);
                if (plan != null)
                {
                    _store.Set(planKey, string.Empty);
                }

                written++;
            }

            keys.Add("o" + Text(trade.Key));
        }

        var live = new SortedSet<long>(openIds);
        live.UnionWith(closed.Select(g => g.Key));
        keys.RemoveWhere(key => !live.Contains(IdOf(key)));
        string value = string.Join(",", keys);
        if (!string.Equals(value, stored ?? string.Empty, StringComparison.Ordinal))
        {
            _store.Set(StoreKey, value);
        }

        return new TradeJournalResult(written, keys.Count, ignored.AsReadOnly());
    }

    private static ClosedTradeInfo Sum(IReadOnlyList<ClosedTradeInfo> parts)
    {
        ClosedTradeInfo first = parts[0];
        ClosedTradeInfo last = parts[parts.Count - 1];
        return last with
        {
            EntryTimeUtc = first.EntryTimeUtc,
            NetProfit = parts.Sum(t => t.NetProfit),
            Commission = parts.Sum(t => t.Commission),
            Swap = parts.Sum(t => t.Swap),
            VolumeUnits = SumVolume(parts),
            ClosingPrice = AverageClosingPrice(parts),
        };
    }

    // Journal data only (SIM-001, OPS-002): the closed volume, or null when a part does not report it.
    private static double? SumVolume(IReadOnlyList<ClosedTradeInfo> parts)
    {
        double total = 0.0;
        foreach (ClosedTradeInfo part in parts)
        {
            if (!part.VolumeUnits.HasValue)
            {
                return null;
            }

            total += part.VolumeUnits.Value;
        }

        return total;
    }

    // Journal data only (SIM-001, OPS-002): the volume-weighted closing price of the parts, or null
    // when a part does not report its price or volume or the total volume is not greater than zero.
    private static double? AverageClosingPrice(IReadOnlyList<ClosedTradeInfo> parts)
    {
        if (parts.Count == 1)
        {
            // One closing deal: its own price, without rounding through the average.
            return parts[0].ClosingPrice;
        }

        double? volume = SumVolume(parts);
        if (!volume.HasValue || !(volume.Value > 0.0))
        {
            return null;
        }

        double weighted = 0.0;
        foreach (ClosedTradeInfo part in parts)
        {
            if (!part.ClosingPrice.HasValue)
            {
                return null;
            }

            weighted += part.ClosingPrice.Value * part.VolumeUnits!.Value;
        }

        return weighted / volume.Value;
    }

    private static string WritePlan(PositionInfo position)
    {
        return position.Side + PlanSeparator.ToString() + position.EntryPrice.ToString("R", CultureInfo.InvariantCulture) + PlanSeparator + position.Comment;
    }

    private static OpenedTradePlan? ReadPlan(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        string[] parts = text.Split(PlanSeparator, 3);
        if (parts.Length != 3
            || !Enum.TryParse(parts[0], false, out Side side)
            || !Enum.IsDefined(typeof(Side), side)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double entry)
            || !double.IsFinite(entry))
        {
            return null;
        }

        return new OpenedTradePlan(side, entry, parts[2]);
    }

    private static SortedSet<string> Load(string? stored, List<string> ignored)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(stored))
        {
            return keys;
        }

        foreach (string token in stored.Split(','))
        {
            if (IsKey(token))
            {
                keys.Add(token);
            }
            else
            {
                ignored.Add(token);
            }
        }

        return keys;
    }

    private static bool IsKey(string token)
    {
        return token.Length >= 2
            && (token[0] == 'o' || token[0] == 'c')
            && long.TryParse(token.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out long _);
    }

    private static long IdOf(string key)
    {
        return long.Parse(key.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private bool IsBotOn(string label, string symbol)
    {
        return RiskEngine.IsBotLabel(label) && string.Equals(symbol, _symbol, StringComparison.Ordinal);
    }
}
