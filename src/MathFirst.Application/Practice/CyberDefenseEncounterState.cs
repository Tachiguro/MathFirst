namespace MathFirst.Application.Practice;

/// <summary>
/// Transient prototype encounter state for the Cyber Defense training shell.
/// This state is presentation-only and has zero authority over curriculum, FSRS,
/// persistence, or mathematical progress.
/// </summary>
public sealed class CyberDefenseEncounterState
{
    public const int PrototypeEnemyHitPoints = 5;
    public const int PrototypeShieldSegments = 3;
    public const int PrototypeEnemyCount = 3;

    public int EnemyIndex { get; private set; }
    public int EnemyHitPoints { get; private set; } = PrototypeEnemyHitPoints;
    public int ShieldSegments { get; private set; } = PrototypeShieldSegments;
    public long Revision { get; private set; }

    public void RecordCorrectAnswer()
    {
        EnemyHitPoints--;
        if (EnemyHitPoints <= 0)
        {
            EnemyHitPoints = PrototypeEnemyHitPoints;
            EnemyIndex = (EnemyIndex + 1) % PrototypeEnemyCount;
        }

        Revision++;
    }

    public void RecordIncorrectAnswer()
    {
        ShieldSegments--;
        if (ShieldSegments <= 0)
        {
            ShieldSegments = PrototypeShieldSegments;
            EnemyHitPoints = PrototypeEnemyHitPoints;
        }

        Revision++;
    }

    public void Reset()
    {
        EnemyHitPoints = PrototypeEnemyHitPoints;
        ShieldSegments = PrototypeShieldSegments;
        Revision++;
    }
}
