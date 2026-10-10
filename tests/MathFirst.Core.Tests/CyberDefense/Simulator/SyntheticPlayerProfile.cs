namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;

/// <summary>
/// Immutable representation of a synthetic player profile.
/// Enforces behavioral invariants for headless simulation and testing.
/// </summary>
public sealed record class SyntheticPlayerProfile
{
    public string Id { get; }
    public string Name { get; }
    public double AccuracyProbability { get; }
    public long MinSyntheticLatencyMilliseconds { get; }
    public long MaxSyntheticLatencyMilliseconds { get; }
    public int EffectiveAttackDamage { get; }
    public SyntheticUpgradePreference UpgradePreference { get; }
    public bool IsUpgradeStrategyExecutable { get; }

    /// <summary>
    /// Convenience alias for <see cref="MinSyntheticLatencyMilliseconds"/>.
    /// </summary>
    public long MinLatencyMilliseconds => MinSyntheticLatencyMilliseconds;

    /// <summary>
    /// Convenience alias for <see cref="MaxSyntheticLatencyMilliseconds"/>.
    /// </summary>
    public long MaxLatencyMilliseconds => MaxSyntheticLatencyMilliseconds;

    public SyntheticPlayerProfile(
        string id,
        string name,
        double accuracyProbability,
        long minSyntheticLatencyMilliseconds,
        long maxSyntheticLatencyMilliseconds,
        int effectiveAttackDamage = 1,
        SyntheticUpgradePreference upgradePreference = SyntheticUpgradePreference.None,
        bool isUpgradeStrategyExecutable = false)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Profile identity cannot be null or whitespace.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Profile name cannot be null or whitespace.", nameof(name));
        }

        if (double.IsNaN(accuracyProbability) || double.IsInfinity(accuracyProbability))
        {
            throw new ArgumentException("Accuracy probability must be a finite number.", nameof(accuracyProbability));
        }

        if (accuracyProbability < 0.0 || accuracyProbability > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accuracyProbability),
                accuracyProbability,
                "Accuracy probability must be between 0.0 and 1.0 inclusive.");
        }

        if (minSyntheticLatencyMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minSyntheticLatencyMilliseconds),
                minSyntheticLatencyMilliseconds,
                "Minimum latency cannot be negative.");
        }

        if (maxSyntheticLatencyMilliseconds < minSyntheticLatencyMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSyntheticLatencyMilliseconds),
                maxSyntheticLatencyMilliseconds,
                "Maximum latency cannot be less than minimum latency.");
        }

        if (effectiveAttackDamage < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveAttackDamage),
                effectiveAttackDamage,
                "Effective attack damage must be at least 1.");
        }

        Id = id;
        Name = name;
        AccuracyProbability = accuracyProbability;
        MinSyntheticLatencyMilliseconds = minSyntheticLatencyMilliseconds;
        MaxSyntheticLatencyMilliseconds = maxSyntheticLatencyMilliseconds;
        EffectiveAttackDamage = effectiveAttackDamage;
        UpgradePreference = upgradePreference;
        IsUpgradeStrategyExecutable = isUpgradeStrategyExecutable;
    }
}
