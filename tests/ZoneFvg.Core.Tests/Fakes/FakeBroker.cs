using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>Kind of a write request to <see cref="FakeBroker"/>.</summary>
public enum BrokerCall
{
    Place,
    Cancel,
    Modify,
    Close,
}

/// <summary>A scripted fault for the next request of one kind (WP06).</summary>
public enum BrokerFault
{
    /// <summary>The request is refused with the scripted code and changes nothing.</summary>
    Rejected,

    /// <summary>The request takes effect, but the result is Unclear.</summary>
    UnclearApplied,

    /// <summary>The request does not take effect, and the result is Unclear.</summary>
    UnclearNotApplied,

    /// <summary>Place only: the order is accepted with its stop loss dropped; the result is Success.</summary>
    DropStopLoss,

    /// <summary>Place only: the order is accepted; its first fill is the scripted fraction and the remainder never fills.</summary>
    PartialFill,

    /// <summary>The result is Success, but nothing changes: Modify keeps the old protection, Place creates no order.</summary>
    SuccessNotApplied,

    /// <summary>Place only: the order is accepted with its take profit dropped; the result is Success.</summary>
    DropTakeProfit,

    /// <summary>Place only: the order is accepted without an expiry; the result is Success.</summary>
    DropExpiry,

    /// <summary>Place only: the order fills at once in full; the result is Success with the order id.</summary>
    FilledAtOnce,
}

/// <summary>One write request received by <see cref="FakeBroker"/>, in arrival order.</summary>
public sealed record BrokerRequest(BrokerCall Call, DateTime AtUtc, LimitOrderRequest? Order, long? TargetId, double? StopLoss, double? TakeProfit, OrderOutcome Outcome);

/// <summary>
/// In-memory broker for the scenario tests (WP06). It holds the account, symbols, positions,
/// pending orders, history and server time, fills limit orders and triggers stops and targets on
/// <see cref="Tick"/>, expires orders on <see cref="AdvanceTo"/>, and records every request.
/// Faults are scripted per request kind and used once, in order.
/// </summary>
/// <remarks>
/// <para>Model: a buy limit fills when the ask is at or below its entry, a sell limit when the
/// bid is at or above it, at the entry price. A buy position closes at its stop when the bid is at
/// or below it and at its target when the bid is at or above it; a sell position at its stop when
/// the ask is at or above it and at its target when the ask is at or below it. Closing books
/// (price move in pips x pip value x volume) minus round-trip commission to balance and equity.
/// Free margin is not modelled beyond the value set in <see cref="Account"/>.</para>
/// <para>While <see cref="IsConnected"/> is false every request returns Unclear and changes
/// nothing; queries still return the last known state.</para>
/// <para>Ids start at 1001 and grow by one for every order and position.</para>
/// </remarks>
public sealed class FakeBroker : IBroker
{
    private const double Tolerance = 1e-9;

    private readonly SortedDictionary<string, SymbolInfo> _symbols = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, (double Bid, double Ask)> _prices = new(StringComparer.Ordinal);
    private readonly List<FakeOrder> _orders = new();
    private readonly List<PositionInfo> _positions = new();
    private readonly List<ClosedTradeInfo> _history = new();
    private readonly Dictionary<BrokerCall, Queue<ScriptedFault>> _faults = new();
    private long _nextId = 1001;
    private DateTime _now;

    public FakeBroker(DateTime serverTimeUtc, AccountSnapshot account, params SymbolInfo[] symbols)
    {
        RequireUtc(serverTimeUtc);
        ServerTimeUtc = serverTimeUtc;
        Account = account;
        foreach (SymbolInfo symbol in symbols)
        {
            _symbols[symbol.Name] = symbol;
        }
    }

    public DateTime ServerTimeUtc
    {
        get => ReportedKind.HasValue ? DateTime.SpecifyKind(_now, ReportedKind.Value) : _now;
        private set => _now = value;
    }

    /// <summary>When set, <see cref="ServerTimeUtc"/> is reported with this kind, to simulate a broken adapter.</summary>
    public DateTimeKind? ReportedKind { get; set; }

    public bool IsConnected { get; set; } = true;

