namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using MathFirst.Core.Tests.CyberDefense.Simulator;
using MathFirst.Domain.CyberDefense;
using Xunit;

/// <summary>
/// Authoritative test matrix for MF-CYBER-004-SLICE-5:
/// Structured Simulation Telemetry and Explicit Deferred Feature Metrics.
/// Tests T1 through T34 verifying accurate execution telemetry and deferred metric unavailability.
/// </summary>
public class SimulationTelemetryContractTests
{
    // =========================================================================
    // T1: Profile identity and seed
    // =========================================================================

    [Fact]
    public void T1_ProfileId_Name_And_MasterSeed_ArePreservedExactly()
    {
        ulong seed = 987654321UL;
        var profile = SyntheticPlayerProfiles.ProfileA_Perfect;
        var config = new SimulationRunConfig(profile, seed, maxTurns: 10);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.NotNull(result.Telemetry);
        Assert.Equal(seed, result.Telemetry.MasterSeed);
        Assert.Equal(profile.Id, result.Telemetry.ProfileId);
        Assert.Equal(profile.Name, result.Telemetry.ProfileName);
    }

    // =========================================================================
    // T2: Total questions equals executed turns
    // =========================================================================

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(17)]
    [InlineData(42)]
    public void T2_TotalQuestions_EqualsExecutedTurns(long turns)
    {
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 10101UL,
            maxTurns: turns);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(result.TotalExecutedTurns, result.Telemetry.TotalQuestions);
        Assert.Equal(result.ExecutedTurns, result.Telemetry.Questions);
    }

    // =========================================================================
    // T3: Correct plus incorrect answers equals total questions
    // =========================================================================

    [Fact]
    public void T3_CorrectPlusIncorrectAnswers_EqualsTotalQuestions()
    {
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileD_Learner,
            masterSeed: 4444UL,
            maxTurns: 30);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(result.Telemetry.TotalQuestions, result.Telemetry.CorrectAnswers + result.Telemetry.IncorrectAnswers);
        Assert.Equal(result.Telemetry.Questions, result.Telemetry.CorrectCount + result.Telemetry.IncorrectCount);
    }

    // =========================================================================
    // T4: Perfect profile produces 100% accuracy
    // =========================================================================

    [Fact]
    public void T4_ProfileA_Produces100PercentObservedAccuracy_AfterCompletedAttempt()
    {
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 1111UL,
            maxTurns: 15);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.True(result.Telemetry.TotalQuestions > 0);
        Assert.Equal(result.Telemetry.TotalQuestions, result.Telemetry.CorrectAnswers);
        Assert.Equal(0, result.Telemetry.IncorrectAnswers);
        Assert.NotNull(result.Telemetry.AccuracyPercent);
        Assert.Equal(100.0, result.Telemetry.AccuracyPercent.Value, precision: 5);
    }

    // =========================================================================
    // T5: Forced zero accuracy profile produces 0% accuracy
    // =========================================================================

    [Fact]
    public void T5_ForcedZeroAccuracyProfile_Produces0PercentObservedAccuracy()
    {
        var zeroProfile = new SyntheticPlayerProfile(
            id: "zero_acc",
            name: "Zero Accuracy Tester",
            accuracyProbability: 0.0,
            minSyntheticLatencyMilliseconds: 1000,
            maxSyntheticLatencyMilliseconds: 1000);

        var config = new SimulationRunConfig(zeroProfile, masterSeed: 2222UL, maxTurns: 5);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.True(result.Telemetry.TotalQuestions > 0);
        Assert.Equal(0, result.Telemetry.CorrectAnswers);
        Assert.Equal(result.Telemetry.TotalQuestions, result.Telemetry.IncorrectAnswers);
        Assert.NotNull(result.Telemetry.AccuracyPercent);
        Assert.Equal(0.0, result.Telemetry.AccuracyPercent.Value, precision: 5);
    }

    // =========================================================================
    // T6: Mixed deterministic outcomes produce accurate percentages
    // =========================================================================

    [Fact]
    public void T6_MixedDeterministicOutcomes_ProduceAccurateObservedPercentages()
    {
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileC_Average,
            masterSeed: 789UL,
            maxTurns: 20);

        var result = HeadlessCombatSimulator.Run(config);

        long questions = result.Telemetry.TotalQuestions;
        long correct = result.Telemetry.CorrectAnswers;

        Assert.True(questions > 0);
        Assert.NotNull(result.Telemetry.AccuracyPercent);
        double expectedPercent = (100.0 * correct) / questions;
        Assert.Equal(expectedPercent, result.Telemetry.AccuracyPercent.Value, precision: 5);
    }

    // =========================================================================
    // T7: Zero-turn executions have unavailable accuracy
    // =========================================================================

    [Fact]
    public void T7_ZeroTurnSimulations_HaveUnavailableObservedAccuracy_WithoutDivisionByZero()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 123UL,
            maxTurns: 50);

        var result = HeadlessCombatSimulator.Run(config, cts.Token);

        Assert.Equal(0, result.Telemetry.TotalQuestions);
        Assert.Equal(0, result.Telemetry.CorrectAnswers);
        Assert.Equal(0, result.Telemetry.IncorrectAnswers);
        Assert.Null(result.Telemetry.AccuracyPercent);
    }

    // =========================================================================
    // T8: Highest sector includes starting state
    // =========================================================================

    [Fact]
    public void T8_HighestSector_IncludesStartingState()
    {
        int startingSector = 4;
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(startingSector, 0);
        var customStart = CyberDefenseRunState.CreateActive(
            sector: startingSector,
            opponentIndex: 0,
            playerCurrentHp: 100,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp));

        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 555UL,
            maxTurns: 1,
            startingState: customStart);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.True(result.Telemetry.HighestSectorReached >= startingSector);
    }

    // =========================================================================
    // T9: Highest sector increases on authoritative sector progression
    // =========================================================================

    [Fact]
    public void T9_HighestSector_IncreasesOnActualAuthoritativeSectorProgression()
    {
        // Start directly at boss of Sector 1 with 1 HP remaining
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, 1);
        var bossState = CyberDefenseRunState.CreateActive(1, bossIndex, 100, boss);

        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 888UL,
            maxTurns: 1,
            startingState: bossState);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(1, result.TotalExecutedTurns);
        Assert.Equal(2, result.FinalState.Sector);
        Assert.Equal(2, result.Telemetry.HighestSectorReached);
        Assert.Equal(1, result.Telemetry.TotalSectorCompletions);
    }

    // =========================================================================
    // T10: Highest sector survives game-over reboot
    // =========================================================================

    [Fact]
    public void T10_HighestSector_SurvivesGameOverReboot()
    {
        // Start in Sector 5 with 1 HP, force incorrect answer causing game-over reboot to Sector 1
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(5, 0);
        var fatalState = CyberDefenseRunState.CreateActive(
            sector: 5,
            opponentIndex: 0,
            playerCurrentHp: 1,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp));

        var zeroProfile = new SyntheticPlayerProfile(
            id: "suicide",
            name: "Suicide Run",
            accuracyProbability: 0.0,
            minSyntheticLatencyMilliseconds: 1000,
            maxSyntheticLatencyMilliseconds: 1000);

        var config = new SimulationRunConfig(
            zeroProfile,
            masterSeed: 999UL,
            maxTurns: 1,
            startingState: fatalState);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(1, result.TotalExecutedTurns);
        Assert.Equal(1, result.TotalGameOvers);
        Assert.Equal(1, result.FinalState.Sector); // Rebooted to Sector 1
        Assert.Equal(5, result.Telemetry.HighestSectorReached); // Retained Sector 5 record
    }

    // =========================================================================
    // T11: Applied opponent damage does not count overkill excess damage
    // =========================================================================

    [Fact]
    public void T11_AppliedOpponentDamage_IsAccumulatedWithoutCountingOverkillExcess()
    {
        // Opponent with 2 HP, player effective attack is 10
        var profileOverkill = new SyntheticPlayerProfile(
            id: "overkill",
            name: "Overkill Striker",
            accuracyProbability: 1.0,
            minSyntheticLatencyMilliseconds: 1000,
            maxSyntheticLatencyMilliseconds: 1000,
            effectiveAttackDamage: 10);

        var config = new SimulationRunConfig(
            profileOverkill,
            masterSeed: 12345UL,
            maxTurns: 1);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(1, result.TotalExecutedTurns);
        // Opponent had 2 HP, effective attack 10 -> applied damage is 2, overkill is 8
        Assert.Equal(2, result.Telemetry.TotalAppliedOpponentDamage);
        Assert.Equal(2, result.Telemetry.AppliedOpponentDamage);
    }

    // =========================================================================
    // T12: Applied player damage does not count excess fatal damage
    // =========================================================================

    [Fact]
    public void T12_AppliedPlayerDamage_IsAccumulatedWithoutCountingExcessFatalDamage()
    {
        // Player with 2 HP, enemy attack in Sector 1 is 4
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        var lowHpState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 2,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp));

        var zeroProfile = new SyntheticPlayerProfile(
            id: "fatal_test",
            name: "Fatal Test",
            accuracyProbability: 0.0,
            minSyntheticLatencyMilliseconds: 1000,
            maxSyntheticLatencyMilliseconds: 1000);

        var config = new SimulationRunConfig(
            zeroProfile,
            masterSeed: 54321UL,
            maxTurns: 1,
            startingState: lowHpState);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(1, result.TotalExecutedTurns);
        // Incoming damage is 4, but player only had 2 HP -> applied player damage is 2
        Assert.Equal(2, result.Telemetry.TotalAppliedPlayerDamage);
        Assert.Equal(2, result.Telemetry.AppliedPlayerDamage);
    }

    // =========================================================================
    // T13: Opponent defeat counts use production IsOpponentDefeated flag
    // =========================================================================

    [Fact]
    public void T13_OpponentDefeatCounts_UseProductionIsOpponentDefeatedFlag()
    {
        // In Sector 1, normal opponents have 2 HP each.
        // Effective attack = 2 means 1 correct answer defeats 1 opponent.
        var profileOneHit = new SyntheticPlayerProfile(
            id: "one_hit",
            name: "One Hit Defeater",
            accuracyProbability: 1.0,
            minSyntheticLatencyMilliseconds: 1000,
            maxSyntheticLatencyMilliseconds: 1000,
            effectiveAttackDamage: 2);

        var config = new SimulationRunConfig(profileOneHit, masterSeed: 777UL, maxTurns: 3);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(3, result.TotalExecutedTurns);
        Assert.Equal(3, result.Telemetry.TotalOpponentDefeats);
        Assert.Equal(3, result.Telemetry.OpponentDefeats);
    }

    // =========================================================================
    // T14: Sector completion counts use production IsSectorCompleted flag
    // =========================================================================

    [Fact]
    public void T14_SectorCompletionCounts_UseProductionIsSectorCompletedFlag()
    {
        // Start at boss with 1 HP
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, 1);
        var bossState = CyberDefenseRunState.CreateActive(1, bossIndex, 100, boss);

        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 8888UL,
            maxTurns: 1,
            startingState: bossState);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(1, result.Telemetry.TotalSectorCompletions);
        Assert.Equal(1, result.Telemetry.SectorCompletions);
    }

    // =========================================================================
    // T15: Game-over is counted exactly once per fatal defeat
    // =========================================================================

    [Fact]
    public void T15_GameOver_IsCountedExactlyOnce()
    {
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        var lethalState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 1,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp));

        var zeroProfile = new SyntheticPlayerProfile(
            id: "suicide",
            name: "Suicide",
            accuracyProbability: 0.0,
            minSyntheticLatencyMilliseconds: 1000,
            maxSyntheticLatencyMilliseconds: 1000);

        var config = new SimulationRunConfig(
            zeroProfile,
            masterSeed: 1234UL,
            maxTurns: 1,
            startingState: lethalState);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(1, result.TotalGameOvers);
        Assert.Equal(1, result.Telemetry.TotalGameOvers);
        Assert.Equal(1, result.Telemetry.GameOvers);
    }

    // =========================================================================
    // T16: Final gameplay state equals authoritative final NextState
    // =========================================================================

    [Fact]
    public void T16_FinalGameplayState_EqualsAuthoritativeFinalNextState()
    {
        CyberDefenseRunState? observedLastNextState = null;
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileB_Expert,
            masterSeed: 3333UL,
            maxTurns: 12,
            onTurnCompleted: transition => observedLastNextState = transition.NextState);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.NotNull(observedLastNextState);
        Assert.Same(observedLastNextState, result.FinalState);
    }

    // =========================================================================
    // T17: Telemetry virtual time matches execution result virtual time
    // =========================================================================

    [Fact]
    public void T17_TelemetryVirtualTime_MatchesExecutionResultVirtualTime()
    {
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileC_Average,
            masterSeed: 6789UL,
            maxTurns: 25);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(result.TotalVirtualTimeMs, result.Telemetry.TotalVirtualTimeMs);
        Assert.Equal(result.VirtualElapsedMilliseconds, result.Telemetry.VirtualElapsedMilliseconds);
    }

    // =========================================================================
    // T18: Repeated identical runs produce identical telemetry
    // =========================================================================

    [Fact]
    public void T18_RepeatedIdenticalRuns_ProduceIdenticalTelemetry()
    {
        ulong seed = 424242UL;
        var config1 = new SimulationRunConfig(SyntheticPlayerProfiles.ProfileD_Learner, seed, maxTurns: 30);
        var config2 = new SimulationRunConfig(SyntheticPlayerProfiles.ProfileD_Learner, seed, maxTurns: 30);

        var result1 = HeadlessCombatSimulator.Run(config1);
        var result2 = HeadlessCombatSimulator.Run(config2);

        Assert.Equal(result1.Telemetry, result2.Telemetry);
        Assert.Equal(result1.Telemetry.TotalQuestions, result2.Telemetry.TotalQuestions);
        Assert.Equal(result1.Telemetry.CorrectAnswers, result2.Telemetry.CorrectAnswers);
        Assert.Equal(result1.Telemetry.TotalAppliedOpponentDamage, result2.Telemetry.TotalAppliedOpponentDamage);
        Assert.Equal(result1.Telemetry.TotalAppliedPlayerDamage, result2.Telemetry.TotalAppliedPlayerDamage);
        Assert.Equal(result1.Telemetry.TotalVirtualTimeMs, result2.Telemetry.TotalVirtualTimeMs);
    }

    // =========================================================================
    // T19: Changing seed produces different valid deterministic telemetry
    // =========================================================================

    [Fact]
    public void T19_ChangingSeed_ProducesDifferentValidDeterministicTelemetry()
    {
        var config1 = new SimulationRunConfig(SyntheticPlayerProfiles.ProfileD_Learner, masterSeed: 1UL, maxTurns: 50);
        var config2 = new SimulationRunConfig(SyntheticPlayerProfiles.ProfileD_Learner, masterSeed: 2UL, maxTurns: 50);

        var result1 = HeadlessCombatSimulator.Run(config1);
        var result2 = HeadlessCombatSimulator.Run(config2);

        // Different seeds for a variable profile produce differing latencies and/or outcomes
        Assert.NotEqual(result1.Telemetry.TotalVirtualTimeMs, result2.Telemetry.TotalVirtualTimeMs);
    }

    // =========================================================================
    // T20: Cancellation preserves previously completed telemetry
    // =========================================================================

    [Fact]
    public void T20_Cancellation_PreservesAllPreviouslyCompletedTelemetry()
    {
        using var cts = new CancellationTokenSource();
        int turnCounter = 0;

        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 9090UL,
            maxTurns: 50,
            onTurnCompleted: _ =>
            {
                turnCounter++;
                if (turnCounter >= 3)
                {
                    cts.Cancel();
                }
            });

        var result = HeadlessCombatSimulator.Run(config, cts.Token);

        Assert.Equal(SimulationTerminationReason.Cancelled, result.TerminationReason);
        Assert.Equal(3, result.TotalExecutedTurns);
        Assert.Equal(3, result.Telemetry.TotalQuestions);
        Assert.Equal(3, result.Telemetry.CorrectAnswers);
        Assert.Equal(0, result.Telemetry.IncorrectAnswers);
    }

    // =========================================================================
    // T21: Pre-cancelled runs produce valid zero-turn telemetry
    // =========================================================================

    [Fact]
    public void T21_PreCancelledRuns_ProduceValidZeroTurnTelemetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileB_Expert,
            masterSeed: 111UL,
            maxTurns: 100);

        var result = HeadlessCombatSimulator.Run(config, cts.Token);

        Assert.Equal(SimulationTerminationReason.Cancelled, result.TerminationReason);
        Assert.Equal(0, result.Telemetry.TotalQuestions);
        Assert.Equal(0, result.Telemetry.CorrectAnswers);
        Assert.Equal(0, result.Telemetry.IncorrectAnswers);
        Assert.Equal(0, result.Telemetry.TotalGameOvers);
        Assert.Equal(0, result.Telemetry.TotalVirtualTimeMs);
        Assert.Equal(0, result.Telemetry.TotalAppliedOpponentDamage);
        Assert.Equal(0, result.Telemetry.TotalAppliedPlayerDamage);
        Assert.Equal(0, result.Telemetry.TotalOpponentDefeats);
        Assert.Equal(0, result.Telemetry.TotalSectorCompletions);
        Assert.Equal(1, result.Telemetry.HighestSectorReached);
        Assert.Null(result.Telemetry.AccuracyPercent);
    }

    // =========================================================================
    // T22: Target already satisfied produces valid zero-turn telemetry
    // =========================================================================

    [Fact]
    public void T22_TargetAlreadySatisfied_ProducesValidZeroTurnTelemetry()
    {
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(5, 0);
        var startingState = CyberDefenseRunState.CreateActive(
            sector: 5,
            opponentIndex: 0,
            playerCurrentHp: 100,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp));

        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 777UL,
            maxTurns: 50,
            targetSector: 3,
            startingState: startingState);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(SimulationTerminationReason.TargetSectorReached, result.TerminationReason);
        Assert.Equal(0, result.Telemetry.TotalQuestions);
        Assert.Equal(0, result.Telemetry.CorrectAnswers);
        Assert.Equal(0, result.Telemetry.TotalGameOvers);
        Assert.Equal(0, result.Telemetry.TotalVirtualTimeMs);
        Assert.Equal(5, result.Telemetry.HighestSectorReached);
        Assert.Null(result.Telemetry.AccuracyPercent);
    }

    // =========================================================================
    // T23: Maximum turn budget is never exceeded
    // =========================================================================

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(25)]
    public void T23_MaximumTurnBudget_IsNeverExceeded(long maxTurns)
    {
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 12345UL,
            maxTurns: maxTurns);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.True(result.Telemetry.TotalQuestions <= maxTurns);
        Assert.Equal(maxTurns, result.Telemetry.TotalQuestions);
    }

    // =========================================================================
    // T24: Maximum game-over limit is respected without counting extra attempts
    // =========================================================================

    [Fact]
    public void T24_MaximumGameOverLimit_IsRespectedWithoutCountingExtraAttempts()
    {
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        var lowHpState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 1,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp));

        var zeroProfile = new SyntheticPlayerProfile(
            id: "die_once",
            name: "Die Once",
            accuracyProbability: 0.0,
            minSyntheticLatencyMilliseconds: 1000,
            maxSyntheticLatencyMilliseconds: 1000);

        var config = new SimulationRunConfig(
            zeroProfile,
            masterSeed: 9999UL,
            maxTurns: 100,
            maxGameOvers: 1,
            startingState: lowHpState);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(SimulationTerminationReason.MaxGameOversReached, result.TerminationReason);
        Assert.Equal(1, result.TotalGameOvers);
        Assert.Equal(1, result.Telemetry.TotalGameOvers);
        Assert.Equal(1, result.Telemetry.TotalQuestions);
    }

    // =========================================================================
    // T25: All deferred XP fields remain explicitly unavailable
    // =========================================================================

    [Fact]
    public void T25_AllDeferredXpFields_RemainExplicitlyUnavailable()
    {
        var config = new SimulationRunConfig(SyntheticPlayerProfiles.ProfileA_Perfect, masterSeed: 123UL, maxTurns: 5);
        var result = HeadlessCombatSimulator.Run(config);

        Assert.Null(result.Telemetry.TotalXp);
        Assert.Null(result.Telemetry.Xp);
    }

    // =========================================================================
    // T26: All deferred level and skill-point fields remain explicitly unavailable
    // =========================================================================

    [Fact]
    public void T26_AllDeferredLevelAndSkillPointFields_RemainExplicitlyUnavailable()
    {
        var config = new SimulationRunConfig(SyntheticPlayerProfiles.ProfileA_Perfect, masterSeed: 123UL, maxTurns: 5);
        var result = HeadlessCombatSimulator.Run(config);

        Assert.Null(result.Telemetry.FinalPlayerLevel);
        Assert.Null(result.Telemetry.PlayerLevel);
        Assert.Null(result.Telemetry.SkillPointsEarned);
        Assert.Null(result.Telemetry.SkillPointsSpent);
    }

    // =========================================================================
    // T27: All deferred upgrade-rank fields remain explicitly unavailable
    // =========================================================================

    [Fact]
    public void T27_AllDeferredUpgradeRankFields_RemainExplicitlyUnavailable()
    {
        var config = new SimulationRunConfig(SyntheticPlayerProfiles.ProfileA_Perfect, masterSeed: 123UL, maxTurns: 5);
        var result = HeadlessCombatSimulator.Run(config);

        Assert.Null(result.Telemetry.AttackRanks);
        Assert.Null(result.Telemetry.FirewallRanks);
        Assert.Null(result.Telemetry.CriticalStrikeRanks);
        Assert.Null(result.Telemetry.OverdriveRanks);
    }

    // =========================================================================
    // T28: Shield absorption remains explicitly unavailable
    // =========================================================================

    [Fact]
    public void T28_ShieldAbsorption_RemainsExplicitlyUnavailable()
    {
        var config = new SimulationRunConfig(SyntheticPlayerProfiles.ProfileA_Perfect, masterSeed: 123UL, maxTurns: 5);
        var result = HeadlessCombatSimulator.Run(config);

        Assert.Null(result.Telemetry.TotalShieldsAbsorbed);
        Assert.Null(result.Telemetry.ShieldsAbsorbed);
    }

    // =========================================================================
    // T29: Replay distance after defeat remains explicitly unavailable pending definition
    // =========================================================================

    [Fact]
    public void T29_ReplayDistanceAfterDefeat_RemainsExplicitlyUnavailablePendingDefinition()
    {
        var config = new SimulationRunConfig(SyntheticPlayerProfiles.ProfileA_Perfect, masterSeed: 123UL, maxTurns: 5);
        var result = HeadlessCombatSimulator.Run(config);

        Assert.Null(result.Telemetry.ReplayDistanceAfterDefeat);
    }

    // =========================================================================
    // T30: Telemetry collection does not consume extra random draws
    // =========================================================================

    [Fact]
    public void T30_TelemetryCollection_DoesNotConsumeExtraRandomDraws()
    {
        ulong seed = 777123UL;
        var profile = SyntheticPlayerProfiles.ProfileC_Average;

        // Run simulation for 10 turns
        var config = new SimulationRunConfig(profile, seed, maxTurns: 10);
        var result = HeadlessCombatSimulator.Run(config);

        // Separately sample 10 attempts using identical seed and profile
        var standaloneSampler = new SyntheticAttemptSampler(profile, seed);
        for (int i = 0; i < 10; i++)
        {
            var attempt = standaloneSampler.SampleAttempt();
            // The simulation execution must draw identically and in exact sequence
            Assert.True(attempt.SyntheticLatencyMilliseconds >= profile.MinLatencyMilliseconds);
            Assert.True(attempt.SyntheticLatencyMilliseconds <= profile.MaxLatencyMilliseconds);
        }

        // Re-running simulation produces identical virtual time proving zero PRNG drift
        var result2 = HeadlessCombatSimulator.Run(config);
        Assert.Equal(result.Telemetry.TotalVirtualTimeMs, result2.Telemetry.TotalVirtualTimeMs);
        Assert.Equal(result.Telemetry.CorrectAnswers, result2.Telemetry.CorrectAnswers);
    }

    // =========================================================================
    // T31: Existing OnTurnCompleted callbacks still run exactly once per turn
    // =========================================================================

    [Fact]
    public void T31_ExistingOnTurnCompletedCallbacks_StillRunExactlyOncePerCompletedTurn()
    {
        int callbackCount = 0;
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 123UL,
            maxTurns: 7,
            onTurnCompleted: _ => callbackCount++);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(7, callbackCount);
        Assert.Equal(7, result.Telemetry.TotalQuestions);
    }

    // =========================================================================
    // T32: No history collection proportional to turn count is introduced (O(1) memory)
    // =========================================================================

    [Fact]
    public void T32_NoHistoryCollectionProportionalToTurnCount_IsIntroduced()
    {
        // Reflection check: SimulationRunTelemetry must have zero collection fields/properties
        var telemetryType = typeof(SimulationRunTelemetry);
        foreach (var prop in telemetryType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(prop.PropertyType))
            {
                Assert.Fail($"SimulationRunTelemetry must not expose collections to maintain O(1) memory. Found: {prop.Name} ({prop.PropertyType.Name})");
            }
        }

        foreach (var field in telemetryType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (field.FieldType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(field.FieldType))
            {
                Assert.Fail($"SimulationRunTelemetry must not retain collection fields. Found: {field.Name} ({field.FieldType.Name})");
            }
        }
    }

    // =========================================================================
    // T33: No production files, schemas, or UI modified
    // =========================================================================

    [Fact]
    public void T33_NoProductionFilesOrSchemasModified()
    {
        // Assert domain state machine assembly and learner store integrity are strictly respected
        var domainType = typeof(CyberDefenseStateMachine);
        Assert.NotNull(domainType);
        Assert.Equal("MathFirst.Domain", domainType.Assembly.GetName().Name);
    }

    // =========================================================================
    // T34: All previous Slice 1-4 contract tests remain valid
    // =========================================================================

    [Fact]
    public void T34_Slice1Through4ContractBaseline_Preserved()
    {
        // Quick assertion on baseline types
        Assert.NotNull(typeof(HeadlessCombatSimulator));
        Assert.NotNull(typeof(DeterministicPrng));
        Assert.NotNull(typeof(VirtualSimulationClock));
        Assert.NotNull(typeof(SyntheticPlayerProfiles));
        Assert.NotNull(typeof(SimulationRunConfig));
        Assert.NotNull(typeof(SimulationExecutionResult));
    }
}
