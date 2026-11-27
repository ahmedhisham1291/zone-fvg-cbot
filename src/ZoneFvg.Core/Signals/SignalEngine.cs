using System;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The signal layer of one symbol (spec section 3.3, docs/architecture.md sections 2 and 3): a
/// pure function of the closed bars it is fed and its parameters. It knows nothing about spread,
/// news, sessions, sizing or orders, and reads no clock (SIG-002).
/// </summary>
/// <remarks>
/// <para>Per closed bar, after the intake-order check (SIG-004):</para>
/// <list type="bullet">
/// <item>D1: the bar is stored and the bias recomputed (SIG-005).</item>
/// <item>H4: the bar is stored, swings are found, a new zone is detected and added to the book,
/// and the book ages its zones (SIG-006 to SIG-014).</item>
/// <item>M15: the bar is stored and applied to the book (touch and invalidation); then each
/// Touched zone, in id order, is checked for a fair value gap (SIG-016). A zone with a valid gap
/// is consumed: if the bias matches its type (SIG-018) it becomes Signalled then Used and its
/// signal is built (SIG-017); otherwise it becomes Used without a signal. Last, the
/// confirmation windows that have run out are closed (SIG-014).</item>
/// </list>
/// <para>Until every timeframe holds its warm-up requirement (SIG-020) the engine does all of this
/// but shows no zones and returns no signals, so its state is exact from the first ready bar
/// (SIG-019).</para>
/// <para>Breakout mode (SIG-026, spec 3.7), chosen by creating the engine with breakout
/// parameters: the intake order, the bias and the warm-up gate are the same; D1 bars also go to a
/// <see cref="BreakoutTracker"/>; H4 bars are only stored, so no zone is detected; M15 bars are
/// stored and go to the tracker with the bias and the latest ATR(M15) and ATR(H4), and a setup
/// becomes a <see cref="SignalKind.Breakout"/> signal when the engine is ready. With the trend
/// filter on (SIG-028) each D1 bar also brings the fast and slow D1 EMA to the tracker, and the D1
/// warm-up covers the slow EMA; the bias and ZoneFvg mode are unchanged.</para>
/// <para>SessionRange mode (SIG-034, spec 3.8), chosen by creating the engine with session range
/// parameters: the intake order, the bias and the warm-up gate are the same; H4 bars are only
/// stored, so no zone is detected; M15 bars are stored and go to a <see cref="SessionRangeTracker"/>
/// with the bias and the latest ATR(D1) and ATR(H4); with RangeTrendFastEma above 0 (SIG-042) the
/// tracker gets <see cref="RangeDirection"/> in place of the bias. A setup becomes a
/// <see cref="SignalKind.SessionRange"/> signal when the engine is ready; a void notice is shown in
/// <see cref="LastRangeVoid"/>.</para>
/// <para>Overnight mode (SIG-043, spec 3.10), chosen by creating the engine with Overnight
/// parameters: the intake order, the bias and the warm-up gate are the same; H4 bars are only
/// stored, so no zone is detected; M15 bars are stored and go to an <see cref="OvernightTracker"/>
/// with the latest ATR(D1) and ATR(M15). A setup becomes a <see cref="SignalKind.Overnight"/> buy
/// signal when the engine is ready; the signal carries the ATR(H4) of the last closed H4 bar, or 0
/// when there is none (information only: the largest stop of spec 3.6 does not apply to this kind).</para>
/// <para>Choch entry model of ZoneFvg mode (SIG-035 to SIG-041, spec 3.9), chosen by creating the
/// engine with Choch parameters: D1 and H4 bars as in ZoneFvg mode; the zone book's confirmation
/// window is ChochWindowBars. At each M15 close, after the book has applied the bar, each Touched
/// zone in id order is checked for a change of character (<see cref="ChochDetector"/>); a zone
/// with one is consumed as by a gap, and the window end is reported as
/// <see cref="ZoneEndReason.NoChoch"/>.</para>
/// </remarks>
public sealed class SignalEngine
{
    private static readonly IReadOnlyList<Signal> NoSignals = Array.AsReadOnly(Array.Empty<Signal>());
    private static readonly IReadOnlyList<Zone> NoZones = Array.AsReadOnly(Array.Empty<Zone>());
    private static readonly IReadOnlyList<Fvg> NoFvgs = Array.AsReadOnly(Array.Empty<Fvg>());

    /// <summary>How long a confirmed gap stays in <see cref="RecentFvgs"/> (PLT-005): 24 hours.</summary>
    private static readonly TimeSpan RecentFvgWindow = TimeSpan.FromHours(24.0);