    /// <summary>The account values returned by <see cref="GetAccount"/>; closing a position updates balance and equity.</summary>
    public AccountSnapshot Account { get; set; }

    /// <summary>Margin per unit of volume returned by <see cref="EstimateMargin"/>: volume x this value.</summary>
    public double MarginPerUnit { get; set; }

    /// <summary>Every write request, in arrival order.</summary>
    public List<BrokerRequest> Requests { get; } = new();

    /// <summary>Every <see cref="EstimateMargin"/> call with its arguments and the value returned.</summary>
    public List<(string Symbol, Side Side, double Volume, double Margin)> MarginRequests { get; } = new();

    /// <summary>Number of read calls (account, symbol, positions, orders, history).</summary>
    public int QueryCount { get; private set; }

    public int WriteRequestCount => Requests.Count;

    public IReadOnlyList<ClosedTradeInfo> History => _history.AsReadOnly();

    /// <summary>
    /// When true, closed trades carry their exit reason, as the adapter reports it for closes it saw
    /// (SIM-001, OPS-002). Default false: the reason is Unknown, as for a close the adapter did not see.
    /// </summary>
    public bool ReportExitReasons { get; set; }

    public int Count(BrokerCall call)
    {
        return Requests.Count(request => request.Call == call);
    }

    // ------------------------------------------------------------------ set-up

    public void SetSymbol(SymbolInfo symbol)
    {
        _symbols[symbol.Name] = symbol;
    }

    public SymbolInfo Symbol(string name)
    {
        return _symbols[name];
    }

    /// <summary>Adds an open position directly, for example one without a stop loss. Returns its id.</summary>
    public long AddPosition(string symbol, Side side, double volume, double entry, double? stopLoss, double? takeProfit, string label, string comment)
    {
        long id = _nextId++;
        _positions.Add(new PositionInfo(id, symbol, side, volume, entry, stopLoss, takeProfit, label, comment, ServerTimeUtc));
        return id;
    }

    /// <summary>Adds a pending order directly. Returns its id.</summary>
    public long AddOrder(string symbol, Side side, double volume, double entry, double? stopLoss, double? takeProfit, DateTime? expiryUtc, string label, string comment)
    {
        long id = _nextId++;
        _orders.Add(new FakeOrder(id, symbol, side, volume, entry, stopLoss, takeProfit, expiryUtc, label, comment, ServerTimeUtc, null));
        return id;
    }

    /// <summary>Adds a closed trade to the history directly, for example one closed before the scenario starts (WP07).</summary>
    public void AddClosedTrade(ClosedTradeInfo trade)
    {
        _history.Add(trade);
    }

    /// <summary>Scripts a fault for the next request of the kind. Faults of one kind are used in the order they were scripted.</summary>
    public void Script(BrokerCall call, BrokerFault fault, string code = "Scripted", double fraction = 0.0)
    {
        if (!_faults.TryGetValue(call, out Queue<ScriptedFault>? queue))
        {
            queue = new Queue<ScriptedFault>();
            _faults[call] = queue;
        }

        queue.Enqueue(new ScriptedFault(fault, code, fraction));
    }

    public int PendingFaults(BrokerCall call)
    {
        return _faults.TryGetValue(call, out Queue<ScriptedFault>? queue) ? queue.Count : 0;
    }

    // ------------------------------------------------------------------ market

    /// <summary>Moves server time forward and expires every order whose expiry is at or before it. Time never moves back.</summary>
    public void AdvanceTo(DateTime utc)
    {
        RequireUtc(utc);
        if (utc < ServerTimeUtc)
        {
            throw new InvalidOperationException("FakeBroker time cannot move back.");
        }

        ServerTimeUtc = utc;
        _orders.RemoveAll(order => order.ExpiryUtc.HasValue && order.ExpiryUtc.Value <= utc);
    }

