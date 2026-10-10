namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Threading;
using MathFirst.Core.Tests.CyberDefense.Simulator;
using MathFirst.Domain.CyberDefense;
using Xunit;

/// <summary>
/// Focused contract and behavioral tests for MF-CYBER-004-SLICE-4:
/// Multi-Turn Simulation Execution Lifecycle, Limits and Budgets.
/// </summary>
public class HeadlessSimulatorExecutionLifecycleTests
{
    // =========================================================================
    // L1 - L4: TURN LIMITS & TARGET SECTOR
    // =========================================================================

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(100)]
    public void L1_Simulation_NeverExecutesMoreThanMaxTurns(long maxTurns)
    {
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 12345UL,
            maxTurns: maxTurns);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.True(result.TotalExecutedTurns <= maxTurns);
        Assert.Equal(maxTurns, result.TotalExecutedTurns);
        Assert.Equal(SimulationTerminationReason.MaxTurnsReached, result.TerminationReason);
    }

    [Fact]
    public void L2_MaxTurns1_ExecutesExactlyOneAttempt()
    {
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 999UL,
            maxTurns: 1);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(1, result.TotalExecutedTurns);
        Assert.Equal(1, result.ExecutedTurns);
        Assert.Equal(SimulationTerminationReason.MaxTurnsReached, result.TerminationReason);
        Assert.Equal(1200, result.TotalVirtualTimeMs);
        Assert.Equal(0, result.TotalGameOvers);
        Assert.Equal(1, result.FinalState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void L3_InitialStateAlreadyAtTargetSector_ReturnsImmediatelyWithZeroTurns()
    {
        int targetSector = 3;
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(targetSector, 0);
        var startingState = CyberDefenseRunState.CreateActive(
            sector: targetSector,
            opponentIndex: 0,
            playerCurrentHp: 100,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp));

        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 42UL,
            maxTurns: 50,
            targetSector: targetSector,
            startingState: startingState);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(0, result.TotalExecutedTurns);
        Assert.Equal(0, result.TotalGameOvers);
        Assert.Equal(0, result.TotalVirtualTimeMs);
        Assert.Equal(SimulationTerminationReason.TargetSectorReached, result.TerminationReason);
        Assert.Same(startingState, result.FinalState);
        Assert.Null(result.LastTerminalSnapshot);
    }

    [Fact]
    public void L4_TransitionReachingTargetSector_TerminatesWithoutExtraTurn()
    {
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, 1);
        var run = CyberDefenseRunState.CreateActive(1, bossIndex, 100, boss);

        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 777UL,
            maxTurns: 1000,
            targetSector: 2,
            startingState: run);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(1, result.TotalExecutedTurns);
        Assert.Equal(SimulationTerminationReason.TargetSectorReached, result.TerminationReason);
        Assert.Equal(2, result.FinalState.Sector);
        Assert.Equal(0, result.FinalState.OpponentIndex);
        Assert.Equal(1200, result.TotalVirtualTimeMs);
        Assert.Equal(0, result.TotalGameOvers);
    }

    // =========================================================================
    // L5 - L9: GAME-OVER & REBOOT SEMANTICS
    // =========================================================================

    [Fact]
    public void L5_MaxGameOvers1_StopsImmediatelyAfterFirstFatalCounterattack()
    {
        var run = CyberDefenseRunState.CreateActive(
            1, 0, 4,
            new OpponentState(OpponentKind.Normal, 2, 2));

        var failProfile = new SyntheticPlayerProfile("FailAlways", "Fail Always", 0.0, 1500, 1500);

        var config = new SimulationRunConfig(
            failProfile,
            masterSeed: 123UL,
            maxTurns: 100,
            maxGameOvers: 1,
            startingState: run);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(1, result.TotalExecutedTurns);
        Assert.Equal(1, result.TotalGameOvers);
        Assert.Equal(SimulationTerminationReason.MaxGameOversReached, result.TerminationReason);
        Assert.NotNull(result.LastTerminalSnapshot);
        Assert.Equal(0, result.LastTerminalSnapshot.PlayerCurrentHp);
        Assert.Equal(100, result.FinalState.PlayerCurrentHp);
        Assert.Equal(1500, result.TotalVirtualTimeMs);
    }

    [Fact]
    public void L6_MaxGameOversGreaterThan1_PermitsContinuationAfterProductionReboot()
    {
        var run = CyberDefenseRunState.CreateActive(
            1, 0, 4,
            new OpponentState(OpponentKind.Normal, 2, 2));

        var failProfile = new SyntheticPlayerProfile("FailAlways", "Fail Always", 0.0, 1000, 1000);

        var config = new SimulationRunConfig(
            failProfile,
            masterSeed: 123UL,
            maxTurns: 5,
            maxGameOvers: 2,
            startingState: run);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(5, result.TotalExecutedTurns);
        Assert.Equal(1, result.TotalGameOvers);
        Assert.Equal(SimulationTerminationReason.MaxTurnsReached, result.TerminationReason);
        Assert.Equal(84, result.FinalState.PlayerCurrentHp);
        Assert.Equal(5000, result.TotalVirtualTimeMs);
        Assert.NotNull(result.LastTerminalSnapshot);
    }

    [Fact]
    public void L7_AfterGameOver_NextSimulationTurnStartsFromProductionNextState()
    {
        var run = CyberDefenseRunState.CreateActive(
            1, 0, 4,
            new OpponentState(OpponentKind.Normal, 2, 2));

        var failProfile = new SyntheticPlayerProfile("FailAlways", "Fail Always", 0.0, 1000, 1000);

        var config = new SimulationRunConfig(
            failProfile,
            masterSeed: 1UL,
            maxTurns: 2,
            maxGameOvers: 2,
            startingState: run);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(2, result.TotalExecutedTurns);
        Assert.Equal(1, result.TotalGameOvers);
        Assert.Equal(96, result.FinalState.PlayerCurrentHp);
        Assert.Equal(1, result.FinalState.Sector);
        Assert.Equal(0, result.FinalState.OpponentIndex);
    }

    [Fact]
    public void L8_MostRecentTerminalSnapshot_IsPreservedWhenGameOverOccurs()
    {
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);
        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, 7);
        var run = CyberDefenseRunState.CreateActive(1, bossIndex, 5, boss);

        var failProfile = new SyntheticPlayerProfile("FailBoss", "Fail Boss", 0.0, 1000, 1000);
        var config = new SimulationRunConfig(
            failProfile,
            masterSeed: 1UL,
            maxTurns: 1,
            startingState: run);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.NotNull(result.LastTerminalSnapshot);
        Assert.Equal(1, result.LastTerminalSnapshot.Sector);
        Assert.Equal(bossIndex, result.LastTerminalSnapshot.OpponentIndex);
        Assert.Equal(OpponentKind.Boss, result.LastTerminalSnapshot.Kind);
        Assert.Equal(7, result.LastTerminalSnapshot.OpponentCurrentHp);
        Assert.Equal(bossMaxHp, result.LastTerminalSnapshot.OpponentMaxHp);
        Assert.Equal(0, result.LastTerminalSnapshot.PlayerCurrentHp);
    }

    [Fact]
    public void L9_GameOverCount_IsNeverDuplicated()
    {
        var run = CyberDefenseRunState.CreateActive(
            1, 0, 4,
            new OpponentState(OpponentKind.Normal, 2, 2));

        var failProfile = new SyntheticPlayerProfile("Fail", "Fail", 0.0, 1000, 1000);
        var config = new SimulationRunConfig(
            failProfile,
            masterSeed: 1UL,
            maxTurns: 5,
            maxGameOvers: 5,
            startingState: run);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(5, result.TotalExecutedTurns);
        Assert.Equal(1, result.TotalGameOvers);
    }

    // =========================================================================
    // L10 - L13: DETERMINISM, REPRODUCIBILITY & VIRTUAL TIME
    // =========================================================================

    [Fact]
    public void L10_IdenticalInputs_YieldIdenticalExecutionResults()
    {
        var config1 = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileC_Average,
            masterSeed: 424242UL,
            maxTurns: 50);

        var config2 = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileC_Average,
            masterSeed: 424242UL,
            maxTurns: 50);

        var result1 = HeadlessCombatSimulator.Run(config1);
        var result2 = HeadlessCombatSimulator.Run(config2);

        Assert.Equal(result1.TotalExecutedTurns, result2.TotalExecutedTurns);
        Assert.Equal(result1.TotalGameOvers, result2.TotalGameOvers);
        Assert.Equal(result1.TotalVirtualTimeMs, result2.TotalVirtualTimeMs);
        Assert.Equal(result1.TerminationReason, result2.TerminationReason);
        Assert.Equal(result1.FinalState, result2.FinalState);
        Assert.Equal(result1.LastTerminalSnapshot, result2.LastTerminalSnapshot);
    }

    [Fact]
    public void L11_DifferentDeterministicSeeds_ProduceDistinctValidHistories()
    {
        var config1 = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileC_Average,
            masterSeed: 11111UL,
            maxTurns: 50);

        var config2 = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileC_Average,
            masterSeed: 99999UL,
            maxTurns: 50);

        var result1 = HeadlessCombatSimulator.Run(config1);
        var result2 = HeadlessCombatSimulator.Run(config2);

        Assert.Equal(50, result1.TotalExecutedTurns);
        Assert.Equal(50, result2.TotalExecutedTurns);
        Assert.NotEqual(result1.TotalVirtualTimeMs, result2.TotalVirtualTimeMs);
    }

    [Fact]
    public void L12_AccumulatedVirtualMilliseconds_ExactlyMatchesSampledAttemptDurations()
    {
        ulong seed = 54321UL;
        var profile = SyntheticPlayerProfiles.ProfileD_Learner;
        long turns = 30;

        var config = new SimulationRunConfig(profile, seed, turns);
        var result = HeadlessCombatSimulator.Run(config);

        var replaySampler = new SyntheticAttemptSampler(profile, seed);
        long expectedElapsedMs = 0;
        for (int i = 0; i < turns; i++)
        {
            var attempt = replaySampler.SampleAttempt();
            expectedElapsedMs += attempt.SyntheticLatencyMilliseconds;
        }

        Assert.Equal(expectedElapsedMs, result.TotalVirtualTimeMs);
    }

    [Fact]
    public void L13_ProfileA_ProducesNoIncorrectAnswersWithinBoundedRun()
    {
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 123456789UL,
            maxTurns: 100);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(100, result.TotalExecutedTurns);
        Assert.Equal(0, result.TotalGameOvers);
        Assert.Equal(CyberDefenseCombatPolicy.PlayerMaxHp, result.FinalState.PlayerCurrentHp);
        Assert.Null(result.LastTerminalSnapshot);
    }

    // =========================================================================
    // L14 - L15: COOPERATIVE CANCELLATION
    // =========================================================================

    [Fact]
    public void L14_PreCancelledToken_ProducesCancelledResultWithZeroTurns()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var startingState = CyberDefenseRunState.InitialRun();
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 42UL,
            maxTurns: 100,
            startingState: startingState);

        var result = HeadlessCombatSimulator.Run(config, cts.Token);

        Assert.Equal(0, result.TotalExecutedTurns);
        Assert.Equal(0, result.TotalGameOvers);
        Assert.Equal(0, result.TotalVirtualTimeMs);
        Assert.Equal(SimulationTerminationReason.Cancelled, result.TerminationReason);
        Assert.Same(startingState, result.FinalState);
        Assert.Null(result.LastTerminalSnapshot);
    }

    [Fact]
    public void L15_CancellationChecks_DoNotMutateCombatStateOrAdvanceVirtualTime()
    {
        using var cts = new CancellationTokenSource();
        long turnCount = 0;
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 42UL,
            maxTurns: 100,
            onTurnCompleted: _ =>
            {
                turnCount++;
                if (turnCount == 1)
                {
                    cts.Cancel();
                }
            });

        var result = HeadlessCombatSimulator.Run(config, cts.Token);

        Assert.Equal(1, result.TotalExecutedTurns);
        Assert.Equal(SimulationTerminationReason.Cancelled, result.TerminationReason);
        Assert.Equal(1200, result.TotalVirtualTimeMs);
        Assert.Equal(1, result.FinalState.CurrentOpponent.CurrentHp);
    }

    // =========================================================================
    // L16 - L19: FAIL-CLOSED CONFIGURATION VALIDATION
    // =========================================================================

    [Fact]
    public void L16_InvalidMaxTurns_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationRunConfig(SyntheticPlayerProfiles.ProfileA_Perfect, 1UL, maxTurns: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationRunConfig(SyntheticPlayerProfiles.ProfileA_Perfect, 1UL, maxTurns: -10));
    }

    [Fact]
    public void L17_InvalidTargetSector_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationRunConfig(SyntheticPlayerProfiles.ProfileA_Perfect, 1UL, maxTurns: 10, targetSector: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationRunConfig(SyntheticPlayerProfiles.ProfileA_Perfect, 1UL, maxTurns: 10, targetSector: -3));
    }

    [Fact]
    public void L18_InvalidMaxGameOvers_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationRunConfig(SyntheticPlayerProfiles.ProfileA_Perfect, 1UL, maxTurns: 10, maxGameOvers: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationRunConfig(SyntheticPlayerProfiles.ProfileA_Perfect, 1UL, maxTurns: 10, maxGameOvers: -1));
    }

    [Fact]
    public void L19_NullProfile_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SimulationRunConfig(null!, 1UL, maxTurns: 10));
        Assert.Throws<ArgumentNullException>(() =>
            HeadlessCombatSimulator.Run(null!));
    }

    // =========================================================================
    // L20 - L24: CONTRACT INTEGRITY & NON-INTERFERENCE
    // =========================================================================

    [Fact]
    public void L20_NoSecondaryCombatRulesOrDuplicateTransitionsIntroduced()
    {
        ulong seed = 88888UL;
        var profile = SyntheticPlayerProfiles.ProfileC_Average;
        long turns = 20;

        var config = new SimulationRunConfig(profile, seed, turns);
        var simResult = HeadlessCombatSimulator.Run(config);

        var manualSampler = new SyntheticAttemptSampler(profile, seed);
        var state = CyberDefenseRunState.InitialRun();
        long manualTurns = 0;
        int manualGameOvers = 0;
        CyberDefenseTerminalRunSnapshot? manualLastSnapshot = null;

        for (int i = 0; i < turns; i++)
        {
            var attempt = manualSampler.SampleAttempt();
            var transition = CyberDefenseStateMachine.ApplyAttempt(state, attempt.IsCorrect, profile.EffectiveAttackDamage);
            manualTurns++;
            if (transition.IsGameOver)
            {
                manualGameOvers++;
                manualLastSnapshot = transition.TerminalSnapshot;
            }
            state = transition.NextState;
        }

        Assert.Equal(manualTurns, simResult.TotalExecutedTurns);
        Assert.Equal(manualGameOvers, simResult.TotalGameOvers);
        Assert.Equal(manualLastSnapshot, simResult.LastTerminalSnapshot);
        Assert.Equal(state, simResult.FinalState);
    }

    [Fact]
    public void L21_Slice1_StepAndApplyAttemptBehavior_RemainsUnchanged()
    {
        var run = CyberDefenseRunState.InitialRun();
        var stepResult = HeadlessCombatSimulator.Step(run, isCorrect: true, effectiveAttackDamage: 1);
        var applyResult = HeadlessCombatSimulator.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.Equal(stepResult, applyResult);
        Assert.Equal(1, stepResult.AppliedOpponentDamage);
        Assert.Equal(1, stepResult.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void L22_SplitMix64_GoldenVectors_RemainUnchanged()
    {
        var rng = new DeterministicPrng(0UL);
        Assert.Equal(0xE220A8397B1DCDAFUL, rng.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, rng.NextUInt64());
        Assert.Equal(0x06C45D188009454FUL, rng.NextUInt64());
        Assert.Equal(0xF88BB8A8724C81ECUL, rng.NextUInt64());
    }

    [Fact]
    public void L23_Slice3_StreamIndependence_RemainsUnchanged()
    {
        var sampler = new SyntheticAttemptSampler(SyntheticPlayerProfiles.ProfileA_Perfect, 12345UL);
        Assert.Equal(SyntheticAttemptSampler.CorrectnessStreamDomainSalt, 0x434F525245435401UL);
        Assert.Equal(SyntheticAttemptSampler.LatencyStreamDomainSalt, 0x4C4154454E435902UL);
        Assert.True(sampler.SampleCorrectness());
        Assert.Equal(1200, sampler.SampleLatency());
    }

    [Fact]
    public void L24_SimulatorUsesZeroDatabaseIoNetworkOrUiServices()
    {
        var config = new SimulationRunConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            masterSeed: 12345UL,
            maxTurns: 50);

        var result = HeadlessCombatSimulator.Run(config);
        Assert.NotNull(result);
    }
}