    private readonly SignalParameters _parameters;
    private readonly BarStore _bars;
    private readonly ZoneBook _book;
    private readonly BreakoutTracker? _tracker;
    private readonly SessionRangeTracker? _range;
    private readonly OvernightTracker? _night;
    private readonly ChochParameters? _choch;

    // SIG-028: period of the slow D1 EMA of the breakout trend filter; 0 when the filter is off or in ZoneFvg mode.
    private readonly int _trendSlowEmaPeriod;

    // SIG-042: period of the fast D1 EMA of the SessionRange direction; 0 when it is off or in the other modes.
    private readonly int _rangeTrendFastEma;

    // Display only (PLT-005): written when a gap confirms a zone, never read by any decision.
    private readonly List<Fvg> _recentFvgs = new List<Fvg>();

    // Journal only (OPS-009): what the last accepted bar did to the zones, never read by any decision.
    private readonly List<ZoneEnd> _ended = new List<ZoneEnd>();
    private int _created;
    private bool _hasAccepted;
    private DateTime _lastCloseUtc;
    private int _lastRank;

    /// <summary>Creates an engine in ZoneFvg mode with no bars (SIG-020): not ready, Neutral bias, no zones.</summary>
    /// <param name="parameters">The signal parameters. They are validated first.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public SignalEngine(SignalParameters parameters)
        : this(parameters, null)
    {
    }

    /// <summary>
    /// Creates an engine with no bars (SIG-020, SIG-026): not ready, Neutral bias, no zones. With
    /// breakout parameters the engine runs in Breakout mode with a
    /// <see cref="BreakoutTracker"/> of these parameters and <see cref="SignalParameters.RewardRisk"/>;
    /// with null it is the ZoneFvg engine.
    /// </summary>
    /// <param name="parameters">The signal parameters. They are validated first.</param>
    /// <param name="breakout">The breakout parameters, or null for ZoneFvg mode. They are validated by the tracker.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public SignalEngine(SignalParameters parameters, BreakoutParameters? breakout)
        : this(parameters, breakout, null, TimeSpan.Zero, null, null)
    {
    }

    /// <summary>
    /// Creates an engine in SessionRange mode with no bars (SIG-020, SIG-034): not ready, Neutral
    /// bias, no zones, with a <see cref="SessionRangeTracker"/> of these parameters,
    /// <see cref="SignalParameters.RewardRisk"/> and the Friday cap of the order expiry.
    /// </summary>
    /// <param name="parameters">The signal parameters. They are validated first.</param>
    /// <param name="sessionRange">The session range parameters. They are validated by the tracker.</param>
    /// <param name="fridayNoEntryUtc">FridayNoEntryUtc of the execution parameters: the latest expiry of a Friday setup (SIG-032).</param>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> or <paramref name="sessionRange"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public SignalEngine(SignalParameters parameters, SessionRangeParameters sessionRange, TimeSpan fridayNoEntryUtc)
        : this(parameters, null, sessionRange ?? throw new ArgumentNullException(nameof(sessionRange)), fridayNoEntryUtc, null, null)
    {
    }

    /// <summary>
    /// Creates an engine in Overnight mode with no bars (SIG-020, SIG-043): not ready, Neutral bias,
    /// no zones, with an <see cref="OvernightTracker"/> of these parameters. A factory rather than a
    /// constructor, so that the existing calls <c>new SignalEngine(parameters, null)</c> keep meaning
    /// ZoneFvg mode.
    /// </summary>
    /// <param name="parameters">The signal parameters. They are validated first.</param>
    /// <param name="overnight">The Overnight parameters. They are validated by the tracker.</param>
    /// <returns>The engine.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> or <paramref name="overnight"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public static SignalEngine WithOvernight(SignalParameters parameters, OvernightParameters overnight)
    {
        ArgumentNullException.ThrowIfNull(overnight);
        return new SignalEngine(parameters, null, null, TimeSpan.Zero, null, overnight);
    }