    /// <summary>A new price for the symbol: fills limit orders, then triggers stops and targets.</summary>
    public void Tick(string symbol, double bid, double ask)
    {
        _prices[symbol] = (bid, ask);
        foreach (FakeOrder order in _orders.Where(o => o.Symbol == symbol).ToList())
        {
            bool fills = order.Side == Side.Buy ? ask <= order.Entry + Tolerance : bid >= order.Entry - Tolerance;
            if (fills && !order.NoMoreFills)
            {
                Fill(order);
            }
        }

        foreach (PositionInfo position in _positions.Where(p => p.Symbol == symbol).ToList())
        {
            (double Price, TradeExitReason Reason)? exit = ExitPrice(position, bid, ask);
            if (exit.HasValue)
            {
                Book(position, exit.Value.Price, exit.Value.Reason);
            }
        }
    }

    // ------------------------------------------------------------------ IBroker reads

    public AccountSnapshot GetAccount()
    {
        QueryCount++;
        return Account;
    }

    public SymbolInfo GetSymbol(string symbol)
    {
        QueryCount++;
        if (!_symbols.TryGetValue(symbol, out SymbolInfo? info))
        {
            throw new ArgumentException("Unknown symbol " + symbol + ".", nameof(symbol));
        }

        return info;
    }

    public IReadOnlyList<PositionInfo> GetPositions()
    {
        QueryCount++;
        return _positions.ToList().AsReadOnly();
    }

    public IReadOnlyList<PendingOrderInfo> GetPendingOrders()
    {
        QueryCount++;
        return _orders.Select(order => order.ToInfo()).ToList().AsReadOnly();
    }

    public IReadOnlyList<ClosedTradeInfo> GetHistory(DateTime sinceUtc)
    {
        QueryCount++;
        return _history.Where(trade => trade.CloseTimeUtc >= sinceUtc).ToList().AsReadOnly();
    }

    public double EstimateMargin(string symbol, Side side, double volumeUnits)
    {
        double margin = volumeUnits * MarginPerUnit;
        MarginRequests.Add((symbol, side, volumeUnits, margin));
        return margin;
    }

    // ------------------------------------------------------------------ IBroker writes

    public OrderResult PlaceLimitOrder(LimitOrderRequest request)
    {
        ScriptedFault? fault = NextFault(BrokerCall.Place);
        OrderResult result = Place(request, fault);
        Requests.Add(new BrokerRequest(BrokerCall.Place, ServerTimeUtc, request, null, request.StopLossPrice, request.TakeProfitPrice, result.Outcome));
        return result;
    }

    public OrderResult CancelOrder(long orderId)
    {
        ScriptedFault? fault = NextFault(BrokerCall.Cancel);
        OrderResult result = Apply(fault, orderId, () => _orders.Any(o => o.Id == orderId), () => _orders.RemoveAll(o => o.Id == orderId), "OrderNotFound");
        Requests.Add(new BrokerRequest(BrokerCall.Cancel, ServerTimeUtc, null, orderId, null, null, result.Outcome));
        return result;
    }

    public OrderResult ModifyPositionProtection(long positionId, double? stopLoss, double? takeProfit)
    {
        ScriptedFault? fault = NextFault(BrokerCall.Modify);
        OrderResult result = Modify(positionId, stopLoss, takeProfit, fault);
        Requests.Add(new BrokerRequest(BrokerCall.Modify, ServerTimeUtc, null, positionId, stopLoss, takeProfit, result.Outcome));
        return result;
    }

    public OrderResult ClosePosition(long positionId)
    {
        ScriptedFault? fault = NextFault(BrokerCall.Close);
        OrderResult result = Apply(fault, positionId, () => _positions.Any(p => p.Id == positionId), () => CloseAtMarket(positionId), "PositionNotFound");
        Requests.Add(new BrokerRequest(BrokerCall.Close, ServerTimeUtc, null, positionId, null, null, result.Outcome));
        return result;
    }

    // ------------------------------------------------------------------ internals

    private ScriptedFault? NextFault(BrokerCall call)
    {
        if (_faults.TryGetValue(call, out Queue<ScriptedFault>? queue) && queue.Count > 0)
        {
            return queue.Dequeue();
        }

        return null;
    }

