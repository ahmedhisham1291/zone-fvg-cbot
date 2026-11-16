using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Simulation;

/// <summary>
/// Portfolio combiner (SIM-002, spec 12.1 step 7): merges per-symbol trade logs by time, replays
/// the account-level caps of RSK-004, RSK-005 and RSK-006 on the merged list, and reports the
/// combined equity curve, the maximum drawdown and how often each cap would have applied.
/// </summary>
/// <remarks>
/// <para>Trades are taken in order of open time; trades opening at the same instant are taken by
/// symbol (ordinal), then by log and row, so the result does not depend on the order of the
/// logs. Every trade that closes at or before an open time is closed first.</para>
/// <para>When a trade opens it is checked against the accepted trades still open, in the order of
/// the risk engine; the first cap that refuses it is counted and the trade is left out of the
/// combined list, the equity curve and later checks:</para>
/// <list type="number">
/// <item>RSK-004: the risk_money of the open trades plus the new one must not exceed
/// MaxOpenRiskPercent of InitialCapital.</item>
/// <item>RSK-005: for the base and the quote currency of the new trade, on the side it takes in
/// each, at most MaxPositionsPerCurrency trades including the new one.</item>
/// <item>RSK-006: M - W must not exceed DailyLossLimitPercent of InitialCapital, where M is the
/// balance at 00:00 CE(S)T and W the balance plus, for each open trade and the new one,
/// -risk_money + commission (commission counts, swap does not; architecture section 10). After a
/// refusal the rest of that CE(S)T day is blocked.</item>
/// </list>
/// <para>Money is taken as logged. Balances start at InitialCapital and change only when a trade
/// closes; the drawdown is the largest fall of that balance from its running peak, also in
/// percent of the peak (as RSK-008 measures it). Open-trade swings are not in the logs.</para>
/// </remarks>
public static class PortfolioCombiner
{
    /// <summary>Combines the logs (SIM-002).</summary>
    /// <param name="logs">One trade log per symbol (or any split); none may be null.</param>
    /// <param name="parameters">Initial capital and the caps.</param>
    /// <returns>The combined portfolio.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">A log is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public static PortfolioResult Combine(IReadOnlyList<IReadOnlyList<TradeRecord>> logs, PortfolioParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        List<Entry> entries = Merge(logs);

        var accepted = new List<Entry>();
        var skipped = new List<SkippedTrade>();
        DateTime? blockedDay = null;
        foreach (Entry entry in entries)
        {
            DateTime dayStart = CestClock.StartOfCestDayUtc(entry.Trade.OpenTimeUtc);
            PortfolioCap? cap = blockedDay == dayStart ? PortfolioCap.DailyBudget : Check(entry.Trade, accepted, dayStart, parameters);
            if (cap == null)
            {
                accepted.Add(entry);
                continue;
            }

            if (cap == PortfolioCap.DailyBudget)
            {
                blockedDay = dayStart;
            }

            skipped.Add(new SkippedTrade(entry.Trade, cap.Value));
        }

        accepted.Sort(CompareByClose);
        var trades = new List<TradeRecord>(accepted.Count);
        foreach (Entry entry in accepted)
        {
            trades.Add(entry.Trade);
        }

