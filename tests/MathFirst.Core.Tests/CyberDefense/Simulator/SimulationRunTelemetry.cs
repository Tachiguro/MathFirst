namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;

/// <summary>
/// Immutable structured simulation telemetry model for bounded headless combat simulations.
/// Measures verified outcomes from authoritative combat transitions and virtual time,
/// while explicitly representing deferred gameplay features as unavailable.
/// </summary>
public sealed record class SimulationRunTelemetry
{
    // Identity
    public ulong MasterSeed { get; }
    public string ProfileId { get; }
    public string ProfileName { get; }

    // Execution metrics
    public long TotalQuestions { get; }
    public long CorrectAnswers { get; }
    public long IncorrectAnswers { get; }
    public double? AccuracyPercent { get; }
    public int HighestSectorReached { get; }
    public int TotalGameOvers { get; }
    public long TotalVirtualTimeMs { get; }
    public long TotalAppliedOpponentDamage { get; }
    public long TotalAppliedPlayerDamage { get; }
    public int TotalOpponentDefeats { get; }
    public int TotalSectorCompletions { get; }

    // Explicitly deferred gameplay metrics (GDD future systems)
    public int? FinalPlayerLevel { get; }
    public long? TotalXp { get; }
    public int? SkillPointsEarned { get; }
    public int? SkillPointsSpent { get; }
    public int? AttackRanks { get; }
    public int? FirewallRanks { get; }
    public int? CriticalStrikeRanks { get; }
    public int? OverdriveRanks { get; }
    public long? TotalShieldsAbsorbed { get; }
    public int? ReplayDistanceAfterDefeat { get; }

    // Convenience aliases aligning with domain and simulator conventions
    public long Questions => TotalQuestions;
    public long CorrectCount => CorrectAnswers;
    public long IncorrectCount => IncorrectAnswers;
    public int GameOvers => TotalGameOvers;
    public long VirtualElapsedMilliseconds => TotalVirtualTimeMs;
    public long AppliedOpponentDamage => TotalAppliedOpponentDamage;
    public long AppliedPlayerDamage => TotalAppliedPlayerDamage;
    public int OpponentDefeats => TotalOpponentDefeats;
    public int SectorCompletions => TotalSectorCompletions;
    public long? ShieldsAbsorbed => TotalShieldsAbsorbed;
    public long? Xp => TotalXp;
    public int? PlayerLevel => FinalPlayerLevel;