    private SignalEngine(SignalParameters parameters, BreakoutParameters? breakout, SessionRangeParameters? sessionRange, TimeSpan fridayNoEntryUtc, ChochParameters? choch, OvernightParameters? overnight)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        _parameters = parameters;
        _bars = new BarStore(parameters, breakout, sessionRange, choch, overnight);
        _book = choch is null ? new ZoneBook(parameters) : new ZoneBook(parameters, choch.ChochWindowBars);
        _choch = choch;
        _tracker = breakout is null ? null : new BreakoutTracker(breakout, parameters.RewardRisk);
        _range = sessionRange is null ? null : new SessionRangeTracker(sessionRange, parameters.RewardRisk, fridayNoEntryUtc);
        _night = overnight is null ? null : new OvernightTracker(overnight);
        _trendSlowEmaPeriod = breakout?.TrendSlowEmaPeriod ?? 0;
        _rangeTrendFastEma = sessionRange?.RangeTrendFastEma ?? 0;
        Bias = Bias.Neutral;
        RangeDirection = Bias.Neutral;
    }

    /// <summary>
    /// Creates an engine in ZoneFvg mode with the Choch entry model and no bars (SIG-020, SIG-041):
    /// not ready, Neutral bias, no zones. The zone book's confirmation window is ChochWindowBars
    /// (SIG-036) and the warm-up is that of
    /// <see cref="WarmupCalculator.For(SignalParameters, BreakoutParameters, SessionRangeParameters, ChochParameters)"/>.
    /// A factory rather than a constructor, so that the existing calls <c>new SignalEngine(parameters, null)</c>
    /// keep meaning ZoneFvg mode with the Fvg model.
    /// </summary>
    /// <param name="parameters">The signal parameters. They are validated first.</param>
    /// <param name="choch">The Choch parameters. They are validated next.</param>
    /// <returns>The engine.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> or <paramref name="choch"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public static SignalEngine WithChoch(SignalParameters parameters, ChochParameters choch)
    {
        ArgumentNullException.ThrowIfNull(choch);
        return new SignalEngine(parameters, null, null, TimeSpan.Zero, choch, null);
    }

    /// <summary>
    /// Bars needed per timeframe before the engine is ready (SIG-020): the value of
    /// <see cref="WarmupCalculator.For(SignalParameters, BreakoutParameters)"/> for the parameters
    /// (SIG-028: with the breakout trend filter on, D1 covers the slow EMA). It enumerates D1, H4, M15.
    /// </summary>
    public IReadOnlyDictionary<TimeFrame, int> WarmupBars => _bars.WarmupBars;

    /// <summary>True once every timeframe holds at least its warm-up requirement (SIG-020). It stays true.</summary>
    public bool IsReady => _bars.IsReady;

    /// <summary>
    /// Bias from the stored D1 bars, recomputed at every D1 bar (SIG-005). It is kept up to date
    /// during the warm-up as well; it gates only the signals (SIG-015, SIG-018).
    /// </summary>
    public Bias Bias { get; private set; }

    /// <summary>
    /// SessionRange mode with RangeTrendFastEma above 0 (SIG-042, spec 3.8 and row AY of v0.17):
    /// the side the trading day's setup takes, recomputed at every D1 bar from the stored D1 bars.
    /// Long when D1 EMA(RangeTrendFastEma) is above D1 EMA(BiasEmaPeriod) by more than
    /// <see cref="PriceMath.DefaultTolerance"/>, Short when below by more than it, Neutral (no setup)
    /// otherwise and while either EMA has too few D1 bars. Both EMAs are computed as the bias EMA
    /// (<see cref="Ema.Value"/> over the same D1 bars). Always Neutral in the other modes and with
    /// RangeTrendFastEma 0, when the tracker is given <see cref="Bias"/> as before.
    /// </summary>
    internal Bias RangeDirection { get; private set; }

    /// <summary>
    /// The bias written into the journal's <c>bias=</c> field (OPS-001, SIG-042, orchestrating
    /// session's answer of 2026-11-27 on pull request 103): <see cref="RangeDirection"/>, the side
    /// the SessionRange setup took, in SessionRange mode with RangeTrendFastEma above 0; otherwise
    /// <see cref="Bias"/>, so every other line is unchanged.
    /// </summary>
    internal Bias JournalBias => _rangeTrendFastEma > 0 ? RangeDirection : Bias;

    /// <summary>
    /// Breakout mode (SIG-022, SIG-026): the tracker's reference day, null before the first D1 bar.
    /// Always null in ZoneFvg mode.
    /// </summary>
    public BreakoutReference? Reference => _tracker?.Reference;

    /// <summary>
    /// SessionRange mode (SIG-032, SIG-034): the setup of the trading day of the last M15 bar, also
    /// after its void; null before the evaluation, on a day without a setup and in the other modes.
    /// </summary>
    public SessionRangeSetup? RangeSetup => _range?.Setup;

    /// <summary>
    /// Overnight mode (SIG-043): the setup of the trading day of the last M15 bar; null before the
    /// evaluation, on a day without a setup and in the other modes.
    /// </summary>
    public OvernightSetup? NightSetup => _night?.Setup;

    /// <summary>
    /// SessionRange mode (SIG-033, SIG-034): the setup that the last accepted bar made void, null
    /// when that bar made none void and always null in the other modes. It is reported also while
    /// the engine is not ready and during a replay, so the caller can cancel the order of that
    /// setup (EXE-024). Reading it changes nothing.
    /// </summary>
    public SessionRangeSetup? LastRangeVoid { get; private set; }

    /// <summary>
    /// The non-terminal zones (Active or Touched), ordered by id with ordinal comparison (SIG-014);
    /// empty while the engine is not ready (SIG-020).
    /// </summary>
    public IReadOnlyList<Zone> Zones => IsReady ? _book.Zones : NoZones;

    /// <summary>
    /// Read-only copy of the fair value gaps that confirmed a zone (SIG-016) while the engine was
    /// ready, whatever the bias, in the order they were confirmed, for chart drawing (PLT-005). A
    /// gap is listed while its confirmation (the close of c1) is at most 24 hours before the close
    /// of the last accepted bar. Empty while the engine is not ready (SIG-020).
    /// </summary>
    /// <remarks>
    /// Reading it changes nothing, and no decision of the engine reads the gaps it lists. The
    /// list is a new read-only copy at every read; it does not change when later bars arrive.
    /// </remarks>
    public IReadOnlyList<Fvg> RecentFvgs => IsReady && _recentFvgs.Count > 0 ? Array.AsReadOnly(_recentFvgs.ToArray()) : NoFvgs;

    /// <summary>
    /// What the last accepted bar did to the zones (OPS-009): the zones it created and the zones
    /// that ended, with their reasons, also while the engine is not ready. Journal only: reading
    /// it changes nothing and no decision of the engine reads it. A new read-only copy at every read.
    /// </summary>
    public ZoneActivity LastActivity => _created == 0 && _ended.Count == 0 ? ZoneActivity.None : new ZoneActivity(_created, Array.AsReadOnly(_ended.ToArray()));

    /// <summary>
    /// Feeds one closed bar (SIG-001, SIG-004) and returns the signals it confirms, ordered by zone
    /// id, at most one per zone (SIG-018); none while the engine is not ready (SIG-020). In
    /// Breakout mode at most one signal, of kind Breakout (SIG-026); in SessionRange mode at most
    /// one signal, of kind SessionRange (SIG-034).
    /// </summary>
    /// <remarks>
    /// Bars must arrive in strictly increasing order of the pair (close time, rank), where the close
    /// time is <see cref="BarIntakeOrder.CloseTimeUtc"/> and the rank is
    /// <see cref="BarIntakeOrder.Rank"/>: by close time, and D1, H4, M15 among bars that close at
    /// the same instant. Any other bar is rejected before anything changes (SIG-004).
    /// </remarks>
    /// <param name="timeFrame">The timeframe of the bar.</param>
    /// <param name="bar">The closed bar.</param>
    /// <param name="replay">True while replaying history; copied to <see cref="Signal.FromReplay"/>.</param>
    /// <returns>The signals confirmed by this bar; empty for D1 and H4 bars.</returns>
    /// <exception cref="InvalidOperationException">The bar does not come after the last accepted bar in intake order. Nothing changed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The timeframe is not a defined value. Nothing changed.</exception>
    public IReadOnlyList<Signal> OnBarClosed(TimeFrame timeFrame, Bar bar, bool replay)
    {
        ArgumentNullException.ThrowIfNull(bar);
        int rank = BarIntakeOrder.Rank(timeFrame);
        DateTime closeUtc = BarIntakeOrder.CloseTimeUtc(timeFrame, bar);
        RequireIntakeOrder(timeFrame, closeUtc, rank);

        _created = 0;
        _ended.Clear();
        LastRangeVoid = null;
        _bars.Add(timeFrame, bar);
        _hasAccepted = true;
        _lastCloseUtc = closeUtc;
        _lastRank = rank;
        ForgetOldFvgs();

        switch (timeFrame)
        {
            case TimeFrame.D1:
                Bias = BiasCalculator.Compute(_bars.Bars(TimeFrame.D1), _parameters.BiasEmaPeriod);
                if (_range is not null && _rangeTrendFastEma > 0)
                {
                    IReadOnlyList<Bar> d1 = _bars.Bars(TimeFrame.D1);
                    RangeDirection = Direction(Ema.Value(d1, d1.Count - 1, _rangeTrendFastEma), Ema.Value(d1, d1.Count - 1, _parameters.BiasEmaPeriod));
                }

                if (_tracker is not null)
                {
                    OnBreakoutD1Closed(_tracker, bar);
                }

                return NoSignals;
            case TimeFrame.H4:
                if (_tracker is null && _range is null && _night is null)
                {
                    OnH4Closed(bar);
                }

                return NoSignals;
            default:
                if (_range is not null)
                {
                    return OnRangeM15Closed(_range, bar, replay);
                }

                if (_night is not null)
                {
                    return OnNightM15Closed(_night, bar, replay);
                }

                if (_choch is not null)
                {
                    return OnChochM15Closed(_choch, bar, replay);
                }

                return _tracker is null ? OnM15Closed(bar, replay) : OnBreakoutM15Closed(_tracker, bar, replay);
        }
    }

    /// <summary>
    /// SessionRange mode (SIG-033, SIG-034): tells the tracker that the order of the setup with this
    /// id is no longer pending (filled, expired, cancelled or never placed), so a later bar does not
    /// report it void. Nothing happens for another id and in the other modes.
    /// </summary>
    /// <param name="id">The setup id, RH- or RL-.</param>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is null.</exception>
    public void OrderNoLongerPending(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        _range?.OrderNoLongerPending(id);
    }

    // SIG-034: ATR(D1) and ATR(H4) of the last closed bars, null while their window is not full. The intake
    // order (SIG-004) has made sure that this bar opens after the last M15 bar, so the tracker does not throw.
    private IReadOnlyList<Signal> OnRangeM15Closed(SessionRangeTracker tracker, Bar bar, bool replay)
    {
        IReadOnlyList<Bar> d1 = _bars.Bars(TimeFrame.D1);
        IReadOnlyList<Bar> h4 = _bars.Bars(TimeFrame.H4);
        double? atrD1 = d1.Count == 0 ? null : Atr.Value(d1, d1.Count - 1, _parameters.AtrPeriod);
        double? atrH4 = h4.Count == 0 ? null : Atr.Value(h4, h4.Count - 1, _parameters.AtrPeriod);
        // SIG-042: with RangeTrendFastEma on, the side comes from the two D1 EMAs of the last D1 bar, else from the bias of 3.1.
        Bias side = _rangeTrendFastEma > 0 ? RangeDirection : Bias;
        SessionRangeEvent? answer = tracker.OnM15Closed(bar, side, atrD1, atrH4);
        if (answer is null)
        {
            return NoSignals;
        }

        SessionRangeSetup setup = answer.Setup;
        if (answer.Kind == SessionRangeEventKind.Void)
        {
            LastRangeVoid = setup;
            return NoSignals;
        }

        if (!IsReady)
        {
            return NoSignals;
        }

        DateTime closeUtc = BarIntakeOrder.CloseTimeUtc(TimeFrame.M15, bar);
        var signal = new Signal(setup.Id, setup.Side, setup.Entry, setup.Stop, setup.Target, null, closeUtc, replay, atrH4.GetValueOrDefault())
        {
            Kind = SignalKind.SessionRange,
            Level = setup.Entry,
            ExpiryUtc = setup.ExpiryUtc,
        };
        return Array.AsReadOnly(new[] { signal });
    }

    // SIG-043: ATR(D1) of the last closed D1 bar and ATR(M15) of this bar; ATR(H4) is carried for information only
    // (0 when there is none; the execution layer does not bound this kind's stop by it). The intake order (SIG-004) has made
    // sure that this bar opens after the last M15 bar, so the tracker does not throw.
    private IReadOnlyList<Signal> OnNightM15Closed(OvernightTracker tracker, Bar bar, bool replay)
    {
        IReadOnlyList<Bar> d1 = _bars.Bars(TimeFrame.D1);
        IReadOnlyList<Bar> h4 = _bars.Bars(TimeFrame.H4);
        IReadOnlyList<Bar> m15 = _bars.Bars(TimeFrame.M15);
        double? atrD1 = d1.Count == 0 ? null : Atr.Value(d1, d1.Count - 1, _parameters.AtrPeriod);
        double? atrM15 = Atr.Value(m15, m15.Count - 1, _parameters.AtrPeriod);
        OvernightSetup? setup = tracker.OnM15Closed(bar, atrD1, atrM15);
        if (setup is null || !IsReady)
        {
            return NoSignals;
        }

        double? atrH4 = h4.Count == 0 ? null : Atr.Value(h4, h4.Count - 1, _parameters.AtrPeriod);
        var signal = new Signal(setup.Id, Side.Buy, setup.Entry, setup.Stop, setup.Target, null, setup.EvaluationCloseUtc, replay, Usable(atrH4) ? atrH4!.Value : 0.0)
        {
            Kind = SignalKind.Overnight,
            Level = setup.Entry,
            ExpiryUtc = setup.ExpiryUtc,
        };
        return Array.AsReadOnly(new[] { signal });
    }

    /// <summary>
    /// The SessionRange direction of SIG-042 from the fast and the slow D1 EMA of one D1 bar: Long
    /// when fast is above slow by more than <see cref="PriceMath.DefaultTolerance"/>, Short when
    /// below by more than it, Neutral when they are equal within it or either is null (fail closed).
    /// </summary>
    /// <param name="fast">D1 EMA(RangeTrendFastEma), or null while its window is not full.</param>
    /// <param name="slow">D1 EMA(BiasEmaPeriod), or null while its window is not full.</param>
    /// <returns>The direction; Neutral gives no setup.</returns>
    internal static Bias Direction(double? fast, double? slow)
    {
        if (fast is null || slow is null)
        {
            return Bias.Neutral;
        }

        if (PriceMath.GreaterThan(fast.Value, slow.Value, PriceMath.DefaultTolerance))
        {
            return Bias.Long;
        }

        return PriceMath.LessThan(fast.Value, slow.Value, PriceMath.DefaultTolerance) ? Bias.Short : Bias.Neutral;
    }

    // SIG-022, SIG-028: the D1 bar becomes the reference; with the trend filter on, the tracker also gets the
    // fast and slow D1 EMA of this bar, each null while its window is not full (the tracker then fails closed).
    private void OnBreakoutD1Closed(BreakoutTracker tracker, Bar bar)
    {
        if (_trendSlowEmaPeriod == 0)
        {
            tracker.OnD1Closed(bar);
            return;
        }

        IReadOnlyList<Bar> d1 = _bars.Bars(TimeFrame.D1);
        double? fast = Ema.Value(d1, d1.Count - 1, _parameters.BiasEmaPeriod);
        double? slow = Ema.Value(d1, d1.Count - 1, _trendSlowEmaPeriod);
        tracker.OnD1Closed(bar, fast, slow);
    }

    // SIG-026: the ATR values are computed as in OnM15Closed; a setup becomes a signal only when ready.
    private IReadOnlyList<Signal> OnBreakoutM15Closed(BreakoutTracker tracker, Bar bar, bool replay)
    {
        IReadOnlyList<Bar> m15 = _bars.Bars(TimeFrame.M15);
        IReadOnlyList<Bar> h4 = _bars.Bars(TimeFrame.H4);
        double? atrM15 = Atr.Value(m15, m15.Count - 1, _parameters.AtrPeriod);
        double? atrH4 = h4.Count == 0 ? null : Atr.Value(h4, h4.Count - 1, _parameters.AtrPeriod);
        BreakoutSetup? setup = tracker.OnM15Closed(bar, Bias, atrM15, atrH4);
        if (setup is null || !IsReady)
        {
            return NoSignals;
        }

        var signal = new Signal(setup.Id, setup.Side, setup.Entry, setup.Stop, setup.Target, null, setup.ConfirmationCloseUtc, replay, setup.AtrH4)
        {
            Kind = SignalKind.Breakout,
            Level = setup.Level,
        };
        return Array.AsReadOnly(new[] { signal });
    }

    private void RequireIntakeOrder(TimeFrame timeFrame, DateTime closeUtc, int rank)
    {
        if (!_hasAccepted)
        {
            return;
        }

        bool after = closeUtc > _lastCloseUtc || (closeUtc == _lastCloseUtc && rank > _lastRank);
        if (!after)
        {
            string rejected = "the " + timeFrame + " bar closing at " + Second(closeUtc) + " UTC";
            string last = "the last accepted " + BarIntakeOrder.SameInstant[_lastRank] + " bar, which closed at " + Second(_lastCloseUtc) + " UTC";
            throw new InvalidOperationException("Bar rejected (SIG-004): " + rejected + " does not come after " + last + " in the order close time, then D1, H4, M15.");
        }
    }

    private void OnH4Closed(Bar bar)
    {
        IReadOnlyList<Bar> h4 = _bars.Bars(TimeFrame.H4);
        IReadOnlyList<Swing> swings = SwingDetector.Find(h4, _parameters.SwingStrength);
        ZoneDetection detection = ZoneDetector.Detect(h4, swings, _parameters);
        if (detection.Zone is not null)
        {
            IReadOnlyList<Zone> before = _book.Zones;
            if (_book.Add(detection.Zone))
            {
                _created++;
                RecordSuperseded(before);
            }
        }

        Ended(_book.OnH4Closed(bar), ZoneEndReason.Expired);
    }

    // OPS-009: the zones that were in the book before an Add and are gone after it were superseded (SIG-012).
    private void RecordSuperseded(IReadOnlyList<Zone> before)
    {
        var after = new HashSet<string>(StringComparer.Ordinal);
        foreach (Zone zone in _book.Zones)
        {
            after.Add(zone.Id);
        }

        foreach (Zone zone in before)
        {
            if (!after.Contains(zone.Id))
            {
                _ended.Add(new ZoneEnd(zone.Id, ZoneEndReason.Superseded));
            }
        }
    }

    private void Ended(IReadOnlyList<Zone> changed, ZoneEndReason reason)
    {
        foreach (Zone zone in changed)
        {
            if (zone.State is ZoneState.Invalid or ZoneState.Used or ZoneState.Expired)
            {
                _ended.Add(new ZoneEnd(zone.Id, reason));
            }
        }
    }

    private IReadOnlyList<Signal> OnM15Closed(Bar bar, bool replay)
    {
        Ended(_book.OnM15Closed(bar), ZoneEndReason.Invalid);
        List<Signal>? signals = null;
        IReadOnlyList<TouchedZone> touched = _book.TouchedZones;
        if (touched.Count > 0)
        {
            IReadOnlyList<Bar> m15 = _bars.Bars(TimeFrame.M15);
            IReadOnlyList<Bar> h4 = _bars.Bars(TimeFrame.H4);
            double? atrM15 = Atr.Value(m15, m15.Count - 1, _parameters.AtrPeriod);
            double? atrH4 = h4.Count == 0 ? null : Atr.Value(h4, h4.Count - 1, _parameters.AtrPeriod);

            // Without both ATR values no gap can be measured, so no zone is confirmed (fail closed).
            if (atrM15 is not null && atrH4 is not null)
            {
                foreach (TouchedZone candidate in touched)
                {
                    Signal? signal = Confirm(m15, candidate, atrM15.Value, atrH4.Value, replay);
                    if (signal is not null && IsReady)
                    {
                        signals ??= new List<Signal>();
                        signals.Add(signal);
                    }
                }
            }
        }

        Ended(_book.EndConfirmationWindows(), ZoneEndReason.NoFvg);
        return signals is null ? NoSignals : signals.AsReadOnly();
    }

    /// <summary>
    /// Checks one Touched zone for a gap. A zone with a valid gap is consumed whatever the bias;
    /// the signal is built only when the bias matches the zone type.
    /// </summary>
    private Signal? Confirm(IReadOnlyList<Bar> m15, TouchedZone candidate, double atrM15, double atrH4, bool replay)
    {
        Zone zone = candidate.Zone;
        Fvg? fvg = FvgDetector.Find(m15, zone, candidate.BarsSinceTouch, atrM15, atrH4, _parameters);
        if (fvg is null)
        {
            return null;
        }

        if (IsReady)
        {
            _recentFvgs.Add(fvg);
        }

        if (!BiasAllows(zone.Type))
        {
            _book.MarkUsed(zone.Id);
            _ended.Add(new ZoneEnd(zone.Id, ZoneEndReason.BiasMismatch));
            return null;
        }

        _book.MarkSignalled(zone.Id);
        _ended.Add(new ZoneEnd(zone.Id, ZoneEndReason.Signalled));
        return FvgDetector.BuildSignal(zone, fvg, atrM15, atrH4, _parameters, replay);
    }

    // SIG-041: the Choch model's M15 step. The book applies the bar first (touch, extreme, invalidation); then each
    // Touched zone, in id order, is checked against the breaks of this bar, found once per bar; last the windows end.
    private IReadOnlyList<Signal> OnChochM15Closed(ChochParameters choch, Bar bar, bool replay)
    {
        Ended(_book.OnM15Closed(bar), ZoneEndReason.Invalid);
        List<Signal>? signals = null;
        IReadOnlyList<TouchedZone> touched = _book.TouchedZones;
        if (touched.Count > 0)
        {
            IReadOnlyList<Bar> m15 = _bars.Bars(TimeFrame.M15);
            IReadOnlyList<Bar> h4 = _bars.Bars(TimeFrame.H4);
            double? atrM15 = Atr.Value(m15, m15.Count - 1, _parameters.AtrPeriod);
            double? atrH4 = h4.Count == 0 ? null : Atr.Value(h4, h4.Count - 1, _parameters.AtrPeriod);

            // SIG-037: without both ATR values nothing confirms and the windows go on (fail closed).
            if (Usable(atrM15) && Usable(atrH4))
            {
                // SIG-040 (answer of 2026-11-23): the opposing zones as the bar left them, before any is consumed.
                var atClose = new ChochBar(m15, h4, _book.Zones, atrM15!.Value, atrH4!.Value, BarIntakeOrder.CloseTimeUtc(TimeFrame.M15, bar), replay);
                atClose.Up = ChochDetector.BrokenSwing(m15, ZoneType.Demand, _parameters.SwingStrength);
                atClose.Down = ChochDetector.BrokenSwing(m15, ZoneType.Supply, _parameters.SwingStrength);
                foreach (TouchedZone candidate in touched)
                {
                    Signal? signal = ConfirmChoch(choch, atClose, candidate.Zone);
                    if (signal is not null && IsReady)
                    {
                        signals ??= new List<Signal>();
                        signals.Add(signal);
                    }
                }
            }
        }

        Ended(_book.EndConfirmationWindows(), ZoneEndReason.NoChoch);
        return signals is null ? NoSignals : signals.AsReadOnly();
    }

    /// <summary>
    /// Checks one Touched zone for a change of character at this bar (SIG-037 to SIG-040). A zone
    /// with one that counts (with the sweep when RequireSweep is set, and a stop strictly beyond
    /// the entry) is consumed whatever the bias; with ChochUseBias and a mismatch it becomes Used
    /// without a signal (SIG-036).
    /// </summary>
    private Signal? ConfirmChoch(ChochParameters choch, ChochBar c1, Zone zone)
    {
        bool buy = zone.Type == ZoneType.Demand;
        Swing? level = buy ? c1.Up : c1.Down;
        if (level is null || zone.TouchBarOpenUtc is null)
        {
            return null;
        }

        bool swept = ChochDetector.Swept(c1.M15, zone.Type, zone.TouchBarOpenUtc.Value, _parameters.SwingStrength);
        if (choch.RequireSweep && !swept)
        {
            return null;
        }

        double entry = level.Price;
        double stop = ChochDetector.Stop(zone, c1.AtrM15, _parameters, choch);

        // Answer of 2026-11-23: a stop that is not strictly beyond the entry confirms nothing (fail closed).
        bool stopBeyond = buy ? PriceMath.LessThan(stop, entry, PriceMath.DefaultTolerance) : PriceMath.GreaterThan(stop, entry, PriceMath.DefaultTolerance);
        if (!stopBeyond)
        {
            return null;
        }

        if (choch.ChochUseBias && !BiasAllows(zone.Type))
        {
            _book.MarkUsed(zone.Id);
            _ended.Add(new ZoneEnd(zone.Id, ZoneEndReason.BiasMismatch));
            return null;
        }

        _book.MarkSignalled(zone.Id);
        _ended.Add(new ZoneEnd(zone.Id, ZoneEndReason.Signalled));
        Side side = buy ? Side.Buy : Side.Sell;
        ChochTarget target = ChochDetector.Target(side, entry, stop, c1.Zones, c1.H4, c1.AtrH4, _parameters, choch);
        return new Signal(zone.Id, side, entry, stop, target.Price, null, c1.CloseUtc, c1.Replay, c1.AtrH4)
        {
            Kind = SignalKind.Choch,
            Level = entry,
            Swept = swept,
            TargetSource = target.Source,
        };
    }

    private static bool Usable(double? atr)
    {
        return atr is not null && double.IsFinite(atr.Value) && atr.Value > 0.0;
    }

    private void ForgetOldFvgs()
    {
        DateTime lastClose = _lastCloseUtc;
        _recentFvgs.RemoveAll(fvg => lastClose - (fvg.C1OpenUtc + BarIntakeOrder.Length(TimeFrame.M15)) > RecentFvgWindow);
    }

    private bool BiasAllows(ZoneType type)
    {
        return (type == ZoneType.Demand && Bias == Bias.Long) || (type == ZoneType.Supply && Bias == Bias.Short);
    }

    /// <summary>What one M15 close gives every zone of the Choch model (SIG-037 to SIG-041): read once per bar.</summary>
    private sealed class ChochBar
    {
        public ChochBar(IReadOnlyList<Bar> m15, IReadOnlyList<Bar> h4, IReadOnlyList<Zone> zones, double atrM15, double atrH4, DateTime closeUtc, bool replay)
        {
            M15 = m15;
            H4 = h4;
            Zones = zones;
            AtrM15 = atrM15;
            AtrH4 = atrH4;
            CloseUtc = closeUtc;
            Replay = replay;
        }

        public IReadOnlyList<Bar> M15 { get; }

        public IReadOnlyList<Bar> H4 { get; }

        public IReadOnlyList<Zone> Zones { get; }

        public double AtrM15 { get; }

        public double AtrH4 { get; }

        public DateTime CloseUtc { get; }

        public bool Replay { get; }

        public Swing? Up { get; set; }

        public Swing? Down { get; set; }
    }
}
