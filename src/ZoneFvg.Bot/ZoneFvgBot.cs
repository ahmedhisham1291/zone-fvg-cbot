using System;
using System.Globalization;
using System.IO;
using cAlgo.API;
using ZoneFvg.Bot;
using ZoneFvg.Core;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;
using CoreEntryMode = ZoneFvg.Core.App.EntryMode;
using CoreNewsFailMode = ZoneFvg.Core.News.NewsFailMode;
using CoreTimeFrame = ZoneFvg.Core.Domain.TimeFrame;

namespace cAlgo.Robots
{
    /// <summary>
    /// The cTrader adapter (PLT-002, PLT-003): builds the core's parameters, connects cTrader events
    /// to <see cref="BotCore"/>, and contains no trading rule, filter, risk calculation or state
    /// machine. Every handler converts and forwards. Runs on an M15 chart, one instance per symbol.
    /// </summary>
    // Robot attribute (AccessRights, TimeZone): https://help.ctrader.com/ctrader-algo/references/Attributes/RobotAttribute/
    // AccessRights.None is enough for Http and local storage:
    // https://help.ctrader.com/ctrader-algo/guides/network-access/ and https://help.ctrader.com/ctrader-algo/guides/local-storage/
    [Robot(AccessRights = AccessRights.None, TimeZone = TimeZones.UTC)]
    public class ZoneFvgBot : Robot
    {
        private const string NewsHistoryResource = "ZoneFvg.Bot.news-history.csv";

        private BotCore? _core;
        private CTraderBars? _bars;
        private CTraderBroker? _broker;
        private AdapterLog? _log;
        private ChartPainter? _painter;

        // ------------------------------------------------------------------ parameters (spec section 7, PLT-006)
        // Parameter attribute (Name, DefaultValue, Group): https://help.ctrader.com/ctrader-algo/references/Attributes/ParameterAttribute/
        // Parameter types: https://help.ctrader.com/ctrader-algo/guides/parameter-types

        /// <summary>Entry mode of this instance, SessionRange (the default from spec v0.16, meant for XAUUSD only, PLT-020), ZoneFvg, Breakout or Overnight (the second XAUUSD instance, PLT-021, spec 3.10) (PLT-012, PLT-015, spec 3.7, 3.8 and 7).</summary>
        [Parameter("EntryMode", DefaultValue = "SessionRange", Group = "Entry mode")]
        public string EntryMode { get; set; } = "SessionRange";

        /// <summary>Breakout entry beyond the confirming bar in ATR(M15) (PLT-012, SIG-024).</summary>
        [Parameter("BreakoutEntryBufferAtr", DefaultValue = 0.1, Group = "Entry mode")]
        public double BreakoutEntryBufferAtr { get; set; }

        /// <summary>Breakout stop distance in ATR(H4) (PLT-012, SIG-024).</summary>
        [Parameter("BreakoutStopAtr", DefaultValue = 1.0, Group = "Entry mode")]
        public double BreakoutStopAtr { get; set; }

        /// <summary>Slow D1 EMA period of the breakout trend filter; 0 switches it off (SIG-028, spec 3.1).</summary>
        [Parameter("TrendSlowEmaPeriod", DefaultValue = 200, Group = "Entry mode")]
        public int TrendSlowEmaPeriod { get; set; }

        /// <summary>Latest close time of a confirming bar, UTC, HH:mm; equal to SessionEndUtc switches it off (SIG-029, spec 3.7).</summary>
        [Parameter("BreakoutLastConfirmUtc", DefaultValue = "08:00", Group = "Entry mode")]
        public string BreakoutLastConfirmUtc { get; set; } = "08:00";

        /// <summary>SessionRange: start of the range window, New York time, HH:mm (PLT-015, SIG-031, spec 3.8).</summary>
        [Parameter("RangeStartNy", DefaultValue = "19:00", Group = "Entry mode")]
        public string RangeStartNy { get; set; } = "19:00";

        /// <summary>SessionRange: end of the range window, New York time, HH:mm (PLT-015, SIG-031, spec 3.8).</summary>
        [Parameter("RangeEndNy", DefaultValue = "03:00", Group = "Entry mode")]
        public string RangeEndNy { get; set; } = "03:00";

        /// <summary>SessionRange: the day's order ends, New York time, HH:mm (PLT-015, SIG-032, spec 3.8).</summary>
        [Parameter("RangeLastEntryNy", DefaultValue = "12:00", Group = "Entry mode")]
        public string RangeLastEntryNy { get; set; } = "12:00";

        /// <summary>SessionRange: an open position is closed, New York time, HH:mm (PLT-015, EXE-025, spec 3.8).</summary>
        [Parameter("RangeExitNy", DefaultValue = "16:30", Group = "Entry mode")]
        public string RangeExitNy { get; set; } = "16:30";

        /// <summary>SessionRange: smallest range in ATR(D1) (PLT-015, SIG-031, spec 3.8).</summary>
        [Parameter("RangeMinWidthAtr", DefaultValue = 0.5, Group = "Entry mode")]
        public double RangeMinWidthAtr { get; set; }

        /// <summary>SessionRange: stop distance in ATR(H4) (PLT-015, SIG-032, spec 3.8).</summary>
        [Parameter("RangeStopAtr", DefaultValue = 1.0, Group = "Entry mode")]
        public double RangeStopAtr { get; set; }

        /// <summary>SessionRange: the side of the setup from D1 EMA(this) against D1 EMA(BiasEmaPeriod), 0 = the close against EMA(BiasEmaPeriod) (SIG-042, spec 3.8 and row AY of v0.17).</summary>
        [Parameter("RangeTrendFastEma", DefaultValue = 20, Group = "Entry mode")]
        public int RangeTrendFastEma { get; set; }

        /// <summary>Overnight: the evaluation bar closes at or after this New York time, HH:mm (PLT-021, SIG-043, spec 3.10).</summary>
        [Parameter("NightEntryNy", DefaultValue = "18:15", Group = "Overnight")]
        public string NightEntryNy { get; set; } = "18:15";

        /// <summary>Overnight: the evaluation bar closes before this New York time, HH:mm (PLT-021, SIG-043, spec 3.10).</summary>
        [Parameter("NightLatestNy", DefaultValue = "19:00", Group = "Overnight")]
        public string NightLatestNy { get; set; } = "19:00";

        /// <summary>Overnight: an open position is closed, New York time, HH:mm (PLT-021, EXE-033, spec 3.10).</summary>
        [Parameter("NightExitNy", DefaultValue = "02:45", Group = "Overnight")]
        public string NightExitNy { get; set; } = "02:45";

