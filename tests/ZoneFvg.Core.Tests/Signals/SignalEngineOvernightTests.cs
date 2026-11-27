using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-043: the signal engine in Overnight mode, fed the bars of <see cref="OvernightBotData"/>
/// (ATR(D1) 40, ATR(H4) 24, ATR(M15) 3, every M15 close 2650, bars until 2026-03-19 00:00 UTC).
/// From 2026-03-02 to 2026-03-18 the trading days that start on a Monday to Thursday evening are
/// 3, 4, 5, 6, 10, 11, 12, 13, 17, 18 and 19 March; the engine is ready from 2026-02-21 12:00 UTC
/// (1968 M15 bars from 2026-02-01).
/// </summary>
public class SignalEngineOvernightTests
{
    private static List<Signal> Run(SignalEngine engine, BarSet bars, DateTime fromUtc)
    {
        var signals = new List<Signal>();
        foreach (FeedBar item in bars.Feed)
        {
            IReadOnlyList<Signal> found = engine.OnBarClosed(item.TimeFrame, item.Bar, false);
            if (item.CloseUtc >= fromUtc)
            {
                signals.AddRange(found);
            }
        }

        return signals;
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void OnBarClosed_OvernightMode_OneBuySignalPerMondayToThursdayEvening()
    {
        var engine = SignalEngine.WithOvernight(new SignalParameters(), new OvernightParameters());

        List<Signal> signals = Run(engine, OvernightBotData.Bars(), OvernightBotData.Utc(2026, 3, 1, 0, 0));

        string[] expected = { "ON-20260303", "ON-20260304", "ON-20260305", "ON-20260306", "ON-20260310", "ON-20260311", "ON-20260312", "ON-20260313", "ON-20260317", "ON-20260318", "ON-20260319" };
        Assert.Equal(expected, signals.Select(signal => signal.ZoneId).ToArray());
        Assert.All(signals, signal =>
        {
            Assert.Equal(SignalKind.Overnight, signal.Kind);
            Assert.Equal(Side.Buy, signal.Side);
            Assert.Equal(OvernightBotData.SignalEntry, signal.Entry, 9);
            Assert.Equal(OvernightBotData.SignalStop, signal.Stop, 9);
            Assert.Equal(OvernightBotData.SignalTarget, signal.Target, 9);
            Assert.Equal(signal.Entry, signal.Level);
            Assert.Null(signal.Fvg);
            Assert.Equal(24.0, signal.AtrH4, 9);
            Assert.Equal(signal.ConfirmationCloseUtc.AddMinutes(60.0), signal.ExpiryUtc);
            Assert.Equal(new TimeSpan(18, 15, 0), NewYorkClock.ToNewYork(signal.ConfirmationCloseUtc).TimeOfDay);
            Assert.False(signal.FromReplay);
        });
        Assert.Equal(OvernightBotData.Utc(2026, 3, 3, 23, 15), signals[1].ConfirmationCloseUtc);
        Assert.Equal(OvernightBotData.Utc(2026, 3, 9, 22, 15), signals[4].ConfirmationCloseUtc);
        Assert.Empty(engine.Zones);
        Assert.Null(engine.RangeSetup);
        Assert.Equal("ON-20260319", engine.NightSetup!.Id);
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void OnBarClosed_BeforeTheEngineIsReady_NoSignal()
    {
        var engine = SignalEngine.WithOvernight(new SignalParameters(), new OvernightParameters());

        List<Signal> signals = Run(engine, OvernightBotData.Bars(), DateTime.MinValue);

        Assert.DoesNotContain(signals, signal => signal.ConfirmationCloseUtc < OvernightBotData.Utc(2026, 2, 21, 0, 0));
        Assert.Contains(signals, signal => signal.ZoneId == "ON-20260224");
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void OnBarClosed_ReplayFlag_IsCopied()
    {
        var engine = SignalEngine.WithOvernight(new SignalParameters(), new OvernightParameters());
        var signals = new List<Signal>();
        foreach (FeedBar item in OvernightBotData.Bars().Feed)
        {
            signals.AddRange(engine.OnBarClosed(item.TimeFrame, item.Bar, true));
        }

        Assert.NotEmpty(signals);
        Assert.All(signals, signal => Assert.True(signal.FromReplay));
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Engine_OvernightMode_WarmupOfTheDefaultsAndRefusedWithAnotherMode()
    {
        var engine = SignalEngine.WithOvernight(new SignalParameters(), new OvernightParameters());

        Assert.Equal(WarmupCalculator.For(new SignalParameters()), engine.WarmupBars);
        Assert.Throws<ArgumentNullException>(() => SignalEngine.WithOvernight(new SignalParameters(), null!));
        Assert.Throws<ArgumentException>(() => WarmupCalculator.For(new SignalParameters(), null, new SessionRangeParameters(), null, new OvernightParameters()));
        Assert.Throws<ArgumentException>(() => WarmupCalculator.For(new SignalParameters(), new BreakoutParameters(), null, null, new OvernightParameters()));
        Assert.Throws<ArgumentException>(() => WarmupCalculator.For(new SignalParameters(), null, null, new ChochParameters(), new OvernightParameters()));
        Assert.Equal(WarmupCalculator.For(new SignalParameters()), WarmupCalculator.For(new SignalParameters(), null, null, null, new OvernightParameters()));
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void OnBarClosed_WithoutH4Bars_NoSignal()
    {
        BarSet all = OvernightBotData.Bars();
        var bars = new BarSet(all.D1, Array.Empty<Bar>(), all.M15);
        var engine = SignalEngine.WithOvernight(new SignalParameters(), new OvernightParameters());

        // H4 is never ready, so the engine gives no signal at all (fail closed).
        Assert.Empty(Run(engine, bars, DateTime.MinValue));
    }
}
