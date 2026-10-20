namespace MathFirst.Application.Persistence;

using MathFirst.Domain.CyberDefense;

/// <summary>
/// Validated purchased ranks and immutable run modifiers read at one Gameplay Store revision.
/// The concrete store returns this only when no reset is pending and all reset epochs agree.
/// </summary>
public sealed record CyberDefenseCombatModifierSnapshot(
    int CriticalStrikeRank,
    int OverdriveRank,
    CyberDefenseCombatModifierState ModifierState,
    long StoreRevision,
    long ResetEpoch,
    GameplayResetIntentRecord ResetIntent);
