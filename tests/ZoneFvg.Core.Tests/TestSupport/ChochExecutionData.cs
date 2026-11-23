using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Hand-built data of the execution scenarios of the Choch model (EXE-027, EXE-028, EXE-029,
/// NWS-015), next to <see cref="ExecutionData"/>: EURUSD, server time Wednesday 2026-01-14 10:00
/// UTC, quiet news (one USD event at 18:00), the default execution parameters.
/// </summary>
/// <remarks>
/// <para><b>Pip size.</b> EURUSD has a pip of 0.0001 and a tick of 0.00001, so one tick is 0.1
/// pip and every stop distance below is an exact count of ticks: 10 pips is 100 ticks, 5 pips
/// (ChochMinStopPips) is 50 ticks, and 4.9 pips is one tick less.</para>
/// <para><b>Spread.</b> 0.00002 (0.2 pip), quoted as bid 1.10040 and ask 1.10042 for a buy (above
/// the entry 1.1, so not reached) and bid 1.09958, ask 1.0996 for a sell. With 0.2 pip and no
/// commission the cost is 4% of a 5-pip stop, below CostToRiskMax (10%).</para>
/// <para><b>Risk.</b> RiskPercent 0.2 of 100,000 = 200 USD (fixed sizing), so a 5-pip stop needs
/// 200 / (5 x 0.0001) = 400,000 units, below MaxVolumeUnits 500,000; a 10-pip stop 200,000.</para>
/// <para><b>Buy.</b> Zone D-20260114-0400, entry and level 1.1, stop 1.099 (10 pips), target 1.102
/// (2R), ATR(H4) 0.002 (MaxStopAtr allows 40 pips), confirmed at 10:00, not swept, target source
/// default. <b>Sell</b> (zone S-20260114-0400): entry 1.1, stop 1.101, target 1.098.</para>
/// </remarks>
public static class ChochExecutionData
{
    public const double Spread = 0.00002;

    public static readonly ChochParameters Defaults = new();

    public static RiskParameters Risk => RiskData.Parameters with { RiskPercent = 0.2 };

    public static SymbolInfo BuyQuote(double ask = 1.10042, double bid = 1.1004)
    {
        return ExecutionData.Quoted(bid, ask, Spread);
    }

    public static SymbolInfo SellQuote(double bid = 1.09958, double ask = 1.0996)
    {
        return ExecutionData.Quoted(bid, ask, Spread);
    }

    public static Signal Buy(double entry = 1.1, double stop = 1.099, double target = 1.102, double? level = null, DateTime? closeUtc = null, double atrH4 = 0.002, Fvg? fvg = null, string source = "default")
    {
        return new Signal(ExecutionData.BuyZone, Side.Buy, entry, stop, target, fvg, closeUtc ?? ExecutionData.Now, false, atrH4)
        {
            Kind = SignalKind.Choch,
            Level = level ?? entry,
            Swept = false,
            TargetSource = source,
        };
    }

    public static Signal Sell(double entry = 1.1, double stop = 1.101, double target = 1.098, DateTime? closeUtc = null, double atrH4 = 0.002)
    {
        return new Signal(ExecutionData.SellZone, Side.Sell, entry, stop, target, null, closeUtc ?? ExecutionData.Now, false, atrH4)
        {
            Kind = SignalKind.Choch,
            Level = entry,
            Swept = true,
            TargetSource = "zone:D-20260113-1200",
        };
    }

    public static ChochExecutionHarness Harness(SymbolInfo? symbol = null, string news = ExecutionData.QuietNews, ExecutionParameters? execution = null, DateTime? now = null, ChochParameters? choch = null, bool chochModel = true)
    {
        FakeBroker broker = ExecutionHarness.DefaultBroker(symbol ?? BuyQuote(), now);
        return new ChochExecutionHarness(broker, news, execution ?? ExecutionData.ExecutionDefaults, chochModel ? choch ?? Defaults : null);
    }
}

/// <summary>The execution layer of one EURUSD instance in the Choch model (or without it), as <see cref="ExecutionHarness"/>.</summary>
public sealed class ChochExecutionHarness
{
    public ChochExecutionHarness(FakeBroker broker, string news, ExecutionParameters execution, ChochParameters? choch)
    {
        Broker = broker;
        var newsService = new NewsService(new NewsParameters(), new[] { NewsTestData.ForexFactoryFeed() }, new MemoryStore(), new FakeTransport(NewsTestData.Failed()), true, news);
        var counter = new RequestCounter(ChochExecutionData.Risk);
        Filters = new PreTradeFilters(execution, ExecutionData.SignalDefaults, ChochExecutionData.Risk, Broker, newsService, Events, choch: choch);
        Orders = new OrderManager(Broker, counter, Events);
    }

    public FakeBroker Broker { get; }

    public RecordingEvents Events { get; } = new();

    public PreTradeFilters Filters { get; }

    public OrderManager Orders { get; }

    public PreTradeResult Evaluate(Signal signal, IReadOnlyList<Zone>? zones = null)
    {
        var context = new PreTradeContext(ExecutionData.Symbol, BotState.Running, zones ?? Array.Empty<Zone>(), Orders.HasUnresolved(ExecutionData.Symbol));
        return Filters.Evaluate(signal, context);
    }

    public SkipRecord SkipOf(Signal signal, IReadOnlyList<Zone>? zones = null)
    {
        int before = Events.Skips.Count;
        PreTradeResult result = Evaluate(signal, zones);
        Assert.False(result.IsAllowed);
        Assert.Equal(before + 1, Events.Skips.Count);
        return result.Skip!;
    }

    public PlannedOrder OrderOf(Signal signal, IReadOnlyList<Zone>? zones = null)
    {
        PreTradeResult result = Evaluate(signal, zones);
        string why = result.Skip is null ? string.Empty : result.Skip.Reason + ": " + result.Skip.Detail;
        Assert.True(result.IsAllowed, "expected an order but the signal was skipped with " + why);
        return result.Order!;
    }
}
