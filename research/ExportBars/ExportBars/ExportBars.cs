// Research helper for the strategy search (owner's go of 2026-11-23). NOT part of the trading bot.
// It only reads bars and writes them out. It places no order, changes no order and reads no
// account data. See research/README.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using cAlgo.API;

namespace ZoneFvg.Research
{
    /// <summary>
    /// Writes the closed bars of the chart's symbol to files in the folder cTrader gives every
    /// cBot (no elevated access rights), or to the log when files cannot be written.
    /// One row per closed bar: bar open time in UTC, open, high, low, close, tick volume.
    /// Series written: the chart's time frame, and m15, h4 and D1 as the platform reports them.
    /// A bar that is still forming is never written.
    /// </summary>
    [Robot(AccessRights = AccessRights.None, TimeZone = TimeZones.UTC)]
    public class ExportBars : Robot
    {
        private const string FilePrefix = "ExportBars_";
        private const string Header = "time_utc,open,high,low,close,tick_volume";
        private const int LogBarsPerLine = 12;
        private const int FlushEveryRows = 20000;

        private readonly List<BarOutput> _series = new List<BarOutput>();
        private readonly StringBuilder _row = new StringBuilder(96);
        private bool _toLog;
        private string _priceFormat = "F5";
        private string _folder = string.Empty;

        /// <summary>Tick only if the file export fails: the bars are then printed to the log.</summary>
        [Parameter("Print bars to the log instead of files", DefaultValue = false)]
        public bool UseLog { get; set; }

        /// <summary>Opens the outputs and writes the bars that are already closed.</summary>
        protected override void OnStart()
        {
            _priceFormat = "F" + Symbol.Digits.ToString(CultureInfo.InvariantCulture);

            string info;
            try
            {
                info = InfoLine();
            }
            catch (Exception e)
            {
                info = "XI symbol=" + SymbolName + " infoError=" + e.GetType().Name;
            }

            Say(info);

            var chartName = this.TimeFrame.ToString();
            _series.Add(new BarOutput(Bars, SymbolName, chartName, true));
            AddExtra(TimeFrame.Minute15, chartName);
            AddExtra(TimeFrame.Hour4, chartName);
            AddExtra(TimeFrame.Daily, chartName);

            _toLog = UseLog;
            if (!_toLog && !TryOpenFiles(info, true) && !TryOpenFiles(info, false))
            {
                _toLog = true;
            }

            if (_toLog)
            {
                PrepareLogMode();
            }
            else
            {
                Say("XF files are written to " + _folder + " - let the backtest run to its end");
            }

            WriteClosedBars();
        }

        /// <summary>Called when a new chart bar opens: writes every bar that has closed since.</summary>
        protected override void OnBar()
        {
            WriteClosedBars();
        }

        /// <summary>Writes the last closed bars, closes the files and prints one summary line per series.</summary>
        protected override void OnStop()
        {
            WriteClosedBars();
            var c = CultureInfo.InvariantCulture;
            foreach (var s in _series)
            {
                if (_toLog)
                {
                    PrintLine(s);
                }

                var target = _toLog ? (s.Enabled ? "log" : "skipped") : s.FileName;
                Say("XS " + s.Name
                    + " rows=" + s.Rows.ToString(c)
                    + " first=" + (s.Rows > 0 ? s.FirstTime.ToString("yyyy-MM-dd HH:mm", c) : "none")
                    + " last=" + (s.Rows > 0 ? s.LastTime.ToString("yyyy-MM-dd HH:mm", c) : "none")
                    + " to=" + target);
            }

            CloseWriters();
            Say(_toLog
                ? "XDONE " + SymbolName + " - save this log and send it"
                : "XDONE " + SymbolName + " - files are in " + _folder);
        }

        private static StreamWriter OpenWriter(string fileName, bool compress)
        {
            var file = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.Read);
            try
            {
                Stream stream = compress ? new GZipStream(file, CompressionLevel.Optimal) : file;
                var writer = new StreamWriter(stream, new UTF8Encoding(false), 1 << 16);
                writer.WriteLine(Header);
                writer.Flush();
                return writer;
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }

        private static string Clean(string text)
        {
            var result = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    result.Append(ch);
                }
            }

