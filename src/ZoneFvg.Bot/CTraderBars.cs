using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Recovery;
using ApiBar = cAlgo.API.Bar;
using ApiTimeFrame = cAlgo.API.TimeFrame;
using CoreBar = ZoneFvg.Core.Domain.Bar;
using CoreTimeFrame = ZoneFvg.Core.Domain.TimeFrame;

namespace ZoneFvg.Bot
{
    /// <summary>
    /// Closed-bar feed of the D1, H4 and M15 series (architecture section 1; SIG-001, SIG-004).
    /// </summary>
    /// <remarks>
    /// <para>A bar counts as closed when its nominal close time (open time plus 15 minutes, 4 hours
    /// or 24 hours, <see cref="BarIntakeOrder.CloseTimeUtc"/>) is at or before the server time. No
    /// tick after that moment can belong to the bar, so this never passes a forming bar, whatever
    /// the series' last index holds when an event fires, and it also releases Friday's last bars
    /// while the market is closed. From a bar-closed event the server time is used as is; from the
    /// timer a bar must have closed at least one minute earlier (<see cref="PumpOnTimer"/>).</para>
    /// <para>Bars are passed to the core in (nominal close time, D1/H4/M15 rank) order, never in
    /// the order their events arrive (architecture section 10, "Live bar order"). The only state
    /// kept is the open time of the last bar passed per timeframe, so that each bar is passed once;
    /// the core ignores a repeated bar in any case.</para>
    /// <para>Bar times are in the cBot's time zone, which is UTC (PLT-003); they are given kind UTC.</para>
    /// </remarks>
    internal sealed class CTraderBars : IBarHistory
    {
        private const int MaxLoadRounds = 50;

        /// <summary>Delay after a nominal close before the timer passes the bar (see <see cref="PumpOnTimer"/>).</summary>
        private static readonly TimeSpan TimerSettle = TimeSpan.FromMinutes(1.0);

        private readonly Bars _d1;
        private readonly Bars _h4;
        private readonly Bars _m15;
        private readonly Func<DateTime> _serverTimeUtc;
        private readonly AdapterLog _log;
        private readonly DateTime?[] _lastPassedOpen = new DateTime?[3];
        private bool _firstPassLogged;