        return new PortfolioResult(parameters, entries.Count, trades, skipped);
    }

    private static List<Entry> Merge(IReadOnlyList<IReadOnlyList<TradeRecord>> logs)
    {
        var entries = new List<Entry>();
        for (int log = 0; log < logs.Count; log++)
        {
            IReadOnlyList<TradeRecord>? records = logs[log];
            if (records == null)
            {
                throw new ArgumentException("Trade log " + SimText.Int(log + 1) + " is null.", nameof(logs));
            }

            for (int row = 0; row < records.Count; row++)
            {
                entries.Add(new Entry(records[row], log, row));
            }
        }

        entries.Sort(CompareByOpen);
        return entries;
    }

    private static PortfolioCap? Check(TradeRecord trade, List<Entry> accepted, DateTime dayStart, PortfolioParameters parameters)
    {
        DateTime now = trade.OpenTimeUtc;
        double balance = parameters.InitialCapital;
        double dayStartBalance = parameters.InitialCapital;
        double openRisk = trade.RiskMoney;
        double worstEquity = AtStop(trade);
        int baseCount = 1;
        int quoteCount = 1;
        foreach (Entry entry in accepted)
        {
            TradeRecord open = entry.Trade;
            if (open.CloseTimeUtc <= now)
            {
                balance += open.ResultMoney;
                dayStartBalance += open.CloseTimeUtc < dayStart ? open.ResultMoney : 0.0;
                continue;
            }

            openRisk += open.RiskMoney;
            worstEquity += AtStop(open);
            baseCount += SameExposure(open, trade.BaseCurrency, trade.Side == Side.Buy) ? 1 : 0;
            quoteCount += SameExposure(open, trade.QuoteCurrency, trade.Side != Side.Buy) ? 1 : 0;
        }

        double capital = parameters.InitialCapital;
        if (PriceMath.GreaterThan(openRisk, capital * parameters.MaxOpenRiskPercent / 100.0, PriceMath.DefaultTolerance))
        {
            return PortfolioCap.OpenRisk;
        }

        if (baseCount > parameters.MaxPositionsPerCurrency || quoteCount > parameters.MaxPositionsPerCurrency)
        {
            return PortfolioCap.CurrencyExposure;
        }

        double used = dayStartBalance - (balance + worstEquity);
        if (PriceMath.GreaterThan(used, capital * parameters.DailyLossLimitPercent / 100.0, PriceMath.DefaultTolerance))
        {
            return PortfolioCap.DailyBudget;
        }

        return null;
    }

    /// <summary>Result of a trade at its stop: -risk_money + commission (commission is zero or negative).</summary>
    private static double AtStop(TradeRecord trade)
    {
        return -trade.RiskMoney + trade.Commission;
    }

    /// <summary>True when <paramref name="open"/> takes the given side of the currency: long the base and short the quote for a buy.</summary>
    private static bool SameExposure(TradeRecord open, string currency, bool isLong)
    {
        bool openBuy = open.Side == Side.Buy;
        bool baseMatch = string.Equals(open.BaseCurrency, currency, StringComparison.Ordinal) && openBuy == isLong;
        bool quoteMatch = string.Equals(open.QuoteCurrency, currency, StringComparison.Ordinal) && openBuy != isLong;
        return baseMatch || quoteMatch;
    }

    private static int CompareByOpen(Entry a, Entry b)
    {
        int result = a.Trade.OpenTimeUtc.CompareTo(b.Trade.OpenTimeUtc);
        return result != 0 ? result : CompareTail(a, b);
    }

    private static int CompareByClose(Entry a, Entry b)
    {
        int result = a.Trade.CloseTimeUtc.CompareTo(b.Trade.CloseTimeUtc);
        if (result == 0)
        {
            result = a.Trade.OpenTimeUtc.CompareTo(b.Trade.OpenTimeUtc);
        }

        return result != 0 ? result : CompareTail(a, b);
    }

    private static int CompareTail(Entry a, Entry b)
    {
        int result = string.CompareOrdinal(a.Trade.Symbol, b.Trade.Symbol);
        if (result == 0)
        {
            result = a.Log.CompareTo(b.Log);
        }

        return result != 0 ? result : a.Row.CompareTo(b.Row);
    }

    private sealed record Entry(TradeRecord Trade, int Log, int Row);
}

/// <summary>An account-level cap replayed by <see cref="PortfolioCombiner"/> (SIM-002).</summary>
public enum PortfolioCap
{
    /// <summary>Total open risk cap (RSK-004).</summary>
    OpenRisk,

    /// <summary>Same-direction exposure per currency (RSK-005).</summary>
    CurrencyExposure,

    /// <summary>Daily risk budget, including the rest of the day after it refused (RSK-006).</summary>
    DailyBudget,
}

/// <summary>A trade the caps would have refused, with the first cap that refused it (SIM-002).</summary>
/// <param name="Trade">The refused trade.</param>
/// <param name="Cap">The cap that refused it.</param>
public sealed record SkippedTrade(TradeRecord Trade, PortfolioCap Cap);

