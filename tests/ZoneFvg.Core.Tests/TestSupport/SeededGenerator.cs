namespace ZoneFvg.Core.Tests;

/// <summary>
/// Small seeded pseudo-random generator for property-style tests. It is a plain linear
/// congruential generator written out here, so a sequence depends only on the seed and can be
/// reproduced by hand or in any other language.
/// </summary>
public sealed class SeededGenerator
{
    private long _state;

    public SeededGenerator(int seed)
    {
        Seed = seed;
        _state = seed & 0x7FFFFFFFL;
    }

    /// <summary>The seed the sequence started from. Print it when a test fails.</summary>
    public int Seed { get; }

    /// <summary>Next whole number from 0 up to, but not including, <paramref name="exclusiveMax"/>.</summary>
    public int Next(int exclusiveMax)
    {
        _state = ((_state * 1103515245L) + 12345L) & 0x7FFFFFFFL;
        return (int)((_state >> 8) % exclusiveMax);
    }
}
