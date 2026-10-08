using System.Reflection;
using System.Runtime.CompilerServices;
using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseStateMachineTests
{
    // =========================================================================
    // A. INPUT VALIDATION
    // =========================================================================

    [Fact]
    public void ApplyAttempt_ThrowsWhenRunStateIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CyberDefenseStateMachine.ApplyAttempt(null!, isCorrect: true, effectiveAttackDamage: 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    [InlineData(int.MinValue)]
    public void ApplyAttempt_ThrowsWhenEffectiveAttackDamageIsZeroOrNegative(int invalidDamage)
    {
        var run = CyberDefenseRunState.InitialRun();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: invalidDamage));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(50)]
    [InlineData(int.MaxValue)]
    public void ApplyAttempt_AcceptsPositiveEffectiveAttackDamage(int validDamage)
    {
        var run = CyberDefenseRunState.InitialRun();

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: validDamage);

        Assert.NotNull(result);
        Assert.Equal(validDamage, result.RequestedAttackDamage);
    }

    // =========================================================================
    // B. PARTIAL NORMAL DAMAGE
    // =========================================================================

    [Fact]
    public void ApplyAttempt_PartialDamage_ReducesCurrentHpWithoutAdvancementOrHealing()
    {
        // Initial run in Sector 1: Normal opponent has MaxHp = 2, CurrentHp = 2
        var run = CyberDefenseRunState.InitialRun();

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.True(result.IsCorrect);
        Assert.Equal(1, result.RequestedAttackDamage);
        Assert.Equal(1, result.AppliedOpponentDamage);
        Assert.Equal(0, result.ExcessOpponentDamage);
        Assert.Equal(0, result.IncomingEnemyDamage);
        Assert.Equal(0, result.AppliedPlayerDamage);
        Assert.Equal(0, result.ExcessEnemyDamage);
        Assert.Equal(0, result.PotentialHealing);
        Assert.Equal(0, result.AppliedHealing);
        Assert.False(result.IsOpponentDefeated);
        Assert.False(result.IsSectorCompleted);
        Assert.False(result.IsGameOver);
        Assert.Null(result.TerminalSnapshot);

        Assert.NotNull(result.NextState);
        Assert.Equal(1, result.NextState.Sector);
        Assert.Equal(0, result.NextState.OpponentIndex);
        Assert.Equal(100, result.NextState.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Normal, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(2, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(1, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void ApplyAttempt_PartialDamage_OnHigherSectorOpponent()
    {
        // Sector 50: Normal opponent MaxHp = 6
        int sector = 50;
        int maxHp = CyberDefenseScalingPolicy.GetNormalMaxHp(sector);
        Assert.Equal(6, maxHp);

        var opponent = new OpponentState(OpponentKind.Normal, maxHp, maxHp);
        var run = CyberDefenseRunState.CreateActive(sector, 2, 85, opponent);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 2);

        Assert.True(result.IsCorrect);
        Assert.Equal(2, result.RequestedAttackDamage);
        Assert.Equal(2, result.AppliedOpponentDamage);
        Assert.Equal(0, result.ExcessOpponentDamage);
        Assert.False(result.IsOpponentDefeated);
        Assert.False(result.IsSectorCompleted);
        Assert.False(result.IsGameOver);
        Assert.Equal(0, result.PotentialHealing);
        Assert.Equal(0, result.AppliedHealing);

        Assert.Equal(sector, result.NextState.Sector);
        Assert.Equal(2, result.NextState.OpponentIndex);
        Assert.Equal(85, result.NextState.PlayerCurrentHp);
        Assert.Equal(maxHp, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(4, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void ApplyAttempt_PartialDamage_DoesNotMutatePlayerHpWhenPlayerDamaged()
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(1, 0, 70, opponent);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.Equal(70, result.NextState.PlayerCurrentHp);
        Assert.Equal(0, result.AppliedHealing);
        Assert.Equal(0, result.PotentialHealing);
    }

    // =========================================================================
    // C. EXACT DEFEAT
    // =========================================================================

    [Fact]
    public void ApplyAttempt_ExactDefeat_FromFullHealth_AdvancesOpponent()
    {
        // Sector 1: Normal opponent MaxHp = 2, CurrentHp = 2. Attack = 2 (exact lethal)
        var run = CyberDefenseRunState.InitialRun();

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 2);

        Assert.True(result.IsCorrect);
        Assert.Equal(2, result.RequestedAttackDamage);
        Assert.Equal(2, result.AppliedOpponentDamage);
        Assert.Equal(0, result.ExcessOpponentDamage);
        Assert.Equal(0, result.IncomingEnemyDamage);
        Assert.Equal(0, result.AppliedPlayerDamage);
        Assert.Equal(0, result.ExcessEnemyDamage);
        Assert.Equal(2, result.PotentialHealing);
        Assert.Equal(0, result.AppliedHealing); // Player at 100 HP cap
        Assert.True(result.IsOpponentDefeated);
        Assert.False(result.IsSectorCompleted);
        Assert.False(result.IsGameOver);
        Assert.Null(result.TerminalSnapshot);

        // Next opponent is index 1 at full HP
        Assert.NotNull(result.NextState);
        Assert.Equal(1, result.NextState.Sector);
        Assert.Equal(1, result.NextState.OpponentIndex);
        Assert.Equal(100, result.NextState.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Normal, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(2, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(2, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void ApplyAttempt_ExactDefeat_FromWeakenedHealth_AdvancesOpponent()
    {
        // Sector 1, Index 1: Normal opponent MaxHp = 2, CurrentHp = 1. Attack = 1 (exact lethal)
        var opponent = new OpponentState(OpponentKind.Normal, 2, 1);
        var run = CyberDefenseRunState.CreateActive(1, 1, 90, opponent);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.True(result.IsOpponentDefeated);
        Assert.Equal(1, result.RequestedAttackDamage);
        Assert.Equal(1, result.AppliedOpponentDamage);
        Assert.Equal(0, result.ExcessOpponentDamage);
        Assert.Equal(2, result.PotentialHealing);
        Assert.Equal(2, result.AppliedHealing);

        Assert.Equal(1, result.NextState.Sector);
        Assert.Equal(2, result.NextState.OpponentIndex);
        Assert.Equal(92, result.NextState.PlayerCurrentHp);
        Assert.Equal(2, result.NextState.CurrentOpponent.CurrentHp);
    }

    // =========================================================================
    // D. OVERKILL AND NO-SPLASH RULES
    // =========================================================================

    [Theory]
    [InlineData(1, 50, 1, 49)]
    [InlineData(2, 50, 2, 48)]
    [InlineData(1, 10, 1, 9)]
    [InlineData(2, 3, 2, 1)]
    public void ApplyAttempt_Overkill_AccountsExcessCorrectlyWithoutSplashDamage(
        int currentHp,
        int attackDamage,
        int expectedApplied,
        int expectedExcess)
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, currentHp);
        var run = CyberDefenseRunState.CreateActive(1, 0, 100, opponent);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: attackDamage);

        Assert.True(result.IsOpponentDefeated);
        Assert.Equal(attackDamage, result.RequestedAttackDamage);
        Assert.Equal(expectedApplied, result.AppliedOpponentDamage);
        Assert.Equal(expectedExcess, result.ExcessOpponentDamage);

        // Next opponent is at full HP, excess damage is completely discarded (no splash)
        Assert.Equal(1, result.NextState.OpponentIndex);
        Assert.Equal(2, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(2, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void ApplyAttempt_Overkill_MaxIntDamage_DoesNotOverflow()
    {
        var run = CyberDefenseRunState.InitialRun(); // Hp = 2

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: int.MaxValue);

        Assert.True(result.IsOpponentDefeated);
        Assert.Equal(int.MaxValue, result.RequestedAttackDamage);
        Assert.Equal(2, result.AppliedOpponentDamage);
        Assert.Equal(int.MaxValue - 2, result.ExcessOpponentDamage);

        Assert.Equal(1, result.NextState.OpponentIndex);
        Assert.Equal(2, result.NextState.CurrentOpponent.CurrentHp);
    }

    // =========================================================================
    // E. DEFEAT HEALING AND 100 HP CAP
    // =========================================================================

    [Theory]
    [InlineData(100, 2, 0, 100)]
    [InlineData(99, 2, 1, 100)]
    [InlineData(98, 2, 2, 100)]
    [InlineData(97, 2, 2, 99)]
    [InlineData(50, 2, 2, 52)]
    [InlineData(1, 2, 2, 3)]
    public void ApplyAttempt_DefeatHealing_RespectsCapAndAvailableCapacity(
        int startingPlayerHp,
        int expectedPotential,
        int expectedApplied,
        int expectedNextPlayerHp)
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 1);
        var run = CyberDefenseRunState.CreateActive(1, 0, startingPlayerHp, opponent);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.True(result.IsOpponentDefeated);
        Assert.Equal(expectedPotential, result.PotentialHealing);
        Assert.Equal(expectedApplied, result.AppliedHealing);
        Assert.Equal(expectedNextPlayerHp, result.NextState.PlayerCurrentHp);
        Assert.True(result.NextState.PlayerCurrentHp <= CyberDefenseCombatPolicy.PlayerMaxHp);
    }

    // =========================================================================
    // F. NORMAL OPPONENT PROGRESSION
    // =========================================================================

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    public void ApplyAttempt_NormalOpponentProgression_AdvancesConsecutiveIndicesInSameSector(
        int startingIndex,
        int expectedNextIndex)
    {
        int sector = 1;
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(sector, startingIndex, 100, opponent);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 2);

        Assert.True(result.IsOpponentDefeated);
        Assert.Equal(sector, result.NextState.Sector);
        Assert.Equal(expectedNextIndex, result.NextState.OpponentIndex);
        Assert.Equal(OpponentKind.Normal, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(2, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(2, result.NextState.CurrentOpponent.CurrentHp);
    }

    // =========================================================================
    // G. FINAL NORMAL TO BOSS TRANSITION
    // =========================================================================

    [Fact]
    public void ApplyAttempt_Sector1_FinalNormalDefeat_SpawnsBossAtFullHp()
    {
        // Sector 1: Normal count = 5. Final normal index = 4. Boss index = 5. Boss MaxHp = 12.
        int sector = 1;
        int finalNormalIndex = 4;
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(sector, finalNormalIndex, 90, opponent);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 5);

        Assert.True(result.IsOpponentDefeated);
        Assert.False(result.IsSectorCompleted);
        Assert.False(result.IsGameOver);

        Assert.Equal(sector, result.NextState.Sector);
        Assert.Equal(5, result.NextState.OpponentIndex);
        Assert.Equal(92, result.NextState.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Boss, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(12, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(12, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void ApplyAttempt_Sector4_FinalNormalDefeat_SpawnsBossAtFullHp()
    {
        // Sector 4: Normal count G(4) = 7. Final normal index = 6. Boss index = 7. Boss MaxHp = 14.
        int sector = 4;
        int finalNormalIndex = 6;
        int normalMaxHp = CyberDefenseScalingPolicy.GetNormalMaxHp(sector);
        Assert.Equal(2, normalMaxHp);

        var opponent = new OpponentState(OpponentKind.Normal, normalMaxHp, normalMaxHp);
        var run = CyberDefenseRunState.CreateActive(sector, finalNormalIndex, 100, opponent);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 10);

        Assert.True(result.IsOpponentDefeated);
        Assert.False(result.IsSectorCompleted);

        Assert.Equal(sector, result.NextState.Sector);
        Assert.Equal(7, result.NextState.OpponentIndex);
        Assert.Equal(OpponentKind.Boss, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(14, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(14, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(64)]
    public void ApplyAttempt_VariousSectors_FinalNormalDefeat_SpawnsExactScaledBoss(int sector)
    {
        int finalNormalIndex = CyberDefenseScalingPolicy.GetBossIndex(sector) - 1;
        int normalMaxHp = CyberDefenseScalingPolicy.GetNormalMaxHp(sector);
        int expectedBossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);

        var opponent = new OpponentState(OpponentKind.Normal, normalMaxHp, normalMaxHp);
        var run = CyberDefenseRunState.CreateActive(sector, finalNormalIndex, 100, opponent);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: normalMaxHp);

        Assert.True(result.IsOpponentDefeated);
        Assert.False(result.IsSectorCompleted);
        Assert.Equal(CyberDefenseScalingPolicy.GetBossIndex(sector), result.NextState.OpponentIndex);
        Assert.Equal(OpponentKind.Boss, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(expectedBossMaxHp, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(expectedBossMaxHp, result.NextState.CurrentOpponent.CurrentHp);
    }

    // =========================================================================
    // H. STATE IMMUTABILITY & CONTRACT INTEGRITY
    // =========================================================================

    [Fact]
    public void ApplyAttempt_NeverMutatesInputRunOrOpponent()
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(1, 0, 95, opponent);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 2);

        // Input run state is intact
        Assert.Equal(1, run.Sector);
        Assert.Equal(0, run.OpponentIndex);
        Assert.Equal(95, run.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Normal, run.CurrentOpponent.Kind);
        Assert.Equal(2, run.CurrentOpponent.MaxHp);
        Assert.Equal(2, run.CurrentOpponent.CurrentHp);

        // NextState is a separate valid instance
        Assert.NotSame(run, result.NextState);
        Assert.NotSame(run.CurrentOpponent, result.NextState.CurrentOpponent);
        Assert.Equal(1, result.NextState.OpponentIndex);
        Assert.Equal(97, result.NextState.PlayerCurrentHp);
    }

    [Fact]
    public void CombatTransitionResult_HasNoPublicSettersOrInitAccessors()
    {
        var type = typeof(CyberDefenseCombatTransitionResult);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(properties);
        foreach (var prop in properties)
        {
            var setMethod = prop.GetSetMethod(nonPublic: true);
            if (setMethod is not null)
            {
                var isInitOnly = setMethod.ReturnParameter
                    .GetRequiredCustomModifiers()
                    .Contains(typeof(IsExternalInit));

                Assert.False(isInitOnly, $"Property '{prop.Name}' has an init-only setter.");
                Assert.False(prop.CanWrite && setMethod.IsPublic, $"Property '{prop.Name}' has a public setter.");
            }
        }
    }

    // =========================================================================
    // I. CORRECT-ANSWER FAIRNESS & UNSUPPORTED PATHS
    // =========================================================================

    [Fact]
    public void ApplyAttempt_CorrectAnswer_NeverDealsPlayerDamageOrEnemyDamage()
    {
        var run = CyberDefenseRunState.InitialRun();

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.Equal(0, result.IncomingEnemyDamage);
        Assert.Equal(0, result.AppliedPlayerDamage);
        Assert.Equal(0, result.ExcessEnemyDamage);
    }

    [Fact]
    public void ApplyAttempt_ThrowsNotSupported_ForIncorrectAnswer()
    {
        var run = CyberDefenseRunState.InitialRun();

        Assert.Throws<NotSupportedException>(() =>
            CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1));
    }

    // =========================================================================
    // J. COMBAT TRANSITION RESULT INVARIANTS & EQUALITY
    // =========================================================================

    [Fact]
    public void CombatTransitionResult_ThrowsWhenNextStateIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: 1,
                appliedOpponentDamage: 1,
                excessOpponentDamage: 0,
                incomingEnemyDamage: 0,
                appliedPlayerDamage: 0,
                excessEnemyDamage: 0,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: null!));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, 0, 0, 0, 0)]
    [InlineData(0, -1, 0, 0, 0, 0, 0, 0)]
    [InlineData(0, 0, -1, 0, 0, 0, 0, 0)]
    [InlineData(0, 0, 0, -1, 0, 0, 0, 0)]
    [InlineData(0, 0, 0, 0, -1, 0, 0, 0)]
    [InlineData(0, 0, 0, 0, 0, -1, 0, 0)]
    [InlineData(0, 0, 0, 0, 0, 0, -1, 0)]
    [InlineData(0, 0, 0, 0, 0, 0, 0, -1)]
    public void CombatTransitionResult_ThrowsWhenAnyDamageOrHealingIsNegative(
        int reqAttack,
        int appOpp,
        int excOpp,
        int incEnemy,
        int appPlayer,
        int excEnemy,
        int potHeal,
        int appHeal)
    {
        var run = CyberDefenseRunState.InitialRun();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: reqAttack,
                appliedOpponentDamage: appOpp,
                excessOpponentDamage: excOpp,
                incomingEnemyDamage: incEnemy,
                appliedPlayerDamage: appPlayer,
                excessEnemyDamage: excEnemy,
                potentialHealing: potHeal,
                appliedHealing: appHeal,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run));
    }

    [Fact]
    public void CombatTransitionResult_ThrowsWhenAppliedDamageExceedsRequested()
    {
        var run = CyberDefenseRunState.InitialRun();

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: 1,
                appliedOpponentDamage: 2,
                excessOpponentDamage: 0,
                incomingEnemyDamage: 0,
                appliedPlayerDamage: 0,
                excessEnemyDamage: 0,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run));
    }

    [Fact]
    public void CombatTransitionResult_ThrowsWhenExcessOpponentDamageDoesNotMatchDifference()
    {
        var run = CyberDefenseRunState.InitialRun();

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: 5,
                appliedOpponentDamage: 2,
                excessOpponentDamage: 2, // Should be 3
                incomingEnemyDamage: 0,
                appliedPlayerDamage: 0,
                excessEnemyDamage: 0,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run));
    }

    [Fact]
    public void CombatTransitionResult_ThrowsWhenAppliedHealingExceedsPotential()
    {
        var run = CyberDefenseRunState.InitialRun();

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: 1,
                appliedOpponentDamage: 1,
                excessOpponentDamage: 0,
                incomingEnemyDamage: 0,
                appliedPlayerDamage: 0,
                excessEnemyDamage: 0,
                potentialHealing: 2,
                appliedHealing: 3,
                isOpponentDefeated: true,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run));
    }

    [Fact]
    public void CombatTransitionResult_ThrowsWhenSectorCompletedWithoutOpponentDefeat()
    {
        var run = CyberDefenseRunState.InitialRun();

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: 1,
                appliedOpponentDamage: 1,
                excessOpponentDamage: 0,
                incomingEnemyDamage: 0,
                appliedPlayerDamage: 0,
                excessEnemyDamage: 0,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: true,
                isGameOver: false,
                nextState: run));
    }

    [Fact]
    public void CombatTransitionResult_ThrowsWhenTerminalSnapshotProvidedForNonGameOver()
    {
        var run = CyberDefenseRunState.InitialRun();
        var snapshot = new CyberDefenseTerminalRunSnapshot(1, 0, OpponentKind.Normal, 2, 2, 0);

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: 1,
                appliedOpponentDamage: 1,
                excessOpponentDamage: 0,
                incomingEnemyDamage: 0,
                appliedPlayerDamage: 0,
                excessEnemyDamage: 0,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run,
                terminalSnapshot: snapshot));
    }

    [Fact]
    public void CombatTransitionResult_ThrowsWhenTerminalSnapshotMissingForGameOver()
    {
        var run = CyberDefenseRunState.InitialRun();

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: false,
                requestedAttackDamage: 1,
                appliedOpponentDamage: 0,
                excessOpponentDamage: 1,
                incomingEnemyDamage: 4,
                appliedPlayerDamage: 4,
                excessEnemyDamage: 0,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: true,
                nextState: run,
                terminalSnapshot: null));
    }

    [Fact]
    public void CombatTransitionResult_EqualityAndHashCode_FunctionCorrectly()
    {
        var run1 = CyberDefenseRunState.InitialRun();
        var run2 = CyberDefenseRunState.InitialRun();

        var res1 = new CyberDefenseCombatTransitionResult(
            true, 1, 1, 0, 0, 0, 0, 0, 0, false, false, false, run1);
        var res2 = new CyberDefenseCombatTransitionResult(
            true, 1, 1, 0, 0, 0, 0, 0, 0, false, false, false, run2);
        var res3 = new CyberDefenseCombatTransitionResult(
            true, 2, 2, 0, 0, 0, 0, 2, 0, true, false, false, run1);

        Assert.Equal(res1, res2);
        Assert.True(res1 == res2);
        Assert.False(res1 != res2);
        Assert.Equal(res1.GetHashCode(), res2.GetHashCode());

        Assert.NotEqual(res1, res3);
        Assert.False(res1 == res3);
        Assert.True(res1 != res3);
    }

    // =========================================================================
    // K. PARTIAL BOSS DAMAGE
    // =========================================================================

    [Fact]
    public void ApplyAttempt_Boss_PartialDamage_Sector1_Attack1()
    {
        // Sector 1: Boss index = 5, Boss MaxHp = 12, CurrentHp = 12
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp);
        var run = CyberDefenseRunState.CreateActive(1, bossIndex, 100, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.True(result.IsCorrect);
        Assert.Equal(1, result.RequestedAttackDamage);
        Assert.Equal(1, result.AppliedOpponentDamage);
        Assert.Equal(0, result.ExcessOpponentDamage);
        Assert.Equal(0, result.IncomingEnemyDamage);
        Assert.Equal(0, result.AppliedPlayerDamage);
        Assert.Equal(0, result.ExcessEnemyDamage);
        Assert.Equal(0, result.PotentialHealing);
        Assert.Equal(0, result.AppliedHealing);
        Assert.False(result.IsOpponentDefeated);
        Assert.False(result.IsSectorCompleted);
        Assert.False(result.IsGameOver);
        Assert.Null(result.TerminalSnapshot);

        Assert.NotNull(result.NextState);
        Assert.Equal(1, result.NextState.Sector);
        Assert.Equal(bossIndex, result.NextState.OpponentIndex);
        Assert.Equal(100, result.NextState.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Boss, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(12, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(11, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void ApplyAttempt_Boss_PartialDamage_Sector1_Attack5()
    {
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp);
        var run = CyberDefenseRunState.CreateActive(1, bossIndex, 100, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 5);

        Assert.True(result.IsCorrect);
        Assert.Equal(5, result.RequestedAttackDamage);
        Assert.Equal(5, result.AppliedOpponentDamage);
        Assert.Equal(0, result.ExcessOpponentDamage);
        Assert.Equal(0, result.PotentialHealing);
        Assert.Equal(0, result.AppliedHealing);
        Assert.False(result.IsOpponentDefeated);
        Assert.False(result.IsSectorCompleted);
        Assert.False(result.IsGameOver);

        Assert.Equal(1, result.NextState.Sector);
        Assert.Equal(bossIndex, result.NextState.OpponentIndex);
        Assert.Equal(100, result.NextState.PlayerCurrentHp);
        Assert.Equal(12, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(7, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void ApplyAttempt_Boss_PartialDamage_RehydratedPartiallyDamagedBoss()
    {
        // Rehydrated partially damaged boss in Sector 1 (CurrentHp = 7, PlayerHp = 80)
        var run = CyberDefenseRunState.Rehydrate(sector: 1, opponentIndex: 5, opponentCurrentHp: 7, playerCurrentHp: 80);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 3);

        Assert.True(result.IsCorrect);
        Assert.Equal(3, result.RequestedAttackDamage);
        Assert.Equal(3, result.AppliedOpponentDamage);
        Assert.Equal(0, result.ExcessOpponentDamage);
        Assert.Equal(0, result.PotentialHealing);
        Assert.Equal(0, result.AppliedHealing);
        Assert.False(result.IsOpponentDefeated);
        Assert.False(result.IsSectorCompleted);
        Assert.False(result.IsGameOver);

        Assert.Equal(1, result.NextState.Sector);
        Assert.Equal(5, result.NextState.OpponentIndex);
        Assert.Equal(80, result.NextState.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Boss, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(12, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(4, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void ApplyAttempt_Boss_PartialDamage_HigherSectorBoss()
    {
        // Sector 10: Boss index = 8, Boss MaxHp = 18
        int sector = 10;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        Assert.Equal(8, bossIndex);
        Assert.Equal(18, bossMaxHp);

        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 90, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 4);

        Assert.True(result.IsCorrect);
        Assert.Equal(4, result.RequestedAttackDamage);
        Assert.Equal(4, result.AppliedOpponentDamage);
        Assert.Equal(0, result.ExcessOpponentDamage);
        Assert.False(result.IsOpponentDefeated);
        Assert.False(result.IsSectorCompleted);
        Assert.False(result.IsGameOver);
        Assert.Equal(0, result.PotentialHealing);
        Assert.Equal(0, result.AppliedHealing);

        Assert.Equal(sector, result.NextState.Sector);
        Assert.Equal(bossIndex, result.NextState.OpponentIndex);
        Assert.Equal(90, result.NextState.PlayerCurrentHp);
        Assert.Equal(18, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(14, result.NextState.CurrentOpponent.CurrentHp);
    }

    // =========================================================================
    // L. EXACT AND ONE-HIT BOSS DEFEAT (OVERKILL & NO SPLASH)
    // =========================================================================

    [Fact]
    public void ApplyAttempt_Boss_ExactDefeat_AccountsDamageAndFlagsCorrectly()
    {
        // Sector 1: Boss MaxHp = 12, CurrentHp = 12. Attack = 12
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        var boss = new OpponentState(OpponentKind.Boss, 12, 12);
        var run = CyberDefenseRunState.CreateActive(1, bossIndex, 100, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 12);

        Assert.True(result.IsCorrect);
        Assert.Equal(12, result.RequestedAttackDamage);
        Assert.Equal(12, result.AppliedOpponentDamage);
        Assert.Equal(0, result.ExcessOpponentDamage);
        Assert.Equal(0, result.IncomingEnemyDamage);
        Assert.Equal(0, result.AppliedPlayerDamage);
        Assert.Equal(0, result.ExcessEnemyDamage);
        Assert.Equal(6, result.PotentialHealing);
        Assert.Equal(0, result.AppliedHealing); // Cap at 100
        Assert.True(result.IsOpponentDefeated);
        Assert.True(result.IsSectorCompleted);
        Assert.False(result.IsGameOver);
        Assert.Null(result.TerminalSnapshot);

        // Next state advances to Sector 2, Index 0, Normal opponent
        Assert.Equal(2, result.NextState.Sector);
        Assert.Equal(0, result.NextState.OpponentIndex);
        Assert.Equal(100, result.NextState.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Normal, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(2, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(2, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void ApplyAttempt_Boss_ExactDefeat_FromWeakenedBoss()
    {
        // Sector 1: Boss CurrentHp = 3. Attack = 3
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        var boss = new OpponentState(OpponentKind.Boss, 12, 3);
        var run = CyberDefenseRunState.CreateActive(1, bossIndex, 90, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 3);

        Assert.True(result.IsOpponentDefeated);
        Assert.True(result.IsSectorCompleted);
        Assert.Equal(3, result.RequestedAttackDamage);
        Assert.Equal(3, result.AppliedOpponentDamage);
        Assert.Equal(0, result.ExcessOpponentDamage);
        Assert.Equal(6, result.PotentialHealing);
        Assert.Equal(6, result.AppliedHealing);

        Assert.Equal(2, result.NextState.Sector);
        Assert.Equal(0, result.NextState.OpponentIndex);
        Assert.Equal(96, result.NextState.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Normal, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(2, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void ApplyAttempt_Boss_OneHitDefeat_Overkill_AccountsExcessCorrectlyWithoutSplashDamage()
    {
        // Sector 1: Boss MaxHp = 12, CurrentHp = 12. Attack = 50
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        var boss = new OpponentState(OpponentKind.Boss, 12, 12);
        var run = CyberDefenseRunState.CreateActive(1, bossIndex, 100, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 50);

        Assert.True(result.IsOpponentDefeated);
        Assert.True(result.IsSectorCompleted);
        Assert.Equal(50, result.RequestedAttackDamage);
        Assert.Equal(12, result.AppliedOpponentDamage);
        Assert.Equal(38, result.ExcessOpponentDamage);

        // Next sector opponent starts at full HP (excess 38 damage is completely discarded)
        Assert.Equal(2, result.NextState.Sector);
        Assert.Equal(0, result.NextState.OpponentIndex);
        Assert.Equal(OpponentKind.Normal, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(2, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(2, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void ApplyAttempt_Boss_OneHitDefeat_MaxIntDamage_DiscardsExcessWithoutOverflow()
    {
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        var boss = new OpponentState(OpponentKind.Boss, 12, 12);
        var run = CyberDefenseRunState.CreateActive(1, bossIndex, 100, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: int.MaxValue);

        Assert.True(result.IsOpponentDefeated);
        Assert.True(result.IsSectorCompleted);
        Assert.Equal(int.MaxValue, result.RequestedAttackDamage);
        Assert.Equal(12, result.AppliedOpponentDamage);
        Assert.Equal(int.MaxValue - 12, result.ExcessOpponentDamage);

        Assert.Equal(2, result.NextState.Sector);
        Assert.Equal(0, result.NextState.OpponentIndex);
        Assert.Equal(2, result.NextState.CurrentOpponent.CurrentHp);
    }

    // =========================================================================
    // M. BOSS DEFEAT HEALING & PLAYER HP CAP
    // =========================================================================

    [Theory]
    [InlineData(100, 6, 0, 100)]
    [InlineData(99, 6, 1, 100)]
    [InlineData(95, 6, 5, 100)]
    [InlineData(94, 6, 6, 100)]
    [InlineData(40, 6, 6, 46)]
    [InlineData(1, 6, 6, 7)]
    public void ApplyAttempt_Boss_DefeatHealing_RespectsCapAndAvailableCapacity(
        int startingPlayerHp,
        int expectedPotential,
        int expectedApplied,
        int expectedNextPlayerHp)
    {
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        var boss = new OpponentState(OpponentKind.Boss, 12, 1);
        var run = CyberDefenseRunState.CreateActive(1, bossIndex, startingPlayerHp, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.True(result.IsOpponentDefeated);
        Assert.True(result.IsSectorCompleted);
        Assert.Equal(expectedPotential, result.PotentialHealing);
        Assert.Equal(expectedApplied, result.AppliedHealing);
        Assert.Equal(expectedNextPlayerHp, result.NextState.PlayerCurrentHp);
        Assert.True(result.NextState.PlayerCurrentHp <= CyberDefenseCombatPolicy.PlayerMaxHp);
    }

    // =========================================================================
    // N. SECTOR ADVANCEMENT & EXACT REFERENCE TRANSITIONS
    // =========================================================================

    [Theory]
    [InlineData(1, 5, 12, 2, 0, 2)]
    [InlineData(4, 7, 14, 5, 0, 2)]
    [InlineData(10, 8, 18, 11, 0, 3)]
    public void ApplyAttempt_Boss_Defeat_ExactReferenceTransitions(
        int currentSector,
        int expectedBossIndex,
        int expectedBossMaxHp,
        int expectedNextSector,
        int expectedNextOpponentIndex,
        int expectedNextOpponentMaxHp)
    {
        Assert.Equal(expectedBossIndex, CyberDefenseScalingPolicy.GetBossIndex(currentSector));
        Assert.Equal(expectedBossMaxHp, CyberDefenseScalingPolicy.GetBossMaxHp(currentSector));

        var boss = new OpponentState(OpponentKind.Boss, expectedBossMaxHp, expectedBossMaxHp);
        var run = CyberDefenseRunState.CreateActive(currentSector, expectedBossIndex, 100, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: expectedBossMaxHp);

        Assert.True(result.IsOpponentDefeated);
        Assert.True(result.IsSectorCompleted);
        Assert.False(result.IsGameOver);

        Assert.Equal(expectedNextSector, result.NextState.Sector);
        Assert.Equal(expectedNextOpponentIndex, result.NextState.OpponentIndex);
        Assert.Equal(OpponentKind.Normal, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(expectedNextOpponentMaxHp, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(expectedNextOpponentMaxHp, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void ApplyAttempt_Boss_Defeat_AdvancesExactlyOneSectorWithoutSkipping()
    {
        int sector = 1;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 100, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: bossMaxHp);

        Assert.Equal(2, result.NextState.Sector);
        Assert.Equal(0, result.NextState.OpponentIndex);
        Assert.Equal(OpponentKind.Normal, result.NextState.CurrentOpponent.Kind);
    }

    // =========================================================================
    // O. BOSS COMBAT IMMUTABILITY & RESULT ACCOUNTING
    // =========================================================================

    [Fact]
    public void ApplyAttempt_Boss_NeverMutatesInputRunOrBossState()
    {
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        var boss = new OpponentState(OpponentKind.Boss, 12, 12);
        var run = CyberDefenseRunState.CreateActive(1, bossIndex, 90, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 5);

        // Original input state is unchanged
        Assert.Equal(1, run.Sector);
        Assert.Equal(bossIndex, run.OpponentIndex);
        Assert.Equal(90, run.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Boss, run.CurrentOpponent.Kind);
        Assert.Equal(12, run.CurrentOpponent.MaxHp);
        Assert.Equal(12, run.CurrentOpponent.CurrentHp);

        // NextState is a separate valid instance
        Assert.NotSame(run, result.NextState);
        Assert.NotSame(run.CurrentOpponent, result.NextState.CurrentOpponent);
        Assert.Equal(7, result.NextState.CurrentOpponent.CurrentHp);
    }

    // =========================================================================
    // P. SECTOR NUMERIC BOUNDARY & OVERFLOW SAFETY
    // =========================================================================

    [Fact]
    public void ApplyAttempt_Boss_SectorMaxInt_NonLethalHit_PreservesActiveSector()
    {
        int sector = int.MaxValue;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 100, boss);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.False(result.IsOpponentDefeated);
        Assert.False(result.IsSectorCompleted);
        Assert.Equal(sector, result.NextState.Sector);
        Assert.Equal(bossIndex, result.NextState.OpponentIndex);
        Assert.Equal(bossMaxHp - 1, result.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void ApplyAttempt_Boss_SectorMaxInt_LethalHit_FailsClosedWithCheckedOverflowException()
    {
        int sector = int.MaxValue;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, 1);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 100, boss);

        Assert.Throws<OverflowException>(() =>
            CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1));

        // Input state remains intact
        Assert.Equal(int.MaxValue, run.Sector);
        Assert.Equal(bossIndex, run.OpponentIndex);
        Assert.Equal(100, run.PlayerCurrentHp);
        Assert.Equal(1, run.CurrentOpponent.CurrentHp);
    }
}
