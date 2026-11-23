using System;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The setup of a trading day in the SessionRange mode (spec section 3.8, SIG-031, SIG-032).
/// Data only.
/// </summary>
/// <param name="Id">Setup id, <c>RH-yyyyMMdd</c> for a buy and <c>RL-yyyyMMdd</c> for a sell, from the date that names the trading day (SIG-032).</param>
/// <param name="Side">Buy with a long bias, Sell with a short bias (SIG-032).</param>
/// <param name="Entry">Stop-entry level: the range high for a buy, the range low for a sell (SIG-032).</param>
/// <param name="Stop">Stop loss: entry - StopAtrMult x ATR(H4) for a buy, entry + StopAtrMult x ATR(H4) for a sell (SIG-032).</param>
/// <param name="Target">Take profit: entry + RewardRisk x stop distance for a buy, entry - RewardRisk x stop distance for a sell (SIG-032).</param>
/// <param name="ExpiryUtc">Order expiry: the UTC instant of LastEntryNy of the trading day, on a Friday not later than FridayNoEntryUtc of that date (SIG-032).</param>
/// <param name="RangeHigh">Highest high of the range bars (SIG-031).</param>
/// <param name="RangeLow">Lowest low of the range bars (SIG-031).</param>
/// <param name="TradingDate">The date that names the trading day (SIG-030), at 00:00.</param>
public sealed record SessionRangeSetup(
    string Id,
    Side Side,
    double Entry,
    double Stop,
    double Target,
    DateTime ExpiryUtc,
    double RangeHigh,
    double RangeLow,
    DateTime TradingDate);
