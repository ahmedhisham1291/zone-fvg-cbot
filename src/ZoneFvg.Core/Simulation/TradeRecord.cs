using System;
using System.Globalization;
using ZoneFvg.Core.Domain;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Simulation;

/// <summary>
/// One closed trade of a backtest trade log (SIM-001). The values are checked on construction and
/// cannot be changed afterwards, so an invalid record cannot exist.
/// </summary>
/// <remarks>
/// <para>Money is in the account currency. <see cref="ResultMoney"/> is the net result: the
/// gross result plus <see cref="Commission"/> plus <see cref="Swap"/>, where a cost is negative.
/// <see cref="RiskMoney"/> is the money that would have been lost at the stop when the trade was
/// opened, without commission, and <see cref="ResultR"/> is <see cref="ResultMoney"/> divided by
/// <see cref="RiskMoney"/> (checked within <see cref="ResultRTolerance"/>).</para>
/// <para>The symbol starts with six capital letters, the base and the quote currency (RSK-005);
/// a broker suffix after them is allowed.</para>
/// </remarks>
public sealed record TradeRecord
{
    /// <summary>Largest allowed difference between <see cref="ResultR"/> and result_money / risk_money, in R (SIM-001).</summary>
    public const double ResultRTolerance = 0.001;

    /// <summary>Creates a record and checks every value (SIM-001).</summary>
    /// <param name="symbol">Symbol name, six capital letters optionally followed by letters, digits, '.' or '_'.</param>
    /// <param name="zoneId">Id of the zone that produced the trade: printable characters without spaces, commas or quotes.</param>
    /// <param name="openTimeUtc">Fill time, kind UTC, whole seconds.</param>
    /// <param name="closeTimeUtc">Close time, kind UTC, whole seconds, not before the open time.</param>
    /// <param name="side">Buy or Sell.</param>
    /// <param name="volumeUnits">Volume in units, greater than zero.</param>
    /// <param name="entry">Entry price, greater than zero.</param>
    /// <param name="stop">Stop price at the open, on the loss side of entry.</param>
    /// <param name="target">Target price at the open, on the profit side of entry.</param>
    /// <param name="exitPrice">Price at which the trade closed, greater than zero.</param>
    /// <param name="exitReason">Why the trade closed, for example Target or Stop: printable characters without spaces, commas or quotes.</param>
    /// <param name="resultMoney">Net result in money, commission and swap included.</param>
    /// <param name="resultR">Net result in R: resultMoney / riskMoney.</param>
    /// <param name="commission">Round-trip commission as charged; zero or negative.</param>
    /// <param name="swap">Swap as charged; negative is a cost.</param>
    /// <param name="riskMoney">Money at risk at the stop when the trade was opened, greater than zero.</param>
    /// <exception cref="ArgumentException">A value is invalid. The message names the column.</exception>
    public TradeRecord(string symbol, string zoneId, DateTime openTimeUtc, DateTime closeTimeUtc, Side side, double volumeUnits, double entry, double stop, double target, double exitPrice, string exitReason, double resultMoney, double resultR, double commission, double swap, double riskMoney)
    {
        string? problem = Problem(symbol, zoneId, openTimeUtc, closeTimeUtc, side, volumeUnits, entry, stop, target, exitPrice, exitReason, resultMoney, resultR, commission, swap, riskMoney);
        if (problem != null)
        {
            throw new ArgumentException("Invalid trade record: " + problem);
        }

        Symbol = symbol;
        ZoneId = zoneId;
        OpenTimeUtc = openTimeUtc;
        CloseTimeUtc = closeTimeUtc;
        Side = side;
        VolumeUnits = volumeUnits;
        Entry = entry;
        Stop = stop;
        Target = target;
        ExitPrice = exitPrice;
        ExitReason = exitReason;
        ResultMoney = resultMoney;
        ResultR = resultR;
        Commission = commission;
        Swap = swap;
        RiskMoney = riskMoney;
    }

    /// <summary>Symbol name (SIM-001).</summary>
    public string Symbol { get; }

    /// <summary>Id of the zone that produced the trade (SIM-001).</summary>
    public string ZoneId { get; }

    /// <summary>Fill time in UTC (SIM-001).</summary>
    public DateTime OpenTimeUtc { get; }

    /// <summary>Close time in UTC (SIM-001).</summary>
    public DateTime CloseTimeUtc { get; }

    /// <summary>Side of the trade (SIM-001).</summary>
    public Side Side { get; }

    /// <summary>Volume in units (SIM-001).</summary>
    public double VolumeUnits { get; }

    /// <summary>Entry price (SIM-001).</summary>
    public double Entry { get; }

    /// <summary>Stop price at the open (SIM-001).</summary>
    public double Stop { get; }

    /// <summary>Target price at the open (SIM-001).</summary>
    public double Target { get; }

    /// <summary>Exit price (SIM-001).</summary>
    public double ExitPrice { get; }

    /// <summary>Exit reason as exported (SIM-001).</summary>
    public string ExitReason { get; }

    /// <summary>Net result in money, commission and swap included (SIM-001).</summary>
    public double ResultMoney { get; }