    /// <summary>
    /// Initializes a new instance of <see cref="SimulationRunTelemetry"/>.
    /// </summary>
    public SimulationRunTelemetry(
        ulong masterSeed,
        string profileId,
        string profileName,
        long totalQuestions,
        long correctAnswers,
        long incorrectAnswers,
        double? accuracyPercent,
        int highestSectorReached,
        int totalGameOvers,
        long totalVirtualTimeMs,
        long totalAppliedOpponentDamage,
        long totalAppliedPlayerDamage,
        int totalOpponentDefeats,
        int totalSectorCompletions,
        int? finalPlayerLevel = null,
        long? totalXp = null,
        int? skillPointsEarned = null,
        int? skillPointsSpent = null,
        int? attackRanks = null,
        int? firewallRanks = null,
        int? criticalStrikeRanks = null,
        int? overdriveRanks = null,
        long? totalShieldsAbsorbed = null,
        int? replayDistanceAfterDefeat = null)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile ID cannot be null or whitespace.", nameof(profileId));
        }

        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new ArgumentException("Profile name cannot be null or whitespace.", nameof(profileName));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(totalQuestions);
        ArgumentOutOfRangeException.ThrowIfNegative(correctAnswers);
        ArgumentOutOfRangeException.ThrowIfNegative(incorrectAnswers);

        if (checked(correctAnswers + incorrectAnswers) != totalQuestions)
        {
            throw new ArgumentException(
                $"Sum of correct ({correctAnswers}) and incorrect ({incorrectAnswers}) answers must equal total questions ({totalQuestions}).",
                nameof(correctAnswers));
        }

        if (highestSectorReached < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(highestSectorReached),
                highestSectorReached,
                "Highest sector reached must be at least 1.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(totalGameOvers);
        ArgumentOutOfRangeException.ThrowIfNegative(totalVirtualTimeMs);
        ArgumentOutOfRangeException.ThrowIfNegative(totalAppliedOpponentDamage);
        ArgumentOutOfRangeException.ThrowIfNegative(totalAppliedPlayerDamage);
        ArgumentOutOfRangeException.ThrowIfNegative(totalOpponentDefeats);
        ArgumentOutOfRangeException.ThrowIfNegative(totalSectorCompletions);

        if (totalQuestions == 0)
        {
            if (accuracyPercent.HasValue)
            {
                throw new ArgumentException(
                    "Accuracy percent must be null when total questions is 0.",
                    nameof(accuracyPercent));
            }
        }
        else
        {
            if (!accuracyPercent.HasValue)
            {
                throw new ArgumentException(
                    "Accuracy percent must have a value when total questions > 0.",
                    nameof(accuracyPercent));
            }

            if (double.IsNaN(accuracyPercent.Value) || double.IsInfinity(accuracyPercent.Value))
            {
                throw new ArgumentException("Accuracy percent must be finite.", nameof(accuracyPercent));
            }

            if (accuracyPercent.Value < 0.0 || accuracyPercent.Value > 100.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(accuracyPercent),
                    accuracyPercent.Value,
                    "Accuracy percent must be between 0.0 and 100.0 inclusive.");
            }
        }

        MasterSeed = masterSeed;
        ProfileId = profileId;
        ProfileName = profileName;
        TotalQuestions = totalQuestions;
        CorrectAnswers = correctAnswers;
        IncorrectAnswers = incorrectAnswers;
        AccuracyPercent = accuracyPercent;
        HighestSectorReached = highestSectorReached;
        TotalGameOvers = totalGameOvers;
        TotalVirtualTimeMs = totalVirtualTimeMs;
        TotalAppliedOpponentDamage = totalAppliedOpponentDamage;
        TotalAppliedPlayerDamage = totalAppliedPlayerDamage;
        TotalOpponentDefeats = totalOpponentDefeats;
        TotalSectorCompletions = totalSectorCompletions;

        FinalPlayerLevel = finalPlayerLevel;
        TotalXp = totalXp;
        SkillPointsEarned = skillPointsEarned;
        SkillPointsSpent = skillPointsSpent;
        AttackRanks = attackRanks;
        FirewallRanks = firewallRanks;
        CriticalStrikeRanks = criticalStrikeRanks;
        OverdriveRanks = overdriveRanks;
        TotalShieldsAbsorbed = totalShieldsAbsorbed;
        ReplayDistanceAfterDefeat = replayDistanceAfterDefeat;
    }

    /// <summary>
    /// Creates a fallback telemetry model representing an unobserved or unspecified configuration.
    /// </summary>
    public static SimulationRunTelemetry CreateFallback(
        ulong masterSeed,
        string profileId,
        string profileName,
        int highestSectorReached,
        long totalExecutedTurns,
        int totalGameOvers,
        long totalVirtualTimeMs)
    {
        return new SimulationRunTelemetry(
            masterSeed: masterSeed,
            profileId: string.IsNullOrWhiteSpace(profileId) ? "unspecified" : profileId,
            profileName: string.IsNullOrWhiteSpace(profileName) ? "Unspecified" : profileName,
            totalQuestions: totalExecutedTurns,
            correctAnswers: 0,
            incorrectAnswers: totalExecutedTurns,
            accuracyPercent: totalExecutedTurns > 0 ? 0.0 : null,
            highestSectorReached: Math.Max(1, highestSectorReached),
            totalGameOvers: totalGameOvers,
            totalVirtualTimeMs: totalVirtualTimeMs,
            totalAppliedOpponentDamage: 0,
            totalAppliedPlayerDamage: 0,
            totalOpponentDefeats: 0,
            totalSectorCompletions: 0);
    }
}
