using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Execution;

/// <summary>
/// Decides whether a signal becomes an order (EXE-001 to EXE-008, EXE-011, EXE-014, EXE-015,
/// EXE-019, NWS-008, NWS-009, NWS-014, RSK-001 to RSK-006, RSK-013, RSK-016, RSK-017). Reads the
/// broker port at every call and keeps nothing between calls; its fields are the validated
/// parameters and the ports.
/// </summary>
/// <remarks>
/// <para>Checks, in this fixed order, stopping at the first failure: inputs (fail closed);
/// EXE-001 freshness; state Running; no bot position without a stop loss (EXE-011); EXE-002
/// session, Friday, spread, market open and symbol busy; NWS-008; NWS-009; stop and target on the
/// correct side (RSK-002) and EXE-003 stop bounds; EXE-005; EXE-004; EXE-006; EXE-015; sizing
/// (RSK-001, RSK-002) with the ladder percent of RSK-013 when LadderEnabled is true, or with the
/// Recovery amount of RSK-016 and RSK-017 when SizingPlan is Recovery; the broker's
/// maximum volume; the account limits of
/// <see cref="RiskEngine.CheckNewTrade"/> (RSK-003 to RSK-006, RSK-010).</para>
/// <para>Levels: entry, stop and target are rounded to the broker's tick size
/// (<see cref="PriceGrid.RoundToTick"/>, EXE-017); for a sell the current spread is added to the
/// stop first (EXE-007). A symbol whose digits or tick size are not usable is skipped as
/// BrokerDataInvalid.
/// Every check from EXE-003 on, sizing, the comment and the order use these levels.</para>
/// <para>A signal of kind Breakout (EXE-019, NWS-014): it must have no gap and a level; the news
/// check is <see cref="NewsService.CheckBreakoutEntry"/>; for a buy the current spread is added to
/// the entry before rounding; directly after EXE-003 the order is skipped as EntryAlreadyReached
/// when the ask (buy) is at or above, or the bid (sell) at or below, the order's entry, and as
/// BrokerDataInvalid when the bid or ask is not usable; EXE-005 and EXE-006 are not run; the
/// planned order is a stop order. A ZoneFvg signal takes exactly the path above.</para>
/// <para>A signal of kind Choch (EXE-027, EXE-028, NWS-015), accepted only when the filters were
/// created with Choch parameters: it must have no gap and a level equal to its entry; the news
/// check is that of the confirming bar (<see cref="NewsService.CheckBreakoutEntry"/>, skip reason
/// NewsChochBar); EXE-003 uses ChochMinStopPips; directly after EXE-003 the limit order is skipped
/// as EntryAlreadyReached when the ask (buy) is at or below, or the bid (sell) at or above, the
/// order's entry; EXE-005 and EXE-006 are not run, and in the place of EXE-006 the reward, target
/// distance over stop distance on the signal's own levels (signed: a target on the wrong side
/// gives a ratio of zero or less), must be at least MinRewardRisk (RewardRiskTooLow). The planned
/// order is a limit order.</para>
/// <para>Every skip is passed to <see cref="IExecutionEvents.Skipped"/> once, with one reason and
/// its measured values (EXE-014); a sanity-cap skip (RSK-002) also raises one alert.</para>
/// </remarks>
public sealed class PreTradeFilters
{
    private static readonly TimeSpan BarLength = TimeSpan.FromMinutes(15.0);

    // EXE-028: the target source of a Choch signal that carries none.
    private const string JournalNotAvailable = "NA";

    private readonly ExecutionParameters _parameters;
    private readonly RiskParameters _riskParameters;
    private readonly PositionSizer _sizer;
    private readonly LadderSizer _ladder;
    private readonly RecoverySizer _recovery;
    private readonly RiskEngine _risk;
    private readonly IBroker _broker;
    private readonly NewsService _news;
    private readonly IExecutionEvents _events;
    private readonly string _parameterHash;
    private readonly bool _dailyBudgetRecordOnly;

    // RSK-016: the divisor of the Recovery plan, the instance's RewardRisk in every mode (owner decision Y).
    private readonly double _rewardRisk;

    // EXE-027, EXE-028: the Choch parameters in the Choch model, null otherwise (a Choch signal is then invalid).
    private readonly ChochParameters? _choch;

