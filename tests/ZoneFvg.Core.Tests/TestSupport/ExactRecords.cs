using System.Globalization;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Bit-for-bit comparison of zones and signals (SIG-002, SIG-019). Every double is compared by its
/// 64-bit pattern, so even 0.0 and -0.0 differ; every time by its ticks and kind.
/// </summary>
public static class ExactRecords
{
    /// <summary>A text key holding every field of the zone; equal keys mean bit-identical zones.</summary>
    public static string Key(Zone zone)
    {
        return string.Join(
            "|",
            zone.Id,
            zone.Type.ToString(),
            Bits(zone.Proximal),
            Bits(zone.Distal),
            Time(zone.ActivationTimeUtc),
            Bits(zone.AtrAtCreation),
            zone.State.ToString(),
            zone.TouchBarOpenUtc is null ? "-" : Time(zone.TouchBarOpenUtc.Value),
            zone.ExtremeSinceTouch is null ? "-" : Bits(zone.ExtremeSinceTouch.Value));
    }

    /// <summary>
    /// A text key holding every field of the signal and its gap; equal keys mean bit-identical
    /// signals. A signal without a gap (Breakout, SIG-026) has "-" for each of the six gap parts;
    /// Kind and Level come last.
    /// </summary>
    public static string Key(Signal signal)
    {
        Fvg? fvg = signal.Fvg;
        return string.Join(
            "|",
            signal.ZoneId,
            signal.Side.ToString(),
            Bits(signal.Entry),
            Bits(signal.Stop),
            Bits(signal.Target),
            fvg is null ? "-" : fvg.Side.ToString(),
            fvg is null ? "-" : Bits(fvg.Upper),
            fvg is null ? "-" : Bits(fvg.Lower),
            fvg is null ? "-" : Time(fvg.C3OpenUtc),
            fvg is null ? "-" : Time(fvg.C2OpenUtc),
            fvg is null ? "-" : Time(fvg.C1OpenUtc),
            Time(signal.ConfirmationCloseUtc),
            signal.FromReplay ? "replay" : "live",
            Bits(signal.AtrH4),
            signal.Kind.ToString(),
            signal.Level is null ? "-" : Bits(signal.Level.Value));
    }

    /// <summary>Keys of every zone, in list order.</summary>
    public static string[] Keys(IEnumerable<Zone> zones)
    {
        return zones.Select(Key).ToArray();
    }

    /// <summary>Keys of every signal, in list order.</summary>
    public static string[] Keys(IEnumerable<Signal> signals)
    {
        return signals.Select(Key).ToArray();
    }

    private static string Bits(double value)
    {
        return BitConverter.DoubleToInt64Bits(value).ToString(CultureInfo.InvariantCulture) + "(" + value.ToString("R", CultureInfo.InvariantCulture) + ")";
    }

    private static string Time(DateTime value)
    {
        return value.Ticks.ToString(CultureInfo.InvariantCulture) + value.Kind.ToString();
    }
}

/// <summary>Raised by a property test to carry the seed of the failing history in its message.</summary>
public sealed class SeededTestFailure : Exception
{
    public SeededTestFailure(int seed, Exception inner)
        : base("Property test failed for seed " + seed.ToString(CultureInfo.InvariantCulture) + ": " + inner.Message, inner)
    {
    }

    /// <summary>Runs the check and, if it fails, throws again with the seed in the message.</summary>
    public static void Run(int seed, Action check)
    {
        try
        {
            check();
        }
        catch (Exception exception)
        {
            throw new SeededTestFailure(seed, exception);
        }
    }
}
