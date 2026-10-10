namespace MathFirst.Core.Tests.CyberDefense.Simulator;

/// <summary>
/// Specifies the termination reason for a bounded multi-turn simulation run.
/// </summary>
public enum SimulationTerminationReason
{
    /// <summary>
    /// The simulation terminated because the configured target sector was reached or exceeded.
    /// </summary>
    TargetSectorReached = 1,

    /// <summary>
    /// The simulation terminated because the maximum turn budget was reached.
    /// </summary>
    MaxTurnsReached = 2,

    /// <summary>
    /// The simulation terminated because the maximum allowed game-over count was reached.
    /// </summary>
    MaxGameOversReached = 3,

    /// <summary>
    /// The simulation terminated because cooperative cancellation was requested.
    /// </summary>
    Cancelled = 4,
}
