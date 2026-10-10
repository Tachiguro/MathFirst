namespace MathFirst.Core.Tests.CyberDefense.Simulator;

/// <summary>
/// Intended future upgrade preference metadata for synthetic player profiles.
/// Note: In Slice 3, upgrade strategies are descriptive metadata only and are not executable.
/// </summary>
public enum SyntheticUpgradePreference
{
    None,
    AttackFocused,
    Balanced,
    Defensive,
    UnupgradedBaseline
}
