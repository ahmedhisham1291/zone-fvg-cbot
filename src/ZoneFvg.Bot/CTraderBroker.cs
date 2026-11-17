using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Internals;
using ZoneFvg.Core.Execution.Ports;
using CoreSymbolInfo = ZoneFvg.Core.Execution.Ports.SymbolInfo;
using CoreTradingSession = ZoneFvg.Core.Execution.Ports.TradingSession;
using Side = ZoneFvg.Core.Domain.Side;

namespace ZoneFvg.Bot
{
    /// <summary>
    /// Broker port over the cTrader Algo API (architecture section 3). One method per port member;
    /// each converts cTrader objects to core records or core requests to one cTrader call. Every
    /// request is synchronous. A clear business rejection maps to <see cref="OrderOutcome.Rejected"/>;
    /// a disconnect, timeout, technical error, unknown code or exception maps to
    /// <see cref="OrderOutcome.Unclear"/> (EXE-009), and an exception is logged, masked.
    /// </summary>
    /// <remarks>
    /// Times are in the robot's time zone, UTC (PLT-003); they are given kind UTC. Ids are cTrader
    /// <c>int</c> values carried as <c>long</c> by the core.
    /// </remarks>
    internal sealed class CTraderBroker : IBroker
    {
        // Robot trading methods (PlaceLimitOrder, ModifyPosition, ClosePosition, CancelPendingOrder):
        // https://help.ctrader.com/ctrader-algo/references/General/Robot
        // Algo properties (Server, Account, Positions, PendingOrders, History, Symbols, AssetConverter):
        // https://help.ctrader.com/ctrader-algo/references/General/Algo/
        private readonly Robot _robot;
        private readonly AdapterLog _log;

        // Journal data only (SIM-001, OPS-002): why each position closed, as seen in Positions.Closed,
        // and the positions the bot itself asked to close. Lost on restart; a close not seen is Unknown.
        // No trading decision reads them.
        private readonly Dictionary<long, TradeExitReason> _exitReasons = new Dictionary<long, TradeExitReason>();
        private readonly HashSet<long> _closedByBot = new HashSet<long>();

