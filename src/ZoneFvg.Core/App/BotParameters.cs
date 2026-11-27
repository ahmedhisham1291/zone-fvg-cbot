using System;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Recovery;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Domain.Invariant;

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
    /// REC-017 (spec v0.10 decision E): the breakout parameters. Null (the default) is ZoneFvg mode;
    /// a value is Breakout mode, in which the signal engine, the replay of recovery, the pre-trade
    /// filters and the parameter hash use these parameters. Their window must equal the session
    /// (<see cref="ExecutionParameters.SessionStartUtc"/> and <see cref="ExecutionParameters.SessionEndUtc"/>).
    /// </summary>
    public BreakoutParameters? Breakout { get; init; }

    /// <summary>
    /// SIG-034 (spec 3.8): the session range parameters. Null (the default) leaves the mode to
    /// <see cref="Breakout"/>; a value is SessionRange mode, in which the signal engine, the replay
    /// of recovery, the pre-trade filters, the timed exit (EXE-025) and the parameter hash use these
    /// parameters. <see cref="Validate"/> checks them and refuses them together with
    /// <see cref="Breakout"/>: one mode per instance.
    /// </summary>
    public SessionRangeParameters? SessionRange { get; init; }

    /// <summary>
    /// SIG-035 (spec 3.9): the parameters of the Choch entry model of ZoneFvg mode. Null (the
    /// default) is the Fvg model; a value is the Choch model (the cBot's EntryModel Choch).
    /// <see cref="Validate"/> refuses it together with <see cref="Breakout"/> or
    /// <see cref="SessionRange"/> and checks it. The signal engine, the replay of recovery, the
    /// pre-trade filters and the parameter hash use it (REC-020); the start line then ends with
    /// <c>mode=ZoneFvg model=Choch</c>.
    /// </summary>
    public ChochParameters? Choch { get; init; }

    /// <summary>
    /// EXE-032 (spec 7.1): the symbol profile the adapter resolved at start, with the effective values
    /// of MaxSpreadPips, MinStopPips, ChochMinStopPips and MaxVolumeUnits and which were automatic.
    /// Null (the default): no profile; the start line and the parameter hash are as before.
    /// <see cref="Validate"/> refuses it when its effective values differ from those in
    /// <see cref="Execution"/>, <see cref="Choch"/> (when set) and <see cref="Risk"/>.
    /// </summary>
    public SymbolProfileResult? SymbolProfile { get; init; }

    /// <summary>
    /// The entry mode (REC-017, SIG-034): <see cref="EntryMode.Breakout"/> when <see cref="Breakout"/>
    /// is set, otherwise <see cref="EntryMode.SessionRange"/> when <see cref="SessionRange"/> is set,
    /// otherwise <see cref="EntryMode.ZoneFvg"/>.
    /// </summary>
    public EntryMode Mode => Breakout is not null ? EntryMode.Breakout : SessionRange is not null ? EntryMode.SessionRange : EntryMode.ZoneFvg;

    /// <summary>
    /// Checks every value and throws for the first problem: the symbol must start with six letters
    /// and contain no '|'; each parameter group and the market holidays must be valid; outside backtests with NewsEnabled
    /// true the feed address must be set; <see cref="BacktestHaltsLogOnly"/> may be true only in a
    /// backtest (REC-014). When <see cref="Breakout"/> is set it must be valid, and its window must
    /// equal the session times of the execution parameters (REC-017). When <see cref="SessionRange"/>
    /// is set it must be valid and <see cref="Breakout"/> must be null (SIG-034), and its
    /// RangeTrendFastEma, when above 0, must be below BiasEmaPeriod (SIG-042). When
    /// <see cref="Choch"/> is set, <see cref="Breakout"/> and <see cref="SessionRange"/> must be
    /// null and it must be valid (SIG-035).
    /// </summary>
    /// <param name="isBacktest">True in a backtest.</param>
    /// <exception cref="ArgumentNullException">A parameter group is null.</exception>
    /// <exception cref="ArgumentException">A value is invalid; for the breakout window the parameter name is <c>Breakout</c>, for breakout and session range parameters set together <c>SessionRange</c>; for Choch parameters with another mode <c>Choch</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter group, the breakout and session range parameters included, has a value out of range.</exception>
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
        ValidateBreakout();
        ValidateSessionRange();
        ValidateChoch();
        ValidateSymbolProfile();
        MonthDay.ParseList(MarketHolidays);
        if (!isBacktest && News.NewsEnabled && string.IsNullOrWhiteSpace(NewsFeedUrl))
        {
            throw new ArgumentException("NewsFeedUrl must be set when the news filter is enabled outside backtests.", nameof(NewsFeedUrl));
        }
    }

    // REC-017: the breakout parameters validate themselves; their window is the session, so the two cannot disagree.
    private void ValidateBreakout()
    {
        if (Breakout is null)
        {
            return;
        }

        Breakout.Validate();
        if (Breakout.WindowStartUtc != Execution.SessionStartUtc || Breakout.WindowEndUtc != Execution.SessionEndUtc)
        {
            throw new ArgumentException(
                "Breakout.WindowStartUtc " + Text(Breakout.WindowStartUtc) + " and Breakout.WindowEndUtc " + Text(Breakout.WindowEndUtc)
                + " must equal Execution.SessionStartUtc " + Text(Execution.SessionStartUtc) + " and Execution.SessionEndUtc " + Text(Execution.SessionEndUtc) + " (REC-017).",
                nameof(Breakout));
        }
    }

    // SIG-034: one mode per instance; the session range parameters validate themselves (SIG-032).
    private void ValidateSessionRange()
    {
        if (SessionRange is null)
        {
            return;
        }

        if (Breakout is not null)
        {
            throw new ArgumentException("Breakout and SessionRange must not both be set: one entry mode per instance (SIG-034).", nameof(SessionRange));
        }

        SessionRange.Validate();

        // SIG-042, row AY: the fast EMA of the direction is below BiasEmaPeriod; 0 is off.
        if (SessionRange.RangeTrendFastEma > 0 && SessionRange.RangeTrendFastEma >= Signal.BiasEmaPeriod)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SessionRangeParameters.RangeTrendFastEma),
                "RangeTrendFastEma must be 0 (off) or below BiasEmaPeriod (" + Text(Signal.BiasEmaPeriod) + ") but was " + Text(SessionRange.RangeTrendFastEma) + " (SIG-042).");
        }
    }

    // EXE-032: the profile's effective values are the values the core uses; the adapter passes the same doubles.
    private void ValidateSymbolProfile()
    {
        if (SymbolProfile is null)
        {
            return;
        }

        Agree(SymbolProfileValues.MaxSpreadPips, SymbolProfile.MaxSpreadPips, Execution.MaxSpreadPips);
        Agree(SymbolProfileValues.MinStopPips, SymbolProfile.MinStopPips, Execution.MinStopPips);
        if (Choch is not null)
        {
            Agree(SymbolProfileValues.ChochMinStopPips, SymbolProfile.ChochMinStopPips, Choch.ChochMinStopPips);
        }

        Agree(SymbolProfileValues.MaxVolumeUnits, SymbolProfile.MaxVolumeUnits, Risk.MaxVolumeUnits);
    }

    private static void Agree(string name, double profile, double used)
    {
        if (!profile.Equals(used))
        {
            throw new ArgumentException(
                "SymbolProfile." + name + " " + Text(profile) + " must equal the " + name + " the core uses, " + Text(used) + " (EXE-032).",
                nameof(SymbolProfile));
        }
    }

    // SIG-035: the Choch model belongs to ZoneFvg mode; its values validate themselves.
    private void ValidateChoch()
    {
        if (Choch is null)
        {
            return;
        }

        if (Breakout is not null || SessionRange is not null)
        {
            string mode = Breakout is not null ? "Breakout" : "SessionRange";
            throw new ArgumentException("EntryModel Choch needs EntryMode ZoneFvg, but EntryMode is " + mode + " (SIG-035).", nameof(Choch));
        }

        Choch.Validate();
    }
}
