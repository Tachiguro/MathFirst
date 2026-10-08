namespace MathFirst.Domain.CyberDefense;

public static class CyberDefenseCombatPolicy
{
    public const int PlayerMaxHp = 100;
    public const int NormalEnemyBaseDamage = 4;
    public const int BossEnemyBaseDamage = 6;
    public const int NormalDefeatHealing = 2;
    public const int BossDefeatHealing = 6;

    public static int GetEnemyDamage(int sector, OpponentKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sector, 1);

        int damageBonus = sector / 50;

        return kind switch
        {
            OpponentKind.Normal => checked(NormalEnemyBaseDamage + damageBonus),
            OpponentKind.Boss => checked(BossEnemyBaseDamage + damageBonus),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                $"Undefined opponent kind: {kind}.")
        };
    }

    public static int GetDefeatHealing(OpponentKind kind)
    {
        return kind switch
        {
            OpponentKind.Normal => NormalDefeatHealing,
            OpponentKind.Boss => BossDefeatHealing,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                $"Undefined opponent kind: {kind}.")
        };
    }

    public static void ValidateAttackDamage(int attackDamage)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attackDamage, 1);
    }
}
