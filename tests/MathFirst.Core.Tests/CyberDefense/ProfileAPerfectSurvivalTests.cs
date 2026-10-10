namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Numerics;
using MathFirst.Core.Tests.CyberDefense.Simulator;
using MathFirst.Domain.CyberDefense;
using Xunit;

/// <summary>
/// Authoritative integration-proof test suite for MF-CYBER-004-SLICE-6:
/// Profile A Perfect Survival Proof Through Sector 1000.
/// Proves deterministic survival of Profile A with 100% accuracy, base attack damage 1,
/// zero upgrades, zero game-overs, zero player damage, and exact telemetry coherence.
/// </summary>
public class ProfileAPerfectSurvivalTests
{
    private const ulong CanonicalMasterSeed = 0xC0FFEEUL;

    // =========================================================================
    // 1. INDEPENDENT MATHEMATICAL REFERENCE ORACLE VERIFICATION
    // =========================================================================

    [Fact]
    public void AnalyticalOracle_AcrossSectors1Through999_DerivesExactExpectedTotals()
    {
        long totalNormalTurns = 0;
        long totalBossTurns = 0;
        int totalNormalOpponents = 0;
        int totalBossOpponents = 0;

        for (int sector = 1; sector <= 999; sector++)
        {
            int normalCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(sector);
            int normalHp = CyberDefenseScalingPolicy.GetNormalMaxHp(sector);
            int bossHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);

            // Cross-check scaling policy with independent closed-form formulas
            int refNormalCount = 5 + BitOperations.Log2((uint)sector);
            int refNormalHp = (int)(Math.Floor(Math.Sqrt(sector + 15.0)) - 2.0);
            int refBossHp = (int)(Math.Floor(Math.Sqrt(36.0 * (sector + 15.0))) - 12.0);

            Assert.Equal(refNormalCount, normalCount);
            Assert.Equal(refNormalHp, normalHp);
            Assert.Equal(refBossHp, bossHp);

            // With effective attack damage 1, required attempts equal total opponent HP
            totalNormalTurns += (long)normalCount * normalHp;
            totalBossTurns += bossHp;

            totalNormalOpponents += normalCount;
            totalBossOpponents += 1;
        }

        long totalTurns = totalNormalTurns + totalBossTurns;
        int totalOpponents = totalNormalOpponents + totalBossOpponents;

