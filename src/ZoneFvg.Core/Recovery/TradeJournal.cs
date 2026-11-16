using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Recovery;

/// <summary>Result of one <see cref="TradeJournal.Record"/> call (REC-008).</summary>
/// <param name="Written">Journal lines written by this call.</param>
/// <param name="Known">Trade events (openings and closings) known to be journaled after this call, within the history window.</param>
public sealed record TradeJournalResult(int Written, int Known);

/// <summary>
/// Adds the bot trades of the symbol to the journal from the broker, exactly once per trade
/// event, keyed by position id (REC-008): one <c>opened</c> line for each bot position that is
/// open, and one <c>closed</c> line for each bot position that has closed. Runs in recovery
/// (spec 8.3 step 7) and on every trade event, so trades that opened or closed while the bot was
/// away and trades closed while it runs are journaled by the same code.
/// </summary>
/// <remarks>
/// <para>The broker is the source of the trades. Which position ids are already journaled is kept
/// under <see cref="StoreKey"/> in the key-value store as a comma-separated list such as
/// <c>c1001,o1001,o1004</c>. Entries for positions that are neither open nor in the history window
/// are dropped, so the list stays small. A trade closed while away gets one <c>closed</c> line
/// that also carries its opening time.</para>
/// <para>Partial closes of one position are summed into one line, written when the position is no
/// longer open. Unreadable entries in the stored list are ignored and reported in the journal.</para>
/// </remarks>
public sealed class TradeJournal
{
    /// <summary>Key-value store key of the journaled position ids (REC-008).</summary>
    public const string StoreKey = "journal.trades";

    /// <summary>Journal category of the trade lines (REC-008).</summary>
    public const string Category = "trade";

    private readonly string _symbol;
    private readonly RiskParameters _risk;
    private readonly IBroker _broker;
    private readonly IKeyValueStore _store;
    private readonly IJournalSink _sink;

    /// <summary>Creates the trade journal of one symbol (REC-008).</summary>
    /// <param name="symbol">Symbol of this instance.</param>
    /// <param name="risk">Risk parameters; RiskEpochDate decides how far back history is read.</param>
    /// <param name="broker">Broker port.</param>
    /// <param name="store">Key-value store for the journaled ids.</param>
    /// <param name="sink">Journal port.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The symbol is empty.</exception>
    public TradeJournal(string symbol, RiskParameters risk, IBroker broker, IKeyValueStore store, IJournalSink sink)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            throw new ArgumentException("The symbol must not be empty.", nameof(symbol));
        }

        _symbol = symbol;
        _risk = risk ?? throw new ArgumentNullException(nameof(risk));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <summary>
    /// Journals every bot trade event of the symbol not yet journaled (REC-008): positions open now
    /// and positions closed since the earlier of 00:00 CE(S)T today and RiskEpochDate.
    /// </summary>
    /// <param name="nowUtc">Server time, kind UTC.</param>
    /// <returns>The lines written and the events known.</returns>
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
        SortedSet<string> keys = Load(stored);
        int written = 0;
        foreach (PositionInfo position in open)
        {
            written += Add(keys, "o", position.Id, () => Opened(position));
        }

        foreach (IGrouping<long, ClosedTradeInfo> trade in closed.Where(g => !openIds.Contains(g.Key)))
        {
            written += Add(keys, "c", trade.Key, () => Closed(trade.Key, trade.OrderBy(t => t.CloseTimeUtc).ToList()));
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

        return new TradeJournalResult(written, keys.Count);
    }

    private int Add(SortedSet<string> keys, string prefix, long id, Func<string> line)
    {
        if (!keys.Add(prefix + Text(id)))
        {
            return 0;
        }

        _sink.Write(Category, line());
        return 1;
    }

    private SortedSet<string> Load(string? stored)
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
                _sink.Write(Category, "event=store-entry-ignored key=" + StoreKey + " entry=" + token);
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

    private string Opened(PositionInfo position)
    {
        return "event=opened symbol=" + _symbol
            + " position=" + Text(position.Id)
            + " label=" + position.Label
            + " side=" + position.Side
            + " volume=" + Text(position.VolumeUnits)
            + " entry=" + Text(position.EntryPrice)
            + " stop=" + Price(position.StopLoss)
            + " target=" + Price(position.TakeProfit)
            + " opened=" + Time(position.OpenTimeUtc)
            + " comment=" + position.Comment;
    }

    private string Closed(long positionId, IReadOnlyList<ClosedTradeInfo> parts)
    {
        ClosedTradeInfo last = parts[parts.Count - 1];
        return "event=closed symbol=" + _symbol
            + " position=" + Text(positionId)
            + " label=" + last.Label
            + " opened=" + Time(last.EntryTimeUtc)
            + " closed=" + Time(last.CloseTimeUtc)
            + " net=" + Text(parts.Sum(t => t.NetProfit))
            + " commission=" + Text(parts.Sum(t => t.Commission))
            + " swap=" + Text(parts.Sum(t => t.Swap))
            + " balance=" + Text(last.BalanceAfterClose)
            + " parts=" + Text(parts.Count);
    }

    private bool IsBotOn(string label, string symbol)
    {
        return RiskEngine.IsBotLabel(label) && string.Equals(symbol, _symbol, StringComparison.Ordinal);
    }

    private static string Price(double? value)
    {
        return value.HasValue ? Text(value.Value) : "none";
    }

    private static string Text(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Text(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Time(DateTime value)
    {
        return value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