        /// <summary>Overnight: buy stop above close + spread, in ATR(M15) (PLT-021, SIG-043, spec 3.10).</summary>
        [Parameter("NightEntryBufferAtr", DefaultValue = 0.03, Group = "Overnight")]
        public double NightEntryBufferAtr { get; set; }

        /// <summary>Overnight: stop distance in ATR(D1) (PLT-021, SIG-043, spec 3.10).</summary>
        [Parameter("NightStopAtr", DefaultValue = 1.0, Group = "Overnight")]
        public double NightStopAtr { get; set; }

        /// <summary>Overnight: safety target in R (PLT-021, SIG-043, spec 3.10).</summary>
        [Parameter("NightTargetR", DefaultValue = 5.0, Group = "Overnight")]
        public double NightTargetR { get; set; }

        /// <summary>Overnight: life of the buy stop after the evaluation bar, in minutes (PLT-021, SIG-043, spec 3.10).</summary>
        [Parameter("NightOrderMinutes", DefaultValue = 60, Group = "Overnight")]
        public int NightOrderMinutes { get; set; }

        /// <summary>Entry model of ZoneFvg mode, Fvg (the default from spec v0.16, PLT-020) or Choch (PLT-016, spec 3.9 and 7). Choch needs EntryMode ZoneFvg.</summary>
        [Parameter("EntryModel", DefaultValue = "Fvg", Group = "Entry model")]
        public string EntryModel { get; set; } = "Fvg";

        /// <summary>Choch: a change of character counts only after a sweep (PLT-016, SIG-038).</summary>
        [Parameter("RequireSweep", DefaultValue = false, Group = "Entry model")]
        public bool RequireSweep { get; set; }

        /// <summary>Choch: confirmation window in M15 bars after the touch, 4 to 96 (PLT-016, SIG-036).</summary>
        [Parameter("ChochWindowBars", DefaultValue = 32, Group = "Entry model")]
        public int ChochWindowBars { get; set; }

        /// <summary>Choch: the D1 bias must match the zone (PLT-016, SIG-036). Default true from spec v0.14 (PLT-018).</summary>
        [Parameter("ChochUseBias", DefaultValue = true, Group = "Entry model")]
        public bool ChochUseBias { get; set; }

        /// <summary>Choch: the stop also beyond the zone's distal line (PLT-016, SIG-039).</summary>
        [Parameter("ChochStopAtDistal", DefaultValue = false, Group = "Entry model")]
        public bool ChochStopAtDistal { get; set; }

        /// <summary>Choch: target before the opposing level, in ATR(H4), 0 to 1 (PLT-016, SIG-040).</summary>
        [Parameter("ChochTargetBufferAtr", DefaultValue = 0.1, Group = "Entry model")]
        public double ChochTargetBufferAtr { get; set; }

        /// <summary>Choch: smallest stop in pips (PLT-016, EXE-028); 0 = automatic from the symbol profile, 5 pips or 3.00 USD of gold's price (EXE-030, PLT-018, spec 7.1).</summary>
        [Parameter("ChochMinStopPips", DefaultValue = 0.0, Group = "Entry model")]
        public double ChochMinStopPips { get; set; }

        /// <summary>Choch: smallest reward to risk on the signal's levels, 1 to 10 (PLT-016, EXE-028).</summary>
        [Parameter("MinRewardRisk", DefaultValue = 2.0, Group = "Entry model")]
        public double MinRewardRisk { get; set; }

        /// <summary>Trend filter timeframe (SIG-005). Fixed: D1.</summary>
        [Parameter("BiasTimeFrame", DefaultValue = "D1", Group = "Signal")]
        public string BiasTimeFrame { get; set; } = "D1";

        /// <summary>Zone timeframe (SIG-006). Fixed: H4.</summary>
        [Parameter("ZoneTimeFrame", DefaultValue = "H4", Group = "Signal")]
        public string ZoneTimeFrame { get; set; } = "H4";

        /// <summary>Trend EMA period (SIG-005).</summary>
        [Parameter("BiasEmaPeriod", DefaultValue = 50, Group = "Signal")]
        public int BiasEmaPeriod { get; set; }

        /// <summary>Period of all ATR values (SIG-003).</summary>
        [Parameter("AtrPeriod", DefaultValue = 14, Group = "Signal")]
        public int AtrPeriod { get; set; }

        /// <summary>Bars on each side of a swing (SIG-006).</summary>
        [Parameter("SwingStrength", DefaultValue = 3, Group = "Signal")]
        public int SwingStrength { get; set; }

        /// <summary>Minimum impulse size in ATR(H4) (SIG-007).</summary>
        [Parameter("ImpulseAtrMult", DefaultValue = 2.0, Group = "Signal")]
        public double ImpulseAtrMult { get; set; }

        /// <summary>Bars in an impulse leg (SIG-007).</summary>
        [Parameter("MaxImpulseBars", DefaultValue = 3, Group = "Signal")]
        public int MaxImpulseBars { get; set; }

        /// <summary>Bars in a base (SIG-009).</summary>
        [Parameter("MaxBaseBars", DefaultValue = 3, Group = "Signal")]
        public int MaxBaseBars { get; set; }

        /// <summary>Impulse must break structure (SIG-008).</summary>
        [Parameter("RequireBos", DefaultValue = true, Group = "Signal")]
        public bool RequireBos { get; set; }

        /// <summary>Smallest zone height in ATR(H4) (SIG-011).</summary>
        [Parameter("MinZoneAtr", DefaultValue = 0.2, Group = "Signal")]
        public double MinZoneAtr { get; set; }

        /// <summary>Largest zone height in ATR(H4) (SIG-011).</summary>
        [Parameter("MaxZoneAtr", DefaultValue = 1.5, Group = "Signal")]
        public double MaxZoneAtr { get; set; }

        /// <summary>Zone expiry in H4 bars (SIG-014).</summary>
        [Parameter("MaxZoneAgeBars", DefaultValue = 120, Group = "Signal")]
        public int MaxZoneAgeBars { get; set; }

        /// <summary>M15 bars allowed for the gap after a touch (SIG-014, SIG-016).</summary>
        [Parameter("ConfirmWindowBars", DefaultValue = 16, Group = "Signal")]
        public int ConfirmWindowBars { get; set; }

        /// <summary>Minimum gap height in ATR(M15) (SIG-016).</summary>
        [Parameter("MinFvgAtr", DefaultValue = 0.25, Group = "Signal")]
        public double MinFvgAtr { get; set; }

