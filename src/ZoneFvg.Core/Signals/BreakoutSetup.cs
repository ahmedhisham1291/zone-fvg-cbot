using System;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The result of a breakout confirmation (spec section 3.7, SIG-024, SIG-025). Contract of
/// docs/architecture.md section 3. Data only.
/// </summary>
/// <param name="Id">Breakout id, <c>BH-yyyyMMdd-HHmm</c> for a buy and <c>BL-yyyyMMdd-HHmm</c> for a sell, from the reference bar's UTC open time (SIG-025).</param>
/// <param name="Side">Buy for a break of the reference high, Sell for a break of the reference low (SIG-023).</param>
/// <param name="Entry">Stop-entry level: high(c1) + EntryBufferAtr x ATR(M15) for a buy, low(c1) - EntryBufferAtr x ATR(M15) for a sell (SIG-024).</param>
/// <param name="Stop">Stop loss: entry - StopAtrMult x ATR(H4) for a buy, entry + StopAtrMult x ATR(H4) for a sell (SIG-024).</param>
/// <param name="Target">Take profit: entry + RewardRisk x stop distance for a buy, entry - RewardRisk x stop distance for a sell (SIG-024).</param>
/// <param name="Level">The reference high for a buy, the reference low for a sell (SIG-024).</param>
/// <param name="ConfirmationCloseUtc">Close time of the confirming M15 bar c1: its open time plus 15 minutes (SIG-024).</param>
/// <param name="AtrH4">The ATR(H4) used for the stop (SIG-024).</param>
public sealed record BreakoutSetup(
    string Id,
    Side Side,
    double Entry,
    double Stop,
    double Target,
    double Level,
    DateTime ConfirmationCloseUtc,
    double AtrH4);
