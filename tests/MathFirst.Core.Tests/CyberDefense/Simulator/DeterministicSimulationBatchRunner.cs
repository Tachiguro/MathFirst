namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;
using System.Collections.Generic;
using System.Threading;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Deterministic batch simulation runner.
/// Orchestrates independent executions of <see cref="HeadlessCombatSimulator.Run"/>.
/// </summary>
public static class DeterministicSimulationBatchRunner
{
    /// <summary>
    /// Executes a deterministic batch simulation for a specified configuration.
    /// </summary>
    /// <param name="config">The validated batch configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The deterministic aggregated batch result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
    public static SimulationBatchResult RunBatch(
        SimulationBatchConfig config,
        CancellationToken cancellationToken = default)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        var receipts = new List<SimulationBatchRunReceipt>();
        bool isCancelled = false;

        long totalExecutedTurns = 0;
        long totalCorrectAnswers = 0;
        long totalIncorrectAnswers = 0;
        int totalGameOvers = 0;
        long totalVirtualTimeMs = 0;
        long totalAppliedOpponentDamage = 0;
        long totalAppliedPlayerDamage = 0;
        int totalOpponentDefeats = 0;
        int totalSectorCompletions = 0;
        int highestSectorReached = config.StartingState.Sector;

        // Precedence: Check pre-cancellation before any seed starts
        if (cancellationToken.IsCancellationRequested)
        {
            return new SimulationBatchResult(
                profileId: config.Profile.Id,
                profileName: config.Profile.Name,
                configuredSeedCount: config.Seeds.Count,
                executedSeedCount: 0,
                isCancelled: true,
                receipts: Array.Empty<SimulationBatchRunReceipt>(),
                totalExecutedTurns: 0,
                totalCorrectAnswers: 0,
                totalIncorrectAnswers: 0,
                observedAccuracyPercent: null,
                totalGameOvers: 0,
                totalVirtualTimeMs: 0,
                totalAppliedOpponentDamage: 0,
                totalAppliedPlayerDamage: 0,
                totalOpponentDefeats: 0,
                totalSectorCompletions: 0,
                highestSectorReached: highestSectorReached);
        }

        foreach (ulong seed in config.Seeds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                isCancelled = true;
                break;
            }

            var runConfig = new SimulationRunConfig(
                profile: config.Profile,
                masterSeed: seed,
                maxTurns: config.MaxTurnsPerRun,
                targetSector: config.TargetSector,
                maxGameOvers: config.MaxGameOversPerRun,
                startingState: config.StartingState,
                onTurnCompleted: config.OnTurnCompleted);

            SimulationExecutionResult result = HeadlessCombatSimulator.Run(runConfig, cancellationToken);
            var receipt = new SimulationBatchRunReceipt(seed, config.Profile.Id, result);
            receipts.Add(receipt);

            checked
            {
                totalExecutedTurns += result.TotalExecutedTurns;
                totalCorrectAnswers += result.Telemetry.CorrectAnswers;
                totalIncorrectAnswers += result.Telemetry.IncorrectAnswers;
                totalGameOvers += result.TotalGameOvers;
                totalVirtualTimeMs += result.TotalVirtualTimeMs;
                totalAppliedOpponentDamage += result.Telemetry.TotalAppliedOpponentDamage;
                totalAppliedPlayerDamage += result.Telemetry.TotalAppliedPlayerDamage;
                totalOpponentDefeats += result.Telemetry.TotalOpponentDefeats;
                totalSectorCompletions += result.Telemetry.TotalSectorCompletions;

                if (result.Telemetry.HighestSectorReached > highestSectorReached)
                {
                    highestSectorReached = result.Telemetry.HighestSectorReached;
                }
            }

            if (result.TerminationReason == SimulationTerminationReason.Cancelled || cancellationToken.IsCancellationRequested)
            {
                isCancelled = true;
                break;
            }
        }

        double? observedAccuracyPercent = totalExecutedTurns > 0
            ? (100.0 * (double)totalCorrectAnswers) / (double)totalExecutedTurns
            : null;

        return new SimulationBatchResult(
            profileId: config.Profile.Id,
            profileName: config.Profile.Name,
            configuredSeedCount: config.Seeds.Count,
            executedSeedCount: receipts.Count,
            isCancelled: isCancelled,
            receipts: receipts,
            totalExecutedTurns: totalExecutedTurns,
            totalCorrectAnswers: totalCorrectAnswers,
            totalIncorrectAnswers: totalIncorrectAnswers,
            observedAccuracyPercent: observedAccuracyPercent,
            totalGameOvers: totalGameOvers,
            totalVirtualTimeMs: totalVirtualTimeMs,
            totalAppliedOpponentDamage: totalAppliedOpponentDamage,
            totalAppliedPlayerDamage: totalAppliedPlayerDamage,
            totalOpponentDefeats: totalOpponentDefeats,
            totalSectorCompletions: totalSectorCompletions,
            highestSectorReached: highestSectorReached);
    }
}