        /// <summary>Entry depth into the gap in percent (SIG-017).</summary>
        [Parameter("EntryLevelPct", DefaultValue = 50.0, Group = "Signal")]
        public double EntryLevelPct { get; set; }

        /// <summary>Stop buffer in ATR(M15) (SIG-017).</summary>
        [Parameter("StopBufferAtr", DefaultValue = 0.1, Group = "Signal")]
        public double StopBufferAtr { get; set; }

        /// <summary>Target in R (SIG-017).</summary>
        [Parameter("RewardRisk", DefaultValue = 2.0, Group = "Signal")]
        public double RewardRisk { get; set; }

        /// <summary>Gap height versus spread (EXE-005).</summary>
        [Parameter("MinFvgSpreadMult", DefaultValue = 3.0, Group = "Execution")]
        public double MinFvgSpreadMult { get; set; }

        /// <summary>Limit order life in M15 bars (EXE-015).</summary>
        [Parameter("EntryExpiryBars", DefaultValue = 8, Group = "Execution")]
        public int EntryExpiryBars { get; set; }

        /// <summary>Smallest allowed stop in pips (EXE-003); 0 = automatic from the symbol profile, 12 pips or 8.00 USD of gold's price (EXE-030, PLT-018, spec 7.1).</summary>
        [Parameter("MinStopPips", DefaultValue = 0.0, Group = "Execution")]
        public double MinStopPips { get; set; }

        /// <summary>Largest allowed stop in ATR(H4) (EXE-003).</summary>
        [Parameter("MaxStopAtr", DefaultValue = 2.0, Group = "Execution")]
        public double MaxStopAtr { get; set; }

        /// <summary>Clear space to an opposing zone in R (EXE-006).</summary>
        [Parameter("MinRoomR", DefaultValue = 1.5, Group = "Execution")]
        public double MinRoomR { get; set; }

        /// <summary>Costs as a share of the stop distance (EXE-004).</summary>
        [Parameter("CostToRiskMax", DefaultValue = 0.10, Group = "Execution")]
        public double CostToRiskMax { get; set; }

        /// <summary>Start of the trading window, UTC, HH:mm (EXE-002); 00:00 to 23:59. Default 00:00 from spec v0.14 (PLT-018).</summary>
        [Parameter("SessionStartUtc", DefaultValue = "00:00", Group = "Execution")]
        public string SessionStartUtc { get; set; } = "00:00";

        /// <summary>End of the trading window, UTC, HH:mm, or 24:00 for the end of the day (EXE-002, EXE-029, PLT-016). Default 24:00 from spec v0.14 (PLT-018).</summary>
        [Parameter("SessionEndUtc", DefaultValue = "24:00", Group = "Execution")]
        public string SessionEndUtc { get; set; } = "24:00";

        /// <summary>Last entry time on Friday, UTC, HH:mm (EXE-002).</summary>
        [Parameter("FridayNoEntryUtc", DefaultValue = "12:00", Group = "Execution")]
        public string FridayNoEntryUtc { get; set; } = "12:00";

        /// <summary>Weekend flat time on Friday, UTC, HH:mm (EXE-013).</summary>
        [Parameter("FridayCloseUtc", DefaultValue = "20:00", Group = "Execution")]
        public string FridayCloseUtc { get; set; } = "20:00";

        /// <summary>Spread filter in pips (EXE-002); 0 = automatic from the symbol profile, 1.5 pips or 0.40 USD of gold's price (EXE-030, PLT-018, spec 7.1).</summary>
        [Parameter("MaxSpreadPips", DefaultValue = 0.0, Group = "Execution")]
        public double MaxSpreadPips { get; set; }

        /// <summary>Risk per trade in percent (RSK-001). Default 0.75 from spec v0.16 (PLT-020).</summary>
        [Parameter("RiskPercent", DefaultValue = 0.75, Group = "Risk")]
        public double RiskPercent { get; set; }

        /// <summary>Drawdown brake of fixed sizing: distance below the peak balance in percent; 0 = off, the default from spec v0.18 row BC (RSK-019, PLT-020, PLT-021, spec 4.1).</summary>
        [Parameter("DrawdownBrakePercent", DefaultValue = 0.0, Group = "Risk")]
        public double DrawdownBrakePercent { get; set; }

        /// <summary>Drawdown brake: multiplier of RiskPercent while braked (RSK-019, PLT-020, spec 4.1).</summary>
        [Parameter("DrawdownBrakeFactor", DefaultValue = 0.5, Group = "Risk")]
        public double DrawdownBrakeFactor { get; set; }

        /// <summary>Total open risk in percent (RSK-004).</summary>
        [Parameter("MaxOpenRiskPercent", DefaultValue = 1.5, Group = "Risk")]
        public double MaxOpenRiskPercent { get; set; }

        /// <summary>Correlation cap per currency (RSK-005).</summary>
        [Parameter("MaxPositionsPerCurrency", DefaultValue = 2, Group = "Risk")]
        public int MaxPositionsPerCurrency { get; set; }

        /// <summary>Daily risk budget in percent of InitialCapital (RSK-006, PLT-017). Default 2.0 from spec v0.12.</summary>
        [Parameter("DailyLossLimitPercent", DefaultValue = 2.0, Group = "Risk")]
        public double DailyLossLimitPercent { get; set; }

        /// <summary>Monthly loss limit in percent of InitialCapital (RSK-020, spec v0.19 row BF); 0 switches it off.</summary>
        [Parameter("MonthlyLossLimitPercent", DefaultValue = 2.0, Group = "Risk")]
        public double MonthlyLossLimitPercent { get; set; }

        /// <summary>Kill switch drawdown in percent (RSK-008).</summary>
        [Parameter("MaxDrawdownPercent", DefaultValue = 8.0, Group = "Risk")]
        public double MaxDrawdownPercent { get; set; }

        /// <summary>Base for the FTMO percentages (RSK-004, PLT-017). Default 200,000 from spec v0.12.</summary>
        [Parameter("InitialCapital", DefaultValue = 200000.0, Group = "Risk")]
        public double InitialCapital { get; set; }

        /// <summary>Day boundary zone. Fixed: Europe/Prague, computed by the core's CestClock (RSK-012).</summary>
        [Parameter("DayResetZone", DefaultValue = "Europe/Prague", Group = "Risk")]
        public string DayResetZone { get; set; } = "Europe/Prague";

        /// <summary>FTMO daily loss limit in percent (RSK-009).</summary>
        [Parameter("FtmoDailyLossPercent", DefaultValue = 5.0, Group = "Risk")]
        public double FtmoDailyLossPercent { get; set; }

