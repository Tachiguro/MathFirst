namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;
using System.Collections.Generic;
using System.Linq;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Immutable configuration for a deterministic simulation seed batch.
/// </summary>
public sealed record class SimulationBatchConfig
{
    public SyntheticPlayerProfile Profile { get; }
    public IReadOnlyList<ulong> Seeds { get; }
    public long MaxTurnsPerRun { get; }
    public int? TargetSector { get; }
    public int? MaxGameOversPerRun { get; }
    public CyberDefenseRunState StartingState { get; }
    public Action<CyberDefenseCombatTransitionResult>? OnTurnCompleted { get; }

    public SimulationBatchConfig(
        SyntheticPlayerProfile profile,
        IEnumerable<ulong> seeds,
        long maxTurnsPerRun,
        int? targetSector = null,
        int? maxGameOversPerRun = null,
        CyberDefenseRunState? startingState = null,
        Action<CyberDefenseCombatTransitionResult>? onTurnCompleted = null)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile), "Synthetic player profile cannot be null.");
        }

        if (seeds is null)
        {
            throw new ArgumentNullException(nameof(seeds), "Seed collection cannot be null.");
        }

        ulong[] snapshot = seeds.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("Seed collection cannot be empty.", nameof(seeds));
        }

        var seen = new HashSet<ulong>();
        foreach (ulong seed in snapshot)
        {
            if (!seen.Add(seed))
            {
                throw new ArgumentException($"Duplicate seed detected: {seed}. All seeds in a batch must be unique.", nameof(seeds));
            }
        }

        if (maxTurnsPerRun < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTurnsPerRun),
                maxTurnsPerRun,
                "Maximum turns per run must be at least 1.");
        }

        if (targetSector.HasValue && targetSector.Value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetSector),
                targetSector.Value,
                "Target sector must be at least 1 if specified.");
        }

        if (maxGameOversPerRun.HasValue && maxGameOversPerRun.Value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxGameOversPerRun),
                maxGameOversPerRun.Value,
                "Maximum game-overs must be at least 1 if specified.");
        }

        checked
        {
            _ = (long)snapshot.Length * maxTurnsPerRun;
        }

        Profile = profile;
        Seeds = Array.AsReadOnly(snapshot);
        MaxTurnsPerRun = maxTurnsPerRun;
        TargetSector = targetSector;
        MaxGameOversPerRun = maxGameOversPerRun;
        StartingState = startingState ?? CyberDefenseRunState.InitialRun();
        OnTurnCompleted = onTurnCompleted;
    }
}
