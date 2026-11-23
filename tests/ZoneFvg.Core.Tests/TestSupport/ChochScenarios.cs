using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>What the engine showed after one M15 bar of a Choch run: the signals, the zone activity and the zones.</summary>
public sealed record ChochStep(int TailIndex, IReadOnlyList<Signal> Signals, ZoneActivity Activity, IReadOnlyList<Zone> Zones);

/// <summary>
/// Hand-built histories of the Choch entry model (SIG-035 to SIG-041, prompts/WP37-choch-entry.md).
/// The zone is that of <see cref="EngineScenarios"/>: demand zone D-20260301-0800, proximal
/// 100.2, distal 99.5, active from Monday 2026-03-02 00:00 UTC, ATR(H4) at the M15 bars
/// 15 / 14 = 1.0714286 (no H4 bar follows the leg). The H4 history holds a confirmed swing high at
/// 101.0 (the bar opening at the activation - 100 hours), which the zone's impulse leg closed
/// above (close 102.0): a broken level, not an opposing level of SIG-040 (orchestrating session's
/// answer of 2026-11-23).
/// </summary>
/// <remarks>
/// <para>M15: <see cref="Warmup"/> flat bars (100.6, 100.65, 100.55, 100.6), true range 0.1, end
/// at the activation, so the engine is ready from the first tail bar m0 on.</para>
/// <para><b>Tail A</b> (CHoCH without sweep): m3 is a swing high at 101.0, confirmed at m6; m7
/// touches (bar 0); m8 makes the lowest low 99.9 (a swing low confirmed at m11); m11 closes at
/// 101.05 above 101.0: CHoCH, bar 4. True ranges m0 to m11: 0.15, 0.2, 0.2, 0.2, 0.35, 0.3, 0.25,
/// 0.25, 0.3, 0.35, 0.4, 0.6 (sum 3.55); with two flat bars ATR(M15) at m11 = 3.75 / 14. Buy:
/// entry 101.0, stop 99.9 - 0.1 x 3.75 / 14 = 99.87321429, target 101.0 + 2 x 1.12678571 =
/// 103.25357143 (default). With ChochStopAtDistal: stop 99.5 - 0.1 x 3.75 / 14.</para>
/// <para><b>Tail B</b>: m0 to m10 of tail A, then m11 (a lower high 100.65, confirmed at m14),
/// m12 sweeps (low 99.8 below the swing low 99.9 of m8), m15 closes at 100.75 above 100.65: CHoCH,
/// bar 8. True ranges m2 to m15 sum to 4.7; ATR(M15) = 4.7 / 14. Buy: entry 100.65, stop 99.8 -
/// 0.1 x 4.7 / 14 = 99.76642857, target 100.65 + 2 x 0.88357143 = 102.41714286 (default: the
/// H4 swing high at 101.0 is broken).</para>
/// </remarks>
public static class ChochScenarios
{
    /// <summary>The Choch defaults of SIG-035.</summary>
    public static readonly ChochParameters Defaults = new();

    /// <summary>M15 warm-up of the Choch model with default parameters: (120 + 1) x 16 + 32 + 100 + 3 + 14 + 2.</summary>
    public const int Warmup = 2087;

    /// <summary>ATR(H4) at every tail bar: (13 x 1.0 + 2.0) / 14.</summary>
    public const double AtrH4 = 15.0 / 14.0;

    /// <summary>ATR(M15) at m11 of tail A.</summary>
    public const double AtrM15A = 3.75 / 14.0;

    /// <summary>ATR(M15) at m15 of tail B.</summary>
    public const double AtrM15B = 4.7 / 14.0;

    /// <summary>Open time of the H4 swing high at 101.0 of the reference history: the activation - 100 hours.</summary>
    public static readonly DateTime BrokenSwingOpen = EngineScenarios.Activation.AddHours(-100.0);

    /// <summary>Tail A, m0 to m11.</summary>
    public static readonly (double Open, double High, double Low, double Close)[] TailA =
    {
        (100.6, 100.7, 100.55, 100.65),
        (100.65, 100.8, 100.6, 100.75),
        (100.75, 100.9, 100.7, 100.85),
        (100.85, 101.0, 100.8, 100.9),
        (100.9, 100.95, 100.6, 100.65),
        (100.65, 100.7, 100.4, 100.45),
        (100.45, 100.5, 100.25, 100.3),
        (100.3, 100.35, 100.1, 100.15),
        (100.15, 100.2, 99.9, 100.0),
        (100.0, 100.3, 99.95, 100.25),
        (100.25, 100.6, 100.2, 100.55),
        (100.55, 101.1, 100.5, 101.05),
    };

