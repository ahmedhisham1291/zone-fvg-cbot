using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Hand-written data for the WP38a tests (RSK-002, RSK-004 to RSK-006, RSK-009, RSK-016 to
/// RSK-018): the 200,000 USD account of spec v0.12 with the new defaults, beside the older
/// account that <see cref="RiskData.Parameters"/> keeps.
/// </summary>
/// <remarks>
/// <para>Time: <see cref="ExecutionData.Now"/>, Wednesday 2026-01-14 10:00 UTC; the CE(S)T day starts
/// at 2026-01-13 23:00 UTC; RiskEpochDate is 1 January 2026 (00:00 CET = 2025-12-31 23:00 UTC).</para>
/// <para>Earlier trades close one a day at 12:00 UTC from 2 January, placed at 10:00 the same day,
/// so each closes before the next is placed and before the scenario day.</para>
/// <para>EURUSD, GBPUSD and AUDUSD as in <see cref="ExecutionData.EurUsd"/>: pip 0.0001, pip value
/// 0.0001 USD per unit, step 1,000. The reference buy (entry 1.10000, stop 1.09800, 20 pips) gives
/// volume = R / 0.002, rounded down.</para>
/// </remarks>
public static class RecoverySizingData
{
    public const double Capital = 200000.0;

    /// <summary>The defaults of spec v0.12 with the test epoch: Ladder, 200,000, 2%, caps off, 3,000,000 units.</summary>
    public static RiskParameters Defaults => new() { RiskEpochDate = RiskData.Epoch };

    /// <summary><see cref="Defaults"/> with SizingPlan Recovery (RecoveryRunLossPercent 1.0, cap 1.28%).</summary>
    public static RiskParameters Recovery => Defaults with { SizingPlan = SizingPlan.Recovery };

    /// <summary>Placement time of trade number <paramref name="n"/> (1-based) of a sequence: 2 January plus n - 1 days, 10:00 UTC.</summary>
    public static DateTime Placed(int n)
    {
        return new DateTime(2026, 1, 1 + n, 10, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>Close time of trade number <paramref name="n"/>: two hours after its placement.</summary>
    public static DateTime ClosedAt(int n)
    {
        return Placed(n).AddHours(2.0);
    }

    /// <summary>A closed trade as the sizer sees it.</summary>
    public static LadderEntry Closed(string key, DateTime placedUtc, DateTime closedUtc, double net)
    {
        return new LadderEntry(key, placedUtc, closedUtc, net);
    }

    /// <summary>Trade number <paramref name="n"/> of a sequence, closed with the given net.</summary>
    public static LadderEntry Nth(int n, double net)
    {
        return Closed("ZFVG|v1|EURUSD|T" + n.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), Placed(n), ClosedAt(n), net);
    }

    public static string Label(string symbol, string id)
    {
        return "ZFVG|v1|" + symbol + "|" + id;
    }

    /// <summary>A closed broker trade of the symbol: position id, label id, entry and close time, net, commission and swap.</summary>
    public static ClosedTradeInfo Trade(long positionId, string symbol, string id, DateTime entryUtc, DateTime closeUtc, double net, double commission = 0.0, double swap = 0.0)
    {
        return new ClosedTradeInfo(positionId, symbol, Label(symbol, id), entryUtc, closeUtc, net, commission, swap, Capital + net);
    }

    /// <summary>Closed broker trade number <paramref name="n"/> of the sequence of the symbol.</summary>
    public static ClosedTradeInfo NthTrade(int n, string symbol, double net)
    {
        return Trade(n, symbol, "T" + n.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), Placed(n), ClosedAt(n), net);
    }

    public static AccountSnapshot Account(double balance = Capital)
    {
        return new AccountSnapshot(balance, balance, balance, "USD");
    }

    /// <summary>A pair like <see cref="ExecutionData.EurUsd"/> with another name and base currency.</summary>
    public static SymbolInfo Pair(string name, string baseCurrency)
    {
        return ExecutionData.EurUsd() with { Name = name, BaseCurrency = baseCurrency };
    }

    /// <summary>An EURUSD execution harness on the given account (default 200,000) with these risk parameters.</summary>
    public static ExecutionHarness Harness(RiskParameters risk, double balance = Capital)
    {
        return new ExecutionHarness(ExecutionHarness.DefaultBroker(account: Account(balance)), risk: risk);
    }
}

/// <summary>
/// Pre-trade filters of an instance of any symbol on a shared <see cref="FakeBroker"/> that knows
/// EURUSD, GBPUSD and AUDUSD (WP38a, RSK-006, RSK-017), live or with the backtest waiver of REC-014.
/// </summary>
public sealed class SymbolFilters
{
    public SymbolFilters(RiskParameters risk, AccountSnapshot account, bool dailyBudgetRecordOnly = false, FakeBroker? broker = null, SignalParameters? signal = null)
    {
        Broker = broker ?? ExecutionHarness.DefaultBroker(account: account);
        Broker.SetSymbol(RecoverySizingData.Pair("GBPUSD", "GBP"));
        Broker.SetSymbol(RecoverySizingData.Pair("AUDUSD", "AUD"));
        var news = new NewsService(new NewsParameters(), new[] { NewsTestData.ForexFactoryFeed() }, new MemoryStore(), new FakeTransport(NewsTestData.Failed()), true, ExecutionData.QuietNews);
        Filters = new PreTradeFilters(ExecutionData.ExecutionDefaults, signal ?? ExecutionData.SignalDefaults, risk, Broker, news, Events, dailyBudgetRecordOnly);
    }

    public FakeBroker Broker { get; }

    public RecordingEvents Events { get; } = new();

    public PreTradeFilters Filters { get; }

    /// <summary>The detail of the daily budget refusal that the backtest waiver recorded in the last evaluation, or null.</summary>
    public string? Waived { get; private set; }

    /// <summary>Evaluates the reference buy of <see cref="ExecutionData.Buy"/> for the symbol.</summary>
    public PreTradeResult Evaluate(string symbol, Signal? signal = null)
    {
        var context = new PreTradeContext(symbol, BotState.Running, Array.Empty<Zone>(), false);
        PreTradeResult result = Filters.EvaluateWithWaiver(signal ?? ExecutionData.Buy(), context, out string? waived);
        Waived = waived;
        return result;
    }
}
