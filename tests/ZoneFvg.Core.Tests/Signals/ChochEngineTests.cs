using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The Choch entry model through the signal engine (SIG-036 to SIG-039), on the histories of
/// <see cref="ChochScenarios"/>. Every number is computed by hand in the remarks of
/// <see cref="ChochScenarios"/> and prompts/WP37-choch-entry.md; the expected values below are
/// written as the same arithmetic, not taken from the code under test.
/// </summary>
public class ChochEngineTests
{
    private const int Digits = 9;
    private const string ZoneId = EngineScenarios.ZoneId;

    private static IReadOnlyList<ChochStep> Run((double Open, double High, double Low, double Close)[] tail, ChochParameters? choch = null, BiasHistory bias = BiasHistory.Long, bool mirrored = false)
    {
        return ChochScenarios.Run(ChochScenarios.History(bias, tail, mirrored), choch);
    }

    private static (double Open, double High, double Low, double Close)[] With((double Open, double High, double Low, double Close)[] tail, int index, (double Open, double High, double Low, double Close) bar)
    {
        var copy = tail.ToArray();
        copy[index] = bar;
        return copy;
    }

    private static ZoneEndReason? EndOf(ChochStep step)
    {
        ZoneEnd? end = step.Activity.Ended.SingleOrDefault(e => e.ZoneId == ZoneId);
        return end?.Reason;
    }

    // ------------------------------------------------------------------ SIG-039: tail A, tail B, sell mirror

    [Fact]
    [Trait("Req", "SIG-039")]
    [Trait("Req", "SIG-037")]
    public void TailA_CloseAboveTheSwingHigh_BuyLimitAtTheBrokenLevel()
    {
        IReadOnlyList<ChochStep> steps = Run(ChochScenarios.TailA);

        Signal signal = Assert.Single(ChochScenarios.Signals(steps));
        Assert.Equal(signal, Assert.Single(ChochScenarios.At(steps, 11).Signals));
        Assert.Equal(SignalKind.Choch, signal.Kind);
        Assert.Equal(ZoneId, signal.ZoneId);
        Assert.Equal(Side.Buy, signal.Side);
        Assert.Equal(101.0, signal.Entry);
        Assert.Equal(101.0, signal.Level);
        Assert.Equal(99.9 - (0.1 * ChochScenarios.AtrM15A), signal.Stop, Digits);
        Assert.Equal(99.87321429, signal.Stop, 8);
        Assert.Equal(101.0 + (2.0 * (101.0 - (99.9 - (0.1 * ChochScenarios.AtrM15A)))), signal.Target, Digits);
        Assert.Equal(103.25357143, signal.Target, 8);
        Assert.Null(signal.Fvg);
        Assert.False(signal.Swept);
        Assert.Equal("default", signal.TargetSource);
        Assert.Equal(EngineScenarios.TailOpen(12), signal.ConfirmationCloseUtc);
        Assert.False(signal.FromReplay);
        Assert.Equal(ChochScenarios.AtrH4, signal.AtrH4, Digits);
        Assert.Null(signal.ExpiryUtc);
        Assert.Equal(ZoneEndReason.Signalled, EndOf(ChochScenarios.At(steps, 11)));
    }

    [Fact]
    [Trait("Req", "SIG-039")]
    public void TailA_StopAtDistal_TheLowerOfTheExtremeAndTheDistal()
    {
        Signal signal = Assert.Single(ChochScenarios.Signals(Run(ChochScenarios.TailA, new ChochParameters { ChochStopAtDistal = true })));

        Assert.Equal(99.5 - (0.1 * ChochScenarios.AtrM15A), signal.Stop, Digits);
        Assert.Equal(99.47321429, signal.Stop, 8);
        Assert.Equal(101.0, signal.Entry);
    }