    /// <summary>Net result in R (SIM-001).</summary>
    public double ResultR { get; }

    /// <summary>Round-trip commission as charged, zero or negative (SIM-001).</summary>
    public double Commission { get; }

    /// <summary>Swap as charged (SIM-001).</summary>
    public double Swap { get; }

    /// <summary>Money at risk at the stop when the trade was opened, without commission (SIM-001).</summary>
    public double RiskMoney { get; }

    /// <summary>Base currency: the first three letters of the symbol (SIM-001, RSK-005).</summary>
    public string BaseCurrency => Symbol.Substring(0, 3);

    /// <summary>Quote currency: letters four to six of the symbol (SIM-001, RSK-005).</summary>
    public string QuoteCurrency => Symbol.Substring(3, 3);

    /// <summary>
    /// Describes the first invalid value, in column order, starting with the column name; null
    /// when every value is valid (SIM-001).
    /// </summary>
    internal static string? Problem(string? symbol, string? zoneId, DateTime openTimeUtc, DateTime closeTimeUtc, Side side, double volumeUnits, double entry, double stop, double target, double exitPrice, string? exitReason, double resultMoney, double resultR, double commission, double swap, double riskMoney)
    {
        string? problem = SymbolProblem(symbol)
            ?? TokenProblem("zone_id", zoneId)
            ?? TimeProblem("open_time_utc", openTimeUtc)
            ?? TimeProblem("close_time_utc", closeTimeUtc);
        if (problem != null)
        {
            return problem;
        }

        if (closeTimeUtc < openTimeUtc)
        {
            return "close_time_utc must not be before open_time_utc.";
        }

        if (side != Side.Buy && side != Side.Sell)
        {
            return "side must be Buy or Sell.";
        }

        problem = PositiveProblem("volume_units", volumeUnits)
            ?? PositiveProblem("entry", entry)
            ?? PositiveProblem("stop", stop)
            ?? LevelProblem("stop", side, entry, stop, true)
            ?? PositiveProblem("target", target)
            ?? LevelProblem("target", side, entry, target, false)
            ?? PositiveProblem("exit_price", exitPrice)
            ?? TokenProblem("exit_reason", exitReason)
            ?? FiniteProblem("result_money", resultMoney)
            ?? FiniteProblem("result_r", resultR);
        if (problem != null)
        {
            return problem;
        }

        if (!double.IsFinite(commission) || commission > 0.0)
        {
            return "commission must be a finite number that is zero or negative (a cost), but was " + Text(commission) + ".";
        }

        problem = FiniteProblem("swap", swap) ?? PositiveProblem("risk_money", riskMoney);
        if (problem != null)
        {
            return problem;
        }

        double expectedR = resultMoney / riskMoney;
        if (!(Math.Abs(resultR - expectedR) <= ResultRTolerance))
        {
            return "result_r must equal result_money / risk_money = " + Text(expectedR) + " within " + Text(ResultRTolerance) + ", but was " + Text(resultR) + ".";
        }

        return null;
    }

    private static string? SymbolProblem(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            return "symbol must not be empty.";
        }

        if (symbol.Length < 6)
        {
            return "symbol must start with six capital letters (base and quote currency), but was \"" + symbol + "\".";
        }

        for (int i = 0; i < symbol.Length; i++)
        {
            char c = symbol[i];
            bool capital = c >= 'A' && c <= 'Z';
            bool allowedSuffix = capital || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_';
            if ((i < 6 && !capital) || !allowedSuffix)
            {
                return "symbol must start with six capital letters (base and quote currency), optionally followed by letters, digits, '.' or '_', but was \"" + symbol + "\".";
            }
        }

        return null;
    }

    private static string? TokenProblem(string column, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return column + " must not be empty.";
        }

        foreach (char c in value)
        {
            if (c <= ' ' || c > '~' || c == ',' || c == '"')
            {
                return column + " must contain only printable characters without spaces, commas or quotes, but was \"" + value + "\".";
            }
        }

        return null;
    }

    private static string? TimeProblem(string column, DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            return column + " must be a UTC time (kind UTC).";
        }

        if (value.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            return column + " must be a whole number of seconds.";
        }

        return null;
    }

    private static string? PositiveProblem(string column, double value)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            return column + " must be a finite number greater than zero, but was " + Text(value) + ".";
        }

        return null;
    }

    private static string? FiniteProblem(string column, double value)
    {
        if (!double.IsFinite(value))
        {
            return column + " must be a finite number, but was " + Text(value) + ".";
        }

        return null;
    }

    private static string? LevelProblem(string column, Side side, double entry, double level, bool lossSide)
    {
        bool below = PriceMath.LessThan(level, entry, PriceMath.DefaultTolerance);
        bool above = PriceMath.GreaterThan(level, entry, PriceMath.DefaultTolerance);
        bool wantBelow = (side == Side.Buy) == lossSide;
        if (wantBelow ? below : above)
        {
            return null;
        }

        return column + " must be " + (wantBelow ? "below" : "above") + " entry for a " + (side == Side.Buy ? "Buy" : "Sell") + " trade, but was " + Text(level) + " with entry " + Text(entry) + ".";
    }
}
