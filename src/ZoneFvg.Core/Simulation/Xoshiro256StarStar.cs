using System;

namespace ZoneFvg.Core.Simulation;

/// <summary>
/// Seeded pseudo-random generator xoshiro256** (Blackman and Vigna), seeded through splitmix64 as
/// its authors recommend (SIM-003, SIM-004). Written out here so that a sequence depends only on
/// the seed and is identical on every platform and runtime.
/// </summary>
public sealed class Xoshiro256StarStar
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    /// <summary>Creates a generator whose whole sequence is fixed by <paramref name="seed"/> (SIM-003, SIM-004).</summary>
    /// <param name="seed">The seed. Every value, including zero, is allowed.</param>
    public Xoshiro256StarStar(ulong seed)
    {
        Seed = seed;
        ulong x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    /// <summary>The seed the sequence started from; reports print it (SIM-003, SIM-004).</summary>
    public ulong Seed { get; }

    /// <summary>Next 64-bit output of xoshiro256** (SIM-003, SIM-004).</summary>
    /// <returns>A value from the whole 64-bit range.</returns>
    public ulong NextUInt64()
    {
        unchecked
        {
            ulong result = RotateLeft(_s1 * 5UL, 7) * 9UL;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = RotateLeft(_s3, 45);
            return result;
        }
    }

    /// <summary>
    /// Next whole number from 0 up to, but not including, <paramref name="exclusiveMax"/>, without
    /// modulo bias: outputs below 2^64 mod n are rejected and drawn again (SIM-003, SIM-004).
    /// </summary>
    /// <param name="exclusiveMax">Upper bound, at least 1.</param>
    /// <returns>A value in [0, exclusiveMax).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exclusiveMax"/> is below 1.</exception>
    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax), "The upper bound must be at least 1.");
        }

        ulong bound = (ulong)exclusiveMax;
        ulong threshold = unchecked(0UL - bound) % bound;
        ulong draw = NextUInt64();
        while (draw < threshold)
        {
            draw = NextUInt64();
        }

        return (int)(draw % bound);
    }

    private static ulong SplitMix64(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    private static ulong RotateLeft(ulong value, int bits)
    {
        return (value << bits) | (value >> (64 - bits));
    }
}
