namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Validated immutable configuration for a bounded multi-turn simulation run.
/// </summary>
public sealed record class SimulationRunConfig
{
    public SyntheticPlayerProfile Profile { get; }
    public ulong MasterSeed { get; }
    public long MaxTurns { get; }
    public int? TargetSector { get; }
    public int? MaxGameOvers { get; }
    public CyberDefenseRunState StartingState { get; }
    public Action<CyberDefenseCombatTransitionResult>? OnTurnCompleted { get; }
    public bool EnableBeginnerAssistance { get; }
    public int StartingConsecutiveEarlyDefeats { get; }
    public bool StartingAssistanceActive { get; }
    public int PermanentFirewallRank { get; }
    public bool? TrackShieldTelemetry { get; }

    /// <summary>
    /// Alias for <see cref="EnableBeginnerAssistance"/>.
    /// </summary>
    public bool IsBeginnerAssistanceEnabled => EnableBeginnerAssistance;

    /// <summary>
    /// Initializes a new instance of <see cref="SimulationRunConfig"/> with validated limits and inputs.
    /// </summary>
    /// <param name="profile">The synthetic player profile.</param>
    /// <param name="masterSeed">The 64-bit master seed.</param>
    /// <param name="maxTurns">The maximum number of turns to execute (at least 1).</param>
    /// <param name="targetSector">Optional target sector to reach (at least 1 if specified).</param>
    /// <param name="maxGameOvers">Optional maximum allowed game-overs before stopping (at least 1 if specified).</param>
    /// <param name="startingState">Optional starting combat state (defaults to <see cref="CyberDefenseRunState.InitialRun"/>).</param>
    /// <param name="onTurnCompleted">Optional callback invoked after each completed authoritative turn transition.</param>
    /// <param name="enableBeginnerAssistance">Optional flag to enable beginner assistance streak tracking and emergency shield.</param>
    /// <param name="startingConsecutiveEarlyDefeats">Optional pre-existing consecutive early defeat count (defaults to 0).</param>
    /// <param name="startingAssistanceActive">Optional pre-existing assistance active flag (defaults to false).</param>
    /// <param name="permanentFirewallRank">Optional explicit permanent firewall rank.</param>
    /// <param name="trackShieldTelemetry">Optional explicit override for shield telemetry collection.</param>
    public SimulationRunConfig(
        SyntheticPlayerProfile profile,
        ulong masterSeed,
        long maxTurns,
        int? targetSector = null,
        int? maxGameOvers = null,
        CyberDefenseRunState? startingState = null,
        Action<CyberDefenseCombatTransitionResult>? onTurnCompleted = null,
        bool enableBeginnerAssistance = false,
        int startingConsecutiveEarlyDefeats = 0,
        bool startingAssistanceActive = false,
        int? permanentFirewallRank = null,
        bool? trackShieldTelemetry = null)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile), "Synthetic player profile cannot be null.");
        }

        if (maxTurns < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTurns),
                maxTurns,
                "Maximum turns must be at least 1.");
        }

        if (targetSector.HasValue && targetSector.Value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetSector),
                targetSector.Value,
                "Target sector must be at least 1 if specified.");
        }

        if (maxGameOvers.HasValue && maxGameOvers.Value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxGameOvers),
                maxGameOvers.Value,
                "Maximum game-overs must be at least 1 if specified.");
        }

        if (startingConsecutiveEarlyDefeats < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startingConsecutiveEarlyDefeats),
                startingConsecutiveEarlyDefeats,
                "Starting consecutive early defeats cannot be negative.");
        }

        if (permanentFirewallRank.HasValue)
        {
            if (permanentFirewallRank.Value < 0 || permanentFirewallRank.Value > CyberDefenseFirewallPolicy.MaxRank)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(permanentFirewallRank),
                    permanentFirewallRank.Value,
                    $"Permanent firewall rank must be between 0 and {CyberDefenseFirewallPolicy.MaxRank}.");
            }
        }

        var resolvedStartingState = startingState ?? CyberDefenseRunState.InitialRun();
        int resolvedPermanentRank = permanentFirewallRank ??
            (startingAssistanceActive ? 0 : resolvedStartingState.ShieldCapacity);

        if (startingAssistanceActive && resolvedPermanentRank > 0)
        {
            throw new ArgumentException(
                "Permanent firewall rank must be 0 when starting assistance is active.",
                nameof(permanentFirewallRank));
        }

        Profile = profile;
        MasterSeed = masterSeed;
        MaxTurns = maxTurns;
        TargetSector = targetSector;
        MaxGameOvers = maxGameOvers;
        StartingState = resolvedStartingState;
        OnTurnCompleted = onTurnCompleted;
        EnableBeginnerAssistance = enableBeginnerAssistance;
        StartingConsecutiveEarlyDefeats = startingConsecutiveEarlyDefeats;
        StartingAssistanceActive = startingAssistanceActive;
        PermanentFirewallRank = resolvedPermanentRank;
        TrackShieldTelemetry = trackShieldTelemetry;
    }
}
