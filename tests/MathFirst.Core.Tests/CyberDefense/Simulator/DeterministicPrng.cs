namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;

/// <summary>
/// Deterministic 64-bit pseudo-random number generator based on the SplitMix64 algorithm.
/// Designed for reproducible synthetic player simulations without system clock or static RNG state.
/// </summary>
public sealed class DeterministicPrng
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;
    private const ulong MixMultiplier1 = 0xBF58476D1CE4E5B9UL;
    private const ulong MixMultiplier2 = 0x94D049BB133111EBUL;
    private const double DoubleScale = 1.0 / 9007199254740992.0; // 1.0 / 2^53

    private ulong _state;

    /// <summary>
    /// Initializes a new instance of <see cref="DeterministicPrng"/> with the specified explicit seed.
    /// </summary>
    /// <param name="seed">The explicit 64-bit seed.</param>
    public DeterministicPrng(ulong seed)
    {
        _state = seed;
    }

    /// <summary>
    /// Gets the current internal 64-bit generator state.
    /// </summary>
    public ulong State => _state;

    /// <summary>
    /// Generates the next pseudo-random 64-bit unsigned integer using SplitMix64.
    /// </summary>
    /// <returns>A deterministic 64-bit unsigned integer.</returns>
    public ulong NextUInt64()
    {
        unchecked
        {
            ulong z = (_state += Gamma);
            z = (z ^ (z >> 30)) * MixMultiplier1;
            z = (z ^ (z >> 27)) * MixMultiplier2;
            return z ^ (z >> 31);
        }
    }

    /// <summary>
    /// Generates the next pseudo-random double precision floating point number in [0.0, 1.0)
    /// using the upper 53 bits of the generated 64-bit unsigned integer.
    /// </summary>
    /// <returns>A double in the half-open interval [0.0, 1.0).</returns>
    public double NextDouble()
    {
        return ToDouble(NextUInt64());
    }

    /// <summary>
    /// Maps a 64-bit unsigned integer to a double in [0.0, 1.0) using its upper 53 bits.
    /// </summary>
    /// <param name="value">The 64-bit unsigned integer value.</param>
    /// <returns>A double strictly within [0.0, 1.0).</returns>
    public static double ToDouble(ulong value)
    {
        return (value >> 11) * DoubleScale;
    }
}