    private OrderResult Place(LimitOrderRequest request, ScriptedFault? fault)
    {
        if (!IsConnected)
        {
            return new OrderResult(OrderOutcome.Unclear, null, "Disconnected");
        }

        if (fault != null && fault.Fault == BrokerFault.Rejected)
        {
            return new OrderResult(OrderOutcome.Rejected, null, fault.Code);
        }

        if (fault != null && fault.Fault == BrokerFault.UnclearNotApplied)
        {
            return new OrderResult(OrderOutcome.Unclear, null, fault.Code);
        }

        if (fault != null && fault.Fault == BrokerFault.SuccessNotApplied)
        {
            return new OrderResult(OrderOutcome.Success, _nextId++, null);
        }

        string? refusal = Refusal(request);
        if (refusal != null)
        {
            return new OrderResult(OrderOutcome.Rejected, null, refusal);
        }

        long id = _nextId++;
        double? stop = fault?.Fault == BrokerFault.DropStopLoss ? null : request.StopLossPrice;
        double? target = fault?.Fault == BrokerFault.DropTakeProfit ? null : request.TakeProfitPrice;
        DateTime? expiry = fault?.Fault == BrokerFault.DropExpiry ? null : request.ExpiryUtc;
        double? fraction = fault != null && fault.Fault == BrokerFault.PartialFill ? fault.Fraction : null;
        var order = new FakeOrder(id, request.Symbol, request.Side, request.VolumeUnits, request.EntryPrice, stop, target, expiry, request.Label, request.Comment, ServerTimeUtc, fraction);
        _orders.Add(order);
        if (fault?.Fault == BrokerFault.FilledAtOnce)
        {
            Fill(order);
        }

        return fault != null && fault.Fault == BrokerFault.UnclearApplied
            ? new OrderResult(OrderOutcome.Unclear, null, fault.Code)
            : new OrderResult(OrderOutcome.Success, id, null);
    }

    private string? Refusal(LimitOrderRequest request)
    {
        if (!_symbols.TryGetValue(request.Symbol, out SymbolInfo? symbol))
        {
            return "UnknownSymbol";
        }

        if (!symbol.IsMarketOpen)
        {
            return "MarketClosed";
        }

        double steps = request.VolumeUnits / symbol.VolumeStepUnits;
        bool onStep = Math.Abs(steps - Math.Round(steps)) <= Tolerance;
        if (request.VolumeUnits < symbol.VolumeMinUnits - Tolerance || request.VolumeUnits > symbol.VolumeMaxUnits + Tolerance || !onStep)
        {
            return "InvalidVolume";
        }

        return request.ExpiryUtc <= ServerTimeUtc ? "InvalidExpiry" : null;
    }

    private OrderResult Modify(long positionId, double? stopLoss, double? takeProfit, ScriptedFault? fault)
    {
        int index = _positions.FindIndex(p => p.Id == positionId);
        bool valid = index >= 0 && StopIsValid(_positions[index], stopLoss);
        return Apply(
            fault,
            positionId,
            () => valid,
            () => _positions[index] = _positions[index] with { StopLoss = stopLoss, TakeProfit = takeProfit },
            index < 0 ? "PositionNotFound" : "InvalidStopLoss");
    }

    private bool StopIsValid(PositionInfo position, double? stopLoss)
    {
        if (!stopLoss.HasValue || !_prices.TryGetValue(position.Symbol, out (double Bid, double Ask) price))
        {
            return true;
        }

        return position.Side == Side.Buy ? stopLoss.Value < price.Bid : stopLoss.Value > price.Ask;
    }

    private OrderResult Apply(ScriptedFault? fault, long id, Func<bool> possible, Action effect, string refusalCode)
    {
        if (!IsConnected)
        {
            return new OrderResult(OrderOutcome.Unclear, null, "Disconnected");
        }

        if (fault != null && fault.Fault == BrokerFault.Rejected)
        {
            return new OrderResult(OrderOutcome.Rejected, null, fault.Code);
        }

        if (fault != null && fault.Fault == BrokerFault.UnclearNotApplied)
        {
            return new OrderResult(OrderOutcome.Unclear, null, fault.Code);
        }

        if (fault != null && fault.Fault == BrokerFault.SuccessNotApplied)
        {
            return new OrderResult(OrderOutcome.Success, id, null);
        }

        if (!possible())
        {
            return new OrderResult(OrderOutcome.Rejected, null, refusalCode);
        }

        effect();
        return fault != null && fault.Fault == BrokerFault.UnclearApplied
            ? new OrderResult(OrderOutcome.Unclear, null, fault.Code)
            : new OrderResult(OrderOutcome.Success, id, null);
    }