    [Fact]
    [Trait("Req", "SIG-039")]
    [Trait("Req", "SIG-037")]
    [Trait("Req", "SIG-038")]
    public void TailB_LowerHighSweepAndBreak_EntryAtTheLowerHigh()
    {
        IReadOnlyList<ChochStep> steps = Run(ChochScenarios.TailB);

        Signal signal = Assert.Single(ChochScenarios.Signals(steps));
        Assert.Equal(signal, Assert.Single(ChochScenarios.At(steps, 15).Signals));
        Assert.Equal(100.65, signal.Entry);
        Assert.Equal(100.65, signal.Level);
        Assert.Equal(99.8 - (0.1 * ChochScenarios.AtrM15B), signal.Stop, Digits);
        Assert.Equal(99.76642857, signal.Stop, 8);
        Assert.True(signal.Swept);

        // The H4 swing high at 101.0 lies above 100.65 but was closed above by the leg: not an opposing level.
        Assert.Equal("default", signal.TargetSource);
        Assert.Equal(102.41714286, signal.Target, 8);
    }

    [Fact]
    [Trait("Req", "SIG-039")]
    public void TailA_Mirrored_SellLimitAt99()
    {
        Signal signal = Assert.Single(ChochScenarios.Signals(Run(ChochScenarios.TailA, bias: BiasHistory.Short, mirrored: true)));

        Assert.Equal(EngineScenarios.MirroredZoneId, signal.ZoneId);
        Assert.Equal(Side.Sell, signal.Side);
        Assert.Equal(SignalKind.Choch, signal.Kind);
        Assert.Equal(99.0, signal.Entry, Digits);
        Assert.Equal(99.0, signal.Level!.Value, Digits);
        Assert.Equal(100.12678571, signal.Stop, 8);
        Assert.Equal(96.74642857, signal.Target, 8);
        Assert.False(signal.Swept);
        Assert.Equal("default", signal.TargetSource);
    }

    // ------------------------------------------------------------------ SIG-036: window, invalidation, one signal, bias

    [Fact]
    [Trait("Req", "SIG-036")]
    public void Window_BreakOnBar32_GivesTheSignal_AndTheFvgWindowDoesNotEndTheZone()
    {
        IReadOnlyList<ChochStep> steps = Run(ChochScenarios.LateBreak(32));

        // Bar 16 (the Fvg model's window end) and bar 31: the zone is still Touched.
        Assert.Equal(ZoneState.Touched, Assert.Single(ChochScenarios.At(steps, 7 + 16).Zones).State);
        Assert.Null(EndOf(ChochScenarios.At(steps, 7 + 16)));
        Assert.Equal(ZoneState.Touched, Assert.Single(ChochScenarios.At(steps, 7 + 31).Zones).State);
        Signal signal = Assert.Single(ChochScenarios.Signals(steps));
        Assert.Equal(EngineScenarios.TailOpen(7 + 33), signal.ConfirmationCloseUtc);
        Assert.Equal(101.0, signal.Entry);
        Assert.Equal(ZoneEndReason.Signalled, EndOf(ChochScenarios.At(steps, 7 + 32)));
    }

    [Fact]
    [Trait("Req", "SIG-036")]
    [Trait("Req", "SIG-037")]
    public void Window_BreakOnBar33_NoSignal_ZoneUsedWithNoChochAtBar32()
    {
        IReadOnlyList<ChochStep> steps = Run(ChochScenarios.LateBreak(33));

        Assert.Empty(ChochScenarios.Signals(steps));
        Assert.Equal(ZoneEndReason.NoChoch, EndOf(ChochScenarios.At(steps, 7 + 32)));
        Assert.Empty(ChochScenarios.At(steps, 7 + 32).Zones);
        Assert.All(steps.Where(step => step.TailIndex != 7 + 32), step => Assert.NotEqual(ZoneEndReason.NoChoch, EndOf(step)));
    }

    [Fact]
    [Trait("Req", "SIG-036")]
    public void Window_OtherLength_EndsAfterItsLastBar()
    {
        var choch = new ChochParameters { ChochWindowBars = 4 };

        Assert.Single(ChochScenarios.Signals(Run(ChochScenarios.LateBreak(4), choch)));
        IReadOnlyList<ChochStep> late = Run(ChochScenarios.LateBreak(5), choch);
        Assert.Empty(ChochScenarios.Signals(late));
        Assert.Equal(ZoneEndReason.NoChoch, EndOf(ChochScenarios.At(late, 7 + 4)));
    }

    [Fact]
    [Trait("Req", "SIG-036")]
    public void CloseBelowDistalBeforeTheBreak_ZoneInvalid_NoSignal()
    {
        (double, double, double, double)[] tail = With(ChochScenarios.TailA, 9, (100.0, 100.3, 99.3, 99.4));

        IReadOnlyList<ChochStep> steps = Run(tail);

        Assert.Equal(ZoneEndReason.Invalid, EndOf(ChochScenarios.At(steps, 9)));
        Assert.Empty(ChochScenarios.Signals(steps));
    }

