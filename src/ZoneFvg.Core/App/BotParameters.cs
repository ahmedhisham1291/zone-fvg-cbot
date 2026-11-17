using System;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Recovery;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.App;

/// <summary>
/// All parameters of one bot instance (spec section 7), grouped by module, plus the symbol and the
/// news sources. Instances are immutable; use a <c>with</c> expression to derive a changed copy.
/// </summary>
public sealed record BotParameters
{
    /// <summary>Symbol of this instance, for example EURUSD (one instance per symbol).</summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>Signal parameters (SIG requirements).</summary>
    public SignalParameters Signal { get; init; } = new SignalParameters();

    /// <summary>Execution parameters (EXE requirements).</summary>
    public ExecutionParameters Execution { get; init; } = new ExecutionParameters();

    /// <summary>Risk parameters (RSK requirements). RiskEpochDate must be set.</summary>
    public RiskParameters Risk { get; init; } = new RiskParameters();

    /// <summary>News parameters, including NewsEnabled (NWS requirements, owner decision of 2026-11-16).</summary>
    public NewsParameters News { get; init; } = new NewsParameters();

    /// <summary>Operations parameters: Telegram, heartbeat and phase profit target (OPS requirements). Empty secrets in backtests.</summary>
    public OpsParameters Ops { get; init; } = new OpsParameters();

    /// <summary>Address of the Forex Factory weekly export. Needed outside backtests while NewsEnabled is true.</summary>
    public string NewsFeedUrl { get; init; } = string.Empty;

    /// <summary>
    /// Market holidays as comma-separated <c>MM-dd</c> days whose whole UTC day counts as closed, so
    /// missing bars on them are not data gaps (REC-003, owner decision of 2026-11-16). Default 25
    /// December and 1 January.
    /// </summary>
    public string MarketHolidays { get; init; } = "12-25,01-01";

    /// <summary>The embedded news history used in backtests (spec 6.4), as text.</summary>
    public string EmbeddedNewsHistory { get; init; } = string.Empty;

    /// <summary>
    /// When true, every closed bot trade is also journaled as one machine-readable
    /// <c>tradelog</c> line for the backtest trade log (SIM-001, <see cref="TradeLogLine"/>).
    /// The adapter sets it in backtests only. Journal only: it changes no order and no decision.
    /// Default false.
    /// </summary>
    public bool WriteTradeLog { get; init; }

    /// <summary>
    /// REC-014 (owner decision C of 2026-11-17), backtests only: when true the kill switch, the
    /// emergency guard, the error limit, the request limit and the daily budget are only written
    /// to the journal as <c>would-halt</c> or <c>would-block</c>, once per cause and CE(S)T day,
    /// and the bot keeps trading. The adapter sets it exactly when the run is a backtest; there is
    /// no cBot parameter. <see cref="Validate"/> refuses it outside a backtest. Default false.
    /// </summary>
    public bool BacktestHaltsLogOnly { get; init; }

    /// <summary>
    /// Checks every value and throws for the first problem: the symbol must start with six letters
    /// and contain no '|'; each parameter group and the market holidays must be valid; outside backtests with NewsEnabled
    /// true the feed address must be set; <see cref="BacktestHaltsLogOnly"/> may be true only in a
    /// backtest (REC-014).
    /// </summary>
    /// <param name="isBacktest">True in a backtest.</param>
    /// <exception cref="ArgumentNullException">A parameter group is null.</exception>
    /// <exception cref="ArgumentException">A value is invalid.</exception>
    public void Validate(bool isBacktest)
    {
        if (BacktestHaltsLogOnly && !isBacktest)
        {
            // REC-014: halts are recorded instead of applied in backtests only, never live.
            throw new ArgumentException("BacktestHaltsLogOnly may be true only in a backtest (REC-014).", nameof(BacktestHaltsLogOnly));
        }

        NewsSymbol.Pair(Symbol);
        if (Symbol.IndexOf('|') >= 0)
        {
            throw new ArgumentException("The symbol must not contain '|'.", nameof(Symbol));
        }

        if (Signal == null || Execution == null || Risk == null || News == null || Ops == null)
        {
            throw new ArgumentNullException(nameof(Signal), "Every parameter group must be set.");
        }

        Signal.Validate();
        Execution.Validate();
        Risk.Validate();
        News.Validate();
        Ops.Validate();
        MonthDay.ParseList(MarketHolidays);
        if (!isBacktest && News.NewsEnabled && string.IsNullOrWhiteSpace(NewsFeedUrl))
        {
            throw new ArgumentException("NewsFeedUrl must be set when the news filter is enabled outside backtests.", nameof(NewsFeedUrl));
        }
    }
}
