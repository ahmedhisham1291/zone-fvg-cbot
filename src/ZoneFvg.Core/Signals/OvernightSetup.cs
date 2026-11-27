using System;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The buy setup of a trading day in the Overnight mode (spec section 3.10, SIG-043). Data only.
/// The levels are those of the signal, before the spread: the execution layer adds the spread at
/// placement to the entry, the stop and the target (EXE-033), so that the stop stays
/// NightStopAtr x ATR(D1) below the order's entry.
/// </summary>
/// <param name="Id">Setup id <c>ON-yyyyMMdd</c>, from the date that names the trading day.</param>
/// <param name="Entry">Evaluation bar close + NightEntryBufferAtr x ATR(M15).</param>
/// <param name="Stop">Entry - NightStopAtr x ATR(D1).</param>
/// <param name="Target">Entry + NightTargetR x stop distance.</param>
/// <param name="ExpiryUtc">Close of the evaluation bar + NightOrderMinutes.</param>
/// <param name="EvaluationCloseUtc">Close time of the evaluation bar.</param>
/// <param name="Close">Close price of the evaluation bar.</param>
/// <param name="TradingDate">The date that names the trading day (SIG-030), at 00:00.</param>
public sealed record OvernightSetup(
    string Id,
    double Entry,
    double Stop,
    double Target,
    DateTime ExpiryUtc,
    DateTime EvaluationCloseUtc,
    double Close,
    DateTime TradingDate);