            return result.ToString();
        }

        // Time frames whose bar count is small enough to print to the log.
        private static bool FitsLog(string timeFrameName)
        {
            switch (timeFrameName)
            {
                case "Minute15":
                case "Minute20":
                case "Minute30":
                case "Minute45":
                case "Hour":
                case "Hour2":
                case "Hour3":
                case "Hour4":
                case "Hour6":
                case "Hour8":
                case "Hour12":
                case "Daily":
                case "Day2":
                case "Day3":
                case "Weekly":
                case "Monthly":
                    return true;
                default:
                    return false;
            }
        }

        private void AddExtra(TimeFrame timeFrame, string chartName)
        {
            var name = timeFrame.ToString();
            if (name == chartName)
            {
                return;
            }

            try
            {
                var bars = MarketData.GetBars(timeFrame);
                if (bars == null)
                {
                    Say("XE " + name + " bars are not available");
                    return;
                }

                _series.Add(new BarOutput(bars, SymbolName, name, false));
            }
            catch (Exception e)
            {
                Say("XE " + name + " bars are not available: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private string InfoLine()
        {
            var c = CultureInfo.InvariantCulture;
            return "XI symbol=" + SymbolName
                + " chart=" + this.TimeFrame
                + " backtest=" + (IsBacktesting ? "yes" : "no")
                + " time=" + Server.TimeInUtc.ToString("yyyy-MM-dd HH:mm:ss", c)
                + " pipSize=" + Symbol.PipSize.ToString("R", c)
                + " tickSize=" + Symbol.TickSize.ToString("R", c)
                + " digits=" + Symbol.Digits.ToString(c)
                + " lotSize=" + Symbol.LotSize.ToString(c)
                + " bid=" + Symbol.Bid.ToString("R", c)
                + " ask=" + Symbol.Ask.ToString("R", c)
                + " commission=" + Symbol.Commission.ToString("R", c)
                + " commissionType=" + Symbol.CommissionType
                + " swapLong=" + Symbol.SwapLong.ToString("R", c)
                + " swapShort=" + Symbol.SwapShort.ToString("R", c)
                + " swapType=" + Symbol.SwapCalculationType;
        }

        // Relative file names: cTrader resolves them to the folder every cBot may write to
        // without elevated access rights.
        private bool TryOpenFiles(string info, bool compress)
        {
            try
            {
                _folder = Path.GetFullPath(".");
                // System.IO.File by its full name: cAlgo.API has a File type too.
                System.IO.File.WriteAllText(FilePrefix + SymbolName + "_info.txt", info + Environment.NewLine);
                foreach (var s in _series)
                {
                    s.FileName = FilePrefix + s.Name + (compress ? ".csv.gz" : ".csv");
                    s.Writer = OpenWriter(s.FileName, compress);
                }

                return true;
            }
            catch (Exception e)
            {
                Say("XE files cannot be written (" + (compress ? "gzip" : "plain") + "): "
                    + e.GetType().Name + ": " + e.Message);
                CloseWriters();
                return false;
            }
        }

        private void PrepareLogMode()
        {
            foreach (var s in _series)
            {
                if (s.IsChart && !FitsLog(s.TimeFrameName))
                {
                    s.Enabled = false;
                    Say("XE the chart time frame " + s.TimeFrameName
                        + " has too many bars for the log and is skipped; m15, h4 and D1 are printed");
                }
            }

            Say("XL bars are printed to this log - when the backtest ends, save the log and send it");
        }

        private void WriteClosedBars()
        {
            foreach (var s in _series)
            {
                if (!s.Enabled)
                {
                    continue;
                }

                var bars = s.Bars;

                // The last bar of a series is still forming and is never written.
                var closed = bars.Count - 1;
                while (s.Next < closed)
                {
                    var i = s.Next;
                    s.Next = i + 1;
                    var time = bars.OpenTimes[i];
                    if (time <= s.LastTime)
                    {
                        continue;
                    }

                    WriteBar(
                        s,
                        time,
                        bars.OpenPrices[i],
                        bars.HighPrices[i],
                        bars.LowPrices[i],
                        bars.ClosePrices[i],
                        bars.TickVolumes[i]);
                }
            }
        }

        private void WriteBar(BarOutput s, DateTime time, double open, double high, double low, double close, double volume)
        {
            var c = CultureInfo.InvariantCulture;
            _row.Clear();
            _row.Append(time.ToString(_toLog ? "yyMMddHHmm" : "yyyy-MM-dd HH:mm", c)).Append(',')
                .Append(open.ToString(_priceFormat, c)).Append(',')
                .Append(high.ToString(_priceFormat, c)).Append(',')
                .Append(low.ToString(_priceFormat, c)).Append(',')
                .Append(close.ToString(_priceFormat, c)).Append(',')
                .Append(double.IsNaN(volume) ? "0" : ((long)Math.Round(volume)).ToString(c));

            if (s.Rows == 0)
            {
                s.FirstTime = time;
            }

            s.LastTime = time;
            s.Rows++;

            if (_toLog)
            {
                if (s.LineBars > 0)
                {
                    s.Line.Append(';');
                }

                s.Line.Append(_row);
                s.LineBars++;
                if (s.LineBars >= LogBarsPerLine)
                {
                    PrintLine(s);
                }

                return;
            }

            var writer = s.Writer;
            if (writer == null)
            {
                return;
            }

            writer.WriteLine(_row.ToString());

            // A flush now and then, so that a run that is cut off still leaves most rows on disk.
            if (s.Rows % FlushEveryRows == 0)
            {
                writer.Flush();
            }
        }

        private void PrintLine(BarOutput s)
        {
            if (s.LineBars == 0)
            {
                return;
            }

            Say("XB " + s.Name + " " + s.Line);
            s.Line.Clear();
            s.LineBars = 0;
        }

        private void CloseWriters()
        {
            foreach (var s in _series)
            {
                var writer = s.Writer;
                s.Writer = null;
                if (writer == null)
                {
                    continue;
                }

                try
                {
                    writer.Dispose();
                }
                catch (Exception e)
                {
                    Say("XE closing " + s.FileName + ": " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        // Print with a format string, so that braces in a message cannot break the call.
        private void Say(string text)
        {
            Print("{0}", text);
        }

        private sealed class BarOutput
        {
            public BarOutput(Bars bars, string symbolName, string timeFrameName, bool isChart)
            {
                Bars = bars;
                TimeFrameName = timeFrameName;
                Name = Clean(symbolName) + "_" + Clean(timeFrameName);
                IsChart = isChart;
            }

            public Bars Bars { get; }

            public string TimeFrameName { get; }

            public string Name { get; }

            public bool IsChart { get; }

            public bool Enabled { get; set; } = true;

            public int Next { get; set; }

            public long Rows { get; set; }

            public DateTime FirstTime { get; set; }

            public DateTime LastTime { get; set; } = DateTime.MinValue;

            public string FileName { get; set; } = string.Empty;

            public StreamWriter? Writer { get; set; }

            public StringBuilder Line { get; } = new StringBuilder(1024);

            public int LineBars { get; set; }
        }
    }
}