    [Fact]
    [Trait("Req", "SIG-036")]
    public void SecondBreakAfterTheSignal_NothingMore()
    {
        (double, double, double, double)[] tail = ChochScenarios.TailA.Concat(new[]
        {
            (101.05, 101.5, 101.0, 101.4),
            (101.4, 101.45, 101.1, 101.2),
            (101.2, 101.3, 101.0, 101.1),
            (101.1, 101.35, 101.0, 101.2),
            (101.2, 101.8, 101.1, 101.7),
        }).ToArray();
        IReadOnlyList<Bar> m15 = EngineScenarios.M15(ChochScenarios.Warmup, tail);

        // m16 is a change of character of its own (above the swing high 101.5 of m12) ...
        Swing? second = ChochDetector.BrokenSwing(m15, ZoneType.Demand, 3);
        Assert.NotNull(second);
        Assert.Equal(101.5, second!.Price);

        // ... but the zone gave its one signal at m11.
        Signal signal = Assert.Single(ChochScenarios.Signals(Run(tail)));
        Assert.Equal(EngineScenarios.TailOpen(12), signal.ConfirmationCloseUtc);
    }

    [Fact]
    [Trait("Req", "SIG-036")]
    public void UseBiasFalse_BuyUnderAShortBias()
    {
        Signal signal = Assert.Single(ChochScenarios.Signals(Run(ChochScenarios.TailA, bias: BiasHistory.Short)));

        Assert.Equal(Side.Buy, signal.Side);
        Assert.Equal(101.0, signal.Entry);
    }

    [Fact]
    [Trait("Req", "SIG-036")]
    public void UseBiasTrue_ShortBias_NoSignal_ZoneUsedWithBiasMismatch()
    {
        var choch = new ChochParameters { ChochUseBias = true };

        IReadOnlyList<ChochStep> steps = Run(ChochScenarios.TailA, choch, BiasHistory.Short);

        Assert.Empty(ChochScenarios.Signals(steps));
        Assert.Equal(ZoneEndReason.BiasMismatch, EndOf(ChochScenarios.At(steps, 11)));
        Assert.Empty(ChochScenarios.At(steps, 11).Zones);
        Assert.Single(ChochScenarios.Signals(Run(ChochScenarios.TailA, choch, BiasHistory.Long)));
    }

    // ------------------------------------------------------------------ SIG-037: the change of character

    [Fact]
    [Trait("Req", "SIG-037")]
    public void CloseEqualToTheSwingHigh_ConfirmsNothing()
    {
        IReadOnlyList<ChochStep> steps = Run(With(ChochScenarios.TailA, 11, (100.55, 101.1, 100.5, 101.0)));

        Assert.Empty(ChochScenarios.Signals(steps));
        Assert.Equal(ZoneState.Touched, Assert.Single(ChochScenarios.At(steps, 11).Zones).State);
    }

    [Fact]
    [Trait("Req", "SIG-037")]
    public void CloseOneTickAboveTheSwingHigh_Confirms()
    {
        Signal signal = Assert.Single(ChochScenarios.Signals(Run(With(ChochScenarios.TailA, 11, (100.55, 101.1, 100.5, 101.00001)))));

        Assert.Equal(101.0, signal.Entry);
    }

    [Fact]
    [Trait("Req", "SIG-037")]
    public void SwingHighAlreadyClosedAbove_ConfirmsNothing_NoOlderSwingTried()
    {
        // m7 closes at 101.05 above the swing high 101.0 of m3 without touching (low 100.25); m8 touches.
        // Equal highs 101.1 at m7 and m8 make no new swing high, so at m12 the most recent swing high is
        // still 101.0, already closed above: m12's close 101.05 confirms nothing.
        (double, double, double, double)[] tail = ChochScenarios.TailA.Take(7).Concat(new[]
        {
            (100.3, 101.1, 100.25, 101.05),
            (101.05, 101.1, 100.1, 100.15),
            (100.15, 100.2, 99.9, 100.0),
            (100.0, 100.3, 99.95, 100.25),
            (100.25, 100.6, 100.2, 100.55),
            (100.55, 101.1, 100.5, 101.05),
        }).ToArray();

        IReadOnlyList<ChochStep> steps = Run(tail);

        Assert.Empty(ChochScenarios.Signals(steps));
        Zone zone = Assert.Single(ChochScenarios.At(steps, 12).Zones);
        Assert.Equal(ZoneState.Touched, zone.State);
        Assert.Equal(EngineScenarios.TailOpen(8), zone.TouchBarOpenUtc);
    }