/// <summary>A point of the combined equity curve: the balance after a trade closed (SIM-002).</summary>
/// <param name="TimeUtc">Close time of the trade.</param>
/// <param name="Balance">Balance after the close.</param>
public sealed record EquityPoint(DateTime TimeUtc, double Balance);

/// <summary>
/// Parameters of <see cref="PortfolioCombiner"/> (SIM-002), with the defaults of
/// <see cref="RiskParameters"/>. Immutable; derive changes with <c>with</c>.
/// </summary>
public sealed record PortfolioParameters
{
    /// <summary>Initial capital: the starting balance and the base of the caps (RSK-004, RSK-006). Default 100,000.</summary>
    public double InitialCapital { get; init; } = 100000.0;

    /// <summary>Total open risk cap in percent of <see cref="InitialCapital"/> (RSK-004). Default 1.5.</summary>
    public double MaxOpenRiskPercent { get; init; } = 1.5;

    /// <summary>Most trades on the same side of one currency (RSK-005). Default 2.</summary>
    public int MaxPositionsPerCurrency { get; init; } = 2;

    /// <summary>Daily risk budget in percent of <see cref="InitialCapital"/> (RSK-006). Default 1.5.</summary>
    public double DailyLossLimitPercent { get; init; } = 1.5;

    /// <summary>Checks every value (SIM-002).</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range; the parameter name is the property name.</exception>
    public void Validate()
    {
        if (!double.IsFinite(InitialCapital) || InitialCapital <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(InitialCapital), "InitialCapital must be a finite number greater than zero.");
        }

        RequirePercent(MaxOpenRiskPercent, nameof(MaxOpenRiskPercent));
        if (MaxPositionsPerCurrency < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPositionsPerCurrency), "MaxPositionsPerCurrency must be at least 1.");
        }

        RequirePercent(DailyLossLimitPercent, nameof(DailyLossLimitPercent));
    }

    private static void RequirePercent(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0 || value > 100.0)
        {
            throw new ArgumentOutOfRangeException(name, name + " must be greater than 0 and at most 100.");
        }
    }
}

/// <summary>Output of <see cref="PortfolioCombiner.Combine"/> (SIM-002).</summary>
public sealed class PortfolioResult
{
    internal PortfolioResult(PortfolioParameters parameters, int inputTradeCount, IReadOnlyList<TradeRecord> trades, IReadOnlyList<SkippedTrade> skipped)
    {
        Parameters = parameters;
        InputTradeCount = inputTradeCount;
        Trades = trades;
        Skipped = skipped;

        var curve = new List<EquityPoint>(trades.Count);
        double balance = parameters.InitialCapital;
        double peak = balance;
        foreach (TradeRecord trade in trades)
        {
            balance += trade.ResultMoney;
            curve.Add(new EquityPoint(trade.CloseTimeUtc, balance));
            peak = Math.Max(peak, balance);
            MaxDrawdownMoney = Math.Max(MaxDrawdownMoney, peak - balance);
            MaxDrawdownPercent = Math.Max(MaxDrawdownPercent, (peak - balance) / peak * 100.0);
        }

        EquityCurve = curve;
        FinalBalance = balance;
    }

    /// <summary>The parameters used (SIM-002).</summary>
    public PortfolioParameters Parameters { get; }

    /// <summary>Number of trades in all logs together (SIM-002).</summary>
    public int InputTradeCount { get; }

    /// <summary>The trades the caps allow, in order of close time (SIM-002).</summary>
    public IReadOnlyList<TradeRecord> Trades { get; }

    /// <summary>The trades the caps refuse, in the order they were checked (SIM-002).</summary>
    public IReadOnlyList<SkippedTrade> Skipped { get; }

    /// <summary>The balance after each kept trade, in order of close time (SIM-002).</summary>
    public IReadOnlyList<EquityPoint> EquityCurve { get; }

    /// <summary>Balance after the last kept trade; the initial capital when none (SIM-002).</summary>
    public double FinalBalance { get; }

    /// <summary>Largest fall of the balance from its running peak, in money (SIM-002).</summary>
    public double MaxDrawdownMoney { get; }

    /// <summary>Largest fall of the balance from its running peak, in percent of that peak (SIM-002).</summary>
    public double MaxDrawdownPercent { get; }

