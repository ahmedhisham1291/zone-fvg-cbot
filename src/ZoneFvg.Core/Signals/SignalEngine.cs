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

    // SIG-028: period of the slow D1 EMA of the breakout trend filter; 0 when the filter is off or in ZoneFvg mode.
    private readonly int _trendSlowEmaPeriod;

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
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        _parameters = parameters;
        _bars = new BarStore(parameters, breakout);
        _book = new ZoneBook(parameters);
        _tracker = breakout is null ? null : new BreakoutTracker(breakout, parameters.RewardRisk);
        _trendSlowEmaPeriod = breakout?.TrendSlowEmaPeriod ?? 0;
        Bias = Bias.Neutral;
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
    /// Breakout mode (SIG-022, SIG-026): the tracker's reference day, null before the first D1 bar.
    /// Always null in ZoneFvg mode.
    /// </summary>
    public BreakoutReference? Reference => _tracker?.Reference;

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
    /// Breakout mode at most one signal, of kind Breakout (SIG-026).
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
        _bars.Add(timeFrame, bar);
        _hasAccepted = true;
        _lastCloseUtc = closeUtc;
        _lastRank = rank;
        ForgetOldFvgs();

        switch (timeFrame)
        {
            case TimeFrame.D1:
                Bias = BiasCalculator.Compute(_bars.Bars(TimeFrame.D1), _parameters.BiasEmaPeriod);
                if (_tracker is not null)
                {
                    OnBreakoutD1Closed(_tracker, bar);
                }

                return NoSignals;
            case TimeFrame.H4:
                if (_tracker is null)
                {
                    OnH4Closed(bar);
                }

                return NoSignals;
            default:
                return _tracker is null ? OnM15Closed(bar, replay) : OnBreakoutM15Closed(_tracker, bar, replay);
        }
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

    private void ForgetOldFvgs()
    {
        DateTime lastClose = _lastCloseUtc;
        _recentFvgs.RemoveAll(fvg => lastClose - (fvg.C1OpenUtc + BarIntakeOrder.Length(TimeFrame.M15)) > RecentFvgWindow);
    }

    private bool BiasAllows(ZoneType type)
    {
        return (type == ZoneType.Demand && Bias == Bias.Long) || (type == ZoneType.Supply && Bias == Bias.Short);
    }
}
