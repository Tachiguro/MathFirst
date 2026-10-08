namespace MathFirst.Application.Practice;

using System;

/// <summary>
/// Application-scoped state holder managing the lifetime of the transient Cyber Defense encounter.
/// Respects Calm Mode preferences: when disabled, no combat state is allocated and queries return null.
/// Preserves combat state when toggling between enabled and disabled, and clears state upon full local reset.
/// </summary>
public sealed class CyberDefenseSessionState
{
    private readonly ICyberDefenseModePreferences _preferences;
    private readonly Func<CyberDefenseEncounterState> _encounterFactory;
    private CyberDefenseEncounterState? _encounter;

    public CyberDefenseSessionState(
        ICyberDefenseModePreferences preferences,
        Func<CyberDefenseEncounterState>? encounterFactory = null)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _encounterFactory = encounterFactory ?? (() => new CyberDefenseEncounterState());
    }

    public bool IsCyberDefenseEnabled => _preferences.GetCyberDefenseEnabled();

    public bool HasActiveEncounter => _encounter is not null;

    public CyberDefenseEncounterState? ActiveEncounter
    {
        get
        {
            if (!IsCyberDefenseEnabled)
            {
                return null;
            }

            _encounter ??= _encounterFactory();
            return _encounter;
        }
    }

    public void ClearEncounter()
    {
        _encounter = null;
    }
}
