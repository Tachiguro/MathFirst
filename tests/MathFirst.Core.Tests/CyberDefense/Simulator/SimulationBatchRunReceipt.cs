namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Minimal immutable receipt for an individual seed execution within a simulation batch.
/// </summary>
public sealed record class SimulationBatchRunReceipt
{
    public ulong MasterSeed { get; }
    public string ProfileId { get; }
    public SimulationExecutionResult Result { get; }

    public SimulationTerminationReason TerminationReason => Result.TerminationReason;
    public CyberDefenseRunState FinalState => Result.FinalState;
    public long TotalExecutedTurns => Result.TotalExecutedTurns;
    public int TotalGameOvers => Result.TotalGameOvers;
    public long TotalVirtualTimeMs => Result.TotalVirtualTimeMs;
    public SimulationRunTelemetry Telemetry => Result.Telemetry;
    public CyberDefenseTerminalRunSnapshot? LastTerminalSnapshot => Result.LastTerminalSnapshot;

    public SimulationBatchRunReceipt(
        ulong masterSeed,
        string profileId,
        SimulationExecutionResult result)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile ID cannot be null or whitespace.", nameof(profileId));
        }

        MasterSeed = masterSeed;
        ProfileId = profileId;
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }
}
