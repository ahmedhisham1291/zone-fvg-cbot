using System;
using System.Globalization;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The signal logic of the SessionRange mode (spec sections 3.1 and 3.8, SIG-030 to SIG-033). It
/// is given closed M15 bars one at a time, collects the range bars of the trading day, evaluates
/// the range once per trading day and watches a setup for a void. It keeps only the state of the
/// trading day of the last bar, so the same bars fed again give the same state. It reads no
/// clock, knows nothing of orders and computes no bias or ATR: they are passed in.
/// </summary>
public sealed class SessionRangeTracker
{
    private const string IdDateFormat = "yyyyMMdd";

    private readonly SessionRangeParameters _parameters;
    private readonly double _rewardRisk;
    private readonly TimeSpan _fridayNoEntryUtc;

    // Open time of the last bar received; bars must arrive in strictly increasing open time.
    private DateTime? _lastOpenUtc;

    // SIG-030: the trading day of the last bar and its instants; null until the first bar.
    private DayWindow? _day;

    // SIG-031: the range bars of the trading day received so far.
    private int _rangeBars;
    private double _rangeHigh;
    private double _rangeLow;

    // SIG-031: true once the trading day's evaluation bar has been received.
    private bool _evaluated;

    // SIG-033: the setup that is watched for a void; null when there is none, after the void and
    // after the caller has reported that its order is no longer pending.
    private SessionRangeSetup? _watched;