    /// <summary>Creates the filters. All parameter sets are validated here (EXE-002 to EXE-006, EXE-015).</summary>
    /// <param name="parameters">Execution parameters.</param>
    /// <param name="signalParameters">Signal parameters; used for the parameter hash (EXE-008) and, with SizingPlan Recovery, for RewardRisk (RSK-016).</param>
    /// <param name="riskParameters">Risk parameters (RSK-001 to RSK-006, RSK-010).</param>
    /// <param name="broker">Broker port: the source of time, symbol data, positions, orders, history and margin.</param>
    /// <param name="news">News service for NWS-008, NWS-009 and the next window start (EXE-015).</param>
    /// <param name="events">Hook for skip records and alerts (EXE-014, RSK-002).</param>
    /// <param name="dailyBudgetRecordOnly">
    /// REC-014, backtests only: when true a trade above the daily risk budget (RSK-006) is not
    /// refused; <see cref="EvaluateWithWaiver"/> reports it instead.
    /// Default false (live).
    /// </param>
    /// <param name="breakout">
    /// Breakout parameters in Breakout mode, or null (the default) in ZoneFvg mode; used only for
    /// the parameter hash (EXE-008, EXE-018).
    /// </param>
    /// <param name="sessionRange">
    /// Session range parameters in SessionRange mode, or null (the default) in the other modes;
    /// used only for the parameter hash (EXE-008, SIG-034).
    /// </param>
    /// <param name="choch">
    /// Choch parameters in the Choch model, or null (the default) otherwise; validated here. They
    /// give ChochMinStopPips and MinRewardRisk (EXE-028) and are part of the parameter hash (REC-020).
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public PreTradeFilters(ExecutionParameters parameters, SignalParameters signalParameters, RiskParameters riskParameters, IBroker broker, NewsService news, IExecutionEvents events, bool dailyBudgetRecordOnly = false, BreakoutParameters? breakout = null, SessionRangeParameters? sessionRange = null, ChochParameters? choch = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(signalParameters);
        ArgumentNullException.ThrowIfNull(riskParameters);
        parameters.Validate();
        signalParameters.Validate();
        _parameters = parameters;
        _riskParameters = riskParameters;
        _sizer = new PositionSizer(riskParameters);
        _ladder = new LadderSizer(riskParameters);
        _recovery = new RecoverySizer(riskParameters);
        _rewardRisk = signalParameters.RewardRisk;
        _risk = new RiskEngine(riskParameters);
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _news = news ?? throw new ArgumentNullException(nameof(news));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        choch?.Validate();
        _choch = choch;
        _parameterHash = ParameterHash.Compute(signalParameters, parameters, breakout, sessionRange, choch);
        _dailyBudgetRecordOnly = dailyBudgetRecordOnly;
    }

    /// <summary>The parameter hash written into every comment (EXE-008).</summary>
    internal string ParameterHashValue => _parameterHash;

    /// <summary>
    /// Applies every pre-trade check to the signal (see the class remarks for the order) and
    /// returns the order to place, or the first skip with its reason and measured values
    /// (EXE-014). Places nothing.
    /// </summary>
    /// <param name="signal">The signal.</param>
    /// <param name="context">Symbol, bot state, zones and whether a placement is unresolved.</param>
    /// <returns>The planned order, or the skip.</returns>
    /// <exception cref="ArgumentNullException">An argument or the zone list is null.</exception>
    /// <exception cref="ArgumentException">The symbol is not a six-letter pair name (news rules).</exception>
    public PreTradeResult Evaluate(Signal signal, PreTradeContext context)
    {
        return EvaluateWithWaiver(signal, context, out string? _);
    }

    /// <summary>
    /// <see cref="Evaluate"/> that also reports a daily budget check that
    /// was not applied because of the backtest mode of REC-014 (owner decision C of 2026-11-17).
    /// </summary>
    /// <param name="signal">The signal.</param>
    /// <param name="context">Symbol, bot state, zones and whether a placement is unresolved.</param>
    /// <param name="waivedDailyBudget">
    /// When the order is allowed although it is above the daily risk budget (backtests only), the
    /// budget check's text; otherwise null.
    /// </param>
    /// <returns>The planned order, or the skip.</returns>
    /// <exception cref="ArgumentNullException">An argument or the zone list is null.</exception>
    /// <exception cref="ArgumentException">The symbol is not a six-letter pair name (news rules).</exception>
    public PreTradeResult EvaluateWithWaiver(Signal signal, PreTradeContext context, out string? waivedDailyBudget)
    {
        waivedDailyBudget = null;
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Zones);
        var c = new Candidate(signal, context, _broker.ServerTimeUtc);
        SkipRecord? skip = CheckInputs(c)
            ?? CheckFreshness(c)
            ?? CheckState(c)
            ?? CheckSession(c)
            ?? CheckSymbol(c)
            ?? CheckNews(c)
            ?? CheckStopBounds(c)
            ?? CheckEntryReached(c)
            ?? CheckFvgVersusSpread(c)
            ?? CheckCost(c)
            ?? CheckRoomOrReward(c)
            ?? CheckExpiry(c)
            ?? CheckSizing(c)
            ?? CheckAccountRisk(c);
        if (skip != null)
        {
            _events.Skipped(skip);
            if (SkipReasons.IsSanityCap(skip.Reason))
            {
                _events.Alert(new ExecutionAlert(AlertKind.SanityCap, skip.Symbol, skip.ZoneId + ": " + skip.Reason + ", " + skip.Detail));
            }

            return PreTradeResult.Skipped(skip);
        }

