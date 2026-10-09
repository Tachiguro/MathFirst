namespace MathFirst.Application.Practice;

public enum CyberDefenseOpponentScaleTier
{
    Small,
    MediumSmall,
    Medium,
    Large,
    Boss,
    SectorBoss
}

/// <summary>
/// Authoritative presentation-only opponent sizing policy for Cyber Defense encounters.
/// Computes visual scale tiers and classes ensuring early regular enemies begin small
/// and scale progressively up to imposing bosses without causing any layout shift or keypad displacement.
/// </summary>
public static class CyberDefenseOpponentScalePolicy
{
    public const string SmallClass = "opponent-scale-small";
    public const string MediumSmallClass = "opponent-scale-medium-small";
    public const string MediumClass = "opponent-scale-medium";
    public const string LargeClass = "opponent-scale-large";
    public const string BossClass = "opponent-scale-boss";
    public const string SectorBossClass = "opponent-scale-sector-boss";

    public static CyberDefenseOpponentScaleTier GetScaleTier(CyberDefenseEnemy enemy, int enemyIndex)
    {
        ArgumentNullException.ThrowIfNull(enemy);

        if (enemy.IsSectorBoss)
        {
            return CyberDefenseOpponentScaleTier.SectorBoss;
        }

        if (enemy.IsBoss)
        {
            return CyberDefenseOpponentScaleTier.Boss;
        }

        // Regular enemies scale progressively across wave indices
        return enemyIndex switch
        {
            <= 1 => CyberDefenseOpponentScaleTier.Small,
            <= 3 => CyberDefenseOpponentScaleTier.MediumSmall,
            <= 5 => CyberDefenseOpponentScaleTier.Medium,
            _ => CyberDefenseOpponentScaleTier.Large
        };
    }

    public static string GetScaleClass(CyberDefenseEnemy enemy, int enemyIndex) =>
        GetScaleTier(enemy, enemyIndex) switch
        {
            CyberDefenseOpponentScaleTier.Small => SmallClass,
            CyberDefenseOpponentScaleTier.MediumSmall => MediumSmallClass,
            CyberDefenseOpponentScaleTier.Medium => MediumClass,
            CyberDefenseOpponentScaleTier.Large => LargeClass,
            CyberDefenseOpponentScaleTier.Boss => BossClass,
            CyberDefenseOpponentScaleTier.SectorBoss => SectorBossClass,
            _ => MediumClass
        };

    public static CyberDefenseOpponentScaleTier GetScaleTier(int opponentIndex, int normalOpponentCount, bool isSectorBoss, bool isBoss)
    {
        if (isSectorBoss)
        {
            return CyberDefenseOpponentScaleTier.SectorBoss;
        }

        if (isBoss)
        {
            return CyberDefenseOpponentScaleTier.Boss;
        }

        return opponentIndex switch
        {
            <= 1 => CyberDefenseOpponentScaleTier.Small,
            <= 3 => CyberDefenseOpponentScaleTier.MediumSmall,
            <= 5 => CyberDefenseOpponentScaleTier.Medium,
            _ => CyberDefenseOpponentScaleTier.Large
        };
    }

    public static string GetScaleClass(int opponentIndex, int normalOpponentCount, bool isSectorBoss, bool isBoss) =>
        GetScaleTier(opponentIndex, normalOpponentCount, isSectorBoss, isBoss) switch
        {
            CyberDefenseOpponentScaleTier.Small => SmallClass,
            CyberDefenseOpponentScaleTier.MediumSmall => MediumSmallClass,
            CyberDefenseOpponentScaleTier.Medium => MediumClass,
            CyberDefenseOpponentScaleTier.Large => LargeClass,
            CyberDefenseOpponentScaleTier.Boss => BossClass,
            CyberDefenseOpponentScaleTier.SectorBoss => SectorBossClass,
            _ => MediumClass
        };

    public static double GetBaseScaleFactor(CyberDefenseOpponentScaleTier tier) => tier switch
    {
        CyberDefenseOpponentScaleTier.Small => 0.65,
        CyberDefenseOpponentScaleTier.MediumSmall => 0.78,
        CyberDefenseOpponentScaleTier.Medium => 0.90,
        CyberDefenseOpponentScaleTier.Large => 1.05,
        CyberDefenseOpponentScaleTier.Boss => 1.42,
        CyberDefenseOpponentScaleTier.SectorBoss => 1.50,
        _ => 1.0
    };
}