        /// <summary>FTMO maximum loss in percent (RSK-009).</summary>
        [Parameter("FtmoMaxLossPercent", DefaultValue = 10.0, Group = "Risk")]
        public double FtmoMaxLossPercent { get; set; }

        /// <summary>Emergency guard distance from an FTMO floor in percent (RSK-009).</summary>
        [Parameter("FtmoGuardBufferPercent", DefaultValue = 1.0, Group = "Risk")]
        public double FtmoGuardBufferPercent { get; set; }

        /// <summary>Order requests per instance per day (RSK-010).</summary>
        [Parameter("MaxRequestsPerDay", DefaultValue = 100, Group = "Risk")]
        public int MaxRequestsPerDay { get; set; }

        /// <summary>Order requests per instance per hour (RSK-010, spec 4.3).</summary>
        [Parameter("MaxRequestsPerHour", DefaultValue = 20, Group = "Risk")]
        public int MaxRequestsPerHour { get; set; }

        /// <summary>Unhandled errors per instance per hour (RSK-010, spec 4.3).</summary>
        [Parameter("MaxErrorsPerHour", DefaultValue = 5, Group = "Risk")]
        public int MaxErrorsPerHour { get; set; }

        /// <summary>Start date of drawdown measurement, yyyy-MM-dd, meaning 00:00 Prague time (RSK-008). Must be set.</summary>
        [Parameter("RiskEpochDate", DefaultValue = "", Group = "Risk")]
        public string RiskEpochDate { get; set; } = string.Empty;

        /// <summary>Entries per symbol per day (RSK-010).</summary>
        [Parameter("MaxEntriesPerSymbolPerDay", DefaultValue = 2, Group = "Risk")]
        public int MaxEntriesPerSymbolPerDay { get; set; }

        /// <summary>Entries per account per day (RSK-010).</summary>
        [Parameter("MaxEntriesPerDay", DefaultValue = 6, Group = "Risk")]
        public int MaxEntriesPerDay { get; set; }

        /// <summary>Size sanity cap in units (RSK-002, PLT-017); 0 = automatic from the symbol profile, 3,000,000 units or 1,000 ounces of gold (EXE-030, PLT-018, spec 7.1).</summary>
        [Parameter("MaxVolumeUnits", DefaultValue = 0.0, Group = "Risk")]
        public double MaxVolumeUnits { get; set; }

        /// <summary>Ladder sizing on; when off (the default from spec v0.14, PLT-018) the fixed RiskPercent is used (RSK-013).</summary>
        [Parameter("LadderEnabled", DefaultValue = false, Group = "Risk")]
        public bool LadderEnabled { get; set; }

        /// <summary>Risk of one ladder deal in percent, before growth (RSK-013).</summary>
        [Parameter("LadderDealRiskPercent", DefaultValue = 0.30, Group = "Risk")]
        public double LadderDealRiskPercent { get; set; }

        /// <summary>Growth of a ladder deal per consecutive win (RSK-013).</summary>
        [Parameter("LadderGrowth", DefaultValue = 1.5924, Group = "Risk")]
        public double LadderGrowth { get; set; }

        /// <summary>Weight of the first of the four parts of a ladder deal (RSK-013).</summary>
        [Parameter("LadderFirstWeight", DefaultValue = 0.2962, Group = "Risk")]
        public double LadderFirstWeight { get; set; }

        /// <summary>Largest ladder risk of one entry in percent (RSK-013).</summary>
        [Parameter("MaxRiskPercentPerTrade", DefaultValue = 0.5, Group = "Risk")]
        public double MaxRiskPercentPerTrade { get; set; }

        /// <summary>How the risk of a new trade is set: Ladder or Recovery (RSK-016, RSK-018, PLT-017); read by the core's SizingPlanText.</summary>
        [Parameter("SizingPlan", DefaultValue = "Ladder", Group = "Risk")]
        public string SizingPlan { get; set; } = "Ladder";

        /// <summary>Recovery plan: what the eight base amounts of a losing run lose together, in percent of InitialCapital (RSK-016, PLT-017).</summary>
        [Parameter("RecoveryRunLossPercent", DefaultValue = 1.0, Group = "Risk")]
        public double RecoveryRunLossPercent { get; set; }

        /// <summary>Recovery plan: largest risk of one trade in percent of balance (RSK-016, PLT-017).</summary>
        [Parameter("RecoveryMaxRiskPercent", DefaultValue = 1.28, Group = "Risk")]
        public double RecoveryMaxRiskPercent { get; set; }

        /// <summary>The per-trade sanity cap on or off (RSK-002, PLT-017). Default off (owner decision S of 2026-11-24).</summary>
        [Parameter("SanityCapEnabled", DefaultValue = false, Group = "Risk")]
        public bool SanityCapEnabled { get; set; }

        /// <summary>The per-trade sanity cap in percent of balance, when on (RSK-002, PLT-017).</summary>
        [Parameter("SanityCapPercent", DefaultValue = 0.55, Group = "Risk")]
        public double SanityCapPercent { get; set; }

        /// <summary>The total open-risk cap on or off (RSK-004, PLT-017). Default off (owner decision W of 2026-11-24).</summary>
        [Parameter("OpenRiskCapEnabled", DefaultValue = false, Group = "Risk")]
        public bool OpenRiskCapEnabled { get; set; }

        /// <summary>The same-direction currency cap on or off (RSK-005, PLT-017). Default off (owner decision W of 2026-11-24).</summary>
        [Parameter("CurrencyCapEnabled", DefaultValue = false, Group = "Risk")]
        public bool CurrencyCapEnabled { get; set; }

        /// <summary>News filter on or off (NWS-001).</summary>
        [Parameter("NewsEnabled", DefaultValue = true, Group = "News")]
        public bool NewsEnabled { get; set; }

        /// <summary>Minutes before an event (NWS-001).</summary>
        [Parameter("NewsBeforeMin", DefaultValue = 30, Group = "News")]
        public int NewsBeforeMin { get; set; }

        /// <summary>Minutes after an event (NWS-001).</summary>
        [Parameter("NewsAfterMin", DefaultValue = 30, Group = "News")]
        public int NewsAfterMin { get; set; }

        /// <summary>Behaviour without a calendar. Only value: Block (NWS-008).</summary>
        [Parameter("NewsFailMode", DefaultValue = "Block", Group = "News")]
        public string NewsFailMode { get; set; } = "Block";

        /// <summary>Feeds used. Only value: ForexFactory (NWS-004).</summary>
        [Parameter("NewsSources", DefaultValue = "ForexFactory", Group = "News")]
        public string NewsSources { get; set; } = "ForexFactory";

