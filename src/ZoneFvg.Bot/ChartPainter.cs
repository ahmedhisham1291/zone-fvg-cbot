using System;
using System.Globalization;
using cAlgo.API;
using Fvg = ZoneFvg.Core.Domain.Fvg;
using Zone = ZoneFvg.Core.Domain.Zone;
using ZoneType = ZoneFvg.Core.Domain.ZoneType;

namespace ZoneFvg.Bot
{
    /// <summary>
    /// Optional chart drawings of zones and fair value gaps (PLT-005, spec 9.4). Draws only when
    /// DrawObjects is true and a chart exists. Every chart call is wrapped: the first failure is
    /// logged once and switches drawing off for the rest of the run, so a missing or headless chart
    /// can never throw into the trading code.
    /// </summary>
    /// <remarks>
    /// Not yet fed: the core's public surface (<c>BotCore</c>) does not expose its zones or gaps,
    /// so nothing calls <see cref="DrawZone"/> or <see cref="DrawFvg"/> until the owner decides how
    /// the core should provide them (decision for the owner in pull request WP08; PLT-005 stays
    /// planned).
    /// </remarks>
    internal sealed class ChartPainter
    {
        // ChartArea.DrawRectangle(string, DateTime, double, DateTime, double, Color) returns ChartRectangle:
        // https://help.ctrader.com/ctrader-algo/references/Chart/ChartArea/
        // ChartRectangle / ChartShape.IsFilled: https://help.ctrader.com/ctrader-algo/references/Chart/Drawings/Shapes/ChartRectangle
        private readonly ChartArea? _chart;
        private readonly AdapterLog _log;
        private bool _enabled;

        /// <summary>Creates the painter (PLT-005).</summary>
        /// <param name="chart">The cBot's chart, or null when there is none.</param>
        /// <param name="drawObjects">The DrawObjects parameter.</param>
        /// <param name="log">Masked log.</param>
        public ChartPainter(ChartArea? chart, bool drawObjects, AdapterLog log)
        {
            _chart = chart;
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _enabled = drawObjects && chart != null;
        }

        /// <summary>Draws a zone as a filled rectangle from its activation time to <paramref name="untilUtc"/> (PLT-005).</summary>
        /// <param name="zone">The zone.</param>
        /// <param name="untilUtc">Right edge of the rectangle.</param>
        public void DrawZone(Zone zone, DateTime untilUtc)
        {
            Try(chart =>
            {
                // Color.FromArgb(int alpha, int red, int green, int blue): https://help.ctrader.com/ctrader-algo/references/Utility/Color/
                Color color = zone.Type == ZoneType.Demand ? Color.FromArgb(60, 0, 160, 0) : Color.FromArgb(60, 200, 0, 0);
                ChartRectangle rectangle = chart.DrawRectangle("ZFVG zone " + zone.Id, zone.ActivationTimeUtc, zone.Proximal, untilUtc, zone.Distal, color);
                rectangle.IsFilled = true;
            });
        }

        /// <summary>Draws a fair value gap as a filled rectangle over its three bars (PLT-005).</summary>
        /// <param name="fvg">The gap.</param>
        /// <param name="untilUtc">Right edge of the rectangle.</param>
        public void DrawFvg(Fvg fvg, DateTime untilUtc)
        {
            Try(chart =>
            {
                string name = "ZFVG fvg " + fvg.C2OpenUtc.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
                ChartRectangle rectangle = chart.DrawRectangle(name, fvg.C1OpenUtc, fvg.Upper, untilUtc, fvg.Lower, Color.FromArgb(60, 30, 90, 200));
                rectangle.IsFilled = true;
            });
        }

        private void Try(Action<ChartArea> draw)
        {
            if (!_enabled || _chart == null)
            {
                return;
            }

            try
            {
                draw(_chart);
            }
            catch (Exception exception)
            {
                _enabled = false;
                _log.Error("chart drawing (drawing switched off for this run)", exception);
            }
        }
    }
}
