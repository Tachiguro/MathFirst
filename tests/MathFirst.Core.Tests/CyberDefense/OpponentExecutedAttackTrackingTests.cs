using MathFirst.Core.Tests.CyberDefense.Simulator;
using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class OpponentExecutedAttackTrackingTests
{
    // =========================================================================
    // 1. INITIAL OPPONENT COUNT ZERO
    // =========================================================================

    [Fact]
    public void InitialOpponent_HasExecutedAttacksZero()
    {
        var run = CyberDefenseRunState.InitialRun();

        Assert.Equal(0, run.CurrentOpponent.ExecutedAttacks);
    }

    // =========================================================================
    // 2. ONE CORRECT NONFATAL ATTACK INCREASES COUNT TO ONE
    // =========================================================================

    [Fact]
    public void OneCorrectNonfatalAttack_IncreasesCountToOne()
    {
        // Sector 1 normal opponent has 2 HP; deal 1 damage -> survives with 1 HP
        var run = CyberDefenseRunState.InitialRun();
        Assert.Equal(2, run.CurrentOpponent.CurrentHp);
        Assert.Equal(0, run.CurrentOpponent.ExecutedAttacks);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.False(result.IsOpponentDefeated);
        Assert.Null(result.ExecutedAttacksOnDefeatedOpponent);
        Assert.Equal(1, result.NextState.CurrentOpponent.CurrentHp);
        Assert.Equal(1, result.NextState.CurrentOpponent.ExecutedAttacks);
    }

    // =========================================================================
    // 3. MULTIPLE CORRECT ATTACKS ACCUMULATE EXACT COUNT
    // =========================================================================

    [Fact]
    public void MultipleCorrectAttacks_AccumulateExactCount()
    {
        // Sector 1 boss has 12 HP; hit 4 times for 1 damage each
        int sector = 1;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp, executedAttacks: 0);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 100, boss);

        var r1 = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        Assert.Equal(1, r1.NextState.CurrentOpponent.ExecutedAttacks);

        var r2 = CyberDefenseStateMachine.ApplyAttempt(r1.NextState, isCorrect: true, effectiveAttackDamage: 1);
        Assert.Equal(2, r2.NextState.CurrentOpponent.ExecutedAttacks);

        var r3 = CyberDefenseStateMachine.ApplyAttempt(r2.NextState, isCorrect: true, effectiveAttackDamage: 1);
        Assert.Equal(3, r3.NextState.CurrentOpponent.ExecutedAttacks);

        var r4 = CyberDefenseStateMachine.ApplyAttempt(r3.NextState, isCorrect: true, effectiveAttackDamage: 1);
        Assert.Equal(4, r4.NextState.CurrentOpponent.ExecutedAttacks);
        Assert.False(r4.IsOpponentDefeated);
        Assert.Null(r4.ExecutedAttacksOnDefeatedOpponent);
    }

    // =========================================================================
    // 4. INCORRECT ANSWERS DO NOT INCREMENT COUNT
    // =========================================================================

    [Fact]
    public void IncorrectAnswer_DoesNotIncrementCount()
    {
        var run = CyberDefenseRunState.InitialRun();
        Assert.Equal(0, run.CurrentOpponent.ExecutedAttacks);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1);

        Assert.False(result.IsOpponentDefeated);
        Assert.Null(result.ExecutedAttacksOnDefeatedOpponent);
        Assert.Equal(0, result.NextState.CurrentOpponent.ExecutedAttacks);
    }

    // =========================================================================
    // 5. INCORRECT ANSWERS PRESERVE PREVIOUSLY ACCUMULATED COUNTS
    // =========================================================================

    [Fact]
    public void IncorrectAnswer_PreservesPreviouslyAccumulatedCounts()
    {
        int sector = 1;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp, executedAttacks: 0);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 100, boss);

        // Turn 1: Correct (nonfatal) -> Count becomes 1
        var r1 = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 2);
        Assert.Equal(1, r1.NextState.CurrentOpponent.ExecutedAttacks);

        // Turn 2: Incorrect -> Count preserved at 1
        var r2 = CyberDefenseStateMachine.ApplyAttempt(r1.NextState, isCorrect: false, effectiveAttackDamage: 1);
        Assert.Equal(1, r2.NextState.CurrentOpponent.ExecutedAttacks);

        // Turn 3: Correct (nonfatal) -> Count becomes 2
        var r3 = CyberDefenseStateMachine.ApplyAttempt(r2.NextState, isCorrect: true, effectiveAttackDamage: 2);
        Assert.Equal(2, r3.NextState.CurrentOpponent.ExecutedAttacks);

        // Turn 4: Incorrect -> Count preserved at 2
        var r4 = CyberDefenseStateMachine.ApplyAttempt(r3.NextState, isCorrect: false, effectiveAttackDamage: 1);
        Assert.Equal(2, r4.NextState.CurrentOpponent.ExecutedAttacks);
    }

    // =========================================================================
    // 6. ONE-HIT OPPONENT DEFEAT REPORTS EXACTLY ONE EXECUTED ATTACK
    // =========================================================================

    [Fact]
    public void OneHitOpponentDefeat_ReportsExactlyOneExecutedAttack()
    {
        // A boss with 12 HP defeated by one confirmed attack dealing 12 damage
        int sector = 1;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp, executedAttacks: 0);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 100, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 12);

        Assert.True(result.IsOpponentDefeated);
        Assert.Equal(1, result.ExecutedAttacksOnDefeatedOpponent);
    }

    // =========================================================================
    // 7. MULTI-HIT DEFEAT REPORTS ACTUAL ACCUMULATED COUNT
    // =========================================================================

    [Fact]
    public void MultiHitDefeat_ReportsActualAccumulatedCount()
    {
        // A boss defeated by four actual confirmed successful attacks
        int sector = 1;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp, executedAttacks: 0);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 100, boss);

        var r1 = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 3);
        var r2 = CyberDefenseStateMachine.ApplyAttempt(r1.NextState, isCorrect: true, effectiveAttackDamage: 3);
        var r3 = CyberDefenseStateMachine.ApplyAttempt(r2.NextState, isCorrect: true, effectiveAttackDamage: 3);
        var r4 = CyberDefenseStateMachine.ApplyAttempt(r3.NextState, isCorrect: true, effectiveAttackDamage: 3);

        Assert.True(r4.IsOpponentDefeated);
        Assert.Equal(4, r4.ExecutedAttacksOnDefeatedOpponent);
    }

    // =========================================================================
    // 8. OVERKILL COUNTS AS ONE ACTUAL ATTACK NOT MULTIPLE HITS
    // =========================================================================

    [Fact]
    public void Overkill_CountsAsOneActualAttackNotMultipleHits()
    {
        // Normal opponent with 2 HP hit with 50 damage -> overkill 48 excess, defeated in 1 hit
        var run = CyberDefenseRunState.InitialRun();
        Assert.Equal(2, run.CurrentOpponent.MaxHp);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 50);

        Assert.True(result.IsOpponentDefeated);
        Assert.Equal(1, result.ExecutedAttacksOnDefeatedOpponent);
        Assert.Equal(2, result.AppliedOpponentDamage);
        Assert.Equal(48, result.ExcessOpponentDamage);
    }

    // =========================================================================
    // 9. NEWLY SPAWNED OPPONENT STARTS WITH COUNT ZERO
    // =========================================================================

    [Fact]
    public void NewlySpawnedOpponent_StartsWaitCountZero()
    {
        var run = CyberDefenseRunState.InitialRun();
        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 2);

        Assert.True(result.IsOpponentDefeated);
        Assert.Equal(1, result.NextState.OpponentIndex);
        Assert.Equal(0, result.NextState.CurrentOpponent.ExecutedAttacks);
    }

    // =========================================================================
    // 10. BOSS DEFEAT REPORTS CORRECT ATTACK COUNT
    // =========================================================================

    [Fact]
    public void BossDefeat_ReportsCorrectAttackCount()
    {
        int sector = 1;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp, executedAttacks: 0);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 100, boss);

        // Boss has 12 HP: 5 + 5 + 2 = 12 HP across 3 hits
        var r1 = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 5);
        var r2 = CyberDefenseStateMachine.ApplyAttempt(r1.NextState, isCorrect: true, effectiveAttackDamage: 5);
        var r3 = CyberDefenseStateMachine.ApplyAttempt(r2.NextState, isCorrect: true, effectiveAttackDamage: 5);

        Assert.True(r3.IsOpponentDefeated);
        Assert.True(r3.IsSectorCompleted);
        Assert.Equal(3, r3.ExecutedAttacksOnDefeatedOpponent);
    }

    // =========================================================================
    // 11. SECTOR ADVANCEMENT RESETS COUNT FOR NEXT OPPONENT
    // =========================================================================

    [Fact]
    public void SectorAdvancement_ResetsCountForNextOpponent()
    {
        int sector = 1;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp, executedAttacks: 0);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 100, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: bossMaxHp);

        Assert.True(result.IsSectorCompleted);
        Assert.Equal(2, result.NextState.Sector);
        Assert.Equal(0, result.NextState.OpponentIndex);
        Assert.Equal(0, result.NextState.CurrentOpponent.ExecutedAttacks);
    }

    // =========================================================================
    // 12. GAME-OVER REBOOT RESETS ACTIVE OPPONENT COUNT
    // =========================================================================

    [Fact]
    public void GameOverReboot_ResetsActiveOpponentCount()
    {
        int sector = 1;
        var opp = new OpponentState(OpponentKind.Normal, 2, 2, executedAttacks: 0);
        // Player HP 4, incoming damage 4 -> lethal
        var run = CyberDefenseRunState.CreateActive(sector, 0, 4, opp);

        // Turn 1: Correct attack (nonfatal) -> opponent has 1 attack
        var r1 = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        Assert.Equal(1, r1.NextState.CurrentOpponent.ExecutedAttacks);

        // Turn 2: Incorrect -> lethal damage -> game over
        var r2 = CyberDefenseStateMachine.ApplyAttempt(r1.NextState, isCorrect: false, effectiveAttackDamage: 1);

        Assert.True(r2.IsGameOver);
        Assert.Null(r2.ExecutedAttacksOnDefeatedOpponent);
        Assert.NotNull(r2.TerminalSnapshot);
        Assert.Equal(1, r2.NextState.Sector);
        Assert.Equal(0, r2.NextState.OpponentIndex);
        Assert.Equal(0, r2.NextState.CurrentOpponent.ExecutedAttacks);
    }

    // =========================================================================
    // 13. EXPLICIT DOMAIN REHYDRATION PRESERVES SUPPLIED VALID COUNT
    // =========================================================================

    [Fact]
    public void ExplicitDomainRehydration_PreservesSuppliedValidCount()
    {
        var run = CyberDefenseRunState.Rehydrate(
            sector: 2,
            opponentIndex: 1,
            opponentCurrentHp: 2,
            playerCurrentHp: 90,
            opponentExecutedAttacks: 5);

        Assert.Equal(5, run.CurrentOpponent.ExecutedAttacks);
    }

    // =========================================================================
    // 14. EXISTING REHYDRATION CALLERS RETAIN COMPATIBLE DEFAULT BEHAVIOR
    // =========================================================================

    [Fact]
    public void ExistingRehydrationCallers_RetainCompatibleDefaultBehavior()
    {
        var run = CyberDefenseRunState.Rehydrate(
            sector: 2,
            opponentIndex: 1,
            opponentCurrentHp: 2,
            playerCurrentHp: 90);

        Assert.Equal(0, run.CurrentOpponent.ExecutedAttacks);
    }

    // =========================================================================
    // 15. NEGATIVE COUNTS ARE REJECTED
    // =========================================================================

    [Fact]
    public void NegativeCounts_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OpponentState(OpponentKind.Normal, 5, 5, -1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.Rehydrate(1, 0, 2, 100, -1));

        var run = CyberDefenseRunState.InitialRun();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseCombatTransitionResult(
                true, 1, 1, 0, 0, 0, 0, 0, 0,
                isOpponentDefeated: true,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run,
                terminalSnapshot: null,
                executedAttacksOnDefeatedOpponent: -1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseCombatTransitionResult(
                true, 1, 1, 0, 0, 0, 0, 0, 0,
                isOpponentDefeated: true,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run,
                terminalSnapshot: null,
                executedAttacksOnDefeatedOpponent: 0));

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                true, 1, 1, 0, 0, 0, 0, 0, 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run,
                terminalSnapshot: null,
                executedAttacksOnDefeatedOpponent: 1));
    }

    // =========================================================================
    // 16. ATTACK-COUNT OVERFLOW IS HANDLED SAFELY
    // =========================================================================

    [Fact]
    public void AttackCountOverflow_IsHandledSafely()
    {
        var maxOpponent = new OpponentState(OpponentKind.Normal, 2, 2, int.MaxValue);
        var run = CyberDefenseRunState.CreateActive(1, 0, 100, maxOpponent);

        Assert.Throws<OverflowException>(() =>
            CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1));
    }

    // =========================================================================
    // 17. OPPONENT AND RUN STRUCTURAL EQUALITY REFLECT ATTACK COUNT
    // =========================================================================

    [Fact]
    public void OpponentAndRunStructuralEquality_ReflectAttackCount()
    {
        var opp0 = new OpponentState(OpponentKind.Normal, 2, 2, 0);
        var opp1 = new OpponentState(OpponentKind.Normal, 2, 2, 1);
        var opp0Clone = new OpponentState(OpponentKind.Normal, 2, 2, 0);

        Assert.Equal(opp0, opp0Clone);
        Assert.True(opp0 == opp0Clone);
        Assert.Equal(opp0.GetHashCode(), opp0Clone.GetHashCode());

        Assert.NotEqual(opp0, opp1);
        Assert.True(opp0 != opp1);

        var run0 = CyberDefenseRunState.CreateActive(1, 0, 100, opp0);
        var run1 = CyberDefenseRunState.CreateActive(1, 0, 100, opp1);
        var run0Clone = CyberDefenseRunState.CreateActive(1, 0, 100, opp0Clone);

        Assert.Equal(run0, run0Clone);
        Assert.True(run0 == run0Clone);
        Assert.Equal(run0.GetHashCode(), run0Clone.GetHashCode());

        Assert.NotEqual(run0, run1);
        Assert.True(run0 != run1);
    }

    // =========================================================================
    // 18. COMBAT TRANSITION RESULT EQUALITY REFLECTS DEFEAT METADATA
    // =========================================================================

    [Fact]
    public void CombatTransitionResultEquality_ReflectsDefeatMetadata()
    {
        var run = CyberDefenseRunState.InitialRun();

        var resDefeat1 = new CyberDefenseCombatTransitionResult(
            true, 2, 2, 0, 0, 0, 0, 2, 0, true, false, false, run, null, executedAttacksOnDefeatedOpponent: 1);
        var resDefeat1Clone = new CyberDefenseCombatTransitionResult(
            true, 2, 2, 0, 0, 0, 0, 2, 0, true, false, false, run, null, executedAttacksOnDefeatedOpponent: 1);
        var resDefeat2 = new CyberDefenseCombatTransitionResult(
            true, 2, 2, 0, 0, 0, 0, 2, 0, true, false, false, run, null, executedAttacksOnDefeatedOpponent: 2);
        var resDefeatNull = new CyberDefenseCombatTransitionResult(
            true, 2, 2, 0, 0, 0, 0, 2, 0, true, false, false, run, null, executedAttacksOnDefeatedOpponent: null);
        var resDefeatNullClone = new CyberDefenseCombatTransitionResult(
            true, 2, 2, 0, 0, 0, 0, 2, 0, true, false, false, run, null, executedAttacksOnDefeatedOpponent: null);

        Assert.Equal(resDefeat1, resDefeat1Clone);
        Assert.True(resDefeat1 == resDefeat1Clone);
        Assert.Equal(resDefeat1.GetHashCode(), resDefeat1Clone.GetHashCode());

        Assert.Equal(resDefeatNull, resDefeatNullClone);
        Assert.True(resDefeatNull == resDefeatNullClone);
        Assert.Equal(resDefeatNull.GetHashCode(), resDefeatNullClone.GetHashCode());

        Assert.NotEqual(resDefeat1, resDefeat2);
        Assert.True(resDefeat1 != resDefeat2);

        Assert.NotEqual(resDefeat1, resDefeatNull);
        Assert.True(resDefeat1 != resDefeatNull);
    }

    // =========================================================================
    // 19. EXISTING DAMAGE, HEALING AND SCALING BEHAVIOR REMAINS UNCHANGED
    // =========================================================================

    [Fact]
    public void ExistingDamageHealingAndScaling_RemainsUnchanged()
    {
        var run = CyberDefenseRunState.InitialRun();
        // Initial run has 100 HP, Sector 1 normal opponent has 2 HP
        var hit1 = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        Assert.Equal(1, hit1.AppliedOpponentDamage);
        Assert.Equal(0, hit1.AppliedHealing);
        Assert.Equal(100, hit1.NextState.PlayerCurrentHp);

        // Fatal attack on normal opponent heals 2 HP
        int initialPlayerHp = 90;
        var woundedRun = CyberDefenseRunState.CreateActive(1, 0, initialPlayerHp, hit1.NextState.CurrentOpponent);
        var hit2 = CyberDefenseStateMachine.ApplyAttempt(woundedRun, isCorrect: true, effectiveAttackDamage: 1);
        Assert.True(hit2.IsOpponentDefeated);
        Assert.Equal(2, hit2.PotentialHealing);
        Assert.Equal(2, hit2.AppliedHealing);
        Assert.Equal(92, hit2.NextState.PlayerCurrentHp);
    }

    // =========================================================================
    // 20. EXISTING SIMULATOR INTEGRATION REMAINS COMPATIBLE
    // =========================================================================

    [Fact]
    public void ExistingSimulatorIntegration_RemainsCompatible()
    {
        var run = CyberDefenseRunState.InitialRun();
        var stepResult = HeadlessCombatSimulator.Step(run, isCorrect: true, effectiveAttackDamage: 1);
        var applyResult = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.Equal(stepResult.IsCorrect, applyResult.IsCorrect);
        Assert.Equal(stepResult.NextState.CurrentOpponent.ExecutedAttacks, applyResult.NextState.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(stepResult.ExecutedAttacksOnDefeatedOpponent, applyResult.ExecutedAttacksOnDefeatedOpponent);
        Assert.Equal(stepResult.NextState, applyResult.NextState);
    }
}
