namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// O(1) bounded-memory accumulator for deterministic simulation telemetry.
/// Gathers metrics strictly from authoritative combat transitions and virtual time.
/// </summary>
public sealed class SimulationTelemetryAccumulator
{
    private readonly ulong _masterSeed;
    private readonly string _profileId;
    private readonly string _profileName;

    private long _totalQuestions;
    private long _correctAnswers;
    private long _incorrectAnswers;
    private int _highestSectorReached;
    private int _totalGameOvers;
    private long _totalAppliedOpponentDamage;
    private long _totalAppliedPlayerDamage;
    private int _totalOpponentDefeats;
    private int _totalSectorCompletions;

    /// <summary>
    /// Initializes a new instance of <see cref="SimulationTelemetryAccumulator"/>.
    /// </summary>
    public SimulationTelemetryAccumulator(
        ulong masterSeed,
        string profileId,
        string profileName,
        int startingSector)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile ID cannot be null or whitespace.", nameof(profileId));
        }

        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new ArgumentException("Profile name cannot be null or whitespace.", nameof(profileName));
        }

        if (startingSector < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startingSector), startingSector, "Starting sector must be at least 1.");
        }

        _masterSeed = masterSeed;
        _profileId = profileId;
        _profileName = profileName;
        _highestSectorReached = startingSector;
    }

    /// <summary>
    /// Records an authoritative combat turn transition.
    /// Uses checked arithmetic to prevent silent integer wrapping.
    /// </summary>
    public void RecordTurn(CyberDefenseCombatTransitionResult transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        checked
        {
            _totalQuestions++;

            if (transition.IsCorrect)
            {
                _correctAnswers++;
            }
            else
            {
                _incorrectAnswers++;
            }

            _totalAppliedOpponentDamage += transition.AppliedOpponentDamage;
            _totalAppliedPlayerDamage += transition.AppliedPlayerDamage;

            if (transition.IsOpponentDefeated)
            {
                _totalOpponentDefeats++;
            }

            if (transition.IsSectorCompleted)
            {
                _totalSectorCompletions++;
            }

            if (transition.IsGameOver)
            {
                _totalGameOvers++;
            }

            if (transition.TerminalSnapshot is not null && transition.TerminalSnapshot.Sector > _highestSectorReached)
            {
                _highestSectorReached = transition.TerminalSnapshot.Sector;
            }

            if (transition.NextState.Sector > _highestSectorReached)
            {
                _highestSectorReached = transition.NextState.Sector;
            }
        }
    }

    /// <summary>
    /// Builds an immutable <see cref="SimulationRunTelemetry"/> instance.
    /// </summary>
    /// <param name="totalVirtualTimeMs">Accumulated virtual simulation time in milliseconds.</param>
    /// <returns>The immutable structured simulation telemetry.</returns>
    public SimulationRunTelemetry ToTelemetry(long totalVirtualTimeMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalVirtualTimeMs);

        double? accuracyPercent = _totalQuestions > 0
            ? (100.0 * (double)_correctAnswers) / (double)_totalQuestions
            : null;

        return new SimulationRunTelemetry(
            masterSeed: _masterSeed,
            profileId: _profileId,
            profileName: _profileName,
            totalQuestions: _totalQuestions,
            correctAnswers: _correctAnswers,
            incorrectAnswers: _incorrectAnswers,
            accuracyPercent: accuracyPercent,
            highestSectorReached: _highestSectorReached,
            totalGameOvers: _totalGameOvers,
            totalVirtualTimeMs: totalVirtualTimeMs,
            totalAppliedOpponentDamage: _totalAppliedOpponentDamage,
            totalAppliedPlayerDamage: _totalAppliedPlayerDamage,
            totalOpponentDefeats: _totalOpponentDefeats,
            totalSectorCompletions: _totalSectorCompletions);
    }
}