        // Exact analytical reference oracle values
        Assert.Equal(372516L, totalTurns);
        Assert.Equal(255999L, totalNormalTurns);
        Assert.Equal(116517L, totalBossTurns);
        Assert.Equal(12973, totalNormalOpponents);
        Assert.Equal(999, totalBossOpponents);
        Assert.Equal(13972, totalOpponents);
        Assert.Equal(447019200L, totalTurns * 1200L);
    }

    // =========================================================================
    // 2. FULL SECTOR 1000 SURVIVAL PROOF (SCENARIO A)
    // =========================================================================

    [Fact]
    public void Sector1000_ProfileA_PerfectSurvivalProof()
    {
        // Arrange: Profile A, InitialRun starting state, Sector 1000 target, exact 372,516 turn budget
        var config = new SimulationRunConfig(
            profile: SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: CanonicalMasterSeed,
            maxTurns: 372516,
            targetSector: 1000,
            maxGameOvers: 1,
            startingState: CyberDefenseRunState.InitialRun());

        // Act: Execute authoritative headless combat simulation
        SimulationExecutionResult result = HeadlessCombatSimulator.Run(config);

        // Assert 1: Exact termination boundary and turn budget
        Assert.Equal(SimulationTerminationReason.TargetSectorReached, result.TerminationReason);
        Assert.Equal(372516L, result.TotalExecutedTurns);
        Assert.Equal(372516L, result.ExecutedTurns);
        Assert.Equal(0, result.TotalGameOvers);
        Assert.Equal(0, result.GameOvers);
        Assert.Equal(447019200L, result.TotalVirtualTimeMs);
        Assert.Equal(447019200L, result.VirtualElapsedMilliseconds);
        Assert.Null(result.LastTerminalSnapshot);
        Assert.Null(result.TerminalSnapshot);

        // Assert 2: Final authoritative combat state
        CyberDefenseRunState finalState = result.FinalState;
        Assert.NotNull(finalState);
        Assert.Equal(1000, finalState.Sector);
        Assert.Equal(0, finalState.OpponentIndex);
        Assert.Equal(100, finalState.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Normal, finalState.CurrentOpponent.Kind);
        Assert.Equal(finalState.CurrentOpponent.MaxHp, finalState.CurrentOpponent.CurrentHp);
        Assert.Equal(CyberDefenseScalingPolicy.GetNormalMaxHp(1000), finalState.CurrentOpponent.MaxHp);

        // Assert 3: Telemetry identity and correctness
        SimulationRunTelemetry telemetry = result.Telemetry;
        Assert.NotNull(telemetry);
        Assert.Equal(SyntheticPlayerProfiles.ProfileAId, telemetry.ProfileId);
        Assert.Equal(SyntheticPlayerProfiles.ProfileA_Perfect.Name, telemetry.ProfileName);
        Assert.Equal(CanonicalMasterSeed, telemetry.MasterSeed);

        // Assert 4: Telemetry execution metrics
        Assert.Equal(372516L, telemetry.TotalQuestions);
        Assert.Equal(372516L, telemetry.Questions);
        Assert.Equal(372516L, telemetry.CorrectAnswers);
        Assert.Equal(372516L, telemetry.CorrectCount);
        Assert.Equal(0L, telemetry.IncorrectAnswers);
        Assert.Equal(0L, telemetry.IncorrectCount);
        Assert.NotNull(telemetry.AccuracyPercent);
        Assert.Equal(100.0, telemetry.AccuracyPercent.Value);
        Assert.Equal(1000, telemetry.HighestSectorReached);
        Assert.Equal(0, telemetry.TotalGameOvers);
        Assert.Equal(0, telemetry.GameOvers);

        // Assert 5: Telemetry damage accounting
        Assert.Equal(372516L, telemetry.TotalAppliedOpponentDamage);
        Assert.Equal(372516L, telemetry.AppliedOpponentDamage);
        Assert.Equal(0L, telemetry.TotalAppliedPlayerDamage);
        Assert.Equal(0L, telemetry.AppliedPlayerDamage);

        // Assert 6: Telemetry opponent defeat and sector completion counts
        Assert.Equal(13972, telemetry.TotalOpponentDefeats);
        Assert.Equal(13972, telemetry.OpponentDefeats);
        Assert.Equal(999, telemetry.TotalSectorCompletions);
        Assert.Equal(999, telemetry.SectorCompletions);

        // Assert 7: Telemetry virtual time
        Assert.Equal(447019200L, telemetry.TotalVirtualTimeMs);
        Assert.Equal(447019200L, telemetry.VirtualElapsedMilliseconds);

        // Assert 8: Internal result-to-telemetry coherence
        Assert.Equal(result.TotalExecutedTurns, telemetry.TotalQuestions);
        Assert.Equal(result.TotalGameOvers, telemetry.TotalGameOvers);
        Assert.Equal(result.TotalVirtualTimeMs, telemetry.TotalVirtualTimeMs);

        // Assert 9: Explicit deferred feature metrics remain unavailable
        Assert.Null(telemetry.FinalPlayerLevel);
        Assert.Null(telemetry.PlayerLevel);
        Assert.Null(telemetry.TotalXp);
        Assert.Null(telemetry.Xp);
        Assert.Null(telemetry.SkillPointsEarned);
        Assert.Null(telemetry.SkillPointsSpent);
        Assert.Null(telemetry.AttackRanks);
        Assert.Null(telemetry.FirewallRanks);
        Assert.Null(telemetry.CriticalStrikeRanks);
        Assert.Null(telemetry.OverdriveRanks);
        Assert.Null(telemetry.TotalShieldsAbsorbed);
        Assert.Null(telemetry.ShieldsAbsorbed);
        Assert.Null(telemetry.ReplayDistanceAfterDefeat);
    }

    // =========================================================================
    // 3. BOUNDARY PRECEDENCE VERIFICATION (SCENARIO B)
    // =========================================================================

    [Fact]
    public void TerminationPrecedence_SelectsTargetSectorReached_WhenTargetAndTurnBudgetCoincide()
    {
        // In Sector 1: 5 normal opponents (2 HP each) + 1 boss (12 HP) = 22 turns at 1 attack damage.
        // On turn 22, the boss is defeated, sector becomes 2, and turn budget (22) is reached simultaneously.
        var configCoinciding = new SimulationRunConfig(
            profile: SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: CanonicalMasterSeed,
            maxTurns: 22,
            targetSector: 2,
            startingState: CyberDefenseRunState.InitialRun());

        var resultCoinciding = HeadlessCombatSimulator.Run(configCoinciding);

        // TargetSectorReached must take precedence over MaxTurnsReached
        Assert.Equal(SimulationTerminationReason.TargetSectorReached, resultCoinciding.TerminationReason);
        Assert.Equal(22L, resultCoinciding.TotalExecutedTurns);
        Assert.Equal(2, resultCoinciding.FinalState.Sector);
        Assert.Equal(0, resultCoinciding.FinalState.OpponentIndex);
        Assert.Equal(100, resultCoinciding.FinalState.PlayerCurrentHp);

        // Conversely, at 21 turns, the turn budget exhausts before reaching Sector 2 (boss has 1 HP left)
        var configMaxTurns = new SimulationRunConfig(
            profile: SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: CanonicalMasterSeed,
            maxTurns: 21,
            targetSector: 2,
            startingState: CyberDefenseRunState.InitialRun());

        var resultMaxTurns = HeadlessCombatSimulator.Run(configMaxTurns);

        Assert.Equal(SimulationTerminationReason.MaxTurnsReached, resultMaxTurns.TerminationReason);
        Assert.Equal(21L, resultMaxTurns.TotalExecutedTurns);
        Assert.Equal(1, resultMaxTurns.FinalState.Sector);
        Assert.Equal(5, resultMaxTurns.FinalState.OpponentIndex);
        Assert.Equal(OpponentKind.Boss, resultMaxTurns.FinalState.CurrentOpponent.Kind);
        Assert.Equal(1, resultMaxTurns.FinalState.CurrentOpponent.CurrentHp);
        Assert.Equal(12, resultMaxTurns.FinalState.CurrentOpponent.MaxHp);
        Assert.Equal(100, resultMaxTurns.FinalState.PlayerCurrentHp);
    }

    // =========================================================================
    // 4. SMALL-SECTOR ANALYTICAL BOUNDARY VERIFICATION (SCENARIO C)
    // =========================================================================

    [Fact]
    public void SmallSectorBoundary_Sector1Through2_CompletesCleanlyAtExactBudget()
    {
        // Sector 1 has 5 normal opponents (HP 2 each) = 10 turns.
        // Sector 1 boss has 12 HP = 12 turns. Total = 22 turns.
        var config = new SimulationRunConfig(
            profile: SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 12345UL,
            maxTurns: 22,
            targetSector: 2,
            maxGameOvers: 1,
            startingState: CyberDefenseRunState.InitialRun());

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(SimulationTerminationReason.TargetSectorReached, result.TerminationReason);
        Assert.Equal(22L, result.TotalExecutedTurns);
        Assert.Equal(0, result.TotalGameOvers);
        Assert.Equal(2, result.FinalState.Sector);
        Assert.Equal(0, result.FinalState.OpponentIndex);
        Assert.Equal(100, result.FinalState.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Normal, result.FinalState.CurrentOpponent.Kind);
        Assert.Equal(result.FinalState.CurrentOpponent.MaxHp, result.FinalState.CurrentOpponent.CurrentHp);
        Assert.Null(result.LastTerminalSnapshot);

        SimulationRunTelemetry telemetry = result.Telemetry;
        Assert.Equal(22L, telemetry.TotalQuestions);
        Assert.Equal(22L, telemetry.CorrectAnswers);
        Assert.Equal(0L, telemetry.IncorrectAnswers);
        Assert.Equal(100.0, telemetry.AccuracyPercent);
        Assert.Equal(2, telemetry.HighestSectorReached);
        Assert.Equal(0, telemetry.TotalGameOvers);
        Assert.Equal(22L, telemetry.TotalAppliedOpponentDamage);
        Assert.Equal(0L, telemetry.TotalAppliedPlayerDamage);
        Assert.Equal(6, telemetry.TotalOpponentDefeats); // 5 normal + 1 boss
        Assert.Equal(1, telemetry.TotalSectorCompletions);
        Assert.Equal(22L * 1200L, telemetry.TotalVirtualTimeMs);
    }

    // =========================================================================
    // 5. DETERMINISTIC REPLAY CONSISTENCY (SCENARIO D)
    // =========================================================================

    [Fact]
    public void DeterministicReplay_ProducesExactBitForBitIdenticalTelemetryAndState()
    {
        const ulong seed = 0xDEADBEEFUL;
        var config1 = new SimulationRunConfig(
            profile: SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: seed,
            maxTurns: 100,
            targetSector: 5,
            startingState: CyberDefenseRunState.InitialRun());

        var config2 = new SimulationRunConfig(
            profile: SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: seed,
            maxTurns: 100,
            targetSector: 5,
            startingState: CyberDefenseRunState.InitialRun());

        var result1 = HeadlessCombatSimulator.Run(config1);
        var result2 = HeadlessCombatSimulator.Run(config2);

        Assert.Equal(result1.TerminationReason, result2.TerminationReason);
        Assert.Equal(result1.TotalExecutedTurns, result2.TotalExecutedTurns);
        Assert.Equal(result1.TotalGameOvers, result2.TotalGameOvers);
        Assert.Equal(result1.TotalVirtualTimeMs, result2.TotalVirtualTimeMs);

        // State equivalence
        Assert.Equal(result1.FinalState.Sector, result2.FinalState.Sector);
        Assert.Equal(result1.FinalState.OpponentIndex, result2.FinalState.OpponentIndex);
        Assert.Equal(result1.FinalState.PlayerCurrentHp, result2.FinalState.PlayerCurrentHp);
        Assert.Equal(result1.FinalState.CurrentOpponent.Kind, result2.FinalState.CurrentOpponent.Kind);
        Assert.Equal(result1.FinalState.CurrentOpponent.MaxHp, result2.FinalState.CurrentOpponent.MaxHp);
        Assert.Equal(result1.FinalState.CurrentOpponent.CurrentHp, result2.FinalState.CurrentOpponent.CurrentHp);

        // Telemetry equivalence
        Assert.Equal(result1.Telemetry.TotalQuestions, result2.Telemetry.TotalQuestions);
        Assert.Equal(result1.Telemetry.CorrectAnswers, result2.Telemetry.CorrectAnswers);
        Assert.Equal(result1.Telemetry.IncorrectAnswers, result2.Telemetry.IncorrectAnswers);
        Assert.Equal(result1.Telemetry.AccuracyPercent, result2.Telemetry.AccuracyPercent);
        Assert.Equal(result1.Telemetry.TotalAppliedOpponentDamage, result2.Telemetry.TotalAppliedOpponentDamage);
        Assert.Equal(result1.Telemetry.TotalAppliedPlayerDamage, result2.Telemetry.TotalAppliedPlayerDamage);
        Assert.Equal(result1.Telemetry.TotalOpponentDefeats, result2.Telemetry.TotalOpponentDefeats);
        Assert.Equal(result1.Telemetry.TotalSectorCompletions, result2.Telemetry.TotalSectorCompletions);
    }

    // =========================================================================
    // 6. PROFILE A CONTRACT & UPGRADE-FREE VERIFICATION (SCENARIO E)
    // =========================================================================

    [Fact]
    public void ProfileA_Contract_EnforcesBaselineAttackDamageAndUpgradeFreeStatus()
    {
        SyntheticPlayerProfile profile = SyntheticPlayerProfiles.ProfileA_Perfect;

        Assert.Equal("ProfileA_Perfect", profile.Id);
        Assert.Equal("Profile A (Perfect)", profile.Name);
        Assert.Equal(1.00, profile.AccuracyProbability);
        Assert.Equal(1200, profile.MinSyntheticLatencyMilliseconds);
        Assert.Equal(1200, profile.MaxSyntheticLatencyMilliseconds);
        Assert.Equal(1, profile.EffectiveAttackDamage);
        Assert.Equal(SyntheticUpgradePreference.None, profile.UpgradePreference);
        Assert.False(profile.IsUpgradeStrategyExecutable);

        // Verify that simulation turns deal exactly 1 damage per turn
        int turnCount = 0;
        var config = new SimulationRunConfig(
            profile: profile,
            masterSeed: CanonicalMasterSeed,
            maxTurns: 10,
            onTurnCompleted: transition =>
            {
                turnCount++;
                Assert.True(transition.IsCorrect);
                Assert.Equal(1, transition.RequestedAttackDamage);
                Assert.Equal(1, transition.AppliedOpponentDamage);
                Assert.Equal(0, transition.ExcessOpponentDamage);
                Assert.Equal(0, transition.IncomingEnemyDamage);
                Assert.Equal(0, transition.AppliedPlayerDamage);
            });

        var result = HeadlessCombatSimulator.Run(config);
        Assert.Equal(10, turnCount);
        Assert.Equal(10L, result.TotalExecutedTurns);
        Assert.Equal(10L, result.Telemetry.TotalAppliedOpponentDamage);
    }

    // =========================================================================
    // 7. EXPLICIT UNAVAILABLE TELEMETRY METRICS (SCENARIO F)
    // =========================================================================

    [Fact]
    public void DeferredTelemetryMetrics_RemainExplicitlyUnavailable_AndNeverDefaultToZero()
    {
        var config = new SimulationRunConfig(
            profile: SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: CanonicalMasterSeed,
            maxTurns: 5);

        var result = HeadlessCombatSimulator.Run(config);
        SimulationRunTelemetry telemetry = result.Telemetry;

        // All 10 deferred feature metrics must be null, never 0
        Assert.Null(telemetry.FinalPlayerLevel);
        Assert.Null(telemetry.PlayerLevel);
        Assert.Null(telemetry.TotalXp);
        Assert.Null(telemetry.Xp);
        Assert.Null(telemetry.SkillPointsEarned);
        Assert.Null(telemetry.SkillPointsSpent);
        Assert.Null(telemetry.AttackRanks);
        Assert.Null(telemetry.FirewallRanks);
        Assert.Null(telemetry.CriticalStrikeRanks);
        Assert.Null(telemetry.OverdriveRanks);
        Assert.Null(telemetry.TotalShieldsAbsorbed);
        Assert.Null(telemetry.ShieldsAbsorbed);
        Assert.Null(telemetry.ReplayDistanceAfterDefeat);
    }
}
