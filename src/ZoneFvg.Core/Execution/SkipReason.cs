namespace ZoneFvg.Core.Execution;

/// <summary>
/// The closed list of reasons for which a signal does not become an order (EXE-014): one value
/// per rule of EXE-001 to EXE-006, EXE-011, EXE-015, the news rules (NWS-008, NWS-009), each
/// reason of the risk module (RSK-001 to RSK-006, RSK-010), the broker's maximum volume, and the
/// outcomes of placement that end an attempt (EXE-009).
/// </summary>
public enum SkipReason
{
    /// <summary>A price, time or ATR of the signal is missing, not finite, not UTC or inconsistent (fail closed).</summary>
    InvalidSignal,

    /// <summary>The broker returned data that cannot be used: server time not UTC, symbol data not finite or out of range, or data the risk snapshot rejects (fail closed).</summary>
    BrokerDataInvalid,

    /// <summary>The signal was produced while replaying history (EXE-001).</summary>
    SignalFromReplay,

    /// <summary>The signal's confirmation bar closed more than one M15 bar before server time, or after it (EXE-001).</summary>
    SignalStale,

    /// <summary>The bot state is not Running (EXE-002).</summary>
    BotNotRunning,

    /// <summary>A bot position has no stop loss, so no new entry is placed (EXE-011).</summary>
    PositionWithoutStop,

    /// <summary>Server time is outside SessionStartUtc to SessionEndUtc (EXE-002).</summary>
    OutsideSession,

    /// <summary>It is Friday at or after FridayNoEntryUtc (EXE-002).</summary>
    FridayNoEntry,

    /// <summary>The spread is above MaxSpreadPips (EXE-002).</summary>
    SpreadTooWide,

    /// <summary>The symbol's market is closed (EXE-002).</summary>
    MarketClosed,

    /// <summary>A bot position, a bot pending order or an unresolved placement already exists on the symbol (EXE-002).</summary>
    SymbolBusy,

    /// <summary>The news calendar is unavailable (NWS-008).</summary>
    NewsCalendarUnavailable,

    /// <summary>Server time is inside a news window of the symbol (NWS-009).</summary>
    NewsWindow,

    /// <summary>One of the three FVG bars overlaps a news window of the symbol (NWS-009).</summary>
    NewsFvgBar,

    /// <summary>The stop distance is below MinStopPips (EXE-003).</summary>
    StopTooSmall,

    /// <summary>The stop distance is above MaxStopAtr x the signal's ATR(H4) (EXE-003).</summary>
    StopTooLarge,

    /// <summary>The gap height is below MinFvgSpreadMult x the spread (EXE-005).</summary>
    FvgTooSmallForSpread,

    /// <summary>Spread plus round-trip commission in pips is above CostToRiskMax of the stop distance (EXE-004).</summary>
    CostTooHigh,

    /// <summary>An opposing zone's proximal line lies between entry and MinRoomR x the stop distance (EXE-006).</summary>
    NoRoom,

    /// <summary>The order would live less than 15 minutes before the next news window (EXE-015).</summary>
    ExpiryTooShort,

    /// <summary>The risk module found an input value missing, not finite or inconsistent (RSK-001, fail closed).</summary>
    RiskInvalidInput,

    /// <summary>The stop is not on the loss side of entry; an alert is raised (RSK-002).</summary>
    StopOnWrongSide,

    /// <summary>The target is not on the profit side of entry; an alert is raised (RSK-002).</summary>
    TargetOnWrongSide,

    /// <summary>The volume rounded down to the step is below the symbol's minimum volume (RSK-001).</summary>
    BelowMinimumVolume,

    /// <summary>The volume is above MaxVolumeUnits; an alert is raised (RSK-002).</summary>
    VolumeAboveCap,

    /// <summary>The computed risk is above 0.55% of balance; an alert is raised (RSK-002).</summary>
    RiskAboveSanityCap,

    /// <summary>The volume is above the symbol's maximum volume reported by the broker (WP06).</summary>
    AboveBrokerMaxVolume,

    /// <summary>The broker history does not reach back far enough for the limits (RSK-007, RSK-008).</summary>
    HistoryIncomplete,

    /// <summary>Symbol data needed by the risk rules is missing or invalid (fail closed).</summary>
    MissingSymbolInfo,

    /// <summary>Free margin after the trade would be below 50% of equity (RSK-003).</summary>
    FreeMarginTooLow,

    /// <summary>Total open risk would exceed MaxOpenRiskPercent (RSK-004).</summary>
    OpenRiskCapExceeded,

    /// <summary>Same-side exposure in a currency would exceed MaxPositionsPerCurrency (RSK-005).</summary>
    CurrencyExposureCapExceeded,

    /// <summary>The daily risk budget would be exceeded (RSK-006).</summary>
    DailyBudgetExceeded,

    /// <summary>The symbol has its most entries for the CE(S)T day (RSK-010).</summary>
    SymbolEntryLimitReached,

    /// <summary>The account has its most entries for the CE(S)T day (RSK-010).</summary>
    AccountEntryLimitReached,

    /// <summary>The request limit of RSK-010 would be exceeded, so the order was not sent.</summary>
    RequestLimitReached,

    /// <summary>The broker clearly rejected the order; it is not retried (EXE-009).</summary>
    OrderRejected,

    /// <summary>After an unclear outcome nothing exists at the broker and the order may not be resent: stale, price traded through the entry, or too little life left (EXE-009).</summary>
    UnclearOutcomeNotResent,
}
