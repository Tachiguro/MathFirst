using System.Reflection;
using System.Runtime.CompilerServices;
using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseStateMachinePropertyTests
{
    // =========================================================================
    // 1. BOUNDED 10-SECTOR DETERMINISTIC SIMULATION (ONE-HIT OVERKILL)
    // =========================================================================

    [Fact]
    public void DeterministicSimulation_10Sectors_OneHitDefeat_ProgressesAccuratelyWithoutSplashDamage()
    {
        var run = CyberDefenseRunState.InitialRun();
        const int effectiveAttackDamage = 10000;
        int totalTransitions = 0;
        const int maxStepBudget = 200;

        for (int expectedSector = 1; expectedSector <= 10; expectedSector++)
        {
            int normalCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(expectedSector);
            int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(expectedSector);

            for (int expectedOpponentIndex = 0; expectedOpponentIndex <= bossIndex; expectedOpponentIndex++)
            {
                totalTransitions++;
                Assert.True(totalTransitions <= maxStepBudget, $"Step budget exceeded at transition {totalTransitions}.");

                // Invariant: Current state matches expected position
                Assert.Equal(expectedSector, run.Sector);
                Assert.Equal(expectedOpponentIndex, run.OpponentIndex);
                Assert.Equal(100, run.PlayerCurrentHp);

                OpponentKind expectedKind = expectedOpponentIndex == bossIndex
                    ? OpponentKind.Boss
                    : OpponentKind.Normal;
                int expectedMaxHp = expectedOpponentIndex == bossIndex
                    ? CyberDefenseScalingPolicy.GetBossMaxHp(expectedSector)
                    : CyberDefenseScalingPolicy.GetNormalMaxHp(expectedSector);

                Assert.Equal(expectedKind, run.CurrentOpponent.Kind);
                Assert.Equal(expectedMaxHp, run.CurrentOpponent.MaxHp);
                Assert.Equal(expectedMaxHp, run.CurrentOpponent.CurrentHp);

                // Execute confirmed correct attempt
                var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: effectiveAttackDamage);

                // Invariants on result
                Assert.True(result.IsCorrect);
                Assert.Equal(effectiveAttackDamage, result.RequestedAttackDamage);
                Assert.Equal(expectedMaxHp, result.AppliedOpponentDamage);
                Assert.Equal(effectiveAttackDamage - expectedMaxHp, result.ExcessOpponentDamage);
                Assert.Equal(0, result.IncomingEnemyDamage);
                Assert.Equal(0, result.AppliedPlayerDamage);
                Assert.Equal(0, result.ExcessEnemyDamage);
                Assert.True(result.IsOpponentDefeated);
                Assert.False(result.IsGameOver);
                Assert.Null(result.TerminalSnapshot);

                int expectedPotentialHealing = expectedKind == OpponentKind.Boss ? 6 : 2;
                Assert.Equal(expectedPotentialHealing, result.PotentialHealing);
                Assert.Equal(0, result.AppliedHealing); // Player HP already 100

                bool isBoss = expectedOpponentIndex == bossIndex;
                Assert.Equal(isBoss, result.IsSectorCompleted);

                if (isBoss)
                {
                    Assert.Equal(expectedSector + 1, result.NextState.Sector);
                    Assert.Equal(0, result.NextState.OpponentIndex);
                    Assert.Equal(OpponentKind.Normal, result.NextState.CurrentOpponent.Kind);
                    int nextSectorFirstOpponentHp = CyberDefenseScalingPolicy.GetNormalMaxHp(expectedSector + 1);
                    Assert.Equal(nextSectorFirstOpponentHp, result.NextState.CurrentOpponent.CurrentHp);
                    Assert.Equal(nextSectorFirstOpponentHp, result.NextState.CurrentOpponent.MaxHp);
                }
                else
                {
                    Assert.Equal(expectedSector, result.NextState.Sector);
                    Assert.Equal(expectedOpponentIndex + 1, result.NextState.OpponentIndex);
                    OpponentKind nextKind = expectedOpponentIndex + 1 == bossIndex ? OpponentKind.Boss : OpponentKind.Normal;
                    int nextOpponentHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(expectedSector, expectedOpponentIndex + 1);
                    Assert.Equal(nextKind, result.NextState.CurrentOpponent.Kind);
                    Assert.Equal(nextOpponentHp, result.NextState.CurrentOpponent.CurrentHp);
                    Assert.Equal(nextOpponentHp, result.NextState.CurrentOpponent.MaxHp);
                }

                Assert.Equal(100, result.NextState.PlayerCurrentHp);

                // Advance to next state
                run = result.NextState;
            }
        }

        Assert.Equal(11, run.Sector);
        Assert.Equal(0, run.OpponentIndex);
        Assert.Equal(79, totalTransitions); // Exact sum of total opponents across sectors 1..10
    }

    // =========================================================================
    // 2. MULTI-HIT STEP-BY-STEP PROGRESSION SIMULATION
    // =========================================================================

    [Fact]
    public void DeterministicSimulation_3Sectors_MultiHitIncrementalDamage_ProgressesAccurately()
    {
        var run = CyberDefenseRunState.InitialRun();
        const int effectiveAttackDamage = 1;
        int totalHits = 0;
        const int maxStepBudget = 500;

        for (int expectedSector = 1; expectedSector <= 3; expectedSector++)
        {
            int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(expectedSector);

            for (int expectedOpponentIndex = 0; expectedOpponentIndex <= bossIndex; expectedOpponentIndex++)
            {
                int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(expectedSector, expectedOpponentIndex);

                for (int hp = maxHp; hp >= 1; hp--)
                {
                    totalHits++;
                    Assert.True(totalHits <= maxStepBudget, $"Step budget exceeded at hit {totalHits}.");

                    Assert.Equal(expectedSector, run.Sector);
                    Assert.Equal(expectedOpponentIndex, run.OpponentIndex);
                    Assert.Equal(hp, run.CurrentOpponent.CurrentHp);

                    var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: effectiveAttackDamage);

                    Assert.True(result.IsCorrect);
                    Assert.Equal(1, result.RequestedAttackDamage);
                    Assert.Equal(1, result.AppliedOpponentDamage);
                    Assert.Equal(0, result.ExcessOpponentDamage);
                    Assert.Equal(0, result.IncomingEnemyDamage);
                    Assert.Equal(0, result.AppliedPlayerDamage);
                    Assert.Equal(0, result.ExcessEnemyDamage);
                    Assert.False(result.IsGameOver);
                    Assert.Null(result.TerminalSnapshot);

                    if (hp > 1)
                    {
                        // Nonlethal partial hit
                        Assert.False(result.IsOpponentDefeated);
                        Assert.False(result.IsSectorCompleted);
                        Assert.Equal(0, result.PotentialHealing);
                        Assert.Equal(0, result.AppliedHealing);
                        Assert.Equal(expectedSector, result.NextState.Sector);
                        Assert.Equal(expectedOpponentIndex, result.NextState.OpponentIndex);
                        Assert.Equal(hp - 1, result.NextState.CurrentOpponent.CurrentHp);
                    }
                    else
                    {
                        // Lethal hit
                        Assert.True(result.IsOpponentDefeated);
                        bool isBoss = expectedOpponentIndex == bossIndex;
                        Assert.Equal(isBoss, result.IsSectorCompleted);

                        if (isBoss)
                        {
                            Assert.Equal(expectedSector + 1, result.NextState.Sector);
                            Assert.Equal(0, result.NextState.OpponentIndex);
                            Assert.Equal(CyberDefenseScalingPolicy.GetNormalMaxHp(expectedSector + 1), result.NextState.CurrentOpponent.CurrentHp);
                        }
                        else
                        {
                            Assert.Equal(expectedSector, result.NextState.Sector);
                            Assert.Equal(expectedOpponentIndex + 1, result.NextState.OpponentIndex);
                            Assert.Equal(CyberDefenseScalingPolicy.GetOpponentMaxHp(expectedSector, expectedOpponentIndex + 1), result.NextState.CurrentOpponent.CurrentHp);
                        }
                    }

                    run = result.NextState;
                }
            }
        }

        Assert.Equal(4, run.Sector);
        Assert.Equal(0, run.OpponentIndex);
    }

    // =========================================================================
    // 3. ATTACK DAMAGE VARIATION & OVERKILL INVARIANTS
    // =========================================================================

    [Theory]
    [InlineData(1, 10, 1, 0, false)]
    [InlineData(2, 10, 2, 0, false)]
    [InlineData(3, 10, 3, 0, false)]
    [InlineData(10, 10, 10, 0, true)]
    [InlineData(15, 10, 10, 5, true)]
    [InlineData(50, 10, 10, 40, true)]
    [InlineData(int.MaxValue, 10, 10, int.MaxValue - 10, true)]
    public void AttackDamageVariation_CorrectAnswerDamageAccounting_EnforcesExactAppliedAndExcessFormulas(
        int attackDamage,
        int currentHp,
        int expectedApplied,
        int expectedExcess,
        bool expectedDefeated)
    {
        var run = CyberDefenseRunState.Rehydrate(sector: 1024, opponentIndex: 0, opponentCurrentHp: currentHp, playerCurrentHp: 80);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: attackDamage);

        Assert.True(result.IsCorrect);
        Assert.Equal(attackDamage, result.RequestedAttackDamage);
        Assert.Equal(expectedApplied, result.AppliedOpponentDamage);
        Assert.Equal(expectedExcess, result.ExcessOpponentDamage);
        Assert.Equal(0, result.IncomingEnemyDamage);
        Assert.Equal(0, result.AppliedPlayerDamage);
        Assert.Equal(0, result.ExcessEnemyDamage);
        Assert.Equal(expectedDefeated, result.IsOpponentDefeated);
        Assert.False(result.IsGameOver);
        Assert.Null(result.TerminalSnapshot);

        if (!expectedDefeated)
        {
            Assert.Equal(currentHp - attackDamage, result.NextState.CurrentOpponent.CurrentHp);
            Assert.Equal(80, result.NextState.PlayerCurrentHp);
        }
        else
        {
            // Normal enemy defeat awards 2 HP healing
            Assert.Equal(82, result.NextState.PlayerCurrentHp);
            Assert.Equal(2, result.AppliedHealing);
        }
    }

    // =========================================================================
    // 4. INCORRECT ANSWER INVARIANTS (NONLETHAL COUNTER-DAMAGE)
    // =========================================================================

    [Theory]
    [InlineData(1, 0, 100, 4, 96)]
    [InlineData(1, 5, 100, 6, 94)]
    [InlineData(50, 0, 80, 5, 75)]
    [InlineData(50, 10, 80, 7, 73)]
    [InlineData(100, 0, 50, 6, 44)]
    [InlineData(100, 11, 50, 8, 42)]
    public void IncorrectAnswerInvariants_NonlethalCounterDamage_LeavesOpponentUntouchedAndDepletesPlayerHp(
        int sector,
        int opponentIndex,
        int initialPlayerHp,
        int expectedEnemyDamage,
        int expectedNextPlayerHp)
    {
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, opponentIndex);
        int currentHp = Math.Max(1, maxHp - 1);
        var run = CyberDefenseRunState.Rehydrate(sector, opponentIndex, opponentCurrentHp: currentHp, playerCurrentHp: initialPlayerHp);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 99);

        Assert.False(result.IsCorrect);
        Assert.Equal(0, result.RequestedAttackDamage);
        Assert.Equal(0, result.AppliedOpponentDamage);
        Assert.Equal(0, result.ExcessOpponentDamage);
        Assert.Equal(expectedEnemyDamage, result.IncomingEnemyDamage);
        Assert.Equal(expectedEnemyDamage, result.AppliedPlayerDamage);
        Assert.Equal(0, result.ExcessEnemyDamage);
        Assert.Equal(0, result.PotentialHealing);
        Assert.Equal(0, result.AppliedHealing);
        Assert.False(result.IsOpponentDefeated);
        Assert.False(result.IsSectorCompleted);
        Assert.False(result.IsGameOver);
        Assert.Null(result.TerminalSnapshot);

        // Opponent state must be strictly unchanged
        Assert.Equal(sector, result.NextState.Sector);
        Assert.Equal(opponentIndex, result.NextState.OpponentIndex);
        Assert.Equal(run.CurrentOpponent.Kind, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(run.CurrentOpponent.MaxHp, result.NextState.CurrentOpponent.MaxHp);
        Assert.Equal(currentHp, result.NextState.CurrentOpponent.CurrentHp);

        // Player HP must be exactly reduced
        Assert.Equal(expectedNextPlayerHp, result.NextState.PlayerCurrentHp);
    }

    // =========================================================================
    // 5. TERMINAL INVARIANTS, FATAL DAMAGE & AUTOMATIC REBOOT
    // =========================================================================

    [Theory]
    [InlineData(1, 0, 4, 4, 0)]          // Normal enemy exact lethal: HP 4, Dmg 4 -> Applied 4, Excess 0
    [InlineData(1, 0, 3, 4, 1)]          // Normal enemy overkill: HP 3, Dmg 4 -> Applied 3, Excess 1
    [InlineData(1, 5, 6, 6, 0)]          // Boss exact lethal: HP 6, Dmg 6 -> Applied 6, Excess 0
    [InlineData(1, 5, 2, 6, 4)]          // Boss overkill: HP 2, Dmg 6 -> Applied 2, Excess 4
    [InlineData(100, 0, 5, 6, 1)]        // Sector 100 Normal: HP 5, Dmg 6 -> Applied 5, Excess 1
    [InlineData(100, 11, 8, 8, 0)]       // Sector 100 Boss: HP 8, Dmg 8 -> Applied 8, Excess 0
    [InlineData(10000, 0, 100, 204, 104)]// Sector 10000 Normal: HP 100, Dmg 204 -> Applied 100, Excess 104
    [InlineData(10000, 18, 100, 206, 106)]// Sector 10000 Boss: HP 100, Dmg 206 -> Applied 100, Excess 106
    public void FatalCounterDamage_AcrossDiverseScenarios_GeneratesValidTerminalSnapshotAndReboots(
        int sector,
        int opponentIndex,
        int initialPlayerHp,
        int expectedIncomingDamage,
        int expectedExcessEnemyDamage)
    {
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, opponentIndex);
        int currentHp = Math.Max(1, maxHp / 2);
        var run = CyberDefenseRunState.Rehydrate(sector, opponentIndex, opponentCurrentHp: currentHp, playerCurrentHp: initialPlayerHp);

        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 100);

        Assert.False(result.IsCorrect);
        Assert.Equal(0, result.RequestedAttackDamage);
        Assert.Equal(0, result.AppliedOpponentDamage);
        Assert.Equal(0, result.ExcessOpponentDamage);
        Assert.Equal(expectedIncomingDamage, result.IncomingEnemyDamage);
        Assert.Equal(initialPlayerHp, result.AppliedPlayerDamage);
        Assert.Equal(expectedExcessEnemyDamage, result.ExcessEnemyDamage);
        Assert.Equal(0, result.PotentialHealing);
        Assert.Equal(0, result.AppliedHealing);
        Assert.False(result.IsOpponentDefeated);
        Assert.False(result.IsSectorCompleted);
        Assert.True(result.IsGameOver);

        // Terminal snapshot checks
        Assert.NotNull(result.TerminalSnapshot);
        Assert.Equal(sector, result.TerminalSnapshot.Sector);
        Assert.Equal(opponentIndex, result.TerminalSnapshot.OpponentIndex);
        Assert.Equal(run.CurrentOpponent.Kind, result.TerminalSnapshot.Kind);
        Assert.Equal(currentHp, result.TerminalSnapshot.OpponentCurrentHp);
        Assert.Equal(maxHp, result.TerminalSnapshot.OpponentMaxHp);
        Assert.Equal(0, result.TerminalSnapshot.PlayerCurrentHp);

        // NextState must be fresh initial run
        Assert.Equal(CyberDefenseRunState.InitialRun(), result.NextState);
        Assert.Equal(1, result.NextState.Sector);
        Assert.Equal(0, result.NextState.OpponentIndex);
        Assert.Equal(100, result.NextState.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Normal, result.NextState.CurrentOpponent.Kind);
        Assert.Equal(2, result.NextState.CurrentOpponent.CurrentHp);
        Assert.Equal(2, result.NextState.CurrentOpponent.MaxHp);

        // Immutability of input run
        Assert.Equal(sector, run.Sector);
        Assert.Equal(opponentIndex, run.OpponentIndex);
        Assert.Equal(initialPlayerHp, run.PlayerCurrentHp);
        Assert.Equal(currentHp, run.CurrentOpponent.CurrentHp);
    }

    // =========================================================================
    // 6. REPRODUCIBLE MIXED SEQUENCE COVERAGE
    // =========================================================================

    [Fact]
    public void ReproducibleMixedSequence_DeterministicReplay_ProducesBitIdenticalOutcomes()
    {
        // 30 predetermined combat inputs: (isCorrect, attackDamage)
        var sequence = new (bool IsCorrect, int AttackDamage)[]
        {
            (true, 1),   // S1 N0 (2/2 -> 1/2)
            (true, 1),   // S1 N0 (1/2 -> 0/2, defeated, N1 spawned)
            (false, 5),  // S1 N1 (counterattack: -4 HP -> 96 HP)
            (false, 5),  // S1 N1 (counterattack: -4 HP -> 92 HP)
            (true, 10),  // S1 N1 (1-hit kill, +2 heal -> 94 HP, N2 spawned)
            (true, 2),   // S1 N2 (1-hit kill, +2 heal -> 96 HP, N3 spawned)
            (true, 2),   // S1 N3 (1-hit kill, +2 heal -> 98 HP, N4 spawned)
            (true, 2),   // S1 N4 (1-hit kill, +2 heal -> 100 HP, Boss spawned)
            (false, 1),  // S1 Boss (counterattack: -6 HP -> 94 HP)
            (true, 6),   // S1 Boss (12/12 -> 6/12)
            (true, 6),   // S1 Boss (6/12 -> 0/12, defeated, +6 heal -> 100 HP, S2 N0 spawned)
            (true, 2),   // S2 N0 (1-hit kill, N1 spawned)
            (false, 1),  // S2 N1 (-4 HP -> 96 HP)
            (false, 1),  // S2 N1 (-4 HP -> 92 HP)
            (false, 1),  // S2 N1 (-4 HP -> 88 HP)
            (false, 1),  // S2 N1 (-4 HP -> 84 HP)
            (false, 1),  // S2 N1 (-4 HP -> 80 HP)
            (false, 1),  // S2 N1 (-4 HP -> 76 HP)
            (false, 1),  // S2 N1 (-4 HP -> 72 HP)
            (false, 1),  // S2 N1 (-4 HP -> 68 HP)
            (false, 1),  // S2 N1 (-4 HP -> 64 HP)
            (false, 1),  // S2 N1 (-4 HP -> 60 HP)
            (false, 1),  // S2 N1 (-4 HP -> 56 HP)
            (false, 1),  // S2 N1 (-4 HP -> 52 HP)
            (false, 1),  // S2 N1 (-4 HP -> 48 HP)
            (false, 1),  // S2 N1 (-4 HP -> 44 HP)
            (false, 1),  // S2 N1 (-4 HP -> 40 HP)
            (false, 1),  // S2 N1 (-4 HP -> 36 HP)
            (false, 1),  // S2 N1 (-4 HP -> 32 HP)
            (false, 1)   // S2 N1 (-4 HP -> 28 HP)
        };

        var firstPassResults = RunSimulationSequence(sequence);
        var secondPassResults = RunSimulationSequence(sequence);

        Assert.Equal(sequence.Length, firstPassResults.Count);
        Assert.Equal(sequence.Length, secondPassResults.Count);

        for (int i = 0; i < sequence.Length; i++)
        {
            var res1 = firstPassResults[i];
            var res2 = secondPassResults[i];

            Assert.Equal(res1.IsCorrect, res2.IsCorrect);
            Assert.Equal(res1.RequestedAttackDamage, res2.RequestedAttackDamage);
            Assert.Equal(res1.AppliedOpponentDamage, res2.AppliedOpponentDamage);
            Assert.Equal(res1.ExcessOpponentDamage, res2.ExcessOpponentDamage);
            Assert.Equal(res1.IncomingEnemyDamage, res2.IncomingEnemyDamage);
            Assert.Equal(res1.AppliedPlayerDamage, res2.AppliedPlayerDamage);
            Assert.Equal(res1.ExcessEnemyDamage, res2.ExcessEnemyDamage);
            Assert.Equal(res1.PotentialHealing, res2.PotentialHealing);
            Assert.Equal(res1.AppliedHealing, res2.AppliedHealing);
            Assert.Equal(res1.IsOpponentDefeated, res2.IsOpponentDefeated);
            Assert.Equal(res1.IsSectorCompleted, res2.IsSectorCompleted);
            Assert.Equal(res1.IsGameOver, res2.IsGameOver);
            Assert.Equal(res1.NextState, res2.NextState);
            Assert.Equal(res1.TerminalSnapshot, res2.TerminalSnapshot);
            Assert.Equal(res1, res2);
        }
    }

    private static List<CyberDefenseCombatTransitionResult> RunSimulationSequence((bool IsCorrect, int AttackDamage)[] sequence)
    {
        var results = new List<CyberDefenseCombatTransitionResult>(sequence.Length);
        var run = CyberDefenseRunState.InitialRun();

        foreach (var (isCorrect, attackDamage) in sequence)
        {
            var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect, attackDamage);
            results.Add(result);
            run = result.NextState;
        }

        return results;
    }

    // =========================================================================
    // 7. HIGH-SECTOR NUMERIC BOUNDARY SAFETY
    // =========================================================================

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(1024)]
    [InlineData(10000)]
    public void HighSectorNumericBoundary_SingleTransitions_ExecuteSafely(int sector)
    {
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);

        // Correct nonlethal hit on boss
        var runBoss = CyberDefenseRunState.Rehydrate(sector, bossIndex, opponentCurrentHp: bossMaxHp, playerCurrentHp: 50);
        var resHit = CyberDefenseStateMachine.ApplyAttempt(runBoss, isCorrect: true, effectiveAttackDamage: 1);
        Assert.False(resHit.IsOpponentDefeated);
        Assert.False(resHit.IsSectorCompleted);
        Assert.Equal(bossMaxHp - 1, resHit.NextState.CurrentOpponent.CurrentHp);

        // Correct lethal hit on boss -> sector advances
        var resBossKill = CyberDefenseStateMachine.ApplyAttempt(runBoss, isCorrect: true, effectiveAttackDamage: bossMaxHp);
        Assert.True(resBossKill.IsOpponentDefeated);
        Assert.True(resBossKill.IsSectorCompleted);
        Assert.Equal(sector + 1, resBossKill.NextState.Sector);
        Assert.Equal(0, resBossKill.NextState.OpponentIndex);
        Assert.Equal(56, resBossKill.NextState.PlayerCurrentHp); // 50 + 6 heal

        // Counterattack on normal enemy
        var runNormal = CyberDefenseRunState.Rehydrate(sector, 0, opponentCurrentHp: 1, playerCurrentHp: 100);
        var resCounter = CyberDefenseStateMachine.ApplyAttempt(runNormal, isCorrect: false, effectiveAttackDamage: 1);
        int expectedDmg = CyberDefenseCombatPolicy.GetEnemyDamage(sector, OpponentKind.Normal);
        if (expectedDmg >= 100)
        {
            Assert.True(resCounter.IsGameOver);
            Assert.NotNull(resCounter.TerminalSnapshot);
            Assert.Equal(CyberDefenseRunState.InitialRun(), resCounter.NextState);
        }
        else
        {
            Assert.False(resCounter.IsGameOver);
            Assert.Equal(100 - expectedDmg, resCounter.NextState.PlayerCurrentHp);
        }
    }

    [Fact]
    public void HighSectorNumericBoundary_SectorIntMaxValue_PreservesDefensiveOverflowContracts()
    {
        const int maxSector = int.MaxValue;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(maxSector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(maxSector);

        // Normal opponent defeat advances index within int.MaxValue
        var runNormal = CyberDefenseRunState.Rehydrate(maxSector, 0, opponentCurrentHp: 1, playerCurrentHp: 100);
        var resNormalDefeat = CyberDefenseStateMachine.ApplyAttempt(runNormal, isCorrect: true, effectiveAttackDamage: 1);
        Assert.True(resNormalDefeat.IsOpponentDefeated);
        Assert.False(resNormalDefeat.IsSectorCompleted);
        Assert.Equal(maxSector, resNormalDefeat.NextState.Sector);
        Assert.Equal(1, resNormalDefeat.NextState.OpponentIndex);

        // Boss defeat at int.MaxValue throws checked OverflowException
        var runBoss = CyberDefenseRunState.Rehydrate(maxSector, bossIndex, opponentCurrentHp: 1, playerCurrentHp: 100);
        Assert.Throws<OverflowException>(() =>
            CyberDefenseStateMachine.ApplyAttempt(runBoss, isCorrect: true, effectiveAttackDamage: 1));

        // Boss partial damage at int.MaxValue is fully supported
        var runBossFull = CyberDefenseRunState.Rehydrate(maxSector, bossIndex, opponentCurrentHp: bossMaxHp, playerCurrentHp: 100);
        var resBossPartial = CyberDefenseStateMachine.ApplyAttempt(runBossFull, isCorrect: true, effectiveAttackDamage: 10);
        Assert.False(resBossPartial.IsOpponentDefeated);
        Assert.Equal(bossMaxHp - 10, resBossPartial.NextState.CurrentOpponent.CurrentHp);

        // Fatal hit at int.MaxValue reboots cleanly without integer overflow
        var resFatal = CyberDefenseStateMachine.ApplyAttempt(runNormal, isCorrect: false, effectiveAttackDamage: 1);
        Assert.True(resFatal.IsGameOver);
        Assert.NotNull(resFatal.TerminalSnapshot);
        Assert.Equal(maxSector, resFatal.TerminalSnapshot.Sector);
        Assert.Equal(CyberDefenseRunState.InitialRun(), resFatal.NextState);
    }

    // =========================================================================
    // 8. TRANSITION RESULT CONSTRUCTOR INVARIANT AUDIT
    // =========================================================================

    [Fact]
    public void TransitionResultConstructor_ThrowsOnNullNextState()
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
    public void TransitionResultConstructor_ThrowsOnNegativeNumbers(
        int reqAtk, int appOpp, int excOpp, int incEnm, int appPly, int excEnm, int potHeal, int appHeal)
    {
        var run = CyberDefenseRunState.InitialRun();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: reqAtk,
                appliedOpponentDamage: appOpp,
                excessOpponentDamage: excOpp,
                incomingEnemyDamage: incEnm,
                appliedPlayerDamage: appPly,
                excessEnemyDamage: excEnm,
                potentialHealing: potHeal,
                appliedHealing: appHeal,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run));
    }

    [Fact]
    public void TransitionResultConstructor_ThrowsWhenAppliedOpponentDamageExceedsRequested()
    {
        var run = CyberDefenseRunState.InitialRun();
        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: 5,
                appliedOpponentDamage: 6,
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
    public void TransitionResultConstructor_ThrowsWhenExcessOpponentDamageIsArithmeticallyInconsistent()
    {
        var run = CyberDefenseRunState.InitialRun();
        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: 10,
                appliedOpponentDamage: 4,
                excessOpponentDamage: 5, // Expected 6
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
    public void TransitionResultConstructor_ThrowsWhenAppliedPlayerDamageExceedsIncomingEnemyDamage()
    {
        var run = CyberDefenseRunState.InitialRun();
        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: false,
                requestedAttackDamage: 0,
                appliedOpponentDamage: 0,
                excessOpponentDamage: 0,
                incomingEnemyDamage: 4,
                appliedPlayerDamage: 5,
                excessEnemyDamage: 0,
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run));
    }

    [Fact]
    public void TransitionResultConstructor_ThrowsWhenExcessEnemyDamageIsArithmeticallyInconsistent()
    {
        var run = CyberDefenseRunState.InitialRun();
        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: false,
                requestedAttackDamage: 0,
                appliedOpponentDamage: 0,
                excessOpponentDamage: 0,
                incomingEnemyDamage: 10,
                appliedPlayerDamage: 4,
                excessEnemyDamage: 5, // Expected 6
                potentialHealing: 0,
                appliedHealing: 0,
                isOpponentDefeated: false,
                isSectorCompleted: false,
                isGameOver: false,
                nextState: run));
    }

    [Fact]
    public void TransitionResultConstructor_ThrowsWhenAppliedHealingExceedsPotentialHealing()
    {
        var run = CyberDefenseRunState.InitialRun();
        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseCombatTransitionResult(
                isCorrect: true,
                requestedAttackDamage: 2,
                appliedOpponentDamage: 2,
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
    public void TransitionResultConstructor_ThrowsWhenSectorIsCompletedWithoutOpponentDefeat()
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
                isSectorCompleted: true, // Inconsistent
                isGameOver: false,
                nextState: run));
    }

    // =========================================================================
    // 9. DETERMINISM AND IMMUTABILITY VERIFICATION
    // =========================================================================

    [Fact]
    public void Determinism_MultipleInvocationsOnSameRun_ProduceIdenticalTransitionResults()
    {
        var run = CyberDefenseRunState.Rehydrate(sector: 10, opponentIndex: 2, opponentCurrentHp: 3, playerCurrentHp: 80);

        var resultA = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        var resultB = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.Equal(resultA, resultB);
        Assert.Equal(resultA.NextState, resultB.NextState);
        Assert.Equal(resultA.TerminalSnapshot, resultB.TerminalSnapshot);
    }

    [Theory]
    [InlineData(typeof(CyberDefenseCombatTransitionResult))]
    [InlineData(typeof(CyberDefenseTerminalRunSnapshot))]
    [InlineData(typeof(CyberDefenseRunState))]
    [InlineData(typeof(OpponentState))]
    public void DomainStateTypes_ExposeNoMutableSettersOrInitAccessors(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(properties);

        foreach (var property in properties)
        {
            Assert.True(property.CanRead, $"{property.Name} must be readable.");
            var setMethod = property.GetSetMethod(nonPublic: true);
            if (setMethod != null)
            {
                var isInitOnly = setMethod.ReturnParameter
                    .GetRequiredCustomModifiers()
                    .Contains(typeof(IsExternalInit));
                Assert.False(isInitOnly, $"{property.Name} should not have init-only setter.");
                Assert.False(property.CanWrite, $"{property.Name} should not have setter.");
            }
        }
    }
}
