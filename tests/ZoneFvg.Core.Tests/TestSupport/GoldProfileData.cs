using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Gold data for the symbol profile scenarios (EXE-030, EXE-032): the bars of
/// <see cref="ChochBotData"/> with every price multiplied by a power of two, so every sum, ratio
/// and comparison of the engine scales exactly and the same Choch signal appears at gold's price
/// level. With factor 16 the entry is 1616.0 and the stop distance about 18.03 USD; with factor 2
/// the stop distance is about 2.25 USD (below gold's automatic 3 USD); with factor 4 about 4.51
/// USD. The symbol is XAUUSD (base XAU, quote USD) with a pip of 0.01 or 1 and a pip value per
/// ounce equal to the pip size, tick 0.01, volume step 1 ounce, no commission. EXAMPLE values.
/// </summary>
public static class GoldProfileData
{
    public const string Symbol = "XAUUSD";

    public const string Label = "ZFVG|v1|XAUUSD|" + RecoveryData.ZoneId;

    /// <summary>The hash of the Choch defaults with gold's automatic values (ParameterHashProfileTests).</summary>
    public const string Hash = "a2eedc3a";

    public static readonly DateTime Start = ChochBotData.Start;

    public static readonly DateTime Confirmation = ChochBotData.Confirmation;

    public static BarSet Bars(double factor)
    {
        BarSet reference = ChochBotData.Bars();
        return new BarSet(Scale(reference.D1, factor), Scale(reference.H4, factor), Scale(reference.M15, factor));
    }

    /// <summary>The gold symbol at the given pip size, bid just above the scaled entry (101.05 x factor) and the given spread.</summary>
    public static SymbolInfo Gold(double pip, double factor, double spread)
    {
        double bid = 101.05 * factor;
        return new SymbolInfo(Symbol, "XAU", "USD", pip, pip, 1.0, 1.0, 10000.0, spread, true, 0.0)
        {
            Digits = 2,
            TickSize = 0.01,
            Bid = bid,
            Ask = bid + spread,
        };
    }

    /// <summary>The profile result for gold with every value automatic, from the core function.</summary>
    public static SymbolProfileResult Profile(double pip)
    {
        return SymbolProfile.Resolve(Symbol, "XAU", "USD", pip, 0.0, 0.0, 0.0, 0.0, 0.0);
    }

    /// <summary>The parameters of <see cref="ChochBotData.Parameters"/> on XAUUSD with gold's automatic values at the pip size.</summary>
    public static BotParameters Parameters(double pip)
    {
        SymbolProfileResult profile = Profile(pip);
        BotParameters reference = ChochBotData.Parameters();
        return reference with
        {
            Symbol = Symbol,
            Execution = reference.Execution with { MaxSpreadPips = profile.MaxSpreadPips, MinStopPips = profile.MinStopPips },
            Choch = reference.Choch! with { ChochMinStopPips = profile.ChochMinStopPips },
            Risk = reference.Risk with { MaxVolumeUnits = profile.MaxVolumeUnits },
            SymbolProfile = profile,
        };
    }

    public static BotHarness Harness(double pip, double factor = 16.0, double spread = 0.30)
    {
        var broker = new FakeBroker(Start, ExecutionData.Account(), Gold(pip, factor, spread));
        broker.MarginPerUnit = 0.04;
        return new BotHarness(Bars(factor), broker);
    }

    private static IReadOnlyList<Bar> Scale(IReadOnlyList<Bar> bars, double factor)
    {
        return bars.Select(b => new Bar(b.OpenTimeUtc, b.Open * factor, b.High * factor, b.Low * factor, b.Close * factor)).ToList();
    }
}
