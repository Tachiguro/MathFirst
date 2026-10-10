namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Minimal deterministic execution result for a bounded multi-turn simulation run.
/// </summary>
public sealed record class SimulationExecutionResult
{
    public CyberDefenseRunState FinalState { get; }
    public SimulationTerminationReason TerminationReason { get; }
    public long TotalExecutedTurns { get; }
    public int TotalGameOvers { get; }
    public long TotalVirtualTimeMs { get; }
    public CyberDefenseTerminalRunSnapshot? LastTerminalSnapshot { get; }

    /// <summary>
    /// Convenience alias for <see cref="TotalExecutedTurns"/>.
    /// </summary>
    public long ExecutedTurns => TotalExecutedTurns;

    /// <summary>
    /// Convenience alias for <see cref="TotalGameOvers"/>.
    /// </summary>
    public int GameOvers => TotalGameOvers;

    /// <summary>
    /// Convenience alias for <see cref="TotalVirtualTimeMs"/>.
    /// </summary>
    public long VirtualElapsedMilliseconds => TotalVirtualTimeMs;

    /// <summary>
    /// Convenience alias for <see cref="LastTerminalSnapshot"/>.
    /// </summary>
    public CyberDefenseTerminalRunSnapshot? TerminalSnapshot => LastTerminalSnapshot;

    /// <summary>
    /// Structured simulation telemetry for this execution run.
    /// </summary>
    public SimulationRunTelemetry Telemetry { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="SimulationExecutionResult"/>.
    /// </summary>
    public SimulationExecutionResult(
        CyberDefenseRunState finalState,
        SimulationTerminationReason terminationReason,
        long totalExecutedTurns,
        int totalGameOvers,
        long totalVirtualTimeMs,
        CyberDefenseTerminalRunSnapshot? lastTerminalSnapshot,
        SimulationRunTelemetry telemetry)
    {
        FinalState = finalState ?? throw new ArgumentNullException(nameof(finalState));
        TerminationReason = terminationReason;
        TotalExecutedTurns = totalExecutedTurns >= 0
            ? totalExecutedTurns
            : throw new ArgumentOutOfRangeException(nameof(totalExecutedTurns), totalExecutedTurns, "Total executed turns cannot be negative.");
        TotalGameOvers = totalGameOvers >= 0
            ? totalGameOvers
            : throw new ArgumentOutOfRangeException(nameof(totalGameOvers), totalGameOvers, "Total game-overs cannot be negative.");
        TotalVirtualTimeMs = totalVirtualTimeMs >= 0
            ? totalVirtualTimeMs
            : throw new ArgumentOutOfRangeException(nameof(totalVirtualTimeMs), totalVirtualTimeMs, "Total virtual time cannot be negative.");
        LastTerminalSnapshot = lastTerminalSnapshot;
        Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SimulationExecutionResult"/> with backward compatibility.
    /// </summary>
    public SimulationExecutionResult(
        CyberDefenseRunState finalState,
        SimulationTerminationReason terminationReason,
        long totalExecutedTurns,
        int totalGameOvers,
        long totalVirtualTimeMs,
        CyberDefenseTerminalRunSnapshot? lastTerminalSnapshot = null)
        : this(
            finalState,
            terminationReason,
            totalExecutedTurns,
            totalGameOvers,
            totalVirtualTimeMs,
            lastTerminalSnapshot,
            SimulationRunTelemetry.CreateFallback(
                masterSeed: 0UL,
                profileId: "unspecified",
                profileName: "Unspecified",
                highestSectorReached: finalState?.Sector ?? 1,
                totalExecutedTurns: totalExecutedTurns,
                totalGameOvers: totalGameOvers,
                totalVirtualTimeMs: totalVirtualTimeMs))
    {
    }
}