        /// <summary>Creates the feed over the three series (SIG-001).</summary>
        /// <param name="d1">Daily bars from <c>MarketData.GetBars(TimeFrame.Daily)</c>.</param>
        /// <param name="h4">Four-hour bars.</param>
        /// <param name="m15">Fifteen-minute bars.</param>
        /// <param name="serverTimeUtc">Server time with kind UTC (REC-012).</param>
        /// <param name="log">Masked log.</param>
        public CTraderBars(Bars d1, Bars h4, Bars m15, Func<DateTime> serverTimeUtc, AdapterLog log)
        {
            _d1 = d1 ?? throw new ArgumentNullException(nameof(d1));
            _h4 = h4 ?? throw new ArgumentNullException(nameof(h4));
            _m15 = m15 ?? throw new ArgumentNullException(nameof(m15));
            _serverTimeUtc = serverTimeUtc ?? throw new ArgumentNullException(nameof(serverTimeUtc));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>The cTrader timeframe that serves a core timeframe (SIG-001).</summary>
        /// <param name="timeFrame">D1, H4 or M15.</param>
        /// <returns>Daily, Hour4 or Minute15.</returns>
        public static ApiTimeFrame ToApi(CoreTimeFrame timeFrame)
        {
            // TimeFrame.Daily, Hour4, Minute15: https://help.ctrader.com/ctrader-algo/references/Period/TimeFrame/
            switch (timeFrame)
            {
                case CoreTimeFrame.D1:
                    return ApiTimeFrame.Daily;
                case CoreTimeFrame.H4:
                    return ApiTimeFrame.Hour4;
                case CoreTimeFrame.M15:
                    return ApiTimeFrame.Minute15;
                default:
                    throw new ArgumentOutOfRangeException(nameof(timeFrame), "Unknown timeframe.");
            }
        }

        /// <summary>
        /// The most recent closed bars of the timeframe, oldest first, at most <paramref name="count"/>
        /// (REC-003). Never a forming bar.
        /// </summary>
        /// <param name="timeFrame">D1, H4 or M15.</param>
        /// <param name="count">Largest number of bars wanted.</param>
        /// <returns>The closed bars, oldest first.</returns>
        public IReadOnlyList<CoreBar> ClosedBars(CoreTimeFrame timeFrame, int count)
        {
            DateTime now = _serverTimeUtc();
            Bars bars = Series(timeFrame);
            var newestFirst = new List<CoreBar>();

            // Bars.Count and the indexer Bars[int]: https://help.ctrader.com/ctrader-algo/references/MarketData/Bars/Bars/
            for (int i = bars.Count - 1; i >= 0 && newestFirst.Count < count; i--)
            {
                ApiBar raw = bars[i];
                if (IsClosed(timeFrame, raw, now))
                {
                    newestFirst.Add(Convert(raw));
                }
            }

            newestFirst.Reverse();
            return newestFirst.AsReadOnly();
        }

        /// <summary>
        /// Loads older history until at least <paramref name="count"/> closed bars are available or
        /// the platform has no more (REC-003).
        /// </summary>
        /// <param name="timeFrame">D1, H4 or M15.</param>
        /// <param name="count">Number of closed bars wanted.</param>
        public void RequestMore(CoreTimeFrame timeFrame, int count)
        {
            Bars bars = Series(timeFrame);
            for (int round = 0; round < MaxLoadRounds && bars.Count < count + 1; round++)
            {
                // Bars.LoadMoreHistory() returns the number of bars added at the beginning:
                // https://help.ctrader.com/ctrader-algo/references/MarketData/Bars/Bars/
                if (bars.LoadMoreHistory() <= 0)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Calls <paramref name="handler"/> whenever any of the three series reports a closed bar
        /// (SIG-001). The handler pumps; the event's bar itself is not used, because the pump passes
        /// every closed bar in (close time, rank) order.
        /// </summary>
        /// <param name="handler">The handler.</param>
        public void OnAnyBarClosed(Action handler)
        {
            // Bars.BarClosed (Action<BarClosedEventArgs>): https://help.ctrader.com/ctrader-algo/references/MarketData/Bars/Bars/
            _d1.BarClosed += _ => handler();
            _h4.BarClosed += _ => handler();
            _m15.BarClosed += _ => handler();
        }

        /// <summary>
        /// Marks every bar closed now as already passed. Called right after the core's start or
        /// reconnect recovery, which has read those bars itself (REC-002).
        /// </summary>
        public void SkipToLatest()
        {
            foreach (CoreTimeFrame timeFrame in BarIntakeOrder.SameInstant)
            {
                IReadOnlyList<CoreBar> latest = ClosedBars(timeFrame, 1);
                if (latest.Count == 1)
                {
                    _lastPassedOpen[BarIntakeOrder.Rank(timeFrame)] = latest[0].OpenTimeUtc;
                }
            }

            _firstPassLogged = false;
        }

        /// <summary>
        /// Called from a bar-closed event: a new bar has just begun with a tick, so every tick before
        /// that moment has been applied and every bar whose nominal close is at or before the server
        /// time is complete (SIG-001).
        /// </summary>
        /// <param name="core">The core.</param>
        public void PumpOnBarEvent(BotCore core)
        {
            Pump(core, _serverTimeUtc());
        }

        /// <summary>
        /// Called from the one-minute timer, when no new tick may have arrived yet. A bar is passed
        /// only when its nominal close is at least <see cref="TimerSettle"/> in the past, so that a
        /// tick from just before the close that is still on its way cannot be missed. Mostly needed
        /// for the last bars before the weekend, which no new bar follows until the market reopens.
        /// </summary>
        /// <param name="core">The core.</param>
        public void PumpOnTimer(BotCore core)
        {
            Pump(core, _serverTimeUtc() - TimerSettle);
        }

        /// <summary>
        /// Passes every bar that has closed by <paramref name="closedBy"/> and was not passed before,
        /// ordered by nominal close time and then D1, H4, M15 (SIG-004, architecture section 10).
        /// </summary>
        private void Pump(BotCore core, DateTime closedBy)
        {
            DateTime now = closedBy;
            var ready = new List<(DateTime Close, int Rank, CoreTimeFrame TimeFrame, CoreBar Bar)>();
            foreach (CoreTimeFrame timeFrame in BarIntakeOrder.SameInstant)
            {
                CollectNewClosed(timeFrame, now, ready);
            }

            ready.Sort((a, b) => a.Close != b.Close ? a.Close.CompareTo(b.Close) : a.Rank.CompareTo(b.Rank));
            foreach ((DateTime close, int rank, CoreTimeFrame timeFrame, CoreBar bar) in ready)
            {
                _lastPassedOpen[rank] = bar.OpenTimeUtc;
                LogFirstPass(timeFrame, bar, close, now);
                core.OnBarClosed(timeFrame, bar);
            }
        }

        private void CollectNewClosed(CoreTimeFrame timeFrame, DateTime now, List<(DateTime, int, CoreTimeFrame, CoreBar)> ready)
        {
            Bars bars = Series(timeFrame);
            int rank = BarIntakeOrder.Rank(timeFrame);
            DateTime? last = _lastPassedOpen[rank];
            var newestFirst = new List<(DateTime, int, CoreTimeFrame, CoreBar)>();
            for (int i = bars.Count - 1; i >= 0; i--)
            {
                ApiBar raw = bars[i];
                if (last.HasValue && OpenTimeUtc(raw) <= last.Value)
                {
                    break;
                }

                if (IsClosed(timeFrame, raw, now))
                {
                    CoreBar bar = Convert(raw);
                    newestFirst.Add((BarIntakeOrder.CloseTimeUtc(timeFrame, bar), rank, timeFrame, bar));
                }

                if (!last.HasValue && newestFirst.Count == 1)
                {
                    // Nothing passed yet (no closed bar at start): only the newest closed bar is passed.
                    break;
                }
            }

            newestFirst.Reverse();
            ready.AddRange(newestFirst);
        }

        private void LogFirstPass(CoreTimeFrame timeFrame, CoreBar bar, DateTime close, DateTime now)
        {
            if (_firstPassLogged)
            {
                return;
            }

            _firstPassLogged = true;
            _log.Info("first live bar after start: " + timeFrame + " open " + Stamp(bar.OpenTimeUtc) + " close " + Stamp(close) + " passed for time " + Stamp(now));
        }

        private Bars Series(CoreTimeFrame timeFrame)
        {
            switch (timeFrame)
            {
                case CoreTimeFrame.D1:
                    return _d1;
                case CoreTimeFrame.H4:
                    return _h4;
                case CoreTimeFrame.M15:
                    return _m15;
                default:
                    throw new ArgumentOutOfRangeException(nameof(timeFrame), "Unknown timeframe.");
            }
        }

        private static DateTime OpenTimeUtc(ApiBar bar)
        {
            // Bar.OpenTime: https://help.ctrader.com/ctrader-algo/references/MarketData/Bars/Bar/
            // Times are in the robot's time zone, UTC (PLT-003), so only the kind is set.
            return DateTime.SpecifyKind(bar.OpenTime, DateTimeKind.Utc);
        }

        private static bool IsClosed(CoreTimeFrame timeFrame, ApiBar bar, DateTime now)
        {
            // Only the open time is read here; the prices of a bar are read once it has closed (SIG-001).
            return OpenTimeUtc(bar) + BarIntakeOrder.Length(timeFrame) <= now;
        }

        private static CoreBar Convert(ApiBar bar)
        {
            // Bar.Open, High, Low, Close: https://help.ctrader.com/ctrader-algo/references/MarketData/Bars/Bar/
            return new CoreBar(OpenTimeUtc(bar), bar.Open, bar.High, bar.Low, bar.Close);
        }

        private static string Stamp(DateTime utc)
        {
            return utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }
}
