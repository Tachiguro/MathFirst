namespace MathFirst.Core.Tests.CyberDefense;

using System;
using MathFirst.Core.Tests.CyberDefense.Simulator;
using MathFirst.Domain.CyberDefense;
using Xunit;

public class HeadlessSimulatorEngineContractTests
{
    // =========================================================================
    // A. CORRECT ANSWER, NONFATAL HIT
    // =========================================================================

    [Fact]
    public void Step_CorrectNonfatalHit_NormalOpponent_ReducesHpWithoutDefeatOrGameOver()
    {
        // Sector 1 normal opponent has MaxHp = 2, CurrentHp = 2.
        var run = CyberDefenseRunState.InitialRun();
        int attackDamage = 1;

        var expected = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: attackDamage);
        var actual = HeadlessCombatSimulator.Step(run, isCorrect: true, effectiveAttackDamage: attackDamage);

        AssertEquivalence(expected, actual);
        Assert.Equal(1, actual.AppliedOpponentDamage);
        Assert.Equal(0, actual.ExcessOpponentDamage);
        Assert.Equal(1, actual.NextState.CurrentOpponent.CurrentHp);
        Assert.Equal(run.PlayerCurrentHp, actual.NextState.PlayerCurrentHp);
        Assert.False(actual.IsOpponentDefeated);
        Assert.False(actual.IsSectorCompleted);
        Assert.False(actual.IsGameOver);
        Assert.Null(actual.TerminalSnapshot);
    }

    [Fact]
    public void Step_CorrectNonfatalHit_BossOpponent_PreservesSectorAndOpponentIndex()
    {
        int sector = 1;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 100, boss);
        int attackDamage = 3;

        var expected = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: attackDamage);
        var actual = HeadlessCombatSimulator.Step(run, isCorrect: true, effectiveAttackDamage: attackDamage);

        AssertEquivalence(expected, actual);
        Assert.Equal(3, actual.AppliedOpponentDamage);
        Assert.Equal(0, actual.ExcessOpponentDamage);
        Assert.Equal(bossMaxHp - 3, actual.NextState.CurrentOpponent.CurrentHp);
        Assert.Equal(bossIndex, actual.NextState.OpponentIndex);
        Assert.Equal(sector, actual.NextState.Sector);
        Assert.False(actual.IsOpponentDefeated);
        Assert.False(actual.IsSectorCompleted);
        Assert.False(actual.IsGameOver);
    }

    [Fact]
    public void ApplyAttempt_Alias_ProducesIdenticalResultToStep()
    {
        var run = CyberDefenseRunState.InitialRun();
        int attackDamage = 1;

        var stepResult = HeadlessCombatSimulator.Step(run, isCorrect: true, effectiveAttackDamage: attackDamage);
        var applyResult = HeadlessCombatSimulator.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: attackDamage);

        AssertEquivalence(stepResult, applyResult);
    }

    // =========================================================================
    // B. CORRECT ANSWER, OPPONENT DEFEAT
    // =========================================================================

    [Fact]
    public void Step_CorrectOpponentDefeat_NormalOpponent_HealsPlayerAndAdvancesOpponentIndex()
    {
        // Normal defeat in Sector 1: Normal defeat healing is 2 HP.
        int initialPlayerHp = 90;
        var run = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: initialPlayerHp,
            currentOpponent: new OpponentState(OpponentKind.Normal, 2, 2));

        int attackDamage = 2;

        var expected = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: attackDamage);
        var actual = HeadlessCombatSimulator.Step(run, isCorrect: true, effectiveAttackDamage: attackDamage);

        AssertEquivalence(expected, actual);
        Assert.True(actual.IsOpponentDefeated);
        Assert.False(actual.IsSectorCompleted);
        Assert.False(actual.IsGameOver);
        Assert.Equal(2, actual.AppliedHealing);
        Assert.Equal(initialPlayerHp + 2, actual.NextState.PlayerCurrentHp);
        Assert.Equal(1, actual.NextState.OpponentIndex);
        Assert.Equal(1, actual.NextState.Sector);
        Assert.Equal(2, actual.NextState.CurrentOpponent.CurrentHp);
        Assert.Equal(2, actual.NextState.CurrentOpponent.MaxHp);
    }

    [Fact]
    public void Step_CorrectOpponentDefeat_PlayerAtMaxHp_ClampsHealingToZero()
    {
        var run = CyberDefenseRunState.InitialRun(); // Player HP = 100 (Max)
        int attackDamage = 2;

        var expected = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: attackDamage);
        var actual = HeadlessCombatSimulator.Step(run, isCorrect: true, effectiveAttackDamage: attackDamage);

        AssertEquivalence(expected, actual);
        Assert.True(actual.IsOpponentDefeated);
        Assert.Equal(2, actual.PotentialHealing);
        Assert.Equal(0, actual.AppliedHealing);
        Assert.Equal(100, actual.NextState.PlayerCurrentHp);
    }

    // =========================================================================
    // C. CORRECT ANSWER, BOSS DEFEAT
    // =========================================================================

    [Fact]
    public void Step_CorrectBossDefeat_AdvancesSectorAndResetsOpponentIndex()
    {
        int sector = 1;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        int initialPlayerHp = 80;

        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, initialPlayerHp, boss);
        int attackDamage = bossMaxHp;

        var expected = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: attackDamage);
        var actual = HeadlessCombatSimulator.Step(run, isCorrect: true, effectiveAttackDamage: attackDamage);

        AssertEquivalence(expected, actual);
        Assert.True(actual.IsOpponentDefeated);
        Assert.True(actual.IsSectorCompleted);
        Assert.False(actual.IsGameOver);
        Assert.Equal(6, actual.AppliedHealing); // Boss healing = 6
        Assert.Equal(initialPlayerHp + 6, actual.NextState.PlayerCurrentHp);
        Assert.Equal(2, actual.NextState.Sector);
        Assert.Equal(0, actual.NextState.OpponentIndex);
        Assert.Equal(OpponentKind.Normal, actual.NextState.CurrentOpponent.Kind);
    }

    // =========================================================================
    // D. INCORRECT ANSWER, NONFATAL COUNTERATTACK
    // =========================================================================

    [Fact]
    public void Step_IncorrectNonfatalCounterattack_NormalOpponent_DeductsEnemyDamage()
    {
        var run = CyberDefenseRunState.InitialRun(); // HP = 100. Normal Sector 1 damage = 4.
        int attackDamage = 1;

        var expected = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: attackDamage);
        var actual = HeadlessCombatSimulator.Step(run, isCorrect: false, effectiveAttackDamage: attackDamage);

        AssertEquivalence(expected, actual);
        Assert.False(actual.IsCorrect);
        Assert.Equal(4, actual.IncomingEnemyDamage);
        Assert.Equal(4, actual.AppliedPlayerDamage);
        Assert.Equal(0, actual.ExcessEnemyDamage);
        Assert.Equal(96, actual.NextState.PlayerCurrentHp);
        Assert.False(actual.IsGameOver);
        Assert.Null(actual.TerminalSnapshot);
    }

    [Fact]
    public void Step_IncorrectNonfatalCounterattack_BossOpponent_DeductsBossDamage()
    {
        int sector = 1;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 50, boss);
        int attackDamage = 1;

        // Sector 1 boss damage = 6.
        var expected = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: attackDamage);
        var actual = HeadlessCombatSimulator.Step(run, isCorrect: false, effectiveAttackDamage: attackDamage);

        AssertEquivalence(expected, actual);
        Assert.Equal(6, actual.IncomingEnemyDamage);
        Assert.Equal(6, actual.AppliedPlayerDamage);
        Assert.Equal(0, actual.ExcessEnemyDamage);
        Assert.Equal(44, actual.NextState.PlayerCurrentHp);
        Assert.False(actual.IsGameOver);
    }

    // =========================================================================
    // E. INCORRECT ANSWER, FATAL COUNTERATTACK
    // =========================================================================

    [Theory]
    [InlineData(4, 4, 0)] // Exact fatal: incoming 4, player HP 4, excess 0
    [InlineData(3, 4, 1)] // Excess fatal: incoming 4, player HP 3, excess 1
    [InlineData(1, 4, 3)] // Excess fatal: incoming 4, player HP 1, excess 3
    public void Step_IncorrectFatalCounterattack_NormalOpponent_RebootsRunAndCapturesTerminalSnapshot(
        int playerHp,
        int expectedIncomingDamage,
        int expectedExcessDamage)
    {
        var normalOpponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = CyberDefenseRunState.CreateActive(1, 0, playerHp, normalOpponent);
        int attackDamage = 1;

        var expected = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: attackDamage);
        var actual = HeadlessCombatSimulator.Step(run, isCorrect: false, effectiveAttackDamage: attackDamage);

        AssertEquivalence(expected, actual);
        Assert.True(actual.IsGameOver);
        Assert.Equal(expectedIncomingDamage, actual.IncomingEnemyDamage);
        Assert.Equal(playerHp, actual.AppliedPlayerDamage);
        Assert.Equal(expectedExcessDamage, actual.ExcessEnemyDamage);

        Assert.NotNull(actual.TerminalSnapshot);
        Assert.Equal(1, actual.TerminalSnapshot.Sector);
        Assert.Equal(0, actual.TerminalSnapshot.OpponentIndex);
        Assert.Equal(OpponentKind.Normal, actual.TerminalSnapshot.Kind);
        Assert.Equal(2, actual.TerminalSnapshot.OpponentCurrentHp);
        Assert.Equal(2, actual.TerminalSnapshot.OpponentMaxHp);
        Assert.Equal(0, actual.TerminalSnapshot.PlayerCurrentHp);

        // NextState must be rebooted to fresh initial run
        Assert.Equal(1, actual.NextState.Sector);
        Assert.Equal(0, actual.NextState.OpponentIndex);
        Assert.Equal(CyberDefenseCombatPolicy.PlayerMaxHp, actual.NextState.PlayerCurrentHp);
    }

    [Fact]
    public void Step_IncorrectFatalCounterattack_BossOpponent_CapturesTerminalSnapshotWithBossState()
    {
        int sector = 1;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        // Boss at 7 HP out of 12, player at 5 HP (incoming boss damage = 6 => excess fatal 1)
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, 7);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 5, boss);

        var expected = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1);
        var actual = HeadlessCombatSimulator.Step(run, isCorrect: false, effectiveAttackDamage: 1);

        AssertEquivalence(expected, actual);
        Assert.True(actual.IsGameOver);
        Assert.NotNull(actual.TerminalSnapshot);
        Assert.Equal(OpponentKind.Boss, actual.TerminalSnapshot.Kind);
        Assert.Equal(7, actual.TerminalSnapshot.OpponentCurrentHp);
        Assert.Equal(12, actual.TerminalSnapshot.OpponentMaxHp);
        Assert.Equal(0, actual.TerminalSnapshot.PlayerCurrentHp);
    }

    // =========================================================================
    // F. OVERKILL ACCOUNTING & NO SPLASH DAMAGE
    // =========================================================================

    [Theory]
    [InlineData(2, 5, 3)]
    [InlineData(2, 50, 48)]
    [InlineData(2, 1000, 998)]
    public void Step_Overkill_NormalOpponent_RecordsExcessDamageWithoutSplashingToNextOpponent(
        int opponentHp,
        int attackDamage,
        int expectedExcess)
    {
        var run = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 100,
            currentOpponent: new OpponentState(OpponentKind.Normal, 2, opponentHp));

        var expected = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: attackDamage);
        var actual = HeadlessCombatSimulator.Step(run, isCorrect: true, effectiveAttackDamage: attackDamage);

        AssertEquivalence(expected, actual);
        Assert.Equal(opponentHp, actual.AppliedOpponentDamage);
        Assert.Equal(expectedExcess, actual.ExcessOpponentDamage);
        Assert.True(actual.IsOpponentDefeated);

        // Next opponent must have full max HP (no splash damage)
        int expectedNextOpponentMaxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(
            actual.NextState.Sector,
            actual.NextState.OpponentIndex);
        Assert.Equal(expectedNextOpponentMaxHp, actual.NextState.CurrentOpponent.CurrentHp);
        Assert.Equal(expectedNextOpponentMaxHp, actual.NextState.CurrentOpponent.MaxHp);
    }

    [Fact]
    public void Step_Overkill_BossOpponent_RecordsExcessDamageWithoutSplashingToNextSector()
    {
        int sector = 1;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, 5);
        var run = CyberDefenseRunState.CreateActive(sector, bossIndex, 100, boss);
        int overkillDamage = 50;

        var expected = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: overkillDamage);
        var actual = HeadlessCombatSimulator.Step(run, isCorrect: true, effectiveAttackDamage: overkillDamage);

        AssertEquivalence(expected, actual);
        Assert.Equal(5, actual.AppliedOpponentDamage);
        Assert.Equal(45, actual.ExcessOpponentDamage);
        Assert.True(actual.IsSectorCompleted);
        Assert.Equal(2, actual.NextState.Sector);
        Assert.Equal(0, actual.NextState.OpponentIndex);

        // First opponent in sector 2 must have full max HP (no splash damage)
        int sector2NormalHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(2, 0);
        Assert.Equal(sector2NormalHp, actual.NextState.CurrentOpponent.CurrentHp);
        Assert.Equal(sector2NormalHp, actual.NextState.CurrentOpponent.MaxHp);
    }

    // =========================================================================
    // G. DOMAIN VALIDATION EQUIVALENCE
    // =========================================================================

    [Fact]
    public void Step_NullRunState_ThrowsArgumentNullExceptionMatchingProduction()
    {
        var prodEx = Assert.Throws<ArgumentNullException>(() =>
            CyberDefenseStateMachine.ApplyAttempt(null!, isCorrect: true, effectiveAttackDamage: 1));

        var simEx = Assert.Throws<ArgumentNullException>(() =>
            HeadlessCombatSimulator.Step(null!, isCorrect: true, effectiveAttackDamage: 1));

        Assert.Equal(prodEx.ParamName, simEx.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    [InlineData(int.MinValue)]
    public void Step_InvalidAttackDamage_ThrowsArgumentOutOfRangeExceptionMatchingProduction(int invalidDamage)
    {
        var run = CyberDefenseRunState.InitialRun();

        var prodEx = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: invalidDamage));

        var simEx = Assert.Throws<ArgumentOutOfRangeException>(() =>
            HeadlessCombatSimulator.Step(run, isCorrect: true, effectiveAttackDamage: invalidDamage));

        Assert.Equal(prodEx.ParamName, simEx.ParamName);
    }

    // =========================================================================
    // H. HIGH SECTOR SCALING EQUIVALENCE
    // =========================================================================

    [Theory]
    [InlineData(10, 0, true, 1)]
    [InlineData(50, 0, false, 1)]
    [InlineData(100, 0, true, 10)]
    public void Step_HigherSectors_MaintainsExactProductionEquivalence(
        int sector,
        int opponentIndex,
        bool isCorrect,
        int damage)
    {
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, opponentIndex);
        var opponent = new OpponentState(OpponentKind.Normal, maxHp, maxHp);
        var run = CyberDefenseRunState.CreateActive(sector, opponentIndex, 100, opponent);

        var expected = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect, damage);
        var actual = HeadlessCombatSimulator.Step(run, isCorrect, damage);

        AssertEquivalence(expected, actual);
    }

    // =========================================================================
    // EQUIVALENCE ASSERTION HELPER
    // =========================================================================

    private static void AssertEquivalence(
        CyberDefenseCombatTransitionResult expected,
        CyberDefenseCombatTransitionResult actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.IsCorrect, actual.IsCorrect);
        Assert.Equal(expected.RequestedAttackDamage, actual.RequestedAttackDamage);
        Assert.Equal(expected.AppliedOpponentDamage, actual.AppliedOpponentDamage);
        Assert.Equal(expected.ExcessOpponentDamage, actual.ExcessOpponentDamage);
        Assert.Equal(expected.IncomingEnemyDamage, actual.IncomingEnemyDamage);
        Assert.Equal(expected.AppliedPlayerDamage, actual.AppliedPlayerDamage);
        Assert.Equal(expected.ExcessEnemyDamage, actual.ExcessEnemyDamage);
        Assert.Equal(expected.PotentialHealing, actual.PotentialHealing);
        Assert.Equal(expected.AppliedHealing, actual.AppliedHealing);
        Assert.Equal(expected.IsOpponentDefeated, actual.IsOpponentDefeated);
        Assert.Equal(expected.IsSectorCompleted, actual.IsSectorCompleted);
        Assert.Equal(expected.IsGameOver, actual.IsGameOver);

        Assert.Equal(expected.NextState.Sector, actual.NextState.Sector);
        Assert.Equal(expected.NextState.OpponentIndex, actual.NextState.OpponentIndex);
        Assert.Equal(expected.NextState.PlayerCurrentHp, actual.NextState.PlayerCurrentHp);
        Assert.Equal(expected.NextState.CurrentOpponent.Kind, actual.NextState.CurrentOpponent.Kind);
        Assert.Equal(expected.NextState.CurrentOpponent.MaxHp, actual.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(expected.NextState.CurrentOpponent.CurrentHp, actual.NextState.CurrentOpponent.CurrentHp);
        Assert.Equal(expected.NextState, actual.NextState);

        if (expected.TerminalSnapshot is null)
        {
            Assert.Null(actual.TerminalSnapshot);
        }
        else
        {
            Assert.NotNull(actual.TerminalSnapshot);
            Assert.Equal(expected.TerminalSnapshot.Sector, actual.TerminalSnapshot.Sector);
            Assert.Equal(expected.TerminalSnapshot.OpponentIndex, actual.TerminalSnapshot.OpponentIndex);
            Assert.Equal(expected.TerminalSnapshot.Kind, actual.TerminalSnapshot.Kind);
            Assert.Equal(expected.TerminalSnapshot.OpponentCurrentHp, actual.TerminalSnapshot.OpponentCurrentHp);
            Assert.Equal(expected.TerminalSnapshot.OpponentMaxHp, actual.TerminalSnapshot.OpponentMaxHp);
            Assert.Equal(expected.TerminalSnapshot.PlayerCurrentHp, actual.TerminalSnapshot.PlayerCurrentHp);
            Assert.Equal(expected.TerminalSnapshot, actual.TerminalSnapshot);
        }
    }
}
