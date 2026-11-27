using System;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The signal logic of the Overnight mode (spec section 3.10, SIG-043). It is given closed M15
/// bars one at a time with the ATR(D1) of the last closed D1 bar and the ATR(M15) of the bar, and
/// gives at most one buy setup per trading day (the trading day and New York time of spec 3.8,
/// <see cref="NewYorkClock"/>). It keeps only the state of the trading day of the last bar, reads
/// no clock and knows nothing of spread, news or orders.
/// </summary>
public sealed class OvernightTracker
{
    private readonly OvernightParameters _parameters;

    // Open time of the last bar received; bars must arrive in strictly increasing open time.
    private DateTime? _lastOpenUtc;

    // The trading day of the last bar, null until the first bar.
    private DateTime? _tradingDate;

    // True once the trading day's evaluation bar (the first bar closing at or after NightEntryNy) was received.
    private bool _evaluated;

    /// <summary>Creates a tracker with no trading day (SIG-043).</summary>
    /// <param name="parameters">The Overnight parameters. They are validated first.</param>
    /// <exception cref="ArgumentNullException">The parameters are null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public OvernightTracker(OvernightParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        _parameters = parameters;
    }

    /// <summary>The date that names the trading day of the last bar received (SIG-030), or null before the first bar.</summary>
    public DateTime? TradingDate => _tradingDate;

    /// <summary>The setup of the trading day of the last bar received (SIG-043), or null when it has none (yet).</summary>
    public OvernightSetup? Setup { get; private set; }

    /// <summary>
    /// True when the trading day named by the date gives a signal (SIG-043): its 17:00 New York
    /// start falls on a Monday, Tuesday, Wednesday or Thursday, that is the trading date is a
    /// Tuesday to a Friday.
    /// </summary>
    /// <param name="tradingDate">The date that names the trading day.</param>
    /// <returns>True for a trading day that starts on a Monday to Thursday evening.</returns>
    public static bool TradesOn(DateTime tradingDate)
    {
        DayOfWeek start = tradingDate.AddDays(-1.0).DayOfWeek;
        return start is DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday or DayOfWeek.Thursday;
    }

    /// <summary>
    /// A closed M15 bar (SIG-043). The bar belongs to the trading day of its open time; the first
    /// bar of a new trading day forgets the old day. The first bar of the trading day whose close
    /// time is at or after NightEntryNy is the evaluation bar, and the day gives no further setup
    /// whatever it returns: a setup when its close time is before NightLatestNy, the trading day
    /// starts on a Monday to Thursday evening, both ATRs are positive finite numbers and the levels
    /// are positive finite numbers; otherwise null. Every other bar returns null.
    /// </summary>
    /// <param name="bar">The closed M15 bar. It must open after the last bar received.</param>
    /// <param name="atrD1">ATR(D1) of the last closed D1 bar, or null when there is none.</param>
    /// <param name="atrM15">ATR(M15) of the last closed M15 bar (this bar), or null when there is none.</param>
    /// <returns>The setup, or null.</returns>
    /// <exception cref="ArgumentNullException">The bar is null.</exception>
    /// <exception cref="InvalidOperationException">The bar does not open after the last bar received; nothing is changed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A time of the bar's trading day is outside the range of <see cref="DateTime"/>; nothing is changed.</exception>
    public OvernightSetup? OnM15Closed(Bar bar, double? atrD1, double? atrM15)
    {
        ArgumentNullException.ThrowIfNull(bar);
        if (_lastOpenUtc.HasValue && bar.OpenTimeUtc <= _lastOpenUtc.Value)
        {
            throw new InvalidOperationException("An M15 bar opening at " + Invariant.Minute(bar.OpenTimeUtc) + " does not open after the last bar of " + Invariant.Minute(_lastOpenUtc.Value) + ".");
        }

        // Everything that can throw is computed before the state changes.
        DateTime closeUtc = BarIntakeOrder.CloseTimeUtc(TimeFrame.M15, bar);
        DateTime tradingDate = NewYorkClock.TradingDate(bar.OpenTimeUtc);
        DateTime entryUtc = NewYorkClock.UtcInTradingDay(tradingDate, _parameters.NightEntryNy);
        DateTime latestUtc = NewYorkClock.UtcInTradingDay(tradingDate, _parameters.NightLatestNy);
        if (_tradingDate != tradingDate)
        {
            _tradingDate = tradingDate;
            _evaluated = false;
            Setup = null;
        }

        _lastOpenUtc = bar.OpenTimeUtc;
        if (_evaluated || closeUtc < entryUtc)
        {
            return null;
        }

        _evaluated = true;
        if (closeUtc >= latestUtc || !TradesOn(tradingDate) || !IsPositiveFinite(atrD1) || !IsPositiveFinite(atrM15))
        {
            return null;
        }

        Setup = Levels(bar, tradingDate, closeUtc, atrD1!.Value, atrM15!.Value);
        return Setup;
    }

    private OvernightSetup? Levels(Bar bar, DateTime tradingDate, DateTime closeUtc, double atrD1, double atrM15)
    {
        double entry = bar.Close + (_parameters.NightEntryBufferAtr * atrM15);
        double stop = entry - (_parameters.NightStopAtr * atrD1);
        double target = entry + (_parameters.NightTargetR * (entry - stop));
        if (!IsPositiveFinite(entry) || !IsPositiveFinite(stop) || !IsPositiveFinite(target))
        {
            return null;
        }

        DateTime expiryUtc = closeUtc.AddMinutes(_parameters.NightOrderMinutes);
        return new OvernightSetup(OvernightId.For(tradingDate), entry, stop, target, expiryUtc, closeUtc, bar.Close, tradingDate);
    }

    private static bool IsPositiveFinite(double? value)
    {
        return value.HasValue && double.IsFinite(value.Value) && value.Value > 0.0;
    }
}
