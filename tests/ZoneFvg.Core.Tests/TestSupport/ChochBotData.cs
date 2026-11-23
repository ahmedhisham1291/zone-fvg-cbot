using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Hand-built data of the core scenarios of the Choch model (EXE-027, OPS-011, REC-020): one USDJPY
/// instance as <see cref="RecoveryData"/> (same symbol data, risk, news and harness) with default
/// Choch parameters, whose engine finds tail A of <see cref="ChochScenarios"/> at the reference zone.
/// </summary>
/// <remarks>
/// <para><b>Bars.</b> D1 as <see cref="RecoveryData.Bars"/> (bias Long). H4 =
/// <see cref="EngineScenarios.DemandH4"/> (300 bars) with its leg bar opening at
/// <see cref="Activation"/> - 4 hours, then 40 flat bars (101.05, 101.1, 101.0, 101.05). M15 = 2,400
/// flat bars (100.6, 100.65, 100.55, 100.6) up to the activation, tail A (m0 to m11, 08:00 to 11:00),
/// then 520 flat bars (101.05, 101.1, 101.02, 101.05), whose lows stay above the entry.</para>
/// <para><b>Signal.</b> Zone D-20260303-1600 is touched by m7 (09:45) and m11 (10:45 to 11:00) closes
/// above the M15 swing high 101.0: entry 101.0, stop 99.9 - 0.1 x 3.75 / 14 = 99.873214, target
/// 101.0 + 2 x 1.126786 = 103.253571 (no opposing level: the H4 swing high at 101.0 is broken), not
/// swept, ATR(H4) 15 / 14.</para>
/// <para><b>Order.</b> USDJPY pip 0.01, tick 0.001, spread 0.01, no commission; quote bid 101.05, ask
/// 101.06 (above the entry, not reached). Buy limit at 101.0, stop 99.873, target 103.254 (the
/// tick grid), 112.7 pips; volume 500 / (112.7 x 0.0001) = 44,365, rounded down to 44,000; risk
/// 44,000 x 112.7 x 0.0001 = 495.88; expiry 11:00 + 8 x 15 minutes = 13:00; label
/// ZFVG|v1|USDJPY|D-20260303-1600; hash 88051907 (default parameters with default Choch parameters,
/// <c>ParameterHashChochTests</c>). Cost to risk 1 / 112.7 = 0.0089. Reward on the signal's levels 2.0.</para>
/// </remarks>
public static class ChochBotData
{
    public const string Hash = "88051907";

    public const double Entry = 101.0;

    public const double Stop = 99.873;

    public const double Target = 103.254;

    public const double Volume = 44000.0;

    public const double RiskMoney = 495.88;

    public static readonly DateTime Activation = RecoveryData.Activation;

    /// <summary>Wednesday 2026-03-04 00:00: the start of most scenarios, before the zone is created.</summary>
    public static readonly DateTime Start = Activation.AddHours(-8.0);

    /// <summary>11:00: close of m11, the change of character.</summary>
    public static readonly DateTime Confirmation = Activation.AddHours(3.0);

    /// <summary>13:00: the order's expiry.</summary>
    public static readonly DateTime Expiry = Confirmation.AddHours(2.0);

    /// <summary>The signal line of the taken buy (OPS-011), written by hand.</summary>
    public const string SignalLine =
        "2026-03-04T11:00:00Z signal symbol=USDJPY result=taken bias=Long zone=D-20260303-1600 zoneProximal=100.2 zoneDistal=99.5"
        + " fvgUpper=NA fvgLower=NA entry=101 stop=99.873 target=103.254 stopPips=112.7 volume=44000 spreadPips=1.0 costToRisk=0.009"
        + " news=Available skip=none kind=choch level=101 sweep=no target=default rr=2";

    public static BotParameters Parameters(bool newsEnabled = true)
    {
        return RecoveryData.Parameters(newsEnabled) with { Choch = new ChochParameters() };
    }

    public static SymbolInfo Quote()
    {
        return RecoveryData.UsdJpy() with { Bid = 101.05, Ask = 101.06 };
    }

    public static FakeBroker Broker(DateTime now)
    {
        var broker = new FakeBroker(now, ExecutionData.Account(), Quote());
        broker.MarginPerUnit = 0.04;
        return broker;
    }

    public static BotHarness Harness(DateTime now, bool isBacktest = true, FakeTransport? http = null)
    {
        return new BotHarness(Bars(), Broker(now), isBacktest, http);
    }

    /// <summary>The comment of the order with the given parameter hash.</summary>
    public static string Comment(string hash = Hash)
    {
        return OrderComment.Build(new CommentPlan(Entry, Stop, Target, RiskMoney, hash), 3);
    }

    public static BarSet Bars()
    {
        BarSet reference = RecoveryData.Bars();
        var h4 = new List<Bar>(EngineScenarios.DemandH4(300, Activation));
        h4.AddRange(new BarBuilder(TimeFrame.H4).At(Activation).Repeat(40, (b, _) => b.Ohlc(101.05, 101.1, 101.0, 101.05)).Build());
        BarBuilder m15 = new BarBuilder(TimeFrame.M15)
            .At(Activation.AddMinutes(-15.0 * 2400))
            .Repeat(2400, (b, _) => b.Ohlc(100.6, 100.65, 100.55, 100.6));
        foreach ((double open, double high, double low, double close) in ChochScenarios.TailA)
        {
            m15.Ohlc(open, high, low, close);
        }

        m15.Repeat(520, (b, _) => b.Ohlc(101.05, 101.1, 101.02, 101.05));
        return new BarSet(reference.D1, h4, m15.Build());
    }
}
