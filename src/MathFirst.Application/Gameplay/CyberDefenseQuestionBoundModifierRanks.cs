namespace MathFirst.Application.Gameplay;

using System;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Immutable Critical Strike and Overdrive rank entitlement captured when a math question is
/// presented. A later purchase never changes an already captured value.
/// </summary>
public sealed record CyberDefenseQuestionBoundModifierRanks
{
    /// <summary>Conservative fail-closed entitlement: no Critical Strike and no Overdrive.</summary>
    public static CyberDefenseQuestionBoundModifierRanks Zero { get; } = new(0, 0);

    public int BoundCriticalStrikeRank { get; }
    public int BoundOverdriveRank { get; }

    public CyberDefenseQuestionBoundModifierRanks(int boundCriticalStrikeRank, int boundOverdriveRank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(boundCriticalStrikeRank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(boundCriticalStrikeRank, CyberDefenseCriticalStrikePolicy.MaxRank);
        ArgumentOutOfRangeException.ThrowIfNegative(boundOverdriveRank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(boundOverdriveRank, CyberDefenseOverdrivePolicy.MaxRank);

        BoundCriticalStrikeRank = boundCriticalStrikeRank;
        BoundOverdriveRank = boundOverdriveRank;
    }
}