        waivedDailyBudget = c.WaivedDailyBudget;
        return PreTradeResult.Allowed(Build(c));
    }

    private SkipRecord? CheckInputs(Candidate c)
    {
        if (c.Now.Kind != DateTimeKind.Utc)
        {
            return c.Skip(SkipReason.BrokerDataInvalid, 0.0, 0.0, "server time is not UTC");
        }

        Signal s = c.Signal;
        bool valid = !string.IsNullOrEmpty(c.Context.Symbol)
            && !string.IsNullOrEmpty(s.ZoneId)
            && s.ZoneId.IndexOf('|') < 0
            && (s.Side == Side.Buy || s.Side == Side.Sell)
            && IsPrice(s.Entry) && IsPrice(s.Stop) && IsPrice(s.Target)
            && double.IsFinite(s.AtrH4) && s.AtrH4 >= 0.0
            && s.ConfirmationCloseUtc.Kind == DateTimeKind.Utc
            && KindIsConsistent(s, _choch != null);
        return valid ? null : c.Skip(SkipReason.InvalidSignal, 0.0, 0.0, "signal has a missing, non-finite or inconsistent value");
    }

    // EXE-019 (a): a breakout signal has no gap and a level; a ZoneFvg signal has its gap; any other kind is invalid.
    // EXE-024: a session range signal has no gap, a level and a UTC expiry.
    // EXE-027: a Choch signal, only in the Choch model, has no gap and a level equal to its entry.
    private static bool KindIsConsistent(Signal s, bool chochModel)
    {
        if (s.Kind == SignalKind.Choch)
        {
            return chochModel && s.Fvg == null && s.Level.HasValue && IsPrice(s.Level.Value) && PriceMath.AlmostEqual(s.Level.Value, s.Entry, PriceMath.DefaultTolerance);
        }

        if (s.Kind == SignalKind.Breakout)
        {
            return s.Fvg == null && s.Level.HasValue && IsPrice(s.Level.Value);
        }

        if (s.Kind == SignalKind.SessionRange)
        {
            return s.Fvg == null && s.Level.HasValue && IsPrice(s.Level.Value) && s.ExpiryUtc.HasValue && s.ExpiryUtc.Value.Kind == DateTimeKind.Utc;
        }

        return s.Kind == SignalKind.ZoneFvg
            && s.Fvg != null && s.Fvg.Side == s.Side
            && IsPrice(s.Fvg.Upper) && IsPrice(s.Fvg.Lower) && s.Fvg.Upper > s.Fvg.Lower
            && s.Fvg.C3OpenUtc.Kind == DateTimeKind.Utc && s.Fvg.C2OpenUtc.Kind == DateTimeKind.Utc && s.Fvg.C1OpenUtc.Kind == DateTimeKind.Utc;
    }

    private static bool IsBreakout(Candidate c)
    {
        return c.Signal.Kind == SignalKind.Breakout;
    }

    private static bool IsRange(Candidate c)
    {
        return c.Signal.Kind == SignalKind.SessionRange;
    }

    private static bool IsChoch(Candidate c)
    {
        return c.Signal.Kind == SignalKind.Choch;
    }

    // EXE-018, EXE-024: the kinds placed as a stop order, with the spread on a buy's entry and the entry-reached check.
    private static bool IsStopEntry(Candidate c)
    {
        return IsBreakout(c) || IsRange(c);
    }

    private static SkipRecord? CheckFreshness(Candidate c)
    {
        if (c.Signal.FromReplay)
        {
            return c.Skip(SkipReason.SignalFromReplay, 0.0, 0.0, "signal produced during replay");
        }

        TimeSpan age = c.Now - c.Signal.ConfirmationCloseUtc;
        if (age < TimeSpan.Zero || age > TimeSpan.FromMinutes(ExecutionParameters.MaxSignalAgeMinutes))
        {
            return c.Skip(SkipReason.SignalStale, age.TotalMinutes, ExecutionParameters.MaxSignalAgeMinutes, "signal age " + Text(age.TotalMinutes) + " minutes, allowed 0 to 15");
        }

        return null;
    }

    private SkipRecord? CheckState(Candidate c)
    {
        if (c.Context.State != BotState.Running)
        {
            return c.Skip(SkipReason.BotNotRunning, (double)c.Context.State, (double)BotState.Running, "state " + c.Context.State);
        }

        c.Positions = _broker.GetPositions();
        c.Orders = _broker.GetPendingOrders();
        long[] unprotected = c.Positions.Where(p => RiskEngine.IsBotLabel(p.Label) && !p.StopLoss.HasValue).Select(p => p.Id).OrderBy(id => id).ToArray();
        if (unprotected.Length > 0)
        {
            return c.Skip(SkipReason.PositionWithoutStop, unprotected.Length, 0.0, "bot positions without stop loss: " + string.Join(",", unprotected.Select(id => id.ToString(CultureInfo.InvariantCulture))));
        }

        return null;
    }

    private SkipRecord? CheckSession(Candidate c)
    {
        TimeSpan time = c.Now.TimeOfDay;
        if (time < _parameters.SessionStartUtc || time >= _parameters.SessionEndUtc)
        {
            TimeSpan limit = time < _parameters.SessionStartUtc ? _parameters.SessionStartUtc : _parameters.SessionEndUtc;
            return c.Skip(SkipReason.OutsideSession, time.TotalHours, limit.TotalHours, "time " + Text(time) + " outside " + Text(_parameters.SessionStartUtc) + " to " + Text(_parameters.SessionEndUtc));
        }

        if (c.Now.DayOfWeek == DayOfWeek.Friday && time >= _parameters.FridayNoEntryUtc)
        {
            return c.Skip(SkipReason.FridayNoEntry, time.TotalHours, _parameters.FridayNoEntryUtc.TotalHours, "Friday " + Text(time) + " at or after " + Text(_parameters.FridayNoEntryUtc));
        }

        return null;
    }

    private SkipRecord? CheckSymbol(Candidate c)
    {
        SymbolInfo info = _broker.GetSymbol(c.Context.Symbol);
        string? problem = SymbolProblem(info, c.Context.Symbol);
        if (problem != null)
        {
            return c.Skip(SkipReason.BrokerDataInvalid, 0.0, 0.0, problem);
        }

        c.Info = info;
        c.SpreadPips = PriceMath.ToPips(info.Spread, info.PipSize);
        if (PriceMath.GreaterThan(c.SpreadPips, _parameters.MaxSpreadPips, PriceMath.DefaultTolerance))
        {
            return c.Skip(SkipReason.SpreadTooWide, c.SpreadPips, _parameters.MaxSpreadPips, "spread " + Text(c.SpreadPips) + " pips above " + Text(_parameters.MaxSpreadPips));
        }

        if (!info.IsMarketOpen)
        {
            return c.Skip(SkipReason.MarketClosed, 0.0, 0.0, "market closed");
        }

        int busy = c.Positions.Count(p => IsBotOn(p.Label, p.Symbol, c.Context.Symbol)) + c.Orders.Count(o => IsBotOn(o.Label, o.Symbol, c.Context.Symbol));
        if (busy > 0 || c.Context.UnresolvedPlacement)
        {
            return c.Skip(SkipReason.SymbolBusy, busy, 0.0, "bot positions and orders on the symbol " + Text(busy) + (c.Context.UnresolvedPlacement ? ", unresolved placement" : string.Empty));
        }

        return null;
    }

    private SkipRecord? CheckNews(Candidate c)
    {
        // CheckInputs has made sure that a ZoneFvg signal has its gap (EXE-019 (a)).
        NewsDecision decision;
        if (IsRange(c))
        {
            decision = RangeNews(c);
        }
        else
        {
            // NWS-015: a Choch signal has the confirming-bar check of a breakout.
            decision = IsBreakout(c) || IsChoch(c)
                ? _news.CheckBreakoutEntry(c.Context.Symbol, c.Now, c.Signal.ConfirmationCloseUtc)
                : _news.CheckEntry(c.Context.Symbol, c.Now, c.Signal.Fvg!);
        }

        switch (decision.Reason)
        {
            case NewsBlockReason.None:
                return null;
            case NewsBlockReason.CalendarUnavailable:
                return c.Skip(SkipReason.NewsCalendarUnavailable, 0.0, 0.0, "news calendar unavailable");
            case NewsBlockReason.InsideWindow:
                return c.Skip(SkipReason.NewsWindow, 0.0, 0.0, "inside a news window at " + Second(c.Now));
            case NewsBlockReason.BreakoutBarInWindow when IsChoch(c):
                return c.Skip(SkipReason.NewsChochBar, 0.0, 0.0, "CHoCH bar opening " + BarText(decision.BarOpenUtc) + " overlaps a news window");
            case NewsBlockReason.BreakoutBarInWindow:
                return c.Skip(SkipReason.NewsBreakoutBar, 0.0, 0.0, "breakout bar opening " + BarText(decision.BarOpenUtc) + " overlaps a news window");
            default:
                return c.Skip(SkipReason.NewsFvgBar, 0.0, 0.0, "FVG bar opening " + BarText(decision.BarOpenUtc) + " overlaps a news window");
        }
    }

    // EXE-024 (spec 3.8, 6.1): the news checks of every entry, the calendar and the window at the current time.
    // The mode has no bar of its own that news could have made, so no bar is checked against the windows.
    private NewsDecision RangeNews(Candidate c)
    {
        if (_news.StatusAt(c.Now) == NewsStatus.Unavailable)
        {
            return new NewsDecision(NewsBlockReason.CalendarUnavailable, null);
        }

        bool inside = _news.CalendarAt(c.Now).IsInWindow(c.Context.Symbol, c.Now);
        return new NewsDecision(inside ? NewsBlockReason.InsideWindow : NewsBlockReason.None, null);
    }

    private static string BarText(DateTime? barOpenUtc)
    {
        return barOpenUtc.HasValue ? Second(barOpenUtc.Value) : "unknown";
    }

    private SkipRecord? CheckStopBounds(Candidate c)
    {
        SymbolInfo info = c.Info!;
        Signal s = c.Signal;
        double rawStop = s.Side == Side.Sell ? s.Stop + info.Spread : s.Stop;

        // EXE-019 (c): a buy stop triggers on the ask, so the spread is added to a breakout buy's entry.
        double rawEntry = IsStopEntry(c) && s.Side == Side.Buy ? s.Entry + info.Spread : s.Entry;
        c.Entry = PriceGrid.RoundToTick(rawEntry, info.TickSize);
        c.Stop = PriceGrid.RoundToTick(rawStop, info.TickSize);
        c.Target = PriceGrid.RoundToTick(s.Target, info.TickSize);
        if (!OnLossSide(s.Side, c.Entry, c.Stop))
        {
            return c.Skip(SkipReason.StopOnWrongSide, c.Stop, c.Entry, "stop " + Text(c.Stop) + " entry " + Text(c.Entry));
        }

        if (!OnProfitSide(s.Side, c.Entry, c.Target))
        {
            return c.Skip(SkipReason.TargetOnWrongSide, c.Target, c.Entry, "target " + Text(c.Target) + " entry " + Text(c.Entry));
        }

        c.StopDistance = Math.Abs(c.Entry - c.Stop);
        c.StopPips = PriceMath.ToPips(c.StopDistance, info.PipSize);

        // EXE-028: ChochMinStopPips in place of MinStopPips for a Choch signal; CheckInputs has made sure the model is Choch.
        double minStopPips = IsChoch(c) ? _choch!.ChochMinStopPips : _parameters.MinStopPips;
        if (PriceMath.LessThan(c.StopPips, minStopPips, PriceMath.DefaultTolerance))
        {
            return c.Skip(SkipReason.StopTooSmall, c.StopPips, minStopPips, "stop " + Text(c.StopPips) + " pips below " + Text(minStopPips));
        }

        double maxPips = PriceMath.ToPips(_parameters.MaxStopAtr * s.AtrH4, info.PipSize);
        if (PriceMath.GreaterThan(c.StopPips, maxPips, PriceMath.DefaultTolerance))
        {
            return c.Skip(SkipReason.StopTooLarge, c.StopPips, maxPips, "stop " + Text(c.StopPips) + " pips above " + Text(_parameters.MaxStopAtr) + " x ATR(H4) = " + Text(maxPips) + " pips");
        }

        return null;
    }

    // EXE-019 (d): directly after EXE-003, a breakout order whose entry the market has already reached is not placed.
    // EXE-027: the same for the limit order of a Choch signal, whose entry is reached from the other side.
    private static SkipRecord? CheckEntryReached(Candidate c)
    {
        if (!IsStopEntry(c) && !IsChoch(c))
        {
            return null;
        }

        SymbolInfo info = c.Info!;
        if (!IsPrice(info.Bid) || !IsPrice(info.Ask) || PriceMath.LessThan(info.Ask, info.Bid, PriceMath.DefaultTolerance))
        {
            return c.Skip(SkipReason.BrokerDataInvalid, 0.0, 0.0, "bid " + Text(info.Bid) + " ask " + Text(info.Ask) + " of " + c.Context.Symbol + " are not usable");
        }

        if (IsChoch(c))
        {
            return LimitEntryReached(c, info);
        }

        if (c.Signal.Side == Side.Buy && !PriceMath.LessThan(info.Ask, c.Entry, PriceMath.DefaultTolerance))
        {
            return c.Skip(SkipReason.EntryAlreadyReached, info.Ask, c.Entry, "ask " + Text(info.Ask) + " at or above the entry " + Text(c.Entry));
        }

        if (c.Signal.Side == Side.Sell && !PriceMath.GreaterThan(info.Bid, c.Entry, PriceMath.DefaultTolerance))
        {
            return c.Skip(SkipReason.EntryAlreadyReached, info.Bid, c.Entry, "bid " + Text(info.Bid) + " at or below the entry " + Text(c.Entry));
        }

        return null;
    }

    // EXE-027: a buy limit at or above the ask, or a sell limit at or below the bid, would fill at once.
    private static SkipRecord? LimitEntryReached(Candidate c, SymbolInfo info)
    {
        if (c.Signal.Side == Side.Buy && !PriceMath.GreaterThan(info.Ask, c.Entry, PriceMath.DefaultTolerance))
        {
            return c.Skip(SkipReason.EntryAlreadyReached, info.Ask, c.Entry, "ask " + Text(info.Ask) + " at or below the entry " + Text(c.Entry));
        }

        if (c.Signal.Side == Side.Sell && !PriceMath.LessThan(info.Bid, c.Entry, PriceMath.DefaultTolerance))
        {
            return c.Skip(SkipReason.EntryAlreadyReached, info.Bid, c.Entry, "bid " + Text(info.Bid) + " at or above the entry " + Text(c.Entry));
        }

        return null;
    }

    private SkipRecord? CheckFvgVersusSpread(Candidate c)
    {
        // EXE-019 (b): not applied to a breakout signal, which has no gap; nor to a session range signal (EXE-024);
        // nor to a Choch signal (EXE-028).
        if (IsStopEntry(c) || IsChoch(c))
        {
            return null;
        }

        SymbolInfo info = c.Info!;
        Fvg fvg = c.Signal.Fvg!;
        double heightPips = PriceMath.ToPips(fvg.Upper - fvg.Lower, info.PipSize);
        double minimum = _parameters.MinFvgSpreadMult * c.SpreadPips;
        if (PriceMath.LessThan(heightPips, minimum, PriceMath.DefaultTolerance))
        {
            return c.Skip(SkipReason.FvgTooSmallForSpread, heightPips, minimum, "gap " + Text(heightPips) + " pips below " + Text(_parameters.MinFvgSpreadMult) + " x spread = " + Text(minimum));
        }

        return null;
    }

    private SkipRecord? CheckCost(Candidate c)
    {
        SymbolInfo info = c.Info!;
        double commissionPips = info.RoundTripCommissionPerUnit / info.PipValuePerUnit;
        double cost = c.SpreadPips + commissionPips;
        double limit = _parameters.CostToRiskMax * c.StopPips;
        if (PriceMath.GreaterThan(cost, limit, PriceMath.DefaultTolerance))
        {
            return c.Skip(SkipReason.CostTooHigh, cost, limit, "spread " + Text(c.SpreadPips) + " + commission " + Text(commissionPips) + " pips above " + Text(limit));
        }

        return null;
    }

    // EXE-028: for a Choch signal the reward check takes the place of the room check.
    private SkipRecord? CheckRoomOrReward(Candidate c)
    {
        return IsChoch(c) ? CheckReward(c) : CheckRoom(c);
    }

    // EXE-028: target distance over stop distance on the signal's own levels, before the spread and the price grid.
    // Signed (answer of 2026-11-23): a target on the wrong side of the entry gives a ratio of zero or less.
    private SkipRecord? CheckReward(Candidate c)
    {
        Signal s = c.Signal;
        double risk = Math.Abs(s.Entry - s.Stop);
        double reward = s.Side == Side.Buy ? s.Target - s.Entry : s.Entry - s.Target;
        double ratio = reward / risk;
        double minimum = _choch!.MinRewardRisk;
        if (!double.IsFinite(ratio) || PriceMath.LessThan(ratio, minimum, PriceMath.DefaultTolerance))
        {
            string source = s.TargetSource ?? JournalNotAvailable;
            return c.Skip(SkipReason.RewardRiskTooLow, ratio, minimum, "reward " + Text(ratio) + "R below " + Text(minimum) + "R, target " + source);
        }

        return null;
    }

    private SkipRecord? CheckRoom(Candidate c)
    {
        // EXE-019 (b): not applied to a breakout signal; nor to a session range signal (EXE-024).
        if (IsStopEntry(c))
        {
            return null;
        }

        ZoneType opposing = c.Signal.Side == Side.Buy ? ZoneType.Supply : ZoneType.Demand;
        double direction = c.Signal.Side == Side.Buy ? 1.0 : -1.0;
        double room = _parameters.MinRoomR * c.StopDistance;
        Zone? nearest = null;
        double nearestDistance = 0.0;
        foreach (Zone zone in c.Context.Zones.Where(z => z != null && z.Type == opposing && !IsTerminal(z.State)).OrderBy(z => z.Id, StringComparer.Ordinal))
        {
            double distance = direction * (zone.Proximal - c.Entry);
            bool between = PriceMath.GreaterThan(distance, 0.0, PriceMath.DefaultTolerance) && PriceMath.LessThan(distance, room, PriceMath.DefaultTolerance);
            if (between && (nearest == null || distance < nearestDistance))
            {
                nearest = zone;
                nearestDistance = distance;
            }
        }

        if (nearest != null)
        {
            double inR = nearestDistance / c.StopDistance;
            return c.Skip(SkipReason.NoRoom, inR, _parameters.MinRoomR, "zone " + nearest.Id + " proximal " + Text(nearest.Proximal) + " at " + Text(inR) + "R");
        }

        return null;
    }

    private SkipRecord? CheckExpiry(Candidate c)
    {
        // EXE-024: a session range order ends at the signal's expiry; CheckInputs has made sure that it has one.
        DateTime normal = IsRange(c) ? c.Signal.ExpiryUtc.GetValueOrDefault() : c.Now + TimeSpan.FromTicks(BarLength.Ticks * _parameters.EntryExpiryBars);
        DateTime? window = _news.CalendarAt(c.Now).NextWindowStart(c.Context.Symbol, c.Now);
        c.Expiry = window.HasValue && window.Value < normal ? window.Value : normal;
        TimeSpan life = c.Expiry - c.Now;
        if (life < TimeSpan.FromMinutes(ExecutionParameters.MinOrderLifeMinutes))
        {
            return c.Skip(SkipReason.ExpiryTooShort, life.TotalMinutes, ExecutionParameters.MinOrderLifeMinutes, "order life " + Text(life.TotalMinutes) + " minutes to the news window at " + Second(c.Expiry));
        }

        return null;
    }

    private SkipRecord? CheckSizing(Candidate c)
    {
        SymbolInfo info = c.Info!;
        c.Account = _broker.GetAccount();
        var plan = new TradePlan(c.Context.Symbol, c.Signal.Side, c.Entry, c.Stop, c.Target);
        SkipRecord? refused;
        SizingResult? sizing;
        if (_riskParameters.SizingPlan == SizingPlan.Recovery)
        {
            sizing = SizeRecovery(c, plan, info, out refused);
        }
        else
        {
            sizing = SizeLadder(c, plan, info, out refused);
        }

        if (sizing == null)
        {
            return refused;
        }

        if (!sizing.IsAllowed)
        {
            return c.Skip(SkipReasons.FromRisk(sizing.Reason), sizing.Measured, sizing.Limit, sizing.Detail);
        }

        c.Sizing = sizing;
        if (PriceMath.GreaterThan(sizing.VolumeUnits, info.VolumeMaxUnits, PriceMath.DefaultTolerance))
        {
            return c.Skip(SkipReason.AboveBrokerMaxVolume, sizing.VolumeUnits, info.VolumeMaxUnits, "volume " + Text(sizing.VolumeUnits) + " above the broker maximum " + Text(info.VolumeMaxUnits));
        }

        return null;
    }

    private SizingResult? SizeLadder(Candidate c, TradePlan plan, SymbolInfo info, out SkipRecord? refused)
    {
        refused = null;
        double riskPercent = _riskParameters.RiskPercent;
        if (_riskParameters.LadderEnabled)
        {
            // RSK-013: the percent comes from the bot's entries since RiskEpochDate, read from the broker now; the new entry is placed at server time.
            c.History = _broker.GetHistory(HistoryStart(c.Now));
            try
            {
                DateTime epochStart = CestClock.StartOfCestDateUtc(_riskParameters.RiskEpochDate);
                riskPercent = _ladder.NextRiskPercent(LadderSizer.EntriesFromBroker(c.Positions, c.Orders, c.History, epochStart), c.Now);
            }
            catch (ArgumentException exception)
            {
                refused = c.Skip(SkipReason.BrokerDataInvalid, 0.0, 0.0, exception.Message);
                return null;
            }
        }

        return _sizer.Size(c.Account!, plan, info, riskPercent);
    }

    private SizingResult? SizeRecovery(Candidate c, TradePlan plan, SymbolInfo info, out SkipRecord? refused)
    {
        // RSK-016, RSK-017: the amount comes from the closed trades of this symbol since RiskEpochDate, read from the broker now.
        refused = null;
        c.History = _broker.GetHistory(HistoryStart(c.Now));
        RecoveryAmount amount;
        try
        {
            DateTime epochStart = CestClock.StartOfCestDateUtc(_riskParameters.RiskEpochDate);
            IReadOnlyList<LadderEntry> trades = RecoverySizer.EntriesFromBroker(c.Context.Symbol, c.Positions, c.Orders, c.History, epochStart);
            amount = _recovery.Next(trades, c.Now, c.Account!.Balance, _rewardRisk);
        }
        catch (ArgumentException exception)
        {
            refused = c.Skip(SkipReason.BrokerDataInvalid, 0.0, 0.0, exception.Message);
            return null;
        }

        // R is the risk amount of RSK-001, not rounded; every later check applies to it.
        return _sizer.SizeAmount(c.Account!, plan, info, amount.RiskMoney);
    }

    private SkipRecord? CheckAccountRisk(Candidate c)
    {
        double volume = c.Sizing!.VolumeUnits;
        double margin = _broker.EstimateMargin(c.Context.Symbol, c.Signal.Side, volume);
        DateTime since = HistoryStart(c.Now);
        RiskSnapshot snapshot;
        try
        {
            snapshot = new RiskSnapshot(c.Now, c.Account!, c.Positions, c.Orders, c.History ?? _broker.GetHistory(since), since, SnapshotSymbols(c));
        }
        catch (ArgumentException exception)
        {
            return c.Skip(SkipReason.BrokerDataInvalid, 0.0, 0.0, exception.Message);
        }

        var trade = new PlannedTrade(c.Context.Symbol, c.Signal.Side, c.Entry, c.Stop, volume, margin);
        RiskCheckResult result = _risk.CheckNewTrade(snapshot, trade);
        if (_dailyBudgetRecordOnly && result.Reason == RiskReason.DailyBudgetExceeded)
        {
            // REC-014, backtests only: the budget does not refuse the trade; the checks after it still do.
            c.WaivedDailyBudget = result.Detail;
            result = _risk.CheckNewTradeWithoutDailyBudget(snapshot, trade);
        }

        if (!result.IsAllowed)
        {
            return c.Skip(SkipReasons.FromRisk(result.Reason), result.Measured, result.Limit, result.Detail);
        }

        return null;
    }

    private List<SymbolInfo> SnapshotSymbols(Candidate c)
    {
        // Symbol data for the trade and for every symbol with a bot position or order (RSK-004, RSK-005).
        var names = new SortedSet<string>(StringComparer.Ordinal) { c.Context.Symbol };
        foreach (PositionInfo position in c.Positions.Where(p => RiskEngine.IsBotLabel(p.Label)))
        {
            names.Add(position.Symbol);
        }

        foreach (PendingOrderInfo order in c.Orders.Where(o => RiskEngine.IsBotLabel(o.Label)))
        {
            names.Add(order.Symbol);
        }

        return names.Select(name => name == c.Context.Symbol ? c.Info! : _broker.GetSymbol(name)).ToList();
    }

    private DateTime HistoryStart(DateTime now)
    {
        // The risk engine needs history from the earlier of the day start and the epoch start (RSK-007, RSK-008).
        DateTime dayStart = CestClock.StartOfCestDayUtc(now);
        DateTime epochStart = CestClock.StartOfCestDateUtc(_riskParameters.RiskEpochDate);
        return epochStart < dayStart ? epochStart : dayStart;
    }

    private PlannedOrder Build(Candidate c)
    {
        SymbolInfo info = c.Info!;
        SizingResult sizing = c.Sizing!;
        string comment = OrderComment.Build(new CommentPlan(c.Entry, c.Stop, c.Target, sizing.RiskMoney, _parameterHash), info.Digits);
        string label = OrderLabel.Build(c.Context.Symbol, c.Signal.ZoneId);
        return new PlannedOrder(c.Signal, c.Context.Symbol, c.Signal.Side, c.Entry, c.Stop, c.Target, sizing.VolumeUnits, sizing.RiskMoney, c.StopPips, c.Expiry, info.Digits, label, comment, c.Now)
        {
            TickSize = info.TickSize,
            EntryType = IsStopEntry(c) ? EntryOrderType.Stop : EntryOrderType.Limit,
        };
    }

    private static string? SymbolProblem(SymbolInfo info, string symbol)
    {
        if (info == null || !string.Equals(info.Name, symbol, StringComparison.Ordinal))
        {
            return "no symbol data for " + symbol;
        }

        bool usable = PriceGrid.IsUsable(info.PipSize)
            && IsPrice(info.PipValuePerUnit)
            && IsPrice(info.VolumeMinUnits) && IsPrice(info.VolumeStepUnits) && IsPrice(info.VolumeMaxUnits)
            && double.IsFinite(info.Spread) && info.Spread >= 0.0
            && double.IsFinite(info.RoundTripCommissionPerUnit) && info.RoundTripCommissionPerUnit >= 0.0;
        if (!usable)
        {
            return "symbol data of " + symbol + " is not usable";
        }

        // EXE-017: the price grid comes from the broker; without a usable one no order is sent.
        return PriceGrid.IsUsableGrid(info.Digits, info.TickSize)
            ? null
            : "price grid of " + symbol + " is not usable: digits " + Text(info.Digits) + " tick size " + Text(info.TickSize);
    }

    private static bool OnLossSide(Side side, double entry, double price)
    {
        return side == Side.Buy
            ? PriceMath.LessThan(price, entry, PriceMath.DefaultTolerance)
            : PriceMath.GreaterThan(price, entry, PriceMath.DefaultTolerance);
    }

    private static bool OnProfitSide(Side side, double entry, double price)
    {
        return side == Side.Buy
            ? PriceMath.GreaterThan(price, entry, PriceMath.DefaultTolerance)
            : PriceMath.LessThan(price, entry, PriceMath.DefaultTolerance);
    }

    private static bool IsBotOn(string label, string symbol, string wanted)
    {
        return RiskEngine.IsBotLabel(label) && string.Equals(symbol, wanted, StringComparison.Ordinal);
    }

    private static bool IsTerminal(ZoneState state)
    {
        return state == ZoneState.Used || state == ZoneState.Invalid || state == ZoneState.Expired;
    }

    private static bool IsPrice(double value)
    {
        return double.IsFinite(value) && value > 0.0;
    }

    /// <summary>The values gathered while one signal is checked. Lives for one call only.</summary>
    private sealed class Candidate
    {
        internal Candidate(Signal signal, PreTradeContext context, DateTime now)
        {
            Signal = signal;
            Context = context;
            Now = now;
        }

        internal Signal Signal { get; }

        internal PreTradeContext Context { get; }

        internal DateTime Now { get; }

        internal IReadOnlyList<PositionInfo> Positions { get; set; } = Array.Empty<PositionInfo>();

        internal IReadOnlyList<PendingOrderInfo> Orders { get; set; } = Array.Empty<PendingOrderInfo>();

        internal SymbolInfo? Info { get; set; }

        internal AccountSnapshot? Account { get; set; }

        internal IReadOnlyList<ClosedTradeInfo>? History { get; set; }

        internal SizingResult? Sizing { get; set; }

        internal double SpreadPips { get; set; }

        internal double Entry { get; set; }

        internal double Stop { get; set; }

        internal double Target { get; set; }

        internal double StopDistance { get; set; }

        internal double StopPips { get; set; }

        internal DateTime Expiry { get; set; }

        internal string? WaivedDailyBudget { get; set; }

        internal SkipRecord Skip(SkipReason reason, double measured, double limit, string detail)
        {
            return new SkipRecord(Context.Symbol, Signal.ZoneId ?? string.Empty, reason, measured, limit, detail, Now);
        }
    }
}