    [Fact]
    [Trait("Req", "SIG-037")]
    [Trait("Req", "SIG-041")]
    public void MissingAtrM15_ConfirmsNothing_TheWindowGoesOn()
    {
        // No M15 bar before m0: at m11 only 12 bars exist, so ATR(M15) is missing. Nothing confirms and the
        // zone stays in its window until the window ends at bar 32 (m39) with NoChoch.
        (double, double, double, double)[] tail = ChochScenarios.TailA.Concat(Enumerable.Repeat((100.9, 100.95, 100.85, 100.9), 30)).ToArray();

        IReadOnlyList<ChochStep> steps = ChochScenarios.Run(ChochScenarios.History(BiasHistory.Long, tail, flatBefore: 0));

        Assert.Null(EndOf(ChochScenarios.At(steps, 11)));
        Assert.All(steps.Where(step => step.TailIndex < 39), step => Assert.Null(EndOf(step)));
        Assert.Equal(ZoneEndReason.NoChoch, EndOf(ChochScenarios.At(steps, 39)));
        Assert.Empty(ChochScenarios.Signals(steps));
    }

    [Fact]
    [Trait("Req", "SIG-041")]
    [Trait("Req", "SIG-037")]
    public void ChangeOfCharacterDuringWarmup_ConsumesTheZoneWithoutASignal()
    {
        // Three flat bars before m0: ATR(M15) exists at m11 (14 true ranges need 15 bars), but the engine is far from ready.
        IReadOnlyList<ChochStep> steps = ChochScenarios.Run(ChochScenarios.History(BiasHistory.Long, ChochScenarios.TailA, flatBefore: 3));

        Assert.Equal(ZoneEndReason.Signalled, EndOf(ChochScenarios.At(steps, 11)));
        Assert.Empty(ChochScenarios.Signals(steps));
        Assert.All(steps, step => Assert.Empty(step.Zones));
    }

    [Fact]
    [Trait("Req", "SIG-037")]
    [Trait("Req", "SIG-039")]
    public void StopNotBelowTheEntry_ConfirmsNothing()
    {
        // Answer of 2026-11-23. Before the activation p3 is a swing high at 100.15, below the proximal 100.2.
        // m0 touches (low 100.15) and closes above 100.15: a CHoCH on the touch bar, whose low is the lowest
        // since the touch and equals the entry. With StopBufferAtr 0 the stop equals the entry, so nothing
        // confirms and the window goes on; with the default buffer the stop is below the entry.
        (double, double, double, double)[] tail =
        {
            (100.0, 100.05, 99.95, 100.0),
            (100.0, 100.08, 99.95, 100.05),
            (100.05, 100.1, 100.0, 100.05),
            (100.05, 100.15, 100.0, 100.1),
            (100.1, 100.12, 100.0, 100.05),
            (100.05, 100.1, 100.0, 100.05),
            (100.05, 100.1, 100.0, 100.1),
            (100.15, 100.3, 100.15, 100.25),
        };
        IReadOnlyList<FeedBar> feed = ChochScenarios.History(BiasHistory.Long, tail, flatBefore: ChochScenarios.Warmup - 7);

        IReadOnlyList<ChochStep> withBuffer = ChochScenarios.Run(feed);
        IReadOnlyList<ChochStep> withoutBuffer = ChochScenarios.Run(feed, parameters: EngineScenarios.Defaults with { StopBufferAtr = 0.0 });

        Signal signal = Assert.Single(ChochScenarios.At(withBuffer, 0).Signals);
        Assert.Equal(100.15, signal.Entry);
        Assert.True(signal.Stop < 100.15);
        Assert.Empty(ChochScenarios.Signals(withoutBuffer));
        Assert.Null(EndOf(ChochScenarios.At(withoutBuffer, 0)));
        Assert.Equal(ZoneState.Touched, Assert.Single(ChochScenarios.At(withoutBuffer, 0).Zones).State);
    }