    /// <summary>Creates a tracker with no trading day (SIG-031, SIG-032).</summary>
    /// <param name="parameters">The SessionRange parameters. They are validated first.</param>
    /// <param name="rewardRisk">Target distance in multiples of the stop distance (SIG-032). Must be finite and greater than zero.</param>
    /// <param name="fridayNoEntryUtc">UTC time of day after which no entry is made on a Friday; it caps the expiry of a setup evaluated on a Friday (SIG-032). From 00:00 up to but not including 24:00.</param>
    /// <exception cref="ArgumentNullException">The parameters are null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter, the reward-to-risk ratio or the Friday time is out of range.</exception>
    public SessionRangeTracker(SessionRangeParameters parameters, double rewardRisk, TimeSpan fridayNoEntryUtc)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        if (!double.IsFinite(rewardRisk) || rewardRisk <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(rewardRisk), "rewardRisk must be a finite number greater than zero but was " + Invariant.Text(rewardRisk) + ".");
        }

        if (fridayNoEntryUtc < TimeSpan.Zero || fridayNoEntryUtc >= TimeSpan.FromHours(24.0))
        {
            throw new ArgumentOutOfRangeException(nameof(fridayNoEntryUtc), "fridayNoEntryUtc must be a time of day from 00:00 up to but not including 24:00 but was " + Invariant.Text(fridayNoEntryUtc) + ".");
        }

        _parameters = parameters;
        _rewardRisk = rewardRisk;
        _fridayNoEntryUtc = fridayNoEntryUtc;
    }

    /// <summary>The date that names the trading day of the last bar received (SIG-030), or null before the first bar.</summary>
    public DateTime? TradingDate => _day?.TradingDate;

    /// <summary>The setup of the trading day of the last bar received (SIG-032), or null when it has none (yet).</summary>
    public SessionRangeSetup? Setup { get; private set; }

    /// <summary>
    /// A closed M15 bar (SIG-030 to SIG-033). The bar belongs to the trading day of its open time;
    /// the first bar of a new trading day forgets the old day entirely. A bar that opens at or after
    /// RangeStartNy and before RangeEndNy of its trading day, and arrives before the evaluation, is a
    /// range bar (SIG-031). The first bar of the trading day whose close time is at or after
    /// RangeEndNy evaluates the range: it returns a setup (SIG-031, SIG-032) or null, and the trading
    /// day gives no further setup either way. A later bar that opened at or after RangeEndNy returns
    /// a void notice, once, when the setup is still watched and the bar's low is at or below the
    /// range low (buy) or its high is at or above the range high (sell), with PriceMath (SIG-033).
    /// Every other bar returns null.
    /// </summary>
    /// <param name="bar">The closed M15 bar. It must open after the last bar received.</param>
    /// <param name="bias">The D1 bias at the close of the bar (SIG-005).</param>
    /// <param name="atrD1">ATR(D1) of the last closed D1 bar, or null when there is none.</param>
    /// <param name="atrH4">ATR(H4) of the last closed H4 bar, or null when there is none.</param>
    /// <returns>A setup, a void notice, or null.</returns>
    /// <exception cref="ArgumentNullException">The bar is null.</exception>
    /// <exception cref="InvalidOperationException">The bar does not open after the last bar received; nothing is changed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A time of the bar's trading day is outside the range of <see cref="DateTime"/>; nothing is changed.</exception>
    public SessionRangeEvent? OnM15Closed(Bar bar, Bias bias, double? atrD1, double? atrH4)
    {
        ArgumentNullException.ThrowIfNull(bar);
        if (_lastOpenUtc.HasValue && bar.OpenTimeUtc <= _lastOpenUtc.Value)
        {
            throw new InvalidOperationException("An M15 bar opening at " + Invariant.Minute(bar.OpenTimeUtc) + " does not open after the last bar of " + Invariant.Minute(_lastOpenUtc.Value) + ".");
        }

        // Everything that can throw is computed before the state changes.
        DateTime closeUtc = BarIntakeOrder.CloseTimeUtc(TimeFrame.M15, bar);
        DateTime tradingDate = NewYorkClock.TradingDate(bar.OpenTimeUtc);
        DayWindow day = _day is not null && _day.TradingDate == tradingDate ? _day : Window(tradingDate);
        if (!ReferenceEquals(day, _day))
        {
            StartDay(day);
        }

        _lastOpenUtc = bar.OpenTimeUtc;
        if (!_evaluated)
        {
            return Collect(bar, day, closeUtc, bias, atrD1, atrH4);
        }

        return CheckVoid(bar, day);
    }

    /// <summary>
    /// The caller reports that the order of the setup with this id is no longer pending: it was
    /// filled, it expired or it was cancelled (SIG-033). From then on no void is reported for that
    /// setup. An id that is not the watched setup's changes nothing.
    /// </summary>
    /// <param name="id">The setup id (RH-yyyyMMdd or RL-yyyyMMdd).</param>
    /// <exception cref="ArgumentNullException">The id is null.</exception>
    public void OrderNoLongerPending(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_watched is not null && string.Equals(_watched.Id, id, StringComparison.Ordinal))
        {
            _watched = null;
        }
    }

    private DayWindow Window(DateTime tradingDate)
    {
        return new DayWindow(
            tradingDate,
            NewYorkClock.UtcInTradingDay(tradingDate, _parameters.RangeStartNy),
            NewYorkClock.UtcInTradingDay(tradingDate, _parameters.RangeEndNy),
            NewYorkClock.UtcInTradingDay(tradingDate, _parameters.LastEntryNy));
    }

    private void StartDay(DayWindow day)
    {
        _day = day;
        _rangeBars = 0;
        _rangeHigh = 0.0;
        _rangeLow = 0.0;
        _evaluated = false;
        _watched = null;
        Setup = null;
    }

    // SIG-031: collect a range bar, and evaluate at the first bar that closes at or after RangeEndNy.
    private SessionRangeEvent? Collect(Bar bar, DayWindow day, DateTime closeUtc, Bias bias, double? atrD1, double? atrH4)
    {
        if (bar.OpenTimeUtc >= day.RangeStartUtc && bar.OpenTimeUtc < day.RangeEndUtc)
        {
            _rangeHigh = _rangeBars == 0 ? bar.High : Math.Max(_rangeHigh, bar.High);
            _rangeLow = _rangeBars == 0 ? bar.Low : Math.Min(_rangeLow, bar.Low);
            _rangeBars++;
        }

        if (closeUtc < day.RangeEndUtc)
        {
            return null;
        }

        _evaluated = true;
        SessionRangeSetup? setup = Evaluate(day, closeUtc, bias, atrD1, atrH4);
        Setup = setup;
        _watched = setup;
        return setup is null ? null : new SessionRangeEvent(SessionRangeEventKind.Setup, setup);
    }

    private SessionRangeSetup? Evaluate(DayWindow day, DateTime closeUtc, Bias bias, double? atrD1, double? atrH4)
    {
        if (closeUtc >= day.LastEntryUtc
            || _rangeBars < SessionRangeParameters.MinRangeBars
            || day.TradingDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            || bias == Bias.Neutral
            || !IsPositiveFinite(atrD1)
            || !IsPositiveFinite(atrH4))
        {
            return null;
        }

        double threshold = _parameters.MinWidthAtr * atrD1.GetValueOrDefault();
        if (PriceMath.LessThan(_rangeHigh - _rangeLow, threshold, PriceMath.DefaultTolerance))
        {
            return null;
        }

        return Levels(day, closeUtc, bias == Bias.Long ? Side.Buy : Side.Sell, atrH4.GetValueOrDefault());
    }

    private SessionRangeSetup? Levels(DayWindow day, DateTime closeUtc, Side side, double atrH4)
    {
        double entry;
        double stop;
        double target;
        string prefix;
        if (side == Side.Buy)
        {
            entry = _rangeHigh;
            stop = entry - (_parameters.StopAtrMult * atrH4);
            target = entry + (_rewardRisk * (entry - stop));
            prefix = "RH-";
        }
        else
        {
            entry = _rangeLow;
            stop = entry + (_parameters.StopAtrMult * atrH4);
            target = entry - (_rewardRisk * (stop - entry));
            prefix = "RL-";
        }

        if (!IsPositiveFinite(entry) || !IsPositiveFinite(stop) || !IsPositiveFinite(target))
        {
            return null;
        }

        DateTime expiryUtc = day.LastEntryUtc;
        if (closeUtc.DayOfWeek == DayOfWeek.Friday)
        {
            DateTime fridayCapUtc = closeUtc.Date + _fridayNoEntryUtc;
            if (fridayCapUtc < expiryUtc)
            {
                expiryUtc = fridayCapUtc;
            }
        }

        string id = prefix + day.TradingDate.ToString(IdDateFormat, CultureInfo.InvariantCulture);
        return new SessionRangeSetup(id, side, entry, stop, target, expiryUtc, _rangeHigh, _rangeLow, day.TradingDate);
    }

    // SIG-033: a bar that opened at or after RangeEndNy and reached the other edge of the range voids the watched setup.
    private SessionRangeEvent? CheckVoid(Bar bar, DayWindow day)
    {
        SessionRangeSetup? watched = _watched;
        if (watched is null || bar.OpenTimeUtc < day.RangeEndUtc)
        {
            return null;
        }

        bool reached = watched.Side == Side.Buy
            ? !PriceMath.GreaterThan(bar.Low, watched.RangeLow, PriceMath.DefaultTolerance)
            : !PriceMath.LessThan(bar.High, watched.RangeHigh, PriceMath.DefaultTolerance);
        if (!reached)
        {
            return null;
        }

        _watched = null;
        return new SessionRangeEvent(SessionRangeEventKind.Void, watched);
    }

    private static bool IsPositiveFinite(double? value)
    {
        return value.HasValue && double.IsFinite(value.Value) && value.Value > 0.0;
    }

    private sealed record DayWindow(DateTime TradingDate, DateTime RangeStartUtc, DateTime RangeEndUtc, DateTime LastEntryUtc);
}