        /// <summary>Longest time without a successful refresh, hours (NWS-007).</summary>
        [Parameter("NewsMaxCacheHours", DefaultValue = 48, Group = "News")]
        public int NewsMaxCacheHours { get; set; }

        /// <summary>Feed refresh interval, hours (NWS-006).</summary>
        [Parameter("NewsRefreshHours", DefaultValue = 4, Group = "News")]
        public int NewsRefreshHours { get; set; }

        /// <summary>Manual blackouts, "yyyy-MM-dd HH:mm CUR" separated by ';' (NWS-011).</summary>
        [Parameter("ExtraBlackouts", DefaultValue = "", Group = "News")]
        public string ExtraBlackouts { get; set; } = string.Empty;

        /// <summary>Address of the Forex Factory weekly JSON export (NWS-003). Required live with NewsEnabled.</summary>
        [Parameter("NewsFeedUrl", DefaultValue = "", Group = "News")]
        public string NewsFeedUrl { get; set; } = string.Empty;

        /// <summary>External heartbeat monitor address (OPS-005). A secret.</summary>
        [Parameter("HeartbeatUrl", DefaultValue = "", Group = "Operations")]
        public string HeartbeatUrl { get; set; } = string.Empty;

        /// <summary>Telegram bot token (OPS-004). A secret.</summary>
        [Parameter("TelegramBotToken", DefaultValue = "", Group = "Operations")]
        public string TelegramBotToken { get; set; } = string.Empty;

        /// <summary>Telegram chat id (OPS-004). Treated as a secret.</summary>
        [Parameter("TelegramChatId", DefaultValue = "", Group = "Operations")]
        public string TelegramChatId { get; set; } = string.Empty;

        /// <summary>Profit target of the current FTMO phase in percent (OPS-003): 10 Challenge, 5 Verification.</summary>
        [Parameter("ProfitTargetPercent", DefaultValue = 10.0, Group = "Operations")]
        public double ProfitTargetPercent { get; set; }

        /// <summary>Market holidays as MM-dd separated by commas (REC-003).</summary>
        [Parameter("MarketHolidays", DefaultValue = "12-25,01-01", Group = "Operations")]
        public string MarketHolidays { get; set; } = "12-25,01-01";

        /// <summary>Draw zones and gaps on the chart (PLT-005).</summary>
        [Parameter("DrawObjects", DefaultValue = false, Group = "Operations")]
        public bool DrawObjects { get; set; }

        // ------------------------------------------------------------------ lifecycle and events (PLT-002)

        /// <summary>
        /// Start (spec 9.3): checks the chart timeframe, builds and validates the parameters, creates
        /// the adapters and the core, runs the start checks of RSK-014, subscribes to events, starts
        /// the one-minute timer and runs the core's start recovery. Any failure before that stops the
        /// cBot with a message.
        /// </summary>
        protected override void OnStart()
        {
            // The masker exists before anything is printed, so no message can show a secret (OPS-007).
            _log = new AdapterLog(this, new SecretMasker(new[] { TelegramBotToken, TelegramChatId, HeartbeatUrl }));

            // Algo.TimeFrame and TimeFrame.Minute15: https://help.ctrader.com/ctrader-algo/references/General/Algo/
            if (TimeFrame != TimeFrame.Minute15)
            {
                Refuse("the cBot runs on an M15 chart only; this chart is " + TimeFrame + ".");
                return;
            }

            // Algo.RunningMode: https://help.ctrader.com/ctrader-algo/references/Application/RunningMode/
            bool isBacktest = RunningMode != RunningMode.RealTime;
            BotParameters parameters;
            try
            {
                parameters = BuildParameters(isBacktest);
                parameters.Validate(isBacktest);

                // PLT-012: the breakout values are checked in every mode by the core's own method; they are
                // passed to the core in Breakout mode only. PLT-016 (answer of 2026-11-23): outside Breakout mode an
                // all-day session (SessionEndUtc 24:00) is checked with the last instant before 24:00 as window end.
                ChochText.ForEveryModeCheck(BuildBreakout(parameters.Execution), parameters.Mode).Validate();
                _core = Create(parameters, isBacktest);
            }
            catch (ArgumentException exception)
            {
                Refuse("invalid parameter: " + ParameterMessage(exception));
                return;
            }

            // RSK-014: start checks before the first recovery; a refusal stops the cBot before any event is subscribed.
            string? refusal = StartChecks.Check(parameters, _broker!, isBacktest);
            if (refusal != null)
            {
                _core = null;
                Refuse(refusal);
                return;
            }

            // EXE-031, PLT-018: one line when the spread at start is above the effective MaxSpreadPips; not a refusal.
            if (parameters.SymbolProfile?.SpreadNote != null)
            {
                _log!.Info(parameters.SymbolProfile.SpreadNote);
            }

            // PLT-014: one start line when the news filter is off for this backtest although NewsEnabled is true.
            string? newsOff = BacktestDefaults.NewsFilterOffReason(NewsEnabled, parameters.News.NewsEnabled);
            if (newsOff != null)
            {
                _log!.Info(newsOff);
            }

            Subscribe();
            _core.OnStart(_bars!);
            LogWarmup(parameters);
            _bars!.SkipToLatest();
        }

        // PLT-016: whether the history loaded at start holds the warm-up the core needs (Choch: 2087 M15 bars
        // with the defaults). Diagnostics only: recovery reads the bars itself and stays Recovering without them.
        private void LogWarmup(BotParameters parameters)
        {
            try
            {
                var warmup = WarmupCalculator.For(parameters.Signal, parameters.Breakout, parameters.SessionRange, parameters.Choch, parameters.Overnight);
                CTraderBars bars = _bars!;
                string Part(CoreTimeFrame timeFrame) =>
                    timeFrame + " " + bars.ClosedBars(timeFrame, int.MaxValue).Count.ToString(CultureInfo.InvariantCulture)
                    + " of " + warmup[timeFrame].ToString(CultureInfo.InvariantCulture);
                _log!.Info("closed bars loaded at start against the warm-up: " + Part(CoreTimeFrame.D1) + ", " + Part(CoreTimeFrame.H4) + ", " + Part(CoreTimeFrame.M15));
            }
            catch (Exception exception)
            {
                _log!.Error("warm-up description", exception);
            }
        }

        /// <summary>Timer, once a minute (spec 9.3): passes newly closed bars, then the timer event.</summary>
        protected override void OnTimer()
        {
            if (_core == null || _bars == null)
            {
                return;
            }

            _bars.PumpOnTimer(_core);
            _core.OnTimer();
            _painter?.Paint(_core);
        }

