namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Immutable aggregate result representing the execution of a deterministic seed batch.
/// </summary>
public sealed record class SimulationBatchResult
{
    // Identity & Execution Metadata
    public string ProfileId { get; }
    public string ProfileName { get; }
    public int ConfiguredSeedCount { get; }
    public int ExecutedSeedCount { get; }
    public bool IsCancelled { get; }
    public IReadOnlyList<SimulationBatchRunReceipt> Receipts { get; }

    // Aggregate Metrics
    public long TotalExecutedTurns { get; }
    public long TotalCorrectAnswers { get; }
    public long TotalIncorrectAnswers { get; }
    public double? ObservedAccuracyPercent { get; }
    public int TotalGameOvers { get; }
    public long TotalVirtualTimeMs { get; }
    public long TotalAppliedOpponentDamage { get; }
    public long TotalAppliedPlayerDamage { get; }
    public int TotalOpponentDefeats { get; }
    public int TotalSectorCompletions { get; }
    public int HighestSectorReached { get; }

    // Convenience Aliases
    public int ConfiguredSeeds => ConfiguredSeedCount;
    public int ExecutedSeeds => ExecutedSeedCount;
    public double? AccuracyPercent => ObservedAccuracyPercent;
    public long Questions => TotalExecutedTurns;
    public long CorrectAnswers => TotalCorrectAnswers;
    public long IncorrectAnswers => TotalIncorrectAnswers;
    public int GameOvers => TotalGameOvers;
    public long VirtualElapsedMilliseconds => TotalVirtualTimeMs;
    public long AppliedOpponentDamage => TotalAppliedOpponentDamage;
    public long AppliedPlayerDamage => TotalAppliedPlayerDamage;
    public int OpponentDefeats => TotalOpponentDefeats;
    public int SectorCompletions => TotalSectorCompletions;

    public SimulationBatchResult(
        string profileId,
        string profileName,
        int configuredSeedCount,
        int executedSeedCount,
        bool isCancelled,
        IReadOnlyList<SimulationBatchRunReceipt> receipts,
        long totalExecutedTurns,
        long totalCorrectAnswers,
        long totalIncorrectAnswers,
        double? observedAccuracyPercent,
        int totalGameOvers,
        long totalVirtualTimeMs,
        long totalAppliedOpponentDamage,
        long totalAppliedPlayerDamage,
        int totalOpponentDefeats,
        int totalSectorCompletions,
        int highestSectorReached)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile ID cannot be null or whitespace.", nameof(profileId));
        }

        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new ArgumentException("Profile name cannot be null or whitespace.", nameof(profileName));
        }

        if (receipts is null)
        {
            throw new ArgumentNullException(nameof(receipts));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(configuredSeedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(executedSeedCount);

        if (executedSeedCount > configuredSeedCount)
        {
            throw new ArgumentException(
                $"Executed seeds ({executedSeedCount}) cannot exceed configured seeds ({configuredSeedCount}).",
                nameof(executedSeedCount));
        }

        if (receipts.Count != executedSeedCount)
        {
            throw new ArgumentException(
                $"Receipts count ({receipts.Count}) must equal executed seed count ({executedSeedCount}).",
                nameof(receipts));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(totalExecutedTurns);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCorrectAnswers);
        ArgumentOutOfRangeException.ThrowIfNegative(totalIncorrectAnswers);

        if (checked(totalCorrectAnswers + totalIncorrectAnswers) != totalExecutedTurns)
        {
            throw new ArgumentException(
                $"Sum of correct ({totalCorrectAnswers}) and incorrect ({totalIncorrectAnswers}) answers must equal total executed turns ({totalExecutedTurns}).",
                nameof(totalCorrectAnswers));
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

        if (totalExecutedTurns == 0)
        {
            if (observedAccuracyPercent.HasValue)
            {
                throw new ArgumentException(
                    "Observed accuracy percent must be null when total executed turns is 0.",
                    nameof(observedAccuracyPercent));
            }
        }
        else
        {
            if (!observedAccuracyPercent.HasValue)
            {
                throw new ArgumentException(
                    "Observed accuracy percent must have a value when total executed turns > 0.",
                    nameof(observedAccuracyPercent));
            }

            if (double.IsNaN(observedAccuracyPercent.Value) || double.IsInfinity(observedAccuracyPercent.Value))
            {
                throw new ArgumentException("Observed accuracy percent must be finite.", nameof(observedAccuracyPercent));
            }

            if (observedAccuracyPercent.Value < 0.0 || observedAccuracyPercent.Value > 100.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(observedAccuracyPercent),
                    observedAccuracyPercent.Value,
                    "Observed accuracy percent must be between 0.0 and 100.0 inclusive.");
            }
        }

        ProfileId = profileId;
        ProfileName = profileName;
        ConfiguredSeedCount = configuredSeedCount;
        ExecutedSeedCount = executedSeedCount;
        IsCancelled = isCancelled;
        Receipts = Array.AsReadOnly(receipts.ToArray());
        TotalExecutedTurns = totalExecutedTurns;
        TotalCorrectAnswers = totalCorrectAnswers;
        TotalIncorrectAnswers = totalIncorrectAnswers;
        ObservedAccuracyPercent = observedAccuracyPercent;
        TotalGameOvers = totalGameOvers;
        TotalVirtualTimeMs = totalVirtualTimeMs;
        TotalAppliedOpponentDamage = totalAppliedOpponentDamage;
        TotalAppliedPlayerDamage = totalAppliedPlayerDamage;
        TotalOpponentDefeats = totalOpponentDefeats;
        TotalSectorCompletions = totalSectorCompletions;
        HighestSectorReached = highestSectorReached;
    }
}
