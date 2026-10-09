namespace MathFirst.Application.Persistence;

/// <summary>
/// Specifies the structural outcome classification of a processed combat attempt receipt.
/// </summary>
public enum CyberDefenseReceiptKind
{
    /// <summary>
    /// The attempt was processed against active Cyber Defense combat state machine,
    /// producing a valid combat transition result.
    /// </summary>
    Applied,

    /// <summary>
    /// The attempt was processed while Calm Mode was active; combat transition was
    /// suppressed with zero player/enemy damage, zero advancement, and zero healing.
    /// </summary>
    CalmModeSuppressed
}