        /// <summary>An unhandled exception reported by the platform is forwarded to the core (REC-011).</summary>
        /// <param name="exception">The exception.</param>
        protected override void OnException(Exception exception)
        {
            if (_core != null)
            {
                _core.OnException(exception);
                return;
            }

            _log?.Error("start", exception);
            Stop();
        }

        /// <summary>
        /// A failed trade operation reported by the platform. The core already received the result
        /// of its synchronous request (EXE-009), so the error is only logged.
        /// </summary>
        /// <param name="error">The error.</param>
        protected override void OnError(Error error)
        {
            // Robot.OnError(Error); Error.Code: https://help.ctrader.com/ctrader-algo/references/Trading/Error/
            _log?.Info("trade operation error reported by the platform: " + error.Code);
        }

        /// <summary>Stop: nothing required depends on it (PLT-009, spec 9.4).</summary>
        protected override void OnStop()
        {
            _log?.Info("stopped");
        }

        private void Subscribe()
        {
            BotCore core = _core!;
            CTraderBroker broker = _broker!;
            CTraderBars bars = _bars!;
            ChartPainter painter = _painter!;

            // IServer.Connected / Disconnected (Action): https://help.ctrader.com/ctrader-algo/references/Application/IServer/
            Server.Connected += () =>
            {
                core.OnConnected(bars);
                bars.SkipToLatest();
            };
            Server.Disconnected += () => core.OnDisconnected();

            // Positions.Opened / Closed / Modified: https://help.ctrader.com/ctrader-algo/references/Trading/Positions/Positions/
            Positions.Opened += _ => core.OnTradeEvent();
            Positions.Closed += args =>
            {
                // The close reason is journal data for the trade journal and the backtest trade log (SIM-001).
                // It is recorded first so the journal sees it; the finally block makes sure the core still
                // handles the close if recording fails, and the failure is not swallowed.
                try
                {
                    broker.RecordClose(args);
                }
                finally
                {
                    core.OnTradeEvent();
                }
            };
            Positions.Modified += _ => core.OnTradeEvent();

            // PendingOrders.Created / Filled / Cancelled / Modified: https://help.ctrader.com/ctrader-algo/references/Trading/Orders/PendingOrders/
            PendingOrders.Created += _ => core.OnTradeEvent();
            PendingOrders.Filled += _ => core.OnTradeEvent();
            PendingOrders.Cancelled += _ => core.OnTradeEvent();
            PendingOrders.Modified += _ => core.OnTradeEvent();

            // Bar-closed events of the D1, H4 and M15 series.
            // Drawing (PLT-005) follows the core's work and only reads its snapshots.
            bars.OnAnyBarClosed(() =>
            {
                bars.PumpOnBarEvent(core);
                painter.Paint(core);
            });

            // Timer.Start(TimeSpan): https://help.ctrader.com/ctrader-algo/references/Timer/Timer/
            Timer.Start(TimeSpan.FromMinutes(1.0));
        }

        private BotCore Create(BotParameters parameters, bool isBacktest)
        {
            AdapterLog log = _log!;

            // MarketData.GetBars(TimeFrame): https://help.ctrader.com/ctrader-algo/references/MarketData/MarketData/
            var broker = new CTraderBroker(this, log);
            _broker = broker;
            _bars = new CTraderBars(
                MarketData.GetBars(CTraderBars.ToApi(CoreTimeFrame.D1)),
                MarketData.GetBars(CTraderBars.ToApi(CoreTimeFrame.H4)),
                MarketData.GetBars(CTraderBars.ToApi(CoreTimeFrame.M15)),
                () => broker.ServerTimeUtc,
                log);

            // Algo.LocalStorage and Algo.Http: https://help.ctrader.com/ctrader-algo/references/General/Algo/
            var store = new LocalStorageStore(LocalStorage);
            var http = new HttpTransport(Http, log);
            _painter = CreatePainter(broker, isBacktest, log);

            log.Info("version " + BuildInfo.Version + ", symbol " + SymbolName + ", backtest " + (isBacktest ? "yes" : "no") + ", mode " + parameters.Mode + ModelText(parameters));
            LogCosts(broker, log);
            return new BotCore(parameters, broker, store, http, log, isBacktest);
        }

        // PLT-016: the entry model exists in ZoneFvg mode only, so the other modes name no model.
        private static string ModelText(BotParameters parameters)
        {
            if (parameters.Mode != CoreEntryMode.ZoneFvg)
            {
                return string.Empty;
            }

            return ", model " + (parameters.Choch is null ? ChochText.Fvg : ChochText.Choch);
        }

        private ChartPainter CreatePainter(CTraderBroker broker, bool isBacktest, AdapterLog log)
        {
            // PLT-005, spec 9.4: drawings only where a chart is shown, never in a silent backtest or an optimisation.
            // RunningMode.SilentBacktesting / Optimization: https://help.ctrader.com/ctrader-algo/references/Application/RunningMode/
            bool shown = !isBacktest || RunningMode == RunningMode.VisualBacktesting;
            bool enabled = DrawObjects && shown;
            ChartArea? chart = null;
            if (enabled)
            {
                try
                {
                    // Algo.Chart ("the chart where the cBot is launched"; Chart implements ChartArea):
                    // https://help.ctrader.com/ctrader-algo/references/General/Algo/ and https://help.ctrader.com/ctrader-algo/references/Chart/Chart/
                    chart = Chart;
                }
                catch (Exception exception)
                {
                    log.Error("chart access (nothing is drawn)", exception);
                    enabled = false;
                }
            }

            if (DrawObjects && !shown)
            {
                log.Info("DrawObjects is true, but this run shows no chart; nothing is drawn.");
            }

            return new ChartPainter(chart, enabled, () => broker.ServerTimeUtc, log);
        }

        private void LogCosts(CTraderBroker broker, AdapterLog log)
        {
            try
            {
                log.Info(broker.DescribeCosts(SymbolName));
            }
            catch (Exception exception)
            {
                // Diagnostics only: the core reads the same data itself and fails closed if it is bad.
                log.Error("symbol cost description", exception);
            }
        }

