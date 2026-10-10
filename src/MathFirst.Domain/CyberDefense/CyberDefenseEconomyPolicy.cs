namespace MathFirst.Domain.CyberDefense;

public static class CyberDefenseEconomyPolicy
{
    public const int IncorrectAttemptXp = 1;
    public const int CorrectAttemptXp = 6;
    public const int NormalDefeatBonusXpPerAttack = 1;
    public const int BossDefeatBonusXpPerAttack = 2;
    public const int BossMilestoneBonusXp = 10;

    public static int GetAttemptXp(bool isCorrect)
    {
        return isCorrect ? CorrectAttemptXp : IncorrectAttemptXp;
    }

    public static int GetDefeatBonusXp(OpponentKind kind, int actualSuccessfulAttackCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(actualSuccessfulAttackCount, 1);

        return kind switch
        {
            OpponentKind.Normal => checked(NormalDefeatBonusXpPerAttack * actualSuccessfulAttackCount),
            OpponentKind.Boss => checked(BossDefeatBonusXpPerAttack * actualSuccessfulAttackCount),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                $"Undefined opponent kind: {kind}.")
        };
    }

    public static int GetBossMilestoneBonusXp()
    {
        return BossMilestoneBonusXp;
    }

    public static int CalculateTotalDefeatXp(OpponentKind kind, int actualSuccessfulAttackCount)
    {
        int defeatBonus = GetDefeatBonusXp(kind, actualSuccessfulAttackCount);
        int milestoneBonus = kind == OpponentKind.Boss ? BossMilestoneBonusXp : 0;
        return checked(defeatBonus + milestoneBonus);
    }

    public static int CalculateAttemptTotalXp(
        bool isCorrect,
        bool opponentDefeated,
        OpponentKind kind,
        int actualSuccessfulAttackCount)
    {
        int attemptXp = GetAttemptXp(isCorrect);

        if (!opponentDefeated)
        {
            return attemptXp;
        }

        return checked(attemptXp + CalculateTotalDefeatXp(kind, actualSuccessfulAttackCount));
    }
}
