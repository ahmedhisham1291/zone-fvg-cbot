using System;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;

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
/// </remarks>
public sealed class SignalEngine
{
    private static readonly IReadOnlyList<Signal> NoSignals = Array.AsReadOnly(Array.Empty<Signal>());
    private static readonly IReadOnlyList<Zone> NoZones = Array.AsReadOnly(Array.Empty<Zone>());

    private readonly SignalParameters _parameters;
    private readonly BarStore _bars;
    private readonly ZoneBook _book;
    private bool _hasAccepted;
    private DateTime _lastCloseUtc;
    private int _lastRank;

    /// <summary>Creates an engine with no bars (SIG-020): not ready, Neutral bias, no zones.</summary>
    /// <param name="parameters">The signal parameters. They are validated first.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public SignalEngine(SignalParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        _parameters = parameters;
        _bars = new BarStore(parameters);
        _book = new ZoneBook(parameters);
        Bias = Bias.Neutral;
    }

    /// <summary>
    /// Bars needed per timeframe before the engine is ready (SIG-020): the value of
    /// <see cref="WarmupCalculator.For"/> for the parameters. It enumerates D1, H4, M15.
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
    /// The non-terminal zones (Active or Touched), ordered by id with ordinal comparison (SIG-014);
    /// empty while the engine is not ready (SIG-020).
    /// </summary>
    public IReadOnlyList<Zone> Zones => IsReady ? _book.Zones : NoZones;

    /// <summary>
    /// Feeds one closed bar (SIG-001, SIG-004) and returns the signals it confirms, ordered by zone
    /// id, at most one per zone (SIG-018); none while the engine is not ready (SIG-020).
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

        _bars.Add(timeFrame, bar);
        _hasAccepted = true;
        _lastCloseUtc = closeUtc;
        _lastRank = rank;

        switch (timeFrame)
        {
            case TimeFrame.D1:
                Bias = BiasCalculator.Compute(_bars.Bars(TimeFrame.D1), _parameters.BiasEmaPeriod);
                return NoSignals;
            case TimeFrame.H4:
                OnH4Closed(bar);
                return NoSignals;
            default:
                return OnM15Closed(bar, replay);
        }
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
            string rejected = "the " + timeFrame + " bar closing at " + Stamp(closeUtc) + " UTC";
            string last = "the last accepted " + BarIntakeOrder.SameInstant[_lastRank] + " bar, which closed at " + Stamp(_lastCloseUtc) + " UTC";
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
            _book.Add(detection.Zone);
        }

        _book.OnH4Closed(bar);
    }

    private IReadOnlyList<Signal> OnM15Closed(Bar bar, bool replay)
    {
        _book.OnM15Closed(bar);
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

        _book.EndConfirmationWindows();
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

        if (!BiasAllows(zone.Type))
        {
            _book.MarkUsed(zone.Id);
            return null;
        }

        _book.MarkSignalled(zone.Id);
        return FvgDetector.BuildSignal(zone, fvg, atrM15, _parameters, replay);
    }

    private bool BiasAllows(ZoneType type)
    {
        return (type == ZoneType.Demand && Bias == Bias.Long) || (type == ZoneType.Supply && Bias == Bias.Short);
    }

    private static string Stamp(DateTime timeUtc)
    {
        return timeUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