        private BotParameters BuildParameters(bool isBacktest)
        {
            ParameterText.Fixed(nameof(DayResetZone), DayResetZone, "Europe/Prague");
            CoreEntryMode mode = ParameterText.EnumName<CoreEntryMode>(nameof(EntryMode), EntryMode);

            // EXE-030, EXE-031, PLT-018: the four symbol-dependent values are resolved from the symbol profile before any
            // of them is checked or used; 0 means automatic. The cBot's own properties then hold the effective values,
            // so BuildExecution, ChochText.Build and BuildRisk read them as before. A refusal is an ArgumentException.
            // Symbol.Name, BaseAsset, QuoteAsset, PipSize, Spread: https://help.ctrader.com/ctrader-algo/references/MarketData/Symbols/Symbol/
            SymbolProfileResult profile = SymbolProfile.Resolve(SymbolName, Symbol.BaseAsset.Name, Symbol.QuoteAsset.Name, Symbol.PipSize, Symbol.Spread, MaxSpreadPips, MinStopPips, ChochMinStopPips, MaxVolumeUnits);
            MaxSpreadPips = profile.MaxSpreadPips;
            MinStopPips = profile.MinStopPips;
            ChochMinStopPips = profile.ChochMinStopPips;
            MaxVolumeUnits = profile.MaxVolumeUnits;

            // The signal texts are parsed before the time texts, as before PLT-012, so the order of the refusals is unchanged.
            SignalParameters signal = BuildSignal();
            ExecutionParameters execution = BuildExecution();

            // PLT-015: the six range values are parsed and checked in every mode, with the names cTrader
            // shows; they are passed to the core in SessionRange mode only.
            // SIG-042: RangeTrendFastEma goes with them; that it is below BiasEmaPeriod is checked by BotParameters.Validate.
            SessionRangeParameters sessionRange = SessionRangeText.Build(RangeStartNy, RangeEndNy, RangeLastEntryNy, RangeExitNy, RangeMinWidthAtr, RangeStopAtr, RangeTrendFastEma);

            // PLT-021: the seven Overnight values are parsed and checked in every mode, with the names cTrader
            // shows; they are passed to the core in Overnight mode only.
            OvernightParameters overnight = OvernightText.Build(NightEntryNy, NightLatestNy, NightExitNy, NightEntryBufferAtr, NightStopAtr, NightTargetR, NightOrderMinutes);

            // PLT-016: EntryModel and the seven Choch values are checked in every mode, with the names cTrader shows;
            // Choch with another EntryMode is refused. The values are passed to the core with EntryModel Choch only.
            ChochParameters? choch = ChochText.Build(EntryModel, mode, RequireSweep, ChochWindowBars, ChochUseBias, ChochStopAtDistal, ChochTargetBufferAtr, ChochMinStopPips, MinRewardRisk);
            string newsHistory = isBacktest ? ReadNewsHistory() : string.Empty;
            return new BotParameters
            {
                Symbol = SymbolName,
                Signal = signal,
                Execution = execution,

                // PLT-012, PLT-015: the mode selects whether the breakout or the session range parameters are
                // passed, never both; the core checks their values. A mode the adapter does not know is refused,
                // never run as ZoneFvg.
                Breakout = mode switch
                {
                    CoreEntryMode.Breakout => BuildBreakout(execution),
                    CoreEntryMode.ZoneFvg => null,
                    CoreEntryMode.SessionRange => null,
                    CoreEntryMode.Overnight => null,
                    _ => throw new ArgumentException(nameof(EntryMode) + " must be one of: ZoneFvg, Breakout, SessionRange, Overnight.", nameof(EntryMode)),
                },
                SessionRange = mode == CoreEntryMode.SessionRange ? sessionRange : null,
                Overnight = mode == CoreEntryMode.Overnight ? overnight : null,
                Choch = choch,

                // EXE-032: the profile and the effective values, for the start line and the parameter hash.
                SymbolProfile = profile,
                Risk = BuildRisk(isBacktest),
                News = BuildNews(isBacktest, newsHistory),

                // In backtests no alert or heartbeat is sent (BotParameters.Ops: empty secrets in backtests).
                Ops = isBacktest ? new OpsParameters { ProfitTargetPercent = ProfitTargetPercent } : BuildOps(),
                NewsFeedUrl = ParameterText.Text(NewsFeedUrl),
                MarketHolidays = ParameterText.Text(MarketHolidays),
                EmbeddedNewsHistory = newsHistory,

                // One machine-readable line per closed trade for the backtest trade log (SIM-001,
                // docs/backtest-howto.md). Journal only.
                WriteTradeLog = isBacktest,

                // REC-014 (owner decision C of 2026-11-17): in a backtest halts and the daily budget are
                // only journaled as would-halt or would-block. Never live: no cBot parameter, and the core
                // refuses the flag outside a backtest.
                BacktestHaltsLogOnly = isBacktest,
            };
        }

        private SignalParameters BuildSignal()
        {
            return new SignalParameters
            {
                BiasTimeFrame = ParameterText.EnumName<CoreTimeFrame>(nameof(BiasTimeFrame), BiasTimeFrame),
                ZoneTimeFrame = ParameterText.EnumName<CoreTimeFrame>(nameof(ZoneTimeFrame), ZoneTimeFrame),
                BiasEmaPeriod = BiasEmaPeriod,
                AtrPeriod = AtrPeriod,
                SwingStrength = SwingStrength,
                ImpulseAtrMult = ImpulseAtrMult,
                MaxImpulseBars = MaxImpulseBars,
                MaxBaseBars = MaxBaseBars,
                RequireBos = RequireBos,
                MinZoneAtr = MinZoneAtr,
                MaxZoneAtr = MaxZoneAtr,
                MaxZoneAgeBars = MaxZoneAgeBars,
                ConfirmWindowBars = ConfirmWindowBars,
                MinFvgAtr = MinFvgAtr,
                EntryLevelPct = EntryLevelPct,
                StopBufferAtr = StopBufferAtr,
                RewardRisk = RewardRisk,
            };
        }

        private BreakoutParameters BuildBreakout(ExecutionParameters execution)
        {
            // PLT-012, REC-017: the breakout window is the session window, as parsed for the execution parameters.
            return new BreakoutParameters
            {
                EntryBufferAtr = BreakoutEntryBufferAtr,
                StopAtrMult = BreakoutStopAtr,
                WindowStartUtc = execution.SessionStartUtc,
                WindowEndUtc = execution.SessionEndUtc,
                TrendSlowEmaPeriod = TrendSlowEmaPeriod,
                LastConfirmUtc = ParameterText.TimeOfDay(nameof(BreakoutLastConfirmUtc), BreakoutLastConfirmUtc),
            };
        }

