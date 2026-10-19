using System.Reflection;
using System.Runtime.CompilerServices;
using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class FirewallRechargeAndAbsorptionTests
{
    // =========================================================================
    // 1. RUN STATE SHIELD INVARIANTS & VALIDATION
    // =========================================================================

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(100)]
    public void RunState_RejectsInvalidCapacity(int invalidCapacity)
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.CreateActive(1, 0, 100, opponent, invalidCapacity, 0, 0));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(1, -1)]
    public void RunState_RejectsActiveShieldsOutsideCapacityRange(int capacity, int invalidActive)
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.CreateActive(1, 0, 100, opponent, capacity, invalidActive, 0));
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 5)]
    [InlineData(3, 3, 29)]
    public void RunState_RejectsNonZeroRechargeUnitsWhenAtCapacity(int capacity, int active, int recharge)
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);

        Assert.Throws<ArgumentException>(() =>
            CyberDefenseRunState.CreateActive(1, 0, 100, opponent, capacity, active, recharge));
    }

    [Theory]
    [InlineData(1, 0, 30)]
    [InlineData(2, 1, 30)]
    [InlineData(3, 2, 31)]
    [InlineData(3, 0, 50)]
    public void RunState_RejectsRechargeUnitsAtOrAboveThreshold(int capacity, int active, int invalidRecharge)
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.CreateActive(1, 0, 100, opponent, capacity, active, invalidRecharge));
    }

    [Fact]
    public void RunState_RejectsNegativeRechargeUnits()
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.CreateActive(1, 0, 100, opponent, 2, 1, -1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RunState_InitialRun_AcceptsValidCapacities_AndBeginsFullyCharged(int capacity)
    {
        var run = CyberDefenseRunState.InitialRun(capacity);

        Assert.Equal(capacity, run.ShieldCapacity);
        Assert.Equal(capacity, run.ActiveShields);
        Assert.Equal(0, run.ShieldRechargeUnits);
    }

    [Fact]
    public void RunState_LegacyInitialRun_DefaultsToZeroShields()
    {
        var run = CyberDefenseRunState.InitialRun();

        Assert.Equal(0, run.ShieldCapacity);
        Assert.Equal(0, run.ActiveShields);
        Assert.Equal(0, run.ShieldRechargeUnits);
    }

    [Fact]
    public void RunState_Rehydrate_WithShields_PreservesState()
    {
        var run = CyberDefenseRunState.Rehydrate(
            sector: 2,
            opponentIndex: 1,
            opponentCurrentHp: 2,
            playerCurrentHp: 80,
            opponentExecutedAttacks: 2,
            provenance: OpponentEncounterProvenance.Authoritative,
            shieldCapacity: 3,
            activeShields: 1,
            shieldRechargeUnits: 15);

        Assert.Equal(3, run.ShieldCapacity);
        Assert.Equal(1, run.ActiveShields);
        Assert.Equal(15, run.ShieldRechargeUnits);
    }

    [Fact]
    public void RunState_Immutability_Maintained()
    {
        var run = CyberDefenseRunState.InitialRun(2);
        var type = typeof(CyberDefenseRunState);

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
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

    [Fact]
    public void RunState_EqualityAndHashCode_IncludesShieldProperties()
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var baseState = CyberDefenseRunState.CreateActive(1, 0, 100, opponent, 2, 1, 10);
        var sameState = CyberDefenseRunState.CreateActive(1, 0, 100, opponent, 2, 1, 10);
        var diffCap = CyberDefenseRunState.CreateActive(1, 0, 100, opponent, 3, 1, 10);
        var diffActive = CyberDefenseRunState.CreateActive(1, 0, 100, opponent, 2, 0, 10);
        var diffRecharge = CyberDefenseRunState.CreateActive(1, 0, 100, opponent, 2, 1, 11);

        Assert.Equal(baseState, sameState);
        Assert.Equal(baseState.GetHashCode(), sameState.GetHashCode());
        Assert.True(baseState == sameState);
        Assert.False(baseState != sameState);

        Assert.NotEqual(baseState, diffCap);
        Assert.NotEqual(baseState, diffActive);
        Assert.NotEqual(baseState, diffRecharge);
    }

    // =========================================================================
    // 2. COMBAT TRANSITION RESULT ACCOUNTING & INVARIANTS
    // =========================================================================

    [Fact]
    public void CombatTransitionResult_RejectsInconsistentShieldAbsorptionFlags()
    {
        var run = CyberDefenseRunState.InitialRun();

        // isShieldAbsorbed == true but absorbedEnemyDamage == 0
        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: false,
                requestedAttackDamage: 0,
                appliedOpponentDamage: 0,
                excessOpponentDamage: 0,
                incomingEnemyDamage: 4,
                appliedPlayerDamage: 0,
                excessEnemyDamage: 4,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run,
                isShieldAbsorbed: true,
                absorbedEnemyDamage: 0));

        // isShieldAbsorbed == false but absorbedEnemyDamage > 0
        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: false,
                requestedAttackDamage: 0,
                appliedOpponentDamage: 0,
                excessOpponentDamage: 0,
                incomingEnemyDamage: 4,
                appliedPlayerDamage: 0,
                excessEnemyDamage: 0,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run,
                isShieldAbsorbed: false,
                absorbedEnemyDamage: 4));
    }

    [Fact]
    public void CombatTransitionResult_EnforcesDamageAccountingFormula()
    {
        var run = CyberDefenseRunState.InitialRun();

        // ExcessEnemyDamage must equal Incoming - Applied - Absorbed = 4 - 0 - 4 = 0.
        // If passed excessEnemyDamage = 1, it must throw ArgumentException.
        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: false,
                requestedAttackDamage: 0,
                appliedOpponentDamage: 0,
                excessOpponentDamage: 0,
                incomingEnemyDamage: 4,
                appliedPlayerDamage: 0,
                excessEnemyDamage: 1, // Invalid!
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run,
                isShieldAbsorbed: true,
                absorbedEnemyDamage: 4));
    }

    [Fact]
    public void CombatTransitionResult_EqualityAndHashCode_IncludesShieldAbsorptionFields()
    {
        var run = CyberDefenseRunState.InitialRun();
        var res1 = new CyberDefenseCombatTransitionResult(
            false, 0, 0, 0, 4, 0, 0, 0, 0, false, false, false, run,
            isShieldAbsorbed: true, absorbedEnemyDamage: 4);

        var res2 = new CyberDefenseCombatTransitionResult(
            false, 0, 0, 0, 4, 0, 0, 0, 0, false, false, false, run,
            isShieldAbsorbed: true, absorbedEnemyDamage: 4);

        var res3 = new CyberDefenseCombatTransitionResult(
            false, 0, 0, 0, 4, 4, 0, 0, 0, false, false, false, run,
            isShieldAbsorbed: false, absorbedEnemyDamage: 0);

        Assert.Equal(res1, res2);
        Assert.Equal(res1.GetHashCode(), res2.GetHashCode());
        Assert.NotEqual(res1, res3);
    }

    // =========================================================================
    // 3. LEGACY ZERO-SHIELD BEHAVIOR UNCHANGED
    // =========================================================================

    [Fact]
    public void LegacyZeroShield_IncorrectAttempt_DealsFullDamage_NoShieldAbsorption()
    {
        var run = CyberDefenseRunState.InitialRun(); // capacity 0

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1);

        Assert.False(result.IsCorrect);
        Assert.False(result.IsShieldAbsorbed);
        Assert.Equal(0, result.AbsorbedEnemyDamage);
        Assert.Equal(4, result.IncomingEnemyDamage);
        Assert.Equal(4, result.AppliedPlayerDamage);
        Assert.Equal(0, result.ExcessEnemyDamage);
        Assert.Equal(96, result.NextState.PlayerCurrentHp);
        Assert.Equal(0, result.NextState.ShieldCapacity);
        Assert.Equal(0, result.NextState.ActiveShields);
        Assert.Equal(0, result.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public void LegacyZeroShield_CorrectAttempt_AdvancesOpponent_NoRechargeAccumulation()
    {
        var run = CyberDefenseRunState.InitialRun(); // capacity 0

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.True(result.IsCorrect);
        Assert.False(result.IsShieldAbsorbed);
        Assert.Equal(0, result.AbsorbedEnemyDamage);
        Assert.Equal(0, result.NextState.ShieldCapacity);
        Assert.Equal(0, result.NextState.ActiveShields);
        Assert.Equal(0, result.NextState.ShieldRechargeUnits);
    }

    // =========================================================================
    // 4. SHIELD ABSORPTION (NORMAL ENEMIES & BOSSES)
    // =========================================================================

    [Fact]
    public void Absorption_NormalEnemy_AbsorbsFullDamage_ConsumesOneShield_NoHpLoss()
    {
        // Sector 1: Normal enemy damage = 4.
        var run = CyberDefenseRunState.InitialRun(shieldCapacity: 1);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1);

        Assert.False(result.IsCorrect);
        Assert.True(result.IsShieldAbsorbed);
        Assert.Equal(4, result.IncomingEnemyDamage);
        Assert.Equal(4, result.AbsorbedEnemyDamage);
        Assert.Equal(0, result.AppliedPlayerDamage);
        Assert.Equal(0, result.ExcessEnemyDamage);
        Assert.False(result.IsGameOver);

        // Player HP unchanged (100)
        Assert.Equal(100, result.NextState.PlayerCurrentHp);
        // Consumed 1 shield (1 -> 0)
        Assert.Equal(0, result.NextState.ActiveShields);
        Assert.Equal(1, result.NextState.ShieldCapacity);
        // Incorrect answer adds +1 recharge unit after shield consumption
        Assert.Equal(1, result.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public void Absorption_BossEnemy_AbsorbsFullDamage_ConsumesOneShield_NoHpLoss()
    {
        // Sector 1: Boss index = 5, Boss damage = 6 (BossEnemyBaseDamage).
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp);
        var run = CyberDefenseRunState.CreateActive(1, bossIndex, 10, boss, shieldCapacity: 2, activeShields: 2, shieldRechargeUnits: 0);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1);

        Assert.False(result.IsCorrect);
        Assert.True(result.IsShieldAbsorbed);
        Assert.Equal(6, result.IncomingEnemyDamage);
        Assert.Equal(6, result.AbsorbedEnemyDamage);
        Assert.Equal(0, result.AppliedPlayerDamage);
        Assert.Equal(0, result.ExcessEnemyDamage);
        Assert.False(result.IsGameOver);

        // Player HP unchanged (10)
        Assert.Equal(10, result.NextState.PlayerCurrentHp);
        // Consumed 1 shield (2 -> 1)
        Assert.Equal(1, result.NextState.ActiveShields);
        Assert.Equal(2, result.NextState.ShieldCapacity);
        // Incorrect answer adds +1 recharge unit
        Assert.Equal(1, result.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public void Absorption_LethalEnemyDamage_DoesNotCauseGameOver_WhenShieldIsActive()
    {
        // Player HP is 2, incoming damage is 4. Without shield, this would be game over.
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(1, 0, 2, opponent, shieldCapacity: 1, activeShields: 1, shieldRechargeUnits: 0);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1);

        Assert.False(result.IsGameOver);
        Assert.Null(result.TerminalSnapshot);
        Assert.True(result.IsShieldAbsorbed);
        Assert.Equal(4, result.AbsorbedEnemyDamage);
        Assert.Equal(0, result.AppliedPlayerDamage);
        Assert.Equal(2, result.NextState.PlayerCurrentHp);
    }

    [Fact]
    public void Absorption_ConsumesExactlyOneShieldPerAttack()
    {
        var run = CyberDefenseRunState.InitialRun(shieldCapacity: 3);

        // Attempt 1: 3 -> 2 active shields
        var res1 = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1);
        Assert.True(res1.IsShieldAbsorbed);
        Assert.Equal(2, res1.NextState.ActiveShields);
        Assert.Equal(1, res1.NextState.ShieldRechargeUnits);

        // Attempt 2: 2 -> 1 active shields
        var res2 = CyberDefenseStateMachine.ApplyAttempt(res1.NextState, isCorrect: false, effectiveAttackDamage: 1);
        Assert.True(res2.IsShieldAbsorbed);
        Assert.Equal(1, res2.NextState.ActiveShields);
        Assert.Equal(2, res2.NextState.ShieldRechargeUnits);

        // Attempt 3: 1 -> 0 active shields
        var res3 = CyberDefenseStateMachine.ApplyAttempt(res2.NextState, isCorrect: false, effectiveAttackDamage: 1);
        Assert.True(res3.IsShieldAbsorbed);
        Assert.Equal(0, res3.NextState.ActiveShields);
        Assert.Equal(3, res3.NextState.ShieldRechargeUnits);

        // Attempt 4: 0 shields remaining -> damage taken by player!
        var res4 = CyberDefenseStateMachine.ApplyAttempt(res3.NextState, isCorrect: false, effectiveAttackDamage: 1);
        Assert.False(res4.IsShieldAbsorbed);
        Assert.Equal(0, res4.AbsorbedEnemyDamage);
        Assert.Equal(4, res4.AppliedPlayerDamage);
        Assert.Equal(96, res4.NextState.PlayerCurrentHp);
        Assert.Equal(0, res4.NextState.ActiveShields);
        Assert.Equal(4, res4.NextState.ShieldRechargeUnits);
    }

    // =========================================================================
    // 5. RECHARGE RULES & THRESHOLDS
    // =========================================================================

    [Fact]
    public void Recharge_CorrectAttempt_AddsTwoUnits_WhenShieldMissing()
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(1, 0, 100, opponent, shieldCapacity: 2, activeShields: 1, shieldRechargeUnits: 10);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.Equal(1, result.NextState.ActiveShields);
        Assert.Equal(12, result.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public void Recharge_IncorrectAttempt_AddsOneUnit_WhenShieldMissing()
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(1, 0, 100, opponent, shieldCapacity: 2, activeShields: 0, shieldRechargeUnits: 10);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1);

        // Shield was 0 -> took damage (100 -> 96), incorrect answer adds +1 recharge (10 -> 11)
        Assert.False(result.IsShieldAbsorbed);
        Assert.Equal(96, result.NextState.PlayerCurrentHp);
        Assert.Equal(0, result.NextState.ActiveShields);
        Assert.Equal(11, result.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public void Recharge_NoAccumulation_WhenShieldsAreFull()
    {
        var run = CyberDefenseRunState.InitialRun(shieldCapacity: 2);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.Equal(2, result.NextState.ActiveShields);
        Assert.Equal(0, result.NextState.ShieldRechargeUnits);
    }

    [Theory]
    [InlineData(28, 1, 0, 29)] // Below threshold (29 units) -> 0 shields, 29 units
    [InlineData(28, 2, 1, 0)]  // Reaches threshold (30 units) at capacity -> restores 1 shield, 0 units
    public void Recharge_Threshold_Behavior_BelowAndAtThirtyUnits(
        int startUnits,
        int attackDelta,
        int expectedActive,
        int expectedUnits)
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(1, 0, 100, opponent, shieldCapacity: 1, activeShields: 0, shieldRechargeUnits: startUnits);

        bool isCorrect = (attackDelta == 2);
        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: isCorrect, effectiveAttackDamage: 1);

        Assert.Equal(expectedActive, result.NextState.ActiveShields);
        Assert.Equal(expectedUnits, result.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public void Recharge_ThirtyOneUnits_CarriesSurplus_WhenAnotherShieldIsStillMissing()
    {
        // Capacity = 3, Active = 1 (missing 2 shields), Recharge = 29.
        // Correct attempt adds +2 -> 31 total units.
        // Restores 1 shield -> Active becomes 2 (< 3).
        // Surplus is carried -> Recharge becomes 1.
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(1, 0, 100, opponent, shieldCapacity: 3, activeShields: 1, shieldRechargeUnits: 29);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.Equal(2, result.NextState.ActiveShields);
        Assert.Equal(1, result.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public void Recharge_ThirtyOneUnits_DiscardsSurplus_WhenReachingFullCapacity()
    {
        // Capacity = 1, Active = 0, Recharge = 29.
        // Correct attempt adds +2 -> 31 total units.
        // Restores 1 shield -> Active becomes 1 (== Capacity).
        // At full capacity, surplus is discarded -> Recharge becomes 0.
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(1, 0, 100, opponent, shieldCapacity: 1, activeShields: 0, shieldRechargeUnits: 29);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.Equal(1, result.NextState.ActiveShields);
        Assert.Equal(0, result.NextState.ShieldRechargeUnits);
    }

    // =========================================================================
    // 6. RECHARGE PRESERVATION ACROSS TRANSITIONS
    // =========================================================================

    [Fact]
    public void Recharge_MaintainedAcrossOpponentDefeat()
    {
        // Normal opponent with 1 HP. Correct attack of 1 defeats it.
        var opponent = new OpponentState(OpponentKind.Normal, 2, 1);
        var run = CyberDefenseRunState.CreateActive(1, 0, 100, opponent, shieldCapacity: 2, activeShields: 0, shieldRechargeUnits: 14);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.True(result.IsOpponentDefeated);
        Assert.Equal(1, result.NextState.OpponentIndex);
        Assert.Equal(0, result.NextState.ActiveShields);
        Assert.Equal(16, result.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public void Recharge_MaintainedAcrossBossDefeatAndSectorTransition()
    {
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, 1);
        var run = CyberDefenseRunState.CreateActive(1, bossIndex, 100, boss, shieldCapacity: 3, activeShields: 1, shieldRechargeUnits: 20);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.True(result.IsOpponentDefeated);
        Assert.True(result.IsSectorCompleted);
        Assert.Equal(2, result.NextState.Sector);
        Assert.Equal(0, result.NextState.OpponentIndex);
        Assert.Equal(1, result.NextState.ActiveShields);
        Assert.Equal(22, result.NextState.ShieldRechargeUnits);
    }

    // =========================================================================
    // 7. GAME-OVER AND TERMINAL SNAPSHOT
    // =========================================================================

    [Fact]
    public void GameOver_PreservesConfiguredCapacity_ResetsActiveToCapacity_ResetsRechargeToZero()
    {
        // Player at 4 HP, no active shields, incoming damage = 4 (lethal).
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(1, 0, 4, opponent, shieldCapacity: 2, activeShields: 0, shieldRechargeUnits: 25);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1);

        Assert.True(result.IsGameOver);
        Assert.NotNull(result.TerminalSnapshot);
        Assert.Equal(0, result.TerminalSnapshot.PlayerCurrentHp);
        Assert.Equal(4, result.AppliedPlayerDamage);
        Assert.Equal(0, result.ExcessEnemyDamage);
        Assert.False(result.IsShieldAbsorbed);
        Assert.Equal(0, result.AbsorbedEnemyDamage);

        // Reboot state: Sector 1, Index 0, 100 HP, Capacity preserved (2), Active reset to 2, Recharge reset to 0.
        Assert.Equal(1, result.NextState.Sector);
        Assert.Equal(0, result.NextState.OpponentIndex);
        Assert.Equal(100, result.NextState.PlayerCurrentHp);
        Assert.Equal(2, result.NextState.ShieldCapacity);
        Assert.Equal(2, result.NextState.ActiveShields);
        Assert.Equal(0, result.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public void GameOver_DoesNotGrantPostGameOverRecharge()
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(1, 0, 2, opponent, shieldCapacity: 3, activeShields: 0, shieldRechargeUnits: 10);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1);

        Assert.True(result.IsGameOver);
        Assert.Equal(0, result.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public void OldLethalExcessAccounting_UnchangedWithNoShields()
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(1, 0, 1, opponent, shieldCapacity: 0, activeShields: 0, shieldRechargeUnits: 0);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1);

        Assert.True(result.IsGameOver);
        Assert.Equal(4, result.IncomingEnemyDamage);
        Assert.Equal(1, result.AppliedPlayerDamage);
        Assert.Equal(3, result.ExcessEnemyDamage);
        Assert.False(result.IsShieldAbsorbed);
        Assert.Equal(0, result.AbsorbedEnemyDamage);
    }
}
