using System;
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
        private AdapterLog? _log;

        // ------------------------------------------------------------------ parameters (spec section 7, PLT-006)
        // Parameter attribute (Name, DefaultValue, Group): https://help.ctrader.com/ctrader-algo/references/Attributes/ParameterAttribute/
        // Parameter types: https://help.ctrader.com/ctrader-algo/guides/parameter-types

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

        /// <summary>Smallest allowed stop in pips (EXE-003).</summary>
        [Parameter("MinStopPips", DefaultValue = 12.0, Group = "Execution")]
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

        /// <summary>Start of the trading window, UTC, HH:mm (EXE-002).</summary>
        [Parameter("SessionStartUtc", DefaultValue = "07:00", Group = "Execution")]
        public string SessionStartUtc { get; set; } = "07:00";

        /// <summary>End of the trading window, UTC, HH:mm (EXE-002).</summary>
        [Parameter("SessionEndUtc", DefaultValue = "17:00", Group = "Execution")]
        public string SessionEndUtc { get; set; } = "17:00";

        /// <summary>Last entry time on Friday, UTC, HH:mm (EXE-002).</summary>
        [Parameter("FridayNoEntryUtc", DefaultValue = "12:00", Group = "Execution")]
        public string FridayNoEntryUtc { get; set; } = "12:00";

        /// <summary>Weekend flat time on Friday, UTC, HH:mm (EXE-013).</summary>
        [Parameter("FridayCloseUtc", DefaultValue = "20:00", Group = "Execution")]
        public string FridayCloseUtc { get; set; } = "20:00";

        /// <summary>Spread filter in pips (EXE-002).</summary>
        [Parameter("MaxSpreadPips", DefaultValue = 1.5, Group = "Execution")]
        public double MaxSpreadPips { get; set; }

        /// <summary>Risk per trade in percent (RSK-001).</summary>
        [Parameter("RiskPercent", DefaultValue = 0.5, Group = "Risk")]
        public double RiskPercent { get; set; }

        /// <summary>Total open risk in percent (RSK-004).</summary>
        [Parameter("MaxOpenRiskPercent", DefaultValue = 1.5, Group = "Risk")]
        public double MaxOpenRiskPercent { get; set; }

        /// <summary>Correlation cap per currency (RSK-005).</summary>
        [Parameter("MaxPositionsPerCurrency", DefaultValue = 2, Group = "Risk")]
        public int MaxPositionsPerCurrency { get; set; }

        /// <summary>Daily stop in percent (RSK-006).</summary>
        [Parameter("DailyLossLimitPercent", DefaultValue = 1.5, Group = "Risk")]
        public double DailyLossLimitPercent { get; set; }

        /// <summary>Kill switch drawdown in percent (RSK-008).</summary>
        [Parameter("MaxDrawdownPercent", DefaultValue = 8.0, Group = "Risk")]
        public double MaxDrawdownPercent { get; set; }

        /// <summary>Base for the FTMO percentages (RSK-004).</summary>
        [Parameter("InitialCapital", DefaultValue = 100000.0, Group = "Risk")]
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

        /// <summary>Size sanity cap in units (RSK-002).</summary>
        [Parameter("MaxVolumeUnits", DefaultValue = 500000.0, Group = "Risk")]
        public double MaxVolumeUnits { get; set; }

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
        /// the adapters and the core, subscribes to events, starts the one-minute timer and runs the
        /// core's start recovery. Any failure before the core exists stops the cBot with a message.
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
                _core = Create(parameters, isBacktest);
            }
            catch (ArgumentException exception)
            {
                Refuse("invalid parameter: " + exception.Message);
                return;
            }

            Subscribe();
            _core.OnStart(_bars!);
            _bars!.SkipToLatest();
        }

        /// <summary>Timer, once a minute (spec 9.3): passes newly closed bars, then the timer event.</summary>
        protected override void OnTimer()
        {
            if (_core == null || _bars == null)
            {
                return;
            }

            _bars.Pump(_core);
            _core.OnTimer();
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
            CTraderBars bars = _bars!;

            // IServer.Connected / Disconnected (Action): https://help.ctrader.com/ctrader-algo/references/Application/IServer/
            Server.Connected += () =>
            {
                core.OnConnected(bars);
                bars.SkipToLatest();
            };
            Server.Disconnected += () => core.OnDisconnected();

            // Positions.Opened / Closed / Modified: https://help.ctrader.com/ctrader-algo/references/Trading/Positions/Positions/
            Positions.Opened += _ => core.OnTradeEvent();
            Positions.Closed += _ => core.OnTradeEvent();
            Positions.Modified += _ => core.OnTradeEvent();

            // PendingOrders.Created / Filled / Cancelled / Modified: https://help.ctrader.com/ctrader-algo/references/Trading/Orders/PendingOrders/
            PendingOrders.Created += _ => core.OnTradeEvent();
            PendingOrders.Filled += _ => core.OnTradeEvent();
            PendingOrders.Cancelled += _ => core.OnTradeEvent();
            PendingOrders.Modified += _ => core.OnTradeEvent();

            // Bar-closed events of the D1, H4 and M15 series.
            bars.OnAnyBarClosed(() => bars.Pump(core));

            // Timer.Start(TimeSpan): https://help.ctrader.com/ctrader-algo/references/Timer/Timer/
            Timer.Start(TimeSpan.FromMinutes(1.0));
        }

        private BotCore Create(BotParameters parameters, bool isBacktest)
        {
            AdapterLog log = _log!;

            // MarketData.GetBars(TimeFrame): https://help.ctrader.com/ctrader-algo/references/MarketData/MarketData/
            var broker = new CTraderBroker(this, log);
            _bars = new CTraderBars(
                MarketData.GetBars(CTraderBars.ToApi(CoreTimeFrame.D1)),
                MarketData.GetBars(CTraderBars.ToApi(CoreTimeFrame.H4)),
                MarketData.GetBars(CTraderBars.ToApi(CoreTimeFrame.M15)),
                () => broker.ServerTimeUtc,
                log);

            // Algo.LocalStorage and Algo.Http: https://help.ctrader.com/ctrader-algo/references/General/Algo/
            var store = new LocalStorageStore(LocalStorage);
            var http = new HttpTransport(Http, log);
            if (DrawObjects)
            {
                // PLT-005: ChartPainter is ready, but the core does not expose zones and gaps yet (see ChartPainter).
                log.Info("DrawObjects is true, but drawing is not connected yet; nothing is drawn.");
            }

            log.Info("version " + BuildInfo.Version + ", symbol " + SymbolName + ", backtest " + (isBacktest ? "yes" : "no"));
            LogCosts(broker, log);
            return new BotCore(parameters, broker, store, http, log, isBacktest);
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
            return new BotParameters
            {
                Symbol = SymbolName,
                Signal = BuildSignal(),
                Execution = BuildExecution(),
                Risk = BuildRisk(),
                News = BuildNews(),

                // In backtests no alert or heartbeat is sent (BotParameters.Ops: empty secrets in backtests).
                Ops = isBacktest ? new OpsParameters { ProfitTargetPercent = ProfitTargetPercent } : BuildOps(),
                NewsFeedUrl = ParameterText.Text(NewsFeedUrl),
                MarketHolidays = ParameterText.Text(MarketHolidays),
                EmbeddedNewsHistory = isBacktest ? ReadNewsHistory() : string.Empty,
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

        private ExecutionParameters BuildExecution()
        {
            return new ExecutionParameters
            {
                SessionStartUtc = ParameterText.TimeOfDay(nameof(SessionStartUtc), SessionStartUtc),
                SessionEndUtc = ParameterText.TimeOfDay(nameof(SessionEndUtc), SessionEndUtc),
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

        private RiskParameters BuildRisk()
        {
            return new RiskParameters
            {
                RiskPercent = RiskPercent,
                MaxOpenRiskPercent = MaxOpenRiskPercent,
                MaxPositionsPerCurrency = MaxPositionsPerCurrency,
                DailyLossLimitPercent = DailyLossLimitPercent,
                MaxDrawdownPercent = MaxDrawdownPercent,
                InitialCapital = InitialCapital,
                FtmoDailyLossPercent = FtmoDailyLossPercent,
                FtmoMaxLossPercent = FtmoMaxLossPercent,
                FtmoGuardBufferPercent = FtmoGuardBufferPercent,
                RiskEpochDate = ParameterText.Date(nameof(RiskEpochDate), RiskEpochDate),
                MaxEntriesPerSymbolPerDay = MaxEntriesPerSymbolPerDay,
                MaxEntriesPerDay = MaxEntriesPerDay,
                MaxRequestsPerHour = MaxRequestsPerHour,
                MaxRequestsPerDay = MaxRequestsPerDay,
                MaxErrorsPerHour = MaxErrorsPerHour,
                MaxVolumeUnits = MaxVolumeUnits,
            };
        }

        private NewsParameters BuildNews()
        {
            return new NewsParameters
            {
                NewsEnabled = NewsEnabled,
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

        private void Refuse(string reason)
        {
            // Robot.Stop(): https://help.ctrader.com/ctrader-algo/references/General/Robot
            _log!.Info("refused to start: " + reason);
            Stop();
        }
    }
}