        private ExecutionParameters BuildExecution()
        {
            return new ExecutionParameters
            {
                // PLT-016, EXE-029: SessionEndUtc also accepts 24:00; SessionStartUtc does not.
                SessionStartUtc = ChochText.SessionStartUtc(SessionStartUtc),
                SessionEndUtc = ChochText.SessionEndUtc(SessionEndUtc),
                FridayNoEntryUtc = ParameterText.TimeOfDay(nameof(FridayNoEntryUtc), FridayNoEntryUtc),
                FridayCloseUtc = ParameterText.TimeOfDay(nameof(FridayCloseUtc), FridayCloseUtc),
                MaxSpreadPips = MaxSpreadPips,
                MinStopPips = MinStopPips,
                MaxStopAtr = MaxStopAtr,
                CostToRiskMax = CostToRiskMax,
                MinFvgSpreadMult = MinFvgSpreadMult,
                MinRoomR = MinRoomR,
                EntryExpiryBars = EntryExpiryBars,
            };
        }

        private RiskParameters BuildRisk(bool isBacktest)
        {
            return new RiskParameters
            {
                RiskPercent = RiskPercent,

                // RSK-019, PLT-020: the drawdown brake; the core's validation refuses a value naming the parameter.
                DrawdownBrakePercent = DrawdownBrakePercent,
                DrawdownBrakeFactor = DrawdownBrakeFactor,
                MaxOpenRiskPercent = MaxOpenRiskPercent,
                MaxPositionsPerCurrency = MaxPositionsPerCurrency,
                DailyLossLimitPercent = DailyLossLimitPercent,

                // RSK-020: the monthly loss limit; the core's validation refuses a value naming the parameter.
                MonthlyLossLimitPercent = MonthlyLossLimitPercent,
                MaxDrawdownPercent = MaxDrawdownPercent,
                InitialCapital = InitialCapital,
                FtmoDailyLossPercent = FtmoDailyLossPercent,
                FtmoMaxLossPercent = FtmoMaxLossPercent,
                FtmoGuardBufferPercent = FtmoGuardBufferPercent,
                // PLT-014: an empty text is "not configured"; in a backtest the core's default is the CE(S)T date of
                // the server time at start, live it is refused as before. Any other text is parsed as before.
                RiskEpochDate = BacktestDefaults.RiskEpoch(ParameterText.OptionalDate(nameof(RiskEpochDate), RiskEpochDate), isBacktest, ServerTimeUtc()),
                MaxEntriesPerSymbolPerDay = MaxEntriesPerSymbolPerDay,
                MaxEntriesPerDay = MaxEntriesPerDay,
                MaxRequestsPerHour = MaxRequestsPerHour,
                MaxRequestsPerDay = MaxRequestsPerDay,
                MaxErrorsPerHour = MaxErrorsPerHour,
                MaxVolumeUnits = MaxVolumeUnits,
                LadderEnabled = LadderEnabled,
                LadderDealRiskPercent = LadderDealRiskPercent,
                LadderGrowth = LadderGrowth,
                LadderFirstWeight = LadderFirstWeight,
                MaxRiskPercentPerTrade = MaxRiskPercentPerTrade,

                // PLT-017: the text of SizingPlan is read in the core; a refusal names the parameter.
                SizingPlan = SizingPlanText.Parse(SizingPlan),
                RecoveryRunLossPercent = RecoveryRunLossPercent,
                RecoveryMaxRiskPercent = RecoveryMaxRiskPercent,
                SanityCapEnabled = SanityCapEnabled,
                SanityCapPercent = SanityCapPercent,
                OpenRiskCapEnabled = OpenRiskCapEnabled,
                CurrencyCapEnabled = CurrencyCapEnabled,
            };
        }

        private NewsParameters BuildNews(bool isBacktest, string newsHistory)
        {
            return new NewsParameters
            {
                // PLT-014: in a backtest whose embedded history has no events the filter is off; otherwise NewsEnabled.
                NewsEnabled = BacktestDefaults.NewsFilterOn(NewsEnabled, isBacktest, EmbeddedHistoryProvider.CountEvents(newsHistory)),
                NewsBeforeMin = NewsBeforeMin,
                NewsAfterMin = NewsAfterMin,
                NewsFailMode = ParameterText.EnumName<CoreNewsFailMode>(nameof(NewsFailMode), NewsFailMode),
                NewsSources = ParameterText.Text(NewsSources),
                NewsMaxCacheHours = NewsMaxCacheHours,
                NewsRefreshHours = NewsRefreshHours,
                ExtraBlackouts = ParameterText.Text(ExtraBlackouts),
            };
        }

        private OpsParameters BuildOps()
        {
            return new OpsParameters
            {
                HeartbeatUrl = ParameterText.Text(HeartbeatUrl),
                TelegramBotToken = ParameterText.Text(TelegramBotToken),
                TelegramChatId = ParameterText.Text(TelegramChatId),
                ProfitTargetPercent = ProfitTargetPercent,
            };
        }

        private DateTime ServerTimeUtc()
        {
            // IServer.TimeInUtc, as CTraderBroker.ServerTimeUtc reads it: https://help.ctrader.com/ctrader-algo/references/Application/IServer/
            return DateTime.SpecifyKind(Server.TimeInUtc, DateTimeKind.Utc);
        }

        private static string ReadNewsHistory()
        {
            // The history file is compiled into the cBot as an embedded resource (spec 6.4, NWS-010).
            // A missing resource gives an empty text, which the core rejects: the calendar is then
            // unavailable and no entry is placed (fail closed).
            using Stream? stream = typeof(ZoneFvgBot).Assembly.GetManifestResourceStream(NewsHistoryResource);
            if (stream == null)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static string ParameterMessage(ArgumentException exception)
        {
            // PLT-012: a refusal of a breakout value starts with the name of the cBot parameter that sets it;
            // every other message is unchanged.
            string? name = exception.ParamName switch
            {
                nameof(BreakoutParameters.EntryBufferAtr) => nameof(BreakoutEntryBufferAtr),
                nameof(BreakoutParameters.StopAtrMult) => nameof(BreakoutStopAtr),
                nameof(BreakoutParameters.WindowStartUtc) => nameof(SessionStartUtc),
                nameof(BreakoutParameters.WindowEndUtc) => nameof(SessionEndUtc),
                nameof(BreakoutParameters.TrendSlowEmaPeriod) => nameof(TrendSlowEmaPeriod),
                nameof(BreakoutParameters.LastConfirmUtc) => nameof(BreakoutLastConfirmUtc),
                _ => null,
            };
            return name == null ? exception.Message : name + ": " + exception.Message;
        }

        private void Refuse(string reason)
        {
            // Robot.Stop(): https://help.ctrader.com/ctrader-algo/references/General/Robot
            _log!.Info("refused to start: " + reason);
            Stop();
        }
    }
}