    /// <summary>Tail B, m0 to m15: m0 to m10 of tail A and the five bars of the lower high, the sweep and the CHoCH.</summary>
    public static readonly (double Open, double High, double Low, double Close)[] TailB = TailA.Take(11).Concat(new[]
    {
        (100.55, 100.65, 100.3, 100.35),
        (100.35, 100.4, 99.8, 99.85),
        (99.85, 100.3, 99.85, 100.25),
        (100.25, 100.6, 100.2, 100.55),
        (100.55, 100.8, 100.5, 100.75),
    }).ToArray();

    /// <summary>A flat bar after m8 of tail A: no swing, no close above 101.0, low above 99.9.</summary>
    public static readonly (double Open, double High, double Low, double Close) Flat = (100.0, 100.05, 99.95, 100.0);

    /// <summary>The H4 history of <see cref="EngineScenarios.DemandH4"/> with the warm-up count.</summary>
    public static IReadOnlyList<Bar> H4()
    {
        return EngineScenarios.DemandH4(EngineScenarios.H4Warmup);
    }

    /// <summary>
    /// <see cref="H4"/> with an unbroken swing high at <paramref name="high"/> three bars before the
    /// broken one at 101.0 (the bar opening at the activation - 112 hours). It lies outside the 15
    /// bars of ATR(H4), so ATR(H4) stays 15 / 14, and before the swing at 101.0, so the zone's break
    /// of structure is unchanged.
    /// </summary>
    public static IReadOnlyList<Bar> H4WithSwingHigh(double high)
    {
        IReadOnlyList<Bar> h4 = H4();
        return h4.ReplaceAt(h4.Count - 28, 100.0, high, 99.5, 100.0);
    }

    /// <summary>Open time of the swing bar of <see cref="H4WithSwingHigh"/>.</summary>
    public static DateTime AddedSwingOpen => EngineScenarios.Activation.AddHours(-112.0);

    /// <summary>The feed: D1 of the bias, the H4 bars and <paramref name="flatBefore"/> flat M15 bars then the tail; mirrored around 200 when asked.</summary>
    public static IReadOnlyList<FeedBar> History(
        BiasHistory bias,
        (double Open, double High, double Low, double Close)[] tail,
        bool mirrored = false,
        IReadOnlyList<Bar>? h4 = null,
        int flatBefore = Warmup,
        int extraD1 = 0)
    {
        IReadOnlyList<Bar> h4Bars = h4 ?? H4();
        IReadOnlyList<Bar> m15 = EngineScenarios.M15(flatBefore, tail);
        if (mirrored)
        {
            h4Bars = h4Bars.Mirror(EngineScenarios.Pivot);
            m15 = m15.Mirror(EngineScenarios.Pivot);
        }

        return EngineFeed.Merge(EngineScenarios.D1(bias, EngineScenarios.D1Warmup + extraD1), h4Bars, m15);
    }

    /// <summary>Runs a feed through a new Choch engine and records each tail bar (M15 bars from the activation on).</summary>
    public static IReadOnlyList<ChochStep> Run(IReadOnlyList<FeedBar> feed, ChochParameters? choch = null, bool replay = false, SignalParameters? parameters = null)
    {
        var engine = new SignalEngine(parameters ?? EngineScenarios.Defaults, choch ?? Defaults);
        return Run(engine, feed, replay);
    }

    /// <summary>Runs a feed through the given engine and records each tail bar.</summary>
    public static IReadOnlyList<ChochStep> Run(SignalEngine engine, IReadOnlyList<FeedBar> feed, bool replay = false)
    {
        var steps = new List<ChochStep>();
        foreach (FeedBar item in feed)
        {
            IReadOnlyList<Signal> signals = engine.OnBarClosed(item.TimeFrame, item.Bar, replay);
            if (item.TimeFrame == TimeFrame.M15 && item.Bar.OpenTimeUtc >= EngineScenarios.Activation)
            {
                int k = (int)((item.Bar.OpenTimeUtc - EngineScenarios.Activation).TotalMinutes / 15.0);
                steps.Add(new ChochStep(k, signals.ToArray(), engine.LastActivity, engine.Zones.ToArray()));
            }
            else
            {
                Xunit.Assert.Empty(signals);
            }
        }

        return steps;
    }

    /// <summary>All signals of a run.</summary>
    public static IReadOnlyList<Signal> Signals(IReadOnlyList<ChochStep> steps)
    {
        return steps.SelectMany(step => step.Signals).ToList();
    }

    /// <summary>The step of tail bar k.</summary>
    public static ChochStep At(IReadOnlyList<ChochStep> steps, int k)
    {
        return steps.Single(step => step.TailIndex == k);
    }

    /// <summary>Tail A up to m8, then flat bars, then a bar that closes above 101.0 at bar <paramref name="breakBar"/> of the window (touch at m7).</summary>
    public static (double Open, double High, double Low, double Close)[] LateBreak(int breakBar)
    {
        var tail = TailA.Take(9).ToList();
        while (tail.Count < 7 + breakBar)
        {
            tail.Add(Flat);
        }

        tail.Add((100.0, 101.1, 99.95, 101.05));
        return tail.ToArray();
    }
}