    // ------------------------------------------------------------------ SIG-038: the sweep

    [Fact]
    [Trait("Req", "SIG-038")]
    public void TailB_RequireSweep_SignalAtM15()
    {
        IReadOnlyList<ChochStep> steps = Run(ChochScenarios.TailB, new ChochParameters { RequireSweep = true });

        Signal signal = Assert.Single(ChochScenarios.At(steps, 15).Signals);
        Assert.True(signal.Swept);
        Assert.Single(ChochScenarios.Signals(steps));
    }

    [Fact]
    [Trait("Req", "SIG-038")]
    public void TailB_LowEqualToTheSwingLow_NoSweep()
    {
        // m12's low equals the swing low 99.9 of m8, and m13's low stays above it: no bar sweeps.
        (double, double, double, double)[] tail = With(With(ChochScenarios.TailB, 12, (100.35, 100.4, 99.9, 99.95)), 13, (99.95, 100.3, 99.95, 100.25));

        Signal free = Assert.Single(ChochScenarios.Signals(Run(tail)));
        Assert.False(free.Swept);
        Assert.Equal(100.65, free.Entry);
        Assert.Empty(ChochScenarios.Signals(Run(tail, new ChochParameters { RequireSweep = true })));
    }

    [Fact]
    [Trait("Req", "SIG-038")]
    public void TailA_RequireSweep_NoSignal_ZoneEndsNoChoch()
    {
        IReadOnlyList<ChochStep> steps = Run(ChochScenarios.TailA.Concat(Enumerable.Repeat((101.05, 101.06, 101.04, 101.05), 30)).ToArray(), new ChochParameters { RequireSweep = true });

        Assert.Empty(ChochScenarios.Signals(steps));
        Assert.Null(EndOf(ChochScenarios.At(steps, 11)));
        Assert.Equal(ZoneEndReason.NoChoch, EndOf(ChochScenarios.At(steps, 7 + 32)));
    }

    [Fact]
    [Trait("Req", "SIG-038")]
    public void SweepOnTheTouchBarItself_Counts()
    {
        // m4 is a swing low at 100.3, confirmed at m7; the touch bar m8 has the low 100.1 below it. m9 to m11
        // stay at or above 100.3 (and above the new swing low 100.1 of m8), so only the touch bar sweeps.
        (double, double, double, double)[] tail = ChochScenarios.TailA.Take(4).Concat(new[]
        {
            (100.9, 100.95, 100.3, 100.4),
            (100.4, 100.6, 100.35, 100.5),
            (100.5, 100.6, 100.4, 100.45),
            (100.45, 100.5, 100.38, 100.42),
            (100.42, 100.45, 100.1, 100.15),
            (100.35, 100.45, 100.32, 100.4),
            (100.4, 100.6, 100.35, 100.55),
            (100.55, 101.1, 100.5, 101.05),
        }).ToArray();

        Signal signal = Assert.Single(ChochScenarios.Signals(Run(tail, new ChochParameters { RequireSweep = true })));

        Assert.True(signal.Swept);
        Assert.Equal(101.0, signal.Entry);
        Assert.Equal(EngineScenarios.TailOpen(12), signal.ConfirmationCloseUtc);
    }

    [Fact]
    [Trait("Req", "SIG-038")]
    public void SweepOnTheConfirmingBar_Counts()
    {
        // m11 (as tail B) confirms the swing low 99.9 of m8; m13 sweeps it (low 99.85) and closes above 101.0.
        (double, double, double, double)[] tail = ChochScenarios.TailA.Take(11).Concat(new[]
        {
            (100.55, 100.65, 100.3, 100.35),
            (100.35, 100.5, 100.0, 100.4),
            (100.4, 101.1, 99.85, 101.05),
        }).ToArray();

        Signal signal = Assert.Single(ChochScenarios.Signals(Run(tail, new ChochParameters { RequireSweep = true })));

        Assert.True(signal.Swept);
        Assert.Equal(101.0, signal.Entry);
        Assert.Equal(EngineScenarios.TailOpen(14), signal.ConfirmationCloseUtc);
        Assert.True(signal.Stop < 99.85);
    }
}