    private void Fill(FakeOrder order)
    {
        SymbolInfo symbol = _symbols[order.Symbol];
        double volume = order.Volume;
        if (order.PartialFraction.HasValue)
        {
            volume = Math.Floor((order.Volume * order.PartialFraction.Value / symbol.VolumeStepUnits) + Tolerance) * symbol.VolumeStepUnits;
        }

        _positions.Add(new PositionInfo(_nextId++, order.Symbol, order.Side, volume, order.Entry, order.StopLoss, order.TakeProfit, order.Label, order.Comment, ServerTimeUtc));
        double remainder = order.Volume - volume;
        _orders.Remove(order);
        if (remainder > Tolerance)
        {
            _orders.Add(order with { Volume = remainder, NoMoreFills = true });
        }
    }

    private static (double Price, TradeExitReason Reason)? ExitPrice(PositionInfo position, double bid, double ask)
    {
        if (position.Side == Side.Buy)
        {
            if (position.StopLoss.HasValue && bid <= position.StopLoss.Value + Tolerance)
            {
                return (position.StopLoss.Value, TradeExitReason.StopLoss);
            }

            return position.TakeProfit.HasValue && bid >= position.TakeProfit.Value - Tolerance ? (position.TakeProfit.Value, TradeExitReason.TakeProfit) : null;
        }

        if (position.StopLoss.HasValue && ask >= position.StopLoss.Value - Tolerance)
        {
            return (position.StopLoss.Value, TradeExitReason.StopLoss);
        }

        return position.TakeProfit.HasValue && ask <= position.TakeProfit.Value + Tolerance ? (position.TakeProfit.Value, TradeExitReason.TakeProfit) : null;
    }

    private void CloseAtMarket(long positionId)
    {
        PositionInfo position = _positions.Single(p => p.Id == positionId);
        double exit = position.EntryPrice;
        if (_prices.TryGetValue(position.Symbol, out (double Bid, double Ask) price))
        {
            exit = position.Side == Side.Buy ? price.Bid : price.Ask;
        }

        Book(position, exit, TradeExitReason.ClosedByBot);
    }

    private void Book(PositionInfo position, double exit, TradeExitReason reason)
    {
        SymbolInfo symbol = _symbols[position.Symbol];
        double move = position.Side == Side.Buy ? exit - position.EntryPrice : position.EntryPrice - exit;
        double commission = -symbol.RoundTripCommissionPerUnit * position.VolumeUnits;
        double net = (move / symbol.PipSize * symbol.PipValuePerUnit * position.VolumeUnits) + commission;
        Account = Account with { Balance = Account.Balance + net, Equity = Account.Equity + net };
        _history.Add(new ClosedTradeInfo(position.Id, position.Symbol, position.Label, position.OpenTimeUtc, ServerTimeUtc, net, commission, 0.0, Account.Balance)
        {
            VolumeUnits = position.VolumeUnits,
            ClosingPrice = exit,
            ExitReason = ReportExitReasons ? reason : TradeExitReason.Unknown,
        });
        _positions.Remove(position);
    }

    private static void RequireUtc(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("FakeBroker times must be UTC: " + utc.ToString("o", CultureInfo.InvariantCulture));
        }
    }

    private sealed record ScriptedFault(BrokerFault Fault, string Code, double Fraction);

    private sealed record FakeOrder(
        long Id,
        string Symbol,
        Side Side,
        double Volume,
        double Entry,
        double? StopLoss,
        double? TakeProfit,
        DateTime? ExpiryUtc,
        string Label,
        string Comment,
        DateTime CreatedUtc,
        double? PartialFraction)
    {
        public bool NoMoreFills { get; init; }

        public PendingOrderInfo ToInfo()
        {
            return new PendingOrderInfo(Id, Symbol, Side, Volume, Entry, StopLoss, TakeProfit, Label, Comment, CreatedUtc) { ExpiryUtc = ExpiryUtc };
        }
    }
}
