namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;

/// <summary>
/// Deterministic sampler for synthetic player attempts.
/// Generates correctness and synthetic latency using independent SplitMix64 pseudo-random streams.
/// </summary>
public sealed class SyntheticAttemptSampler
{
    /// <summary>
    /// Fixed 64-bit domain separation salt for the correctness pseudo-random stream (ASCII "CORRECT\x01").
    /// </summary>
    public const ulong CorrectnessStreamDomainSalt = 0x434F525245435401UL;

    /// <summary>
    /// Fixed 64-bit domain separation salt for the latency pseudo-random stream (ASCII "LATENCY\x02").
    /// </summary>
    public const ulong LatencyStreamDomainSalt = 0x4C4154454E435902UL;

    private readonly SyntheticPlayerProfile _profile;
    private readonly DeterministicPrng _correctnessPrng;
    private readonly DeterministicPrng _latencyPrng;
    private readonly ulong? _masterSeed;

    /// <summary>
    /// Gets the profile associated with this sampler.
    /// </summary>
    public SyntheticPlayerProfile Profile => _profile;

    /// <summary>
    /// Gets the underlying correctness pseudo-random generator.
    /// </summary>
    public DeterministicPrng CorrectnessPrng => _correctnessPrng;

    /// <summary>
    /// Gets the underlying latency pseudo-random generator.
    /// </summary>
    public DeterministicPrng LatencyPrng => _latencyPrng;

    /// <summary>
    /// Gets the optional master seed used to derive the streams, if seeded directly.
    /// </summary>
    public ulong? MasterSeed => _masterSeed;

    /// <summary>
    /// Initializes a new instance of <see cref="SyntheticAttemptSampler"/> with a profile and master seed.
    /// Derives independent, domain-separated streams for correctness and latency.
    /// </summary>
    /// <param name="profile">The synthetic player profile.</param>
    /// <param name="masterSeed">The 64-bit master seed.</param>
    public SyntheticAttemptSampler(SyntheticPlayerProfile profile, ulong masterSeed)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _masterSeed = masterSeed;

        ulong correctnessSeed = DeriveStreamSeed(masterSeed, CorrectnessStreamDomainSalt);
        ulong latencySeed = DeriveStreamSeed(masterSeed, LatencyStreamDomainSalt);

        _correctnessPrng = new DeterministicPrng(correctnessSeed);
        _latencyPrng = new DeterministicPrng(latencySeed);
    }

    /// <summary>
    /// Overload initializing <see cref="SyntheticAttemptSampler"/> with inverted parameter order.
    /// </summary>
    /// <param name="masterSeed">The 64-bit master seed.</param>
    /// <param name="profile">The synthetic player profile.</param>
    public SyntheticAttemptSampler(ulong masterSeed, SyntheticPlayerProfile profile)
        : this(profile, masterSeed)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SyntheticAttemptSampler"/> with explicit PRNG instances.
    /// </summary>
    /// <param name="profile">The synthetic player profile.</param>
    /// <param name="correctnessPrng">Explicit correctness generator.</param>
    /// <param name="latencyPrng">Explicit latency generator.</param>
    public SyntheticAttemptSampler(
        SyntheticPlayerProfile profile,
        DeterministicPrng correctnessPrng,
        DeterministicPrng latencyPrng)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _correctnessPrng = correctnessPrng ?? throw new ArgumentNullException(nameof(correctnessPrng));
        _latencyPrng = latencyPrng ?? throw new ArgumentNullException(nameof(latencyPrng));
        _masterSeed = null;
    }

    /// <summary>
    /// Derives an independent 64-bit seed from a master seed and domain separation salt
    /// using a SplitMix64 mixing step.
    /// </summary>
    /// <param name="masterSeed">The master seed.</param>
    /// <param name="domainSalt">The domain separation constant.</param>
    /// <returns>A well-mixed, domain-separated 64-bit seed.</returns>
    public static ulong DeriveStreamSeed(ulong masterSeed, ulong domainSalt)
    {
        unchecked
        {
            ulong z = masterSeed ^ domainSalt;
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>
    /// Samples whether the current synthetic attempt is correct.
    /// Draws exactly one value from the correctness random stream.
    /// </summary>
    /// <returns><c>true</c> if the attempt was correct; otherwise, <c>false</c>.</returns>
    public bool SampleCorrectness()
    {
        double draw = _correctnessPrng.NextDouble();

        if (_profile.AccuracyProbability >= 1.0)
        {
            return true;
        }

        if (_profile.AccuracyProbability <= 0.0)
        {
            return false;
        }

        return draw < _profile.AccuracyProbability;
    }

    /// <summary>
    /// Samples synthetic latency in milliseconds according to profile bounds.
    /// Fixed ranges return immediately without consuming randomness.
    /// Bounded ranges use unbiased rejection sampling from the latency stream.
    /// </summary>
    /// <returns>Synthetic latency in milliseconds.</returns>
    public long SampleLatency()
    {
        long min = _profile.MinSyntheticLatencyMilliseconds;
        long max = _profile.MaxSyntheticLatencyMilliseconds;

        if (min == max)
        {
            return min;
        }

        long range = (max - min) + 1;
        ulong uRange = (ulong)range;

        // Unbiased bounded integer rejection sampling
        ulong bucketCount = ulong.MaxValue / uRange;
        ulong limit = bucketCount * uRange;

        ulong sample;
        do
        {
            sample = _latencyPrng.NextUInt64();
        } while (sample >= limit);

        return checked(min + (long)(sample % uRange));
    }

    /// <summary>
    /// Samples a complete synthetic attempt, generating both correctness and latency.
    /// </summary>
    /// <returns>An immutable <see cref="SyntheticAttemptResult"/>.</returns>
    public SyntheticAttemptResult SampleAttempt()
    {
        bool isCorrect = SampleCorrectness();
        long latency = SampleLatency();
        return new SyntheticAttemptResult(isCorrect, latency);
    }
}
