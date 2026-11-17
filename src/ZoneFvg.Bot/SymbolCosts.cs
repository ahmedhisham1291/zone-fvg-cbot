using System;
using cAlgo.API;
using cAlgo.API.Internals;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Bot
{
    /// <summary>
    /// Unit conversions for <c>SymbolInfo</c> (EXE-002, EXE-004, RSK-001): the value of one pip and
    /// the round-trip commission, both for one unit of volume in the account currency. The
    /// arithmetic is in the core's <see cref="SymbolCostMath"/>: when the account currency is the
    /// symbol's quote or base currency the result follows from the symbol's own price, and only a
    /// cross pair uses the platform's asset converter (current rates live, historical rates in
    /// backtests). The converter returned 0 in the owner's backtest of 2026-11-17 even for USD to
    /// USD, which made every pip value 0 (WP21).
    /// </summary>
    /// <remarks>
    /// <para>Pip value: one pip is <c>PipSize</c> in the quote currency per unit, converted to the
    /// account currency. <c>Symbol.PipValue</c> is not used because the documentation says it is
    /// fixed when the cBot starts and not updated (decision for the owner in pull request WP08).</para>
    /// <para>Price: the symbol's current <c>Symbol.Bid</c> ("Gets the current bid price for this
    /// symbol", https://help.ctrader.com/ctrader-algo/references/MarketData/Symbols/Symbol/), in
    /// quote currency per unit of base currency. It is used only when the account currency is the
    /// base currency (pip value: PipSize / Bid, for example USDJPY with a USD account) or for the
    /// value of one unit of base currency in the quote currency (commission). A Bid that is not a
    /// finite number greater than zero gives NaN.</para>
    /// <para>Commission: <c>Symbol.Commission</c> is read according to <c>Symbol.CommissionType</c>
    /// as the charge for one side (opening or closing) and doubled for the round trip. The
    /// documentation does not say whether it is per side; doubling over-states the cost if it is
    /// not, which only makes the cost filter and the risk budget stricter. The minimum commission
    /// per trade is not included. An unknown commission type gives NaN, which the core rejects, so
    /// no order is placed (fail closed). Decision for the owner in pull request WP08.</para>
    /// <para>A conversion that cannot be done exactly (an invalid price, or a converter rate that
    /// is not a finite number greater than zero) gives NaN, which the core rejects (fail closed).</para>
    /// </remarks>
    internal sealed class SymbolCosts
    {
        private const string Usd = "USD";
        private const double Percent = 100.0;
        private const double SidesPerRoundTrip = 2.0;

        // IAssetConverter.Convert(double value, Asset from, Asset to) and the string variant
        // Convert(double value, string from, string to):
        // https://help.ctrader.com/ctrader-algo/guides/currency-conversion/
        private readonly IAssetConverter _converter;
        private readonly Asset _account;
        private readonly Symbol _symbol;

        /// <summary>Creates the conversions for one symbol (EXE-002).</summary>
        /// <param name="converter">The cBot's <c>AssetConverter</c>.</param>
        /// <param name="account">The account deposit asset (<c>Account.Asset</c>).</param>
        /// <param name="symbol">The symbol.</param>
        public SymbolCosts(IAssetConverter converter, Asset account, Symbol symbol)
        {
            _converter = converter ?? throw new ArgumentNullException(nameof(converter));
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        }

        /// <summary>Value of one pip for one unit of volume, in the account currency (RSK-001).</summary>
        /// <returns>The pip value per unit, or NaN.</returns>
        public double PipValuePerUnit()
        {
            // Symbol.PipSize, Bid, BaseAsset, QuoteAsset: https://help.ctrader.com/ctrader-algo/references/MarketData/Symbols/Symbol/
            return SymbolCostMath.PipValuePerUnit(_symbol.PipSize, BaseName, QuoteName, _account.Name, _symbol.Bid, Convert);
        }

        /// <summary>
        /// How the pip value is converted to the account currency, for the start log (RSK-001).
        /// </summary>
        /// <returns>The conversion path.</returns>
        public AssetConversionPath PipValuePath()
        {
            return SymbolCostMath.PathOf(QuoteName, _account.Name, BaseName, QuoteName);
        }

        /// <summary>
        /// How the commission is converted to the account currency, for the start log (EXE-004):
        /// the paths of each conversion step, joined by "+".
        /// </summary>
        /// <returns>The conversion paths, or "none" for an unknown commission type.</returns>
        public string CommissionPath()
        {
            switch (_symbol.CommissionType)
            {
                case SymbolCommissionType.UsdPerMillionUsdVolume:
                    return Path(BaseName, Usd) + "+" + Path(Usd, _account.Name);
                case SymbolCommissionType.UsdPerOneLot:
                    return Path(Usd, _account.Name);
                case SymbolCommissionType.QuoteCurrencyPerOneLot:
                    return Path(QuoteName, _account.Name);
                case SymbolCommissionType.PercentageOfTradingVolume:
                    return Path(BaseName, _account.Name);
                default:
                    return "none";
            }
        }

        /// <summary>
        /// Commission for opening and closing one unit of volume, in the account currency (EXE-004,
        /// RSK-006). NaN for an unknown commission type or a conversion that cannot be done.
        /// </summary>
        /// <returns>The round-trip commission per unit, or NaN.</returns>
        public double RoundTripCommissionPerUnit()
        {
            return SidesPerRoundTrip * OneSidePerUnit();
        }

        private string BaseName => _symbol.BaseAsset.Name;

        private string QuoteName => _symbol.QuoteAsset.Name;

        private double Convert(double value, string from, string to)
        {
            return _converter.Convert(value, from, to);
        }

        private string Path(string from, string to)
        {
            return SymbolCostMath.PathOf(from, to, BaseName, QuoteName).ToString();
        }

        private double ToAccount(double amount, string from)
        {
            return SymbolCostMath.Convert(amount, from, _account.Name, BaseName, QuoteName, _symbol.Bid, Convert);
        }

        private double OneSidePerUnit()
        {
            // Symbol.Commission, CommissionType, LotSize, BaseAsset:
            // https://help.ctrader.com/ctrader-automate/references/MarketData/Symbols/Symbol/
            // SymbolCommissionType values: https://help.ctrader.com/ctrader-algo/references/MarketData/Symbols/SymbolCommissionType/
            double commission = _symbol.Commission;
            switch (_symbol.CommissionType)
            {
                case SymbolCommissionType.UsdPerMillionUsdVolume:
                    // One unit is one unit of the base currency; its value in USD times the rate per USD.
                    return SymbolCostMath.UsdPerMillionPerUnit(commission, BaseName, QuoteName, _account.Name, _symbol.Bid, Convert);
                case SymbolCommissionType.UsdPerOneLot:
                    return ToAccount(commission / _symbol.LotSize, Usd);
                case SymbolCommissionType.QuoteCurrencyPerOneLot:
                    return ToAccount(commission / _symbol.LotSize, QuoteName);
                case SymbolCommissionType.PercentageOfTradingVolume:
                    return commission / Percent * ToAccount(1.0, BaseName);
                default:
                    return double.NaN;
            }
        }
    }
}
