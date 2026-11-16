using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-014: every skipped signal is recorded with exactly one reason code from the closed
/// <see cref="SkipReason"/> list, plus the measured values behind the decision. Each case below
/// is one evaluation; <see cref="ExecutionHarness.SkipOf"/> checks that it produced exactly one
/// record and no order. The scenario classes of EXE-001 to EXE-006, EXE-011, EXE-015, RSK-002 and
/// RSK-003 check the measured values of their own skips the same way.
/// </summary>
public class Exe014SkipRecordScenarios
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "stale", SkipReason.SignalStale, 30.0, 15.0 };
        yield return new object[] { "replay", SkipReason.SignalFromReplay, 0.0, 0.0 };
        yield return new object[] { "halted", SkipReason.BotNotRunning, (double)BotState.Halted, (double)BotState.Running };
        yield return new object[] { "spread", SkipReason.SpreadTooWide, 1.6, 1.5 };
        yield return new object[] { "stop small", SkipReason.StopTooSmall, 11.9, 12.0 };
        yield return new object[] { "gap", SkipReason.FvgTooSmallForSpread, 2.9, 3.0 };
        yield return new object[] { "room", SkipReason.NoRoom, 1.49, 1.5 };
        yield return new object[] { "expiry", SkipReason.ExpiryTooShort, 14.0, 15.0 };
        yield return new object[] { "volume below minimum", SkipReason.BelowMinimumVolume, 0.0, 1000.0 };
        yield return new object[] { "margin", SkipReason.FreeMarginTooLow, 49990.0, 50000.0 };
        yield return new object[] { "calendar unavailable", SkipReason.NewsCalendarUnavailable, 0.0, 0.0 };
        yield return new object[] { "inside news window", SkipReason.NewsWindow, 0.0, 0.0 };
        yield return new object[] { "FVG bar in news window", SkipReason.NewsFvgBar, 0.0, 0.0 };
        yield return new object[] { "invalid signal", SkipReason.InvalidSignal, 0.0, 0.0 };
        yield return new object[] { "server time not UTC", SkipReason.BrokerDataInvalid, 0.0, 0.0 };
        yield return new object[] { "spread not a number", SkipReason.BrokerDataInvalid, 0.0, 0.0 };
        yield return new object[] { "position data broken", SkipReason.BrokerDataInvalid, 0.0, 0.0 };
        yield return new object[] { "other symbol unknown", SkipReason.BrokerDataInvalid, 0.0, 0.0 };
        yield return new object[] { "other symbol data broken", SkipReason.MissingSymbolInfo, 0.0, 0.0 };
    }

    [Theory]
    [Trait("Req", "EXE-014")]
    [MemberData(nameof(Cases))]
    public void EverySkip_CarriesExactlyOneReasonAndItsMeasuredValues(string name, SkipReason reason, double measured, double limit)
    {
        (ExecutionHarness h, Signal signal, BotState state, IReadOnlyList<Zone> zones) = Build(name);

        SkipRecord skip = h.SkipOf(signal, state, zones);

        Assert.Equal(reason, skip.Reason);
        Assert.Equal(measured, skip.Measured, 6);
        Assert.Equal(limit, skip.Limit, 6);
        Assert.Equal("EURUSD", skip.Symbol);
        Assert.Equal(signal.ZoneId, skip.ZoneId);
        Assert.Equal(h.Broker.ServerTimeUtc, skip.AtUtc);
        Assert.False(string.IsNullOrEmpty(skip.Detail), name + ": no detail");
        Assert.Equal(0, h.Broker.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "EXE-014")]
    public void SkipsInARow_EachEvaluationAddsOneRecord()
    {
        var h = new ExecutionHarness();
        Signal[] signals =
        {
            ExecutionData.Buy(closeUtc: ExecutionData.At(14, 9, 30)),
            ExecutionData.Buy(stop: 1.09881),
            ExecutionData.Buy(fromReplay: true),
            ExecutionData.Sell(gapLower: 1.09986, gapUpper: 1.10015),
        };

        foreach (Signal signal in signals)
        {
            h.Evaluate(signal);
        }

        Assert.Equal(
            new[] { SkipReason.SignalStale, SkipReason.StopTooSmall, SkipReason.SignalFromReplay, SkipReason.FvgTooSmallForSpread },
            h.Events.Skips.Select(skip => skip.Reason));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-014")]
    public void PlacementOutcomes_RejectionAndAbandonment_AreRecordedOnceEach()
    {
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.Rejected, "NoMoney");
        h.EvaluateAndPlace(ExecutionData.Buy());
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied);
        h.EvaluateAndPlace(ExecutionData.Buy(zoneId: "D-20260114-0800"));
        h.Broker.AdvanceTo(ExecutionData.At(14, 10, 30));
        h.Orders.ResolvePending(Array.Empty<Bar>());

        Assert.Equal(new[] { SkipReason.OrderRejected, SkipReason.UnclearOutcomeNotResent }, h.Events.Skips.Select(skip => skip.Reason));
        Assert.Equal(new[] { "D-20260114-0400", "D-20260114-0800" }, h.Events.Skips.Select(skip => skip.ZoneId));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-014")]
    public void Construction_NullArgumentsAreRejected()
    {
        var h = new ExecutionHarness();
        var counter = new RequestCounter(ExecutionData.RiskDefaults);
        var events = new RecordingEvents();
        ExecutionParameters e = ExecutionData.ExecutionDefaults;
        RiskParameters r = ExecutionData.RiskDefaults;

        Assert.Throws<ArgumentNullException>(() => new PreTradeFilters(null!, ExecutionData.SignalDefaults, r, h.Broker, h.News, events));
        Assert.Throws<ArgumentNullException>(() => new PreTradeFilters(e, null!, r, h.Broker, h.News, events));
        Assert.Throws<ArgumentNullException>(() => new PreTradeFilters(e, ExecutionData.SignalDefaults, null!, h.Broker, h.News, events));
        Assert.Throws<ArgumentNullException>(() => new PreTradeFilters(e, ExecutionData.SignalDefaults, r, null!, h.News, events));
        Assert.Throws<ArgumentNullException>(() => new PreTradeFilters(e, ExecutionData.SignalDefaults, r, h.Broker, null!, events));
        Assert.Throws<ArgumentNullException>(() => new PreTradeFilters(e, ExecutionData.SignalDefaults, r, h.Broker, h.News, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PreTradeFilters(e with { MinStopPips = 0.0 }, ExecutionData.SignalDefaults, r, h.Broker, h.News, events));
        Assert.Throws<ArgumentNullException>(() => h.Filters.Evaluate(null!, new PreTradeContext("EURUSD", BotState.Running, Array.Empty<Zone>(), false)));
        Assert.Throws<ArgumentNullException>(() => h.Filters.Evaluate(ExecutionData.Buy(), null!));
        Assert.Throws<ArgumentNullException>(() => h.Filters.Evaluate(ExecutionData.Buy(), new PreTradeContext("EURUSD", BotState.Running, null!, false)));
        Assert.Throws<ArgumentNullException>(() => new OrderManager(null!, counter, events));
        Assert.Throws<ArgumentNullException>(() => new OrderManager(h.Broker, null!, events));
        Assert.Throws<ArgumentNullException>(() => new OrderManager(h.Broker, counter, null!));
        Assert.Throws<ArgumentNullException>(() => h.Orders.TryPlace(null!));
        Assert.Throws<ArgumentNullException>(() => h.Orders.ResolvePending(null!));
        Assert.Throws<ArgumentNullException>(() => SignalOrder.ForEvaluation(null!));
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    private static (ExecutionHarness Harness, Signal Signal, BotState State, IReadOnlyList<Zone> Zones) Build(string name)
    {
        var h = new ExecutionHarness();
        Signal buy = ExecutionData.Buy();
        IReadOnlyList<Zone> none = Array.Empty<Zone>();
        switch (name)
        {
            case "stale":
                return (h, ExecutionData.Buy(closeUtc: ExecutionData.At(14, 9, 30)), BotState.Running, none);
            case "replay":
                return (h, ExecutionData.Buy(fromReplay: true), BotState.Running, none);
            case "halted":
                return (h, buy, BotState.Halted, none);
            case "spread":
                return (new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.EurUsd(spread: 0.00016))), buy, BotState.Running, none);
            case "stop small":
                return (h, ExecutionData.Buy(stop: 1.09881), BotState.Running, none);
            case "gap":
                return (h, ExecutionData.Buy(gapUpper: 1.10015, gapLower: 1.09986), BotState.Running, none);
            case "room":
                return (h, buy, BotState.Running, new[] { ExecutionData.Zone("S-20260113-0800", ZoneType.Supply, 1.10298) });
            case "expiry":
                return (new ExecutionHarness(news: "2026-01-14 10:44,EUR,High,Event\n"), buy, BotState.Running, none);
            case "volume below minimum":
                // Balance 300: risk 1.5, volume 1.5 / (20 x 0.0001) = 750, rounded down to 0.
                return (new ExecutionHarness(ExecutionHarness.DefaultBroker(account: ExecutionData.Account(300.0, 300.0, 300.0))), buy, BotState.Running, none);
            case "margin":
                FakeBroker tight = ExecutionHarness.DefaultBroker(account: ExecutionData.Account(100000.0, 100000.0, 90000.0));
                tight.MarginPerUnit = 0.16004;
                return (new ExecutionHarness(tight), buy, BotState.Running, none);
            case "calendar unavailable":
                // The only event is in February, so the calendar covers another week.
                return (new ExecutionHarness(news: "2026-02-18 18:00,USD,High,Event\n"), buy, BotState.Running, none);
            case "inside news window":
                // Event at 10:15: window 09:45 to 10:45 contains 10:00.
                return (new ExecutionHarness(news: "2026-01-14 10:15,USD,High,Event\n"), buy, BotState.Running, none);
            case "FVG bar in news window":
                // Event at 09:00: window 08:30 to 09:30 overlaps c3 (09:15 to 09:30).
                return (new ExecutionHarness(news: "2026-01-14 09:00,USD,High,Event\n"), buy, BotState.Running, none);
            case "invalid signal":
                return (h, ExecutionData.Buy(entry: double.NaN), BotState.Running, none);
            case "server time not UTC":
                h.Broker.ReportedKind = DateTimeKind.Unspecified;
                return (h, buy, BotState.Running, none);
            case "spread not a number":
                return (new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.EurUsd(spread: double.NaN))), buy, BotState.Running, none);
            case "position data broken":
                // A bot position on another symbol with a volume that is not a number: the risk snapshot refuses it.
                h.Broker.AddPosition("GBPUSD", Side.Buy, double.NaN, 1.25, 1.248, 1.254, "ZFVG|v1|GBPUSD|D-20260113-0800", "c");
                return (h, buy, BotState.Running, none);
            case "other symbol unknown":
                // A bot order on GBPUSD while the broker has no GBPUSD data.
                h.Broker.AddOrder("GBPUSD", Side.Buy, 1000.0, 1.24, 1.238, 1.244, null, "ZFVG|v1|GBPUSD|D-20260113-0800", "c");
                return (h, buy, BotState.Running, none);
            case "other symbol data broken":
                h.Broker.SetSymbol(RiskData.GbpUsd with { PipSize = 0.0 });
                h.Broker.AddOrder("GBPUSD", Side.Buy, 1000.0, 1.24, 1.238, 1.244, null, "ZFVG|v1|GBPUSD|D-20260113-0800", "c");
                return (h, buy, BotState.Running, none);
            default:
                throw new ArgumentException("Unknown case " + name, nameof(name));
        }
    }
}
