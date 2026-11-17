using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using BotCore = ZoneFvg.Core.App.BotCore;
using Fvg = ZoneFvg.Core.Domain.Fvg;
using Zone = ZoneFvg.Core.Domain.Zone;
using ZoneType = ZoneFvg.Core.Domain.ZoneType;

namespace ZoneFvg.Bot
{
    /// <summary>
    /// Optional chart drawings of the current zones and of the fair value gaps confirmed in the
    /// last 24 hours (PLT-005, spec 9.4). Draws only when DrawObjects is true, the run is not a
    /// silent backtest or an optimisation, and a chart exists. Every chart call is wrapped: the
    /// first failure is logged once through the masked log and switches drawing off for the rest
    /// of the run, so a missing or headless chart can never throw into the trading code.
    /// </summary>
    /// <remarks>
    /// The painter only reads the core's read-only snapshots (<see cref="BotCore.ZoneSnapshot"/>,
    /// <see cref="BotCore.FvgSnapshot"/>), which never change the core's state. When drawing is
    /// off it does not read them at all. It keeps no copy of them: each paint draws what the
    /// snapshots hold and removes every other drawing of this cBot. Because cTrader does not list
    /// non-interactive drawings in <c>Chart.Objects</c>, the painter remembers the names it drew in
    /// its previous paint and removes by name every remembered name it does not draw again
    /// (PLT-005, owner acceptance of 2026-11-17). The names are drawing state only; no trading
    /// decision reads them.
    /// </remarks>
    internal sealed class ChartPainter
    {
        private const string Prefix = "ZFVG ";

        // ChartArea.DrawRectangle(string, DateTime, double, DateTime, double, Color) returns ChartRectangle:
        // https://help.ctrader.com/ctrader-algo/references/Chart/ChartArea/
        // ChartRectangle / ChartShape.IsFilled: https://help.ctrader.com/ctrader-algo/references/Chart/Drawings/Shapes/ChartRectangle
        private readonly ChartArea? _chart;
        private readonly Func<DateTime> _serverTimeUtc;
        private readonly AdapterLog _log;
        private bool _enabled;

        // PLT-005: names drawn in the previous paint, so that a drawing no longer in the snapshots is removed by name.
        private HashSet<string> _drawn = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Creates the painter (PLT-005).</summary>
        /// <param name="chart">The cBot's chart, or null when there is none.</param>
        /// <param name="enabled">True when DrawObjects is true and the running mode shows a chart.</param>
        /// <param name="serverTimeUtc">The broker's server time, the right edge of the zone rectangles.</param>
        /// <param name="log">Masked log.</param>
        public ChartPainter(ChartArea? chart, bool enabled, Func<DateTime> serverTimeUtc, AdapterLog log)
        {
            _chart = chart;
            _serverTimeUtc = serverTimeUtc ?? throw new ArgumentNullException(nameof(serverTimeUtc));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _enabled = enabled && chart != null;
            if (enabled && chart == null)
            {
                _log.Info("DrawObjects is true, but there is no chart; nothing is drawn.");
            }
        }

        /// <summary>
        /// Draws the core's current zones and recent gaps and removes this cBot's drawings that
        /// are no longer in the snapshots (PLT-005). Never throws.
        /// </summary>
        /// <param name="core">The core whose snapshots are drawn.</param>
        public void Paint(BotCore core)
        {
            if (!_enabled || _chart == null)
            {
                return;
            }

            try
            {
                _drawn = Draw(_chart, core.ZoneSnapshot, core.FvgSnapshot, _serverTimeUtc(), _drawn);
            }
            catch (Exception exception)
            {
                _enabled = false;
                _log.Error("chart drawing (drawing switched off for this run)", exception);
            }
        }

        private static HashSet<string> Draw(ChartArea chart, IReadOnlyList<Zone> zones, IReadOnlyList<Fvg> fvgs, DateTime nowUtc, HashSet<string> previous)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Zone zone in zones)
            {
                names.Add(DrawZone(chart, zone, nowUtc));
            }

            foreach (Fvg fvg in fvgs)
            {
                names.Add(DrawFvg(chart, fvg));
            }

            // ChartArea.Objects (IReadonlyList<ChartObject>), ChartObject.Name and ChartArea.RemoveObject(string):
            // https://help.ctrader.com/ctrader-algo/references/Chart/ChartArea/ and
            // https://help.ctrader.com/ctrader-algo/guides/ui-operations/chart-objects/ (removal over a copied collection)
            foreach (ChartObject drawing in chart.Objects.ToArray())
            {
                string name = drawing.Name;
                if (name.StartsWith(Prefix, StringComparison.Ordinal) && !names.Contains(name))
                {
                    chart.RemoveObject(name);
                }
            }

            // Non-interactive drawings are not in Chart.Objects ("If a chart object is not interactive, it will also be
            // invisible in lists and collections of chart objects."), so the names of the previous paint are removed
            // by name: https://help.ctrader.com/ctrader-algo/guides/ui-operations/chart-objects/
            foreach (string name in previous.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (!names.Contains(name))
                {
                    chart.RemoveObject(name);
                }
            }

            return names;
        }

        private static string DrawZone(ChartArea chart, Zone zone, DateTime untilUtc)
        {
            // A zone is drawn from its activation time to the server time.
            // Color.FromArgb(int alpha, int red, int green, int blue): https://help.ctrader.com/ctrader-algo/references/Utility/Color/
            string name = Prefix + "zone " + zone.Id;
            Color color = zone.Type == ZoneType.Demand ? Color.FromArgb(60, 0, 160, 0) : Color.FromArgb(60, 200, 0, 0);
            ChartRectangle rectangle = chart.DrawRectangle(name, zone.ActivationTimeUtc, zone.Proximal, untilUtc, zone.Distal, color);
            rectangle.IsFilled = true;
            return name;
        }

        private static string DrawFvg(ChartArea chart, Fvg fvg)
        {
            // A gap is drawn over its three M15 bars: from the open of c3 to the close of c1.
            string name = Prefix + "fvg " + fvg.C1OpenUtc.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
            DateTime c1CloseUtc = fvg.C1OpenUtc.AddMinutes(15.0);
            ChartRectangle rectangle = chart.DrawRectangle(name, fvg.C3OpenUtc, fvg.Upper, c1CloseUtc, fvg.Lower, Color.FromArgb(60, 30, 90, 200));
            rectangle.IsFilled = true;
            return name;
        }
    }
}