    /// <summary>How many trades the cap refused first (SIM-002).</summary>
    /// <param name="cap">The cap.</param>
    /// <returns>The count.</returns>
    public int SkippedCount(PortfolioCap cap)
    {
        int count = 0;
        foreach (SkippedTrade skippedTrade in Skipped)
        {
            count += skippedTrade.Cap == cap ? 1 : 0;
        }

        return count;
    }

    /// <summary>The equity curve as CSV: header <c>time_utc,balance</c>, one row per kept trade (SIM-002).</summary>
    /// <returns>The file content.</returns>
    public string ToEquityCsv()
    {
        var text = new StringBuilder("time_utc,balance\n");
        foreach (EquityPoint point in EquityCurve)
        {
            text.Append(point.TimeUtc.ToString(TradeLogCsv.TimeFormat, CultureInfo.InvariantCulture)).Append(',')
                .Append(point.Balance.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The result as Markdown, with the assumptions first (SIM-002).</summary>
    /// <returns>The report.</returns>
    public string ToMarkdown()
    {
        var text = new StringBuilder();
        text.Append("# Combined portfolio (SIM-002)\n\n");
        text.Append("Assumptions:\n\n");
        text.Append("- The logs are a sample of the past: past trade distributions may not repeat.\n");
        text.Append("- Money is taken as logged. Each trade is checked when it opens against the kept trades still open, with the caps of RSK-004, RSK-005 and RSK-006 in that order; a refused trade is left out.\n");
        text.Append("- The balance changes only when a trade closes; open-trade swings are not in the logs, so the drawdown is measured on closed balances.\n\n");
        text.Append("| Measure | Value |\n");
        text.Append("|---|---|\n");
        text.Append("| Trades in the logs | ").Append(SimText.Int(InputTradeCount)).Append(" |\n");
        text.Append("| Trades kept | ").Append(SimText.Int(Trades.Count)).Append(" |\n");
        text.Append("| Skipped by open risk cap (RSK-004) | ").Append(SimText.Int(SkippedCount(PortfolioCap.OpenRisk))).Append(" |\n");
        text.Append("| Skipped by currency exposure cap (RSK-005) | ").Append(SimText.Int(SkippedCount(PortfolioCap.CurrencyExposure))).Append(" |\n");
        text.Append("| Skipped by daily risk budget (RSK-006) | ").Append(SimText.Int(SkippedCount(PortfolioCap.DailyBudget))).Append(" |\n");
        text.Append("| Initial capital | ").Append(SimText.F2(Parameters.InitialCapital)).Append(" |\n");
        text.Append("| Final balance | ").Append(SimText.F2(FinalBalance)).Append(" |\n");
        text.Append("| Maximum drawdown | ").Append(SimText.F2(MaxDrawdownMoney)).Append(" (").Append(SimText.F2(MaxDrawdownPercent)).Append("%) |\n\n");
        text.Append("| Symbol | Trades kept | Net result |\n");
        text.Append("|---|---|---|\n");
        foreach (KeyValuePair<string, SymbolTotal> pair in BySymbol(Trades))
        {
            text.Append("| ").Append(pair.Key).Append(" | ").Append(SimText.Int(pair.Value.Count)).Append(" | ").Append(SimText.F2(pair.Value.Net)).Append(" |\n");
        }

        return text.ToString();
    }

    /// <summary>Count and net money per symbol, ordered by symbol (ordinal).</summary>
    internal static SortedDictionary<string, SymbolTotal> BySymbol(IReadOnlyList<TradeRecord> trades)
    {
        var totals = new SortedDictionary<string, SymbolTotal>(StringComparer.Ordinal);
        foreach (TradeRecord trade in trades)
        {
            totals.TryGetValue(trade.Symbol, out SymbolTotal? total);
            totals[trade.Symbol] = new SymbolTotal((total?.Count ?? 0) + 1, (total?.Net ?? 0.0) + trade.ResultMoney);
        }

        return totals;
    }

    /// <summary>Count of trades and their net money for one symbol.</summary>
    internal sealed record SymbolTotal(int Count, double Net);
}
