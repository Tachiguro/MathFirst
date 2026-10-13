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

        if (run.CurrentOpponent.Kind != OpponentKind.Normal && run.CurrentOpponent.Kind != OpponentKind.Boss)
        {
            throw new NotSupportedException($"Combat against opponent kind '{run.CurrentOpponent.Kind}' is not supported.");
        }

        if (!isCorrect)
        {
            int incomingEnemyDamage = CyberDefenseCombatPolicy.GetEnemyDamage(run.Sector, run.CurrentOpponent.Kind);

            if (run.ActiveShields > 0)
            {
                int postAbsorptionActiveShields = checked(run.ActiveShields - 1);
                var (nextActiveShields, nextRechargeUnits) = ApplyRecharge(
                    run.ShieldCapacity,
                    postAbsorptionActiveShields,
                    run.ShieldRechargeUnits,
                    CyberDefenseFirewallPolicy.IncorrectAttemptRechargeUnits);

                var nextState = CyberDefenseRunState.CreateActive(
                    run.Sector,
                    run.OpponentIndex,
                    run.PlayerCurrentHp,
                    run.CurrentOpponent,
                    run.ShieldCapacity,
                    nextActiveShields,
                    nextRechargeUnits);

                return new CyberDefenseCombatTransitionResult(
                    isCorrect: false,
                    requestedAttackDamage: 0,
                    appliedOpponentDamage: 0,
                    excessOpponentDamage: 0,
                    incomingEnemyDamage: incomingEnemyDamage,
                    appliedPlayerDamage: 0,
                    excessEnemyDamage: 0,
                    potentialHealing: 0,
                    appliedHealing: 0,
                    isOpponentDefeated: false,
                    isSectorCompleted: false,
                    isGameOver: false,
                    nextState: nextState,
                    terminalSnapshot: null,
                    executedAttacksOnDefeatedOpponent: null,
                    defeatedOpponentProvenance: null,
                    isShieldAbsorbed: true,
                    absorbedEnemyDamage: incomingEnemyDamage);
            }

            if (run.PlayerCurrentHp <= incomingEnemyDamage)
            {
                int appliedPlayerDamage = run.PlayerCurrentHp;
                int excessEnemyDamage = checked(incomingEnemyDamage - appliedPlayerDamage);

                var terminalSnapshot = new CyberDefenseTerminalRunSnapshot(
                    run.Sector,
                    run.OpponentIndex,
                    run.CurrentOpponent.Kind,
                    run.CurrentOpponent.CurrentHp,
                    run.CurrentOpponent.MaxHp,
                    playerCurrentHp: 0);

                var rebootedNextState = CyberDefenseRunState.InitialRun(run.ShieldCapacity);

                return new CyberDefenseCombatTransitionResult(
                    isCorrect: false,
                    requestedAttackDamage: 0,
                    appliedOpponentDamage: 0,
                    excessOpponentDamage: 0,
                    incomingEnemyDamage: incomingEnemyDamage,
                    appliedPlayerDamage: appliedPlayerDamage,
                    excessEnemyDamage: excessEnemyDamage,
                    potentialHealing: 0,
                    appliedHealing: 0,
                    isOpponentDefeated: false,
                    isSectorCompleted: false,
                    isGameOver: true,
                    nextState: rebootedNextState,
                    terminalSnapshot: terminalSnapshot,
                    executedAttacksOnDefeatedOpponent: null,
                    defeatedOpponentProvenance: null,
                    isShieldAbsorbed: false,
                    absorbedEnemyDamage: 0);
            }

            int nextPlayerHp = checked(run.PlayerCurrentHp - incomingEnemyDamage);

            var (unabsorbedNextActiveShields, unabsorbedNextRechargeUnits) = ApplyRecharge(
                run.ShieldCapacity,
                run.ActiveShields,
                run.ShieldRechargeUnits,
                CyberDefenseFirewallPolicy.IncorrectAttemptRechargeUnits);

            var survivedNextState = CyberDefenseRunState.CreateActive(
                run.Sector,
                run.OpponentIndex,
                nextPlayerHp,
                run.CurrentOpponent,
                run.ShieldCapacity,
                unabsorbedNextActiveShields,
                unabsorbedNextRechargeUnits);

            return new CyberDefenseCombatTransitionResult(
                isCorrect: false,
                requestedAttackDamage: 0,
                appliedOpponentDamage: 0,
                excessOpponentDamage: 0,
                incomingEnemyDamage: incomingEnemyDamage,
                appliedPlayerDamage: incomingEnemyDamage,
                excessEnemyDamage: 0,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: survivedNextState,
                terminalSnapshot: null,
                executedAttacksOnDefeatedOpponent: null,
                defeatedOpponentProvenance: null,
                isShieldAbsorbed: false,
                absorbedEnemyDamage: 0);
        }

        int requestedAttackDamage = effectiveAttackDamage;
        int currentOpponentHp = run.CurrentOpponent.CurrentHp;
        int nextExecutedAttacks = checked(run.CurrentOpponent.ExecutedAttacks + 1);

        int appliedOpponentDamage = Math.Min(requestedAttackDamage, currentOpponentHp);
        int excessOpponentDamage = checked(requestedAttackDamage - appliedOpponentDamage);
        int remainingOpponentHp = checked(currentOpponentHp - appliedOpponentDamage);

        var (correctNextActiveShields, correctNextRechargeUnits) = ApplyRecharge(
            run.ShieldCapacity,
            run.ActiveShields,
            run.ShieldRechargeUnits,
            CyberDefenseFirewallPolicy.CorrectAttemptRechargeUnits);

        if (remainingOpponentHp > 0)
        {
            var nextOpponent = new OpponentState(
                run.CurrentOpponent.Kind,
                run.CurrentOpponent.MaxHp,
                remainingOpponentHp,
                nextExecutedAttacks,
                run.CurrentOpponent.Provenance);

            var nextState = CyberDefenseRunState.CreateActive(
                run.Sector,
                run.OpponentIndex,
                run.PlayerCurrentHp,
                nextOpponent,
                run.ShieldCapacity,
                correctNextActiveShields,
                correctNextRechargeUnits);

            return new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: requestedAttackDamage,
                appliedOpponentDamage: appliedOpponentDamage,
                excessOpponentDamage: excessOpponentDamage,
                incomingEnemyDamage: 0,
                appliedPlayerDamage: 0,
                excessEnemyDamage: 0,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: nextState,
                terminalSnapshot: null,
                executedAttacksOnDefeatedOpponent: null,
                defeatedOpponentProvenance: null,
                isShieldAbsorbed: false,
                absorbedEnemyDamage: 0);
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
                nextOpponentCurrentHp,
                executedAttacks: 0,
                provenance: OpponentEncounterProvenance.Authoritative);

            var nextState = CyberDefenseRunState.CreateActive(
                nextSector,
                nextOpponentIndex,
                nextPlayerHp,
                nextOpponent,
                run.ShieldCapacity,
                correctNextActiveShields,
                correctNextRechargeUnits);

            return new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: requestedAttackDamage,
                appliedOpponentDamage: appliedOpponentDamage,
                excessOpponentDamage: excessOpponentDamage,
                incomingEnemyDamage: 0,
                appliedPlayerDamage: 0,
                excessEnemyDamage: 0,
                potentialHealing: potentialHealing,
                appliedHealing: appliedHealing,
                isOpponentDefeated: true,
                isSectorCompleted: isSectorCompleted,
                isGameOver: false,
                nextState: nextState,
                terminalSnapshot: null,
                executedAttacksOnDefeatedOpponent: nextExecutedAttacks,
                defeatedOpponentProvenance: run.CurrentOpponent.Provenance,
                isShieldAbsorbed: false,
                absorbedEnemyDamage: 0);
        }
    }

    private static (int nextActiveShields, int nextRechargeUnits) ApplyRecharge(
        int shieldCapacity,
        int activeShields,
        int shieldRechargeUnits,
        int rechargeDelta)
    {
        if (shieldCapacity <= 0 || activeShields >= shieldCapacity || rechargeDelta <= 0)
        {
            return (activeShields, 0);
        }

        int totalUnits = checked(shieldRechargeUnits + rechargeDelta);
        int threshold = CyberDefenseFirewallPolicy.RechargeThresholdUnits;

        if (totalUnits >= threshold)
        {
            int restoredShields = checked(activeShields + 1);
            if (restoredShields >= shieldCapacity)
            {
                return (shieldCapacity, 0);
            }

            int surplus = checked(totalUnits - threshold);
            return (restoredShields, surplus);
        }

        return (activeShields, totalUnits);
    }
}