        /// <summary>Creates the broker port for one cBot instance (EXE-008, EXE-009).</summary>
        /// <param name="robot">The running cBot.</param>
        /// <param name="log">Masked log.</param>
        public CTraderBroker(Robot robot, AdapterLog log)
        {
            _robot = robot ?? throw new ArgumentNullException(nameof(robot));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>Server time with kind UTC (REC-012).</summary>
        public DateTime ServerTimeUtc
        {
            // IServer.TimeInUtc: https://help.ctrader.com/ctrader-algo/references/Application/IServer/
            get { return DateTime.SpecifyKind(_robot.Server.TimeInUtc, DateTimeKind.Utc); }
        }

        /// <summary>The platform's connection flag; not relied on before trading (spec 8.4).</summary>
        public bool IsConnected
        {
            // IServer.IsConnected: https://help.ctrader.com/ctrader-algo/references/Application/IServer/
            get { return _robot.Server.IsConnected; }
        }

        /// <summary>Balance, equity, free margin and deposit currency (RSK-001, RSK-003).</summary>
        /// <returns>The account values now.</returns>
        public AccountSnapshot GetAccount()
        {
            // IAccount.Balance, Equity, FreeMargin, Asset; Asset.Name:
            // https://help.ctrader.com/ctrader-algo/references/Account/IAccount
            // https://help.ctrader.com/ctrader-algo/references/MarketData/Assets/Asset
            IAccount account = _robot.Account;
            return new AccountSnapshot(account.Balance, account.Equity, account.FreeMargin, account.Asset.Name);
        }

        /// <summary>
        /// Symbol data (EXE-002). Pip value and commission are converted to the account currency
        /// with the symbol's own Bid where that is exact, otherwise with the platform's current
        /// conversion rates; see <see cref="SymbolCosts"/>.
        /// </summary>
        /// <param name="symbol">Symbol name.</param>
        /// <returns>The symbol data now.</returns>
        /// <exception cref="InvalidOperationException">The platform does not know the symbol.</exception>
        public CoreSymbolInfo GetSymbol(string symbol)
        {
            Symbol s = Find(symbol);

            // Symbol members: https://help.ctrader.com/ctrader-automate/references/MarketData/Symbols/Symbol/
            // MarketHours.IsOpened(): https://help.ctrader.com/ctrader-algo/references/MarketData/Symbols/MarketHours/
            bool open = s.IsTradingEnabled && s.MarketHours.IsOpened();
            var costs = new SymbolCosts(_robot.AssetConverter, _robot.Account.Asset, s);
            return new CoreSymbolInfo(
                s.Name,
                s.BaseAsset.Name,
                s.QuoteAsset.Name,
                s.PipSize,
                costs.PipValuePerUnit(),
                s.VolumeInUnitsMin,
                s.VolumeInUnitsStep,
                s.VolumeInUnitsMax,
                s.Spread,
                open,
                costs.RoundTripCommissionPerUnit())
            {
                // EXE-017: the price grid. Symbol.Digits and Symbol.TickSize:
                // https://help.ctrader.com/ctrader-algo/references/MarketData/Symbols/Symbol/
                Digits = s.Digits,
                TickSize = s.TickSize,

                // REC-016: Sessions is deliberately left empty (no session reported: the gap check uses the
                // weekend and holiday model alone). The reference pages of MarketHours and TradingSession
                // do not state the time zone of StartTime and EndTime, and the core needs UTC, so the
                // sessions are only printed by DescribeCosts until the owner confirms their time zone
                // (WP24, decision for the owner). Then this becomes: Sessions = Sessions(s).
            };
        }

        /// <summary>
        /// A one-line description of the symbol's cost data for the start log, so the owner can
        /// compare the converted values with the platform's own (test record WP08), with the path
        /// each conversion took (WP21). Diagnostics only.
        /// </summary>
        /// <param name="symbol">Symbol name.</param>
        /// <returns>The description.</returns>
        public string DescribeCosts(string symbol)
        {
            Symbol s = Find(symbol);
            var costs = new SymbolCosts(_robot.AssetConverter, _robot.Account.Asset, s);
            return "symbol " + s.Name
                + " account " + _robot.Account.Asset.Name
                + " pipSize " + Number(s.PipSize)
                + " digits " + s.Digits.ToString(CultureInfo.InvariantCulture)
                + " tickSize " + Number(s.TickSize)
                + " platformPipValue " + Number(s.PipValue)
                + " bid " + Number(s.Bid)
                + " pipValuePerUnit " + Number(costs.PipValuePerUnit())
                + " pipValuePath " + costs.PipValuePath()
                + " lotSize " + s.LotSize.ToString(CultureInfo.InvariantCulture)
                + " commission " + Number(s.Commission) + " " + s.CommissionType
                + " minCommission " + Number(s.MinCommission) + " " + s.MinCommissionType
                + " roundTripCommissionPerUnit " + Number(costs.RoundTripCommissionPerUnit())
                + " commissionPath " + costs.CommissionPath()
                + " roundTripCommissionPerLot " + Number(costs.RoundTripCommissionPerUnit() * s.LotSize)
                + " spread " + Number(s.Spread)
                + " volumeMin " + Number(s.VolumeInUnitsMin) + " step " + Number(s.VolumeInUnitsStep) + " max " + Number(s.VolumeInUnitsMax)
                + " sessionsAsReported " + CoreTradingSession.ListText(Sessions(s)) + " (time zone not documented; not passed to the core)";
        }

        // REC-016: Symbol.MarketHours.Sessions as reported, StartDay/StartTime/EndDay/EndTime copied
        // unchanged. References (neither page states the time zone of the times):
        // https://help.ctrader.com/ctrader-algo/references/MarketData/Symbols/MarketHours/
        // https://help.ctrader.com/ctrader-algo/references/MarketData/Symbols/TradingSession/
        private static IReadOnlyList<CoreTradingSession> Sessions(Symbol s)
        {
            var reported = s.MarketHours.Sessions;
            var result = new List<CoreTradingSession>(reported.Count);
            for (int i = 0; i < reported.Count; i++)
            {
                var session = reported[i];
                result.Add(new CoreTradingSession(session.StartDay, session.StartTime, session.EndDay, session.EndTime));
            }

            return result.AsReadOnly();
        }

        /// <summary>All open positions of the account (EXE-002, EXE-011).</summary>
        /// <returns>The positions now.</returns>
        public IReadOnlyList<PositionInfo> GetPositions()
        {
            // Positions.Count and indexer: https://help.ctrader.com/ctrader-algo/references/Trading/Positions/Positions/
            // Position members: https://help.ctrader.com/ctrader-algo/references/Trading/Positions/Position
            Positions positions = _robot.Positions;
            var result = new List<PositionInfo>(positions.Count);
            for (int i = 0; i < positions.Count; i++)
            {
                Position p = positions[i];
                result.Add(new PositionInfo(
                    p.Id,
                    p.SymbolName,
                    ToSide(p.TradeType),
                    p.VolumeInUnits,
                    p.EntryPrice,
                    p.StopLoss,
                    p.TakeProfit,
                    p.Label ?? string.Empty,
                    p.Comment ?? string.Empty,
                    Utc(p.EntryTime)));
            }

            return result.AsReadOnly();
        }

        /// <summary>All pending orders of the account, with their expiry (EXE-002, EXE-009, EXE-010).</summary>
        /// <returns>The pending orders now.</returns>
        public IReadOnlyList<PendingOrderInfo> GetPendingOrders()
        {
            // PendingOrders.Count and indexer; PendingOrder members (ExpirationTime and SubmittedTime in the
            // robot's time zone): https://help.ctrader.com/ctrader-algo/references/Trading/Orders/PendingOrder
            PendingOrders orders = _robot.PendingOrders;
            var result = new List<PendingOrderInfo>(orders.Count);
            for (int i = 0; i < orders.Count; i++)
            {
                PendingOrder o = orders[i];
                result.Add(new PendingOrderInfo(
                    o.Id,
                    o.SymbolName,
                    ToSide(o.TradeType),
                    o.VolumeInUnits,
                    o.TargetPrice,
                    o.StopLoss,
                    o.TakeProfit,
                    o.Label ?? string.Empty,
                    o.Comment ?? string.Empty,
                    Utc(o.SubmittedTime))
                {
                    ExpiryUtc = o.ExpirationTime.HasValue ? (DateTime?)Utc(o.ExpirationTime.Value) : null,
                });
            }

            return result.AsReadOnly();
        }

        /// <summary>
        /// Closed trades closed at or after <paramref name="sinceUtc"/>, with the balance after each
        /// close (RSK-007). NetProfit is cTrader's net profit, which includes commission and swap
        /// (architecture section 10).
        /// </summary>
        /// <param name="sinceUtc">Earliest close time wanted.</param>
        /// <returns>The closed trades.</returns>
        /// <exception cref="InvalidOperationException">The history cannot be read (WP07).</exception>
        public IReadOnlyList<ClosedTradeInfo> GetHistory(DateTime sinceUtc)
        {
            try
            {
                // History.Count and indexer: https://help.ctrader.com/ctrader-algo/references/Trading/History/History/
                // HistoricalTrade (NetProfit "including swaps and commissions", Balance "after the Deal"):
                // https://help.ctrader.com/ctrader-algo/references/Trading/History/HistoricalTrade/
                History history = _robot.History;
                var result = new List<ClosedTradeInfo>();
                for (int i = 0; i < history.Count; i++)
                {
                    HistoricalTrade t = history[i];
                    DateTime closed = Utc(t.ClosingTime);
                    if (closed < sinceUtc)
                    {
                        continue;
                    }

                    result.Add(new ClosedTradeInfo(
                        t.PositionId,
                        t.SymbolName,
                        t.Label ?? string.Empty,
                        Utc(t.EntryTime),
                        closed,
                        t.NetProfit,
                        t.Commissions,
                        t.Swap,
                        t.Balance)
                    {
                        // HistoricalTrade.VolumeInUnits ("closed by the Closing Deal") and ClosingPrice
                        // ("execution price of the Closing Deal"): journal data only (SIM-001, OPS-002).
                        VolumeUnits = t.VolumeInUnits,
                        ClosingPrice = t.ClosingPrice,
                        ExitReason = _exitReasons.TryGetValue(t.PositionId, out TradeExitReason reason) ? reason : TradeExitReason.Unknown,
                    });
                }

                return result.AsReadOnly();
            }
            catch (Exception exception) when (!(exception is InvalidOperationException))
            {
                throw new InvalidOperationException("The broker history could not be read.", exception);
            }
        }

        /// <summary>
        /// Remembers why a position closed, from the <c>Positions.Closed</c> event, for the trade
        /// journal and the backtest trade log (SIM-001, OPS-002). Journal data only.
        /// </summary>
        /// <param name="args">The event arguments.</param>
        public void RecordClose(PositionClosedEventArgs args)
        {
            // PositionClosedEventArgs.Position and Reason:
            // https://help.ctrader.com/ctrader-algo/references/EventArgs/PositionClosedEventArgs
            // PositionCloseReason (Closed = "closed by the trader", StopLoss, TakeProfit, StopOut):
            // https://help.ctrader.com/ctrader-algo/references/Trading/Positions/PositionCloseReason/
            long id = args.Position.Id;
            _exitReasons[id] = args.Reason switch
            {
                PositionCloseReason.TakeProfit => TradeExitReason.TakeProfit,
                PositionCloseReason.StopLoss => TradeExitReason.StopLoss,
                PositionCloseReason.StopOut => TradeExitReason.StopOut,
                PositionCloseReason.Closed => _closedByBot.Contains(id) ? TradeExitReason.ClosedByBot : TradeExitReason.ClosedOutside,
                _ => TradeExitReason.Unknown,
            };
        }

        /// <summary>Margin the trade would use (RSK-003), as the platform estimates it.</summary>
        /// <param name="symbol">Symbol name.</param>
        /// <param name="side">Direction.</param>
        /// <param name="volumeUnits">Volume in units.</param>
        /// <returns>The estimated margin.</returns>
        public double EstimateMargin(string symbol, Side side, double volumeUnits)
        {
            // Symbol.GetEstimatedMargin(TradeType, double): https://help.ctrader.com/ctrader-algo/guides/estimated-margin/
            return Find(symbol).GetEstimatedMargin(ToTradeType(side), volumeUnits);
        }

        /// <summary>
        /// Places a limit order with stop loss and take profit given as prices
        /// (<c>ProtectionType.Absolute</c>), expiry, label and comment, synchronously (EXE-008).
        /// The core reads the order back and compares it with the request (EXE-010).
        /// </summary>
        /// <param name="request">The order.</param>
        /// <returns>The outcome.</returns>
        public OrderResult PlaceLimitOrder(LimitOrderRequest request)
        {
            return Send("place limit order", () =>
            {
                // PlaceLimitOrder(TradeType, string symbolName, double volume, double targetPrice, string label,
                //   double? stopLoss, double? takeProfit, ProtectionType? protectionType, DateTime? expiration, string comment):
                // https://help.ctrader.com/ctrader-algo/references/General/Robot
                // ProtectionType.Absolute: stop loss and take profit are prices:
                // https://help.ctrader.com/ctrader-algo/references/Trading/ProtectionType
                TradeResult result = _robot.PlaceLimitOrder(
                    ToTradeType(request.Side),
                    request.Symbol,
                    request.VolumeUnits,
                    request.EntryPrice,
                    request.Label,
                    request.StopLossPrice,
                    request.TakeProfitPrice,
                    ProtectionType.Absolute,
                    request.ExpiryUtc,
                    request.Comment);
                long? id = result.PendingOrder != null ? result.PendingOrder.Id : result.Position?.Id;
                return (result, id);
            });
        }

        /// <summary>Cancels a pending order (EXE-010, EXE-013). An unknown id is a clear rejection.</summary>
        /// <param name="orderId">Broker order id.</param>
        /// <returns>The outcome.</returns>
        public OrderResult CancelOrder(long orderId)
        {
            return Send("cancel order", () =>
            {
                // PendingOrders.FindById(int); Robot.CancelPendingOrder(PendingOrder).
                PendingOrder? order = orderId >= int.MinValue && orderId <= int.MaxValue ? _robot.PendingOrders.FindById((int)orderId) : null;
                if (order == null)
                {
                    return (null, orderId);
                }

                return (_robot.CancelPendingOrder(order), orderId);
            });
        }

        /// <summary>Sets stop loss and take profit of a position as prices; null removes one (EXE-011).</summary>
        /// <param name="positionId">Broker position id.</param>
        /// <param name="stopLoss">Stop-loss price, or null.</param>
        /// <param name="takeProfit">Take-profit price, or null.</param>
        /// <returns>The outcome.</returns>
        public OrderResult ModifyPositionProtection(long positionId, double? stopLoss, double? takeProfit)
        {
            return Send("modify position", () =>
            {
                // Robot.ModifyPosition(Position, double? stopLoss, double? takeProfit, ProtectionType?):
                // https://help.ctrader.com/ctrader-algo/references/Trading/ProtectionType
                Position? position = FindPosition(positionId);
                if (position == null)
                {
                    return (null, positionId);
                }

                return (_robot.ModifyPosition(position, stopLoss, takeProfit, ProtectionType.Absolute), positionId);
            });
        }

        /// <summary>Closes a position at market (EXE-011, EXE-013).</summary>
        /// <param name="positionId">Broker position id.</param>
        /// <returns>The outcome.</returns>
        public OrderResult ClosePosition(long positionId)
        {
            return Send("close position", () =>
            {
                // Robot.ClosePosition(Position).
                Position? position = FindPosition(positionId);
                if (position == null)
                {
                    return (null, positionId);
                }

                // Before the call, because the platform may raise Positions.Closed during it (journal data only).
                _closedByBot.Add(positionId);
                return (_robot.ClosePosition(position), positionId);
            });
        }

        /// <summary>
        /// Maps a trade result to the core's outcome (EXE-009). A null result means the order or
        /// position was not found, which is a clear rejection.
        /// </summary>
        private OrderResult Send(string what, Func<(TradeResult? Result, long? Id)> call)
        {
            try
            {
                (TradeResult? result, long? id) = call();
                if (result == null)
                {
                    return new OrderResult(OrderOutcome.Rejected, id, ErrorCode.EntityNotFound.ToString());
                }

                // TradeResult.IsSuccessful, Error (ErrorCode?): https://help.ctrader.com/ctrader-algo/references/Trading/TradeResult/
                if (result.IsSuccessful)
                {
                    return new OrderResult(OrderOutcome.Success, id, null);
                }

                OrderOutcome outcome = Classify(result.Error);
                string code = result.Error.HasValue ? result.Error.Value.ToString() : "NoErrorCode";
                _log.Info(what + " failed: " + code + " (" + outcome + ")");
                return new OrderResult(outcome, id, code);
            }
            catch (Exception exception)
            {
                // A request that threw may or may not have reached the broker (EXE-009).
                _log.Error(what, exception);
                return new OrderResult(OrderOutcome.Unclear, null, exception.GetType().Name);
            }
        }

        /// <summary>
        /// cTrader error codes (https://help.ctrader.com/ctrader-algo/references/Trading/ErrorCode/):
        /// business refusals are Rejected; TechnicalError, Disconnected, Timeout, a missing code and
        /// any code not listed here are Unclear (EXE-009).
        /// </summary>
        private static OrderOutcome Classify(ErrorCode? error)
        {
            switch (error)
            {
                case ErrorCode.BadVolume:
                case ErrorCode.NoMoney:
                case ErrorCode.MarketClosed:
                case ErrorCode.EntityNotFound:
                case ErrorCode.UnknownSymbol:
                case ErrorCode.InvalidStopLossTakeProfit:
                case ErrorCode.InvalidRequest:
                case ErrorCode.NoTradingPermission:
                    return OrderOutcome.Rejected;
                default:
                    return OrderOutcome.Unclear;
            }
        }

        private Position? FindPosition(long positionId)
        {
            // Positions.FindById(int): https://help.ctrader.com/ctrader-algo/references/Trading/Positions/Positions/
            return positionId >= int.MinValue && positionId <= int.MaxValue ? _robot.Positions.FindById((int)positionId) : null;
        }

        private Symbol Find(string symbol)
        {
            // Symbols.GetSymbol(string): https://help.ctrader.com/ctrader-algo/references/MarketData/Symbols/Symbols
            Symbol? s = string.Equals(symbol, _robot.SymbolName, StringComparison.Ordinal) ? _robot.Symbol : _robot.Symbols.GetSymbol(symbol);
            return s ?? throw new InvalidOperationException("The platform does not know the symbol " + symbol + ".");
        }

        private static Side ToSide(TradeType tradeType)
        {
            // TradeType.Buy and Sell: https://help.ctrader.com/ctrader-algo/references/Trading/TradeType/
            switch (tradeType)
            {
                case TradeType.Buy:
                    return Side.Buy;
                case TradeType.Sell:
                    return Side.Sell;
                default:
                    throw new InvalidOperationException("Unknown trade type " + tradeType + ".");
            }
        }

        private static TradeType ToTradeType(Side side)
        {
            switch (side)
            {
                case Side.Buy:
                    return TradeType.Buy;
                case Side.Sell:
                    return TradeType.Sell;
                default:
                    throw new ArgumentOutOfRangeException(nameof(side), "Unknown side.");
            }
        }

        private static string Number(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static DateTime Utc(DateTime robotTime)
        {
            return DateTime.SpecifyKind(robotTime, DateTimeKind.Utc);
        }
    }
}
