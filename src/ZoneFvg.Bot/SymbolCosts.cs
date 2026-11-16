using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace ZoneFvg.Bot
{
    /// <summary>
    /// Unit conversions for <c>SymbolInfo</c> (EXE-002, EXE-004, RSK-001): the value of one pip and
    /// the round-trip commission, both for one unit of volume in the account currency. The
    /// platform's asset converter does every currency conversion (current rates live, historical
    /// rates in backtests).
    /// </summary>
    /// <remarks>
    /// <para>Pip value: one pip is <c>PipSize</c> in the quote currency per unit, converted to the
    /// account currency. <c>Symbol.PipValue</c> is not used because the documentation says it is
    /// fixed when the cBot starts and not updated (decision for the owner in pull request WP08).</para>
    /// <para>Commission: <c>Symbol.Commission</c> is read according to <c>Symbol.CommissionType</c>
    /// as the charge for one side (opening or closing) and doubled for the round trip. The
    /// documentation does not say whether it is per side; doubling over-states the cost if it is
    /// not, which only makes the cost filter and the risk budget stricter. The minimum commission
    /// per trade is not included. An unknown commission type gives NaN, which the core rejects, so
    /// no order is placed (fail closed). Decision for the owner in pull request WP08.</para>
    /// </remarks>
    internal sealed class SymbolCosts
    {
        private const string Usd = "USD";
        private const double Million = 1000000.0;
        private const double Percent = 100.0;
        private const double SidesPerRoundTrip = 2.0;

        // IAssetConverter.Convert(double value, Asset from, Asset to) and the string variants:
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
        /// <returns>The pip value per unit.</returns>
        public double PipValuePerUnit()
        {
            // Symbol.PipSize and QuoteAsset: https://help.ctrader.com/ctrader-automate/references/MarketData/Symbols/Symbol/
            return _converter.Convert(_symbol.PipSize, _symbol.QuoteAsset, _account);
        }

        /// <summary>
        /// Commission for opening and closing one unit of volume, in the account currency (EXE-004,
        /// RSK-006). NaN for an unknown commission type.
        /// </summary>
        /// <returns>The round-trip commission per unit, or NaN.</returns>
        public double RoundTripCommissionPerUnit()
        {
            return SidesPerRoundTrip * OneSidePerUnit();
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
                    double unitInUsd = _converter.Convert(1.0, _symbol.BaseAsset, Usd);
                    return _converter.Convert(commission / Million * unitInUsd, Usd, _account);
                case SymbolCommissionType.UsdPerOneLot:
                    return _converter.Convert(commission / _symbol.LotSize, Usd, _account);
                case SymbolCommissionType.QuoteCurrencyPerOneLot:
                    return _converter.Convert(commission / _symbol.LotSize, _symbol.QuoteAsset, _account);
                case SymbolCommissionType.PercentageOfTradingVolume:
                    return commission / Percent * _converter.Convert(1.0, _symbol.BaseAsset, _account);
                default:
                    return double.NaN;
            }
        }
    }
}
