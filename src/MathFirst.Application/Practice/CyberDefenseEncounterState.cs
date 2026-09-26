namespace MathFirst.Application.Practice;

public enum CyberDefenseFeedbackKind
{
    None,
    Hit,
    CriticalHit,
    Blocked
}

public sealed record CyberDefenseEnemy(
    string Id,
    string NameKey,
    string DescKey,
    string AssetPath,
    int MaxHitPoints,
    bool IsBoss);

/// <summary>
/// Transient prototype encounter state for the Cyber Defense training shell.
/// This state is presentation-only and has zero authority over curriculum, FSRS,
/// persistence, or mathematical progress.
/// </summary>
public sealed class CyberDefenseEncounterState
{
    public const int PrototypeEnemyHitPoints = 5;
    public const int PrototypeShieldSegments = 3;
    public const int PrototypeEnemyCount = 10;

    public static readonly IReadOnlyList<CyberDefenseEnemy> DefaultRoster =
    [
        new("glitch-drone", "CyberDefense_Opponent_GlitchDrone", "CyberDefense_Opponent_GlitchDrone_Desc", "images/cyber-defense/glitch-drone.svg", 5, false),
        new("data-leech", "CyberDefense_Opponent_DataLeech", "CyberDefense_Opponent_DataLeech_Desc", "images/cyber-defense/data-leech.svg", 5, false),
        new("firewall-breaker", "CyberDefense_Opponent_FirewallBreaker", "CyberDefense_Opponent_FirewallBreaker_Desc", "images/cyber-defense/firewall-breaker.svg", 5, false),
        new("nexus-overlord", "CyberDefense_Opponent_NexusOverlord", "CyberDefense_Opponent_NexusOverlord_Desc", "images/cyber-defense/nexus-overlord.svg", 8, true),
        new("virus-core", "CyberDefense_Opponent_VirusCore", "CyberDefense_Opponent_VirusCore_Desc", "images/cyber-defense/virus-core.svg", 5, false),
        new("signal-phantom", "CyberDefense_Opponent_SignalPhantom", "CyberDefense_Opponent_SignalPhantom_Desc", "images/cyber-defense/signal-phantom.svg", 5, false),
        new("quantum-bug", "CyberDefense_Opponent_QuantumBug", "CyberDefense_Opponent_QuantumBug_Desc", "images/cyber-defense/quantum-bug.svg", 5, false),
        new("trojan-wasp", "CyberDefense_Opponent_TrojanWasp", "CyberDefense_Opponent_TrojanWasp_Desc", "images/cyber-defense/trojan-wasp.svg", 5, false),
        new("crystal-malware", "CyberDefense_Opponent_CrystalMalware", "CyberDefense_Opponent_CrystalMalware_Desc", "images/cyber-defense/crystal-malware.svg", 5, false),
        new("nexus-overlord-prime", "CyberDefense_Opponent_NexusOverlord", "CyberDefense_Opponent_NexusOverlord_Desc", "images/cyber-defense/nexus-overlord.svg", 10, true)
    ];

    private readonly IReadOnlyList<CyberDefenseEnemy> _roster;

    public CyberDefenseEncounterState(IReadOnlyList<CyberDefenseEnemy>? roster = null)
    {
        _roster = roster ?? DefaultRoster;
        EnemyHitPoints = CurrentEnemy.MaxHitPoints;
    }

    public int EnemyIndex { get; private set; }
    public CyberDefenseEnemy CurrentEnemy => _roster[EnemyIndex];
    public bool IsBoss => CurrentEnemy.IsBoss;
    public int EnemyMaxHitPoints => CurrentEnemy.MaxHitPoints;
    public int EnemyHitPoints { get; private set; }
    public int ShieldSegments { get; private set; } = PrototypeShieldSegments;
    public long Revision { get; private set; }
    public CyberDefenseFeedbackKind LastFeedback { get; private set; } = CyberDefenseFeedbackKind.None;
    public int LastDamageDealt { get; private set; }
    public long FeedbackRevision { get; private set; }

    public void RecordCorrectAnswer() => RecordHit(isCritical: false);

    public void RecordCriticalHit() => RecordHit(isCritical: true);

    public void RecordHit(bool isCritical)
    {
        var damage = isCritical ? 2 : 1;
        LastDamageDealt = damage;
        EnemyHitPoints -= damage;
        if (EnemyHitPoints <= 0)
        {
            EnemyIndex = (EnemyIndex + 1) % _roster.Count;
            EnemyHitPoints = CurrentEnemy.MaxHitPoints;
        }

        LastFeedback = isCritical ? CyberDefenseFeedbackKind.CriticalHit : CyberDefenseFeedbackKind.Hit;
        FeedbackRevision++;
        Revision++;
    }

    public void RecordIncorrectAnswer()
    {
        ShieldSegments--;
        if (ShieldSegments <= 0)
        {
            ShieldSegments = PrototypeShieldSegments;
            EnemyHitPoints = CurrentEnemy.MaxHitPoints;
        }

        LastFeedback = CyberDefenseFeedbackKind.Blocked;
        LastDamageDealt = 0;
        FeedbackRevision++;
        Revision++;
    }

    public void TriggerBossEncounter()
    {
        for (var i = 0; i < _roster.Count; i++)
        {
            var idx = (EnemyIndex + i) % _roster.Count;
            if (_roster[idx].IsBoss)
            {
                EnemyIndex = idx;
                EnemyHitPoints = CurrentEnemy.MaxHitPoints;
                LastFeedback = CyberDefenseFeedbackKind.None;
                FeedbackRevision++;
                Revision++;
                break;
            }
        }
    }

    public void Reset()
    {
        EnemyIndex = 0;
        EnemyHitPoints = CurrentEnemy.MaxHitPoints;
        ShieldSegments = PrototypeShieldSegments;
        LastFeedback = CyberDefenseFeedbackKind.None;
        LastDamageDealt = 0;
        FeedbackRevision++;
        Revision++;
    }
}
