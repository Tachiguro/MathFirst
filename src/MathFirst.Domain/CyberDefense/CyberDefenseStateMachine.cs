namespace MathFirst.Domain.CyberDefense;

public static class CyberDefenseStateMachine
{
    public static CyberDefenseCombatTransitionResult ApplyAttempt(
        CyberDefenseRunState run,
        bool isCorrect,
        int effectiveAttackDamage)
    {
        ArgumentNullException.ThrowIfNull(run);
        CyberDefenseCombatPolicy.ValidateAttackDamage(effectiveAttackDamage);

        if (!isCorrect)
        {
            throw new NotSupportedException("Incorrect answer transitions are not supported in Slice 5.");
        }

        if (run.CurrentOpponent.Kind != OpponentKind.Normal && run.CurrentOpponent.Kind != OpponentKind.Boss)
        {
            throw new NotSupportedException($"Combat against opponent kind '{run.CurrentOpponent.Kind}' is not supported in Slice 5.");
        }

        int requestedAttackDamage = effectiveAttackDamage;
        int currentOpponentHp = run.CurrentOpponent.CurrentHp;

        int appliedOpponentDamage = Math.Min(requestedAttackDamage, currentOpponentHp);
        int excessOpponentDamage = checked(requestedAttackDamage - appliedOpponentDamage);
        int remainingOpponentHp = checked(currentOpponentHp - appliedOpponentDamage);

        const int incomingEnemyDamage = 0;
        const int appliedPlayerDamage = 0;
        const int excessEnemyDamage = 0;
        const bool isGameOver = false;
        const CyberDefenseTerminalRunSnapshot? terminalSnapshot = null;

        if (remainingOpponentHp > 0)
        {
            var nextOpponent = new OpponentState(
                run.CurrentOpponent.Kind,
                run.CurrentOpponent.MaxHp,
                remainingOpponentHp);

            var nextState = CyberDefenseRunState.CreateActive(
                run.Sector,
                run.OpponentIndex,
                run.PlayerCurrentHp,
                nextOpponent);

            return new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: requestedAttackDamage,
                appliedOpponentDamage: appliedOpponentDamage,
                excessOpponentDamage: excessOpponentDamage,
                incomingEnemyDamage: incomingEnemyDamage,
                appliedPlayerDamage: appliedPlayerDamage,
                excessEnemyDamage: excessEnemyDamage,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: isGameOver,
                nextState: nextState,
                terminalSnapshot: terminalSnapshot);
        }
        else
        {
            int potentialHealing = CyberDefenseCombatPolicy.GetDefeatHealing(run.CurrentOpponent.Kind);
            int availableHealingCapacity = checked(CyberDefenseCombatPolicy.PlayerMaxHp - run.PlayerCurrentHp);
            int appliedHealing = Math.Min(potentialHealing, Math.Max(0, availableHealingCapacity));
            int nextPlayerHp = checked(run.PlayerCurrentHp + appliedHealing);

            int nextSector;
            int nextOpponentIndex;
            bool isSectorCompleted;

            if (run.CurrentOpponent.Kind == OpponentKind.Normal)
            {
                isSectorCompleted = false;
                nextSector = run.Sector;
                nextOpponentIndex = checked(run.OpponentIndex + 1);
            }
            else
            {
                isSectorCompleted = true;
                nextSector = checked(run.Sector + 1);
                nextOpponentIndex = 0;
            }

            OpponentKind nextOpponentKind = CyberDefenseScalingPolicy.GetOpponentKind(nextSector, nextOpponentIndex);
            int nextOpponentMaxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(nextSector, nextOpponentIndex);
            int nextOpponentCurrentHp = nextOpponentMaxHp;

            var nextOpponent = new OpponentState(
                nextOpponentKind,
                nextOpponentMaxHp,
                nextOpponentCurrentHp);

            var nextState = CyberDefenseRunState.CreateActive(
                nextSector,
                nextOpponentIndex,
                nextPlayerHp,
                nextOpponent);

            return new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: requestedAttackDamage,
                appliedOpponentDamage: appliedOpponentDamage,
                excessOpponentDamage: excessOpponentDamage,
                incomingEnemyDamage: incomingEnemyDamage,
                appliedPlayerDamage: appliedPlayerDamage,
                excessEnemyDamage: excessEnemyDamage,
                potentialHealing: potentialHealing,
                appliedHealing: appliedHealing,
                isOpponentDefeated: true,
                isSectorCompleted: isSectorCompleted,
                isGameOver: isGameOver,
                nextState: nextState,
                terminalSnapshot: terminalSnapshot);
        }
    }
}
