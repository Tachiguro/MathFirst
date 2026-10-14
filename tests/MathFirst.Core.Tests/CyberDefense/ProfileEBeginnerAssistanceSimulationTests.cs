namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.Linq;
using MathFirst.Core.Tests.CyberDefense.Simulator;
using MathFirst.Domain.CyberDefense;
using Xunit;

/// <summary>
/// Authoritative deterministic simulation verification for Fair Beginner Assistance (GDD §9, DEC-01)
/// exercising Synthetic Profile E (Beginner, ~50% accuracy).
///
/// Invariants verified:
/// - Exactly 3 consecutive qualifying early Sector 1 defeats before boss activate assistance for the eligible next run.
/// - 2 defeats do not trigger assistance.
/// - Defeat at the Sector 1 boss is disqualifying and clears the carried early defeat streak within one run.
/// - Defeat in Sector 2 does not qualify and resets streak.
/// - The emergency shield is temporary (capacity 1, active 1), absorbs only its permitted eligible hit,
///   and is not multiplied across retries.
/// - Clearing Sector 1 retires assistance, resetting capacity and active shields to 0 upon entering Sector 2.
/// - If permanent firewall rank is >= 1, assistance is ineligible and never activates.
///   (Note: in-game upgrade purchase engine is tested in domain and store tests; the simulator respects
///   authoritative starting firewall rank without inventing an artificial purchase engine).
/// - Canonical Profile E witness seed 0xBE6144E observes a qualifying defeat, the emergency shield, and identical replay.
///   The remaining candidate seed runs are determinism-only and never reach the assistance path.
/// </summary>
public sealed class ProfileEBeginnerAssistanceSimulationTests
{
    private const ulong CandidateSeedHex = 0xBE6144EUL;
    private static readonly ulong[] CandidateSeeds = { 2001UL, 2002UL, 2003UL, 2004UL, 2005UL };

    /// <summary>
    /// Synthetic stress fixture that answers every attempt incorrectly. It is NOT canonical Profile E.
    /// </summary>
    private static readonly SyntheticPlayerProfile AllIncorrectStressProfile =
        new("stress_all_incorrect", "Stress Fixture (All Incorrect)", 0.0, 1000, 1000);

    // =========================================================================
    // 1. CANONICAL PROFILE E INVARIANTS
    // =========================================================================

    [Fact]
    public void ProfileE_CanonicalProperties_ConformToGddSpecification()
    {
        var profile = SyntheticPlayerProfiles.ProfileE_Beginner;

        Assert.Equal(SyntheticPlayerProfiles.ProfileEId, profile.Id);
        Assert.Equal(0.50, profile.AccuracyProbability);
        Assert.Equal(4000, profile.MinLatencyMilliseconds);
        Assert.Equal(8000, profile.MaxLatencyMilliseconds);
        Assert.Equal(SyntheticUpgradePreference.UnupgradedBaseline, profile.UpgradePreference);
        Assert.False(profile.IsUpgradeStrategyExecutable);
    }

    // =========================================================================
    // 2. STREAK TRIGGERING (EXACTLY 3 DEFEATS VS 2 DEFEATS)
    // =========================================================================

    [Fact]
    public void UninterruptedThreeEarlyDefeatsFromZeroStreak_GrantSingleEmergencyShieldOnlyOnThirdReboot()
    {
        // STRESS FIXTURE (not canonical Profile E): all-incorrect answers make every defeat deterministic.
        var transitions = new List<CyberDefenseCombatTransitionResult>();
        var config = new SimulationRunConfig(
            profile: AllIncorrectStressProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 200,
            maxGameOvers: 3,
            startingState: CyberDefenseRunState.InitialRun(),
            onTurnCompleted: transitions.Add,
            enableBeginnerAssistance: true,
            startingConsecutiveEarlyDefeats: 0,
            permanentFirewallRank: 0);

        var result = HeadlessCombatSimulator.Run(config);

        // 100 HP / 4 damage: each unshielded run from the opening opponent ends on its 25th incorrect answer.
        int turnsPerDefeat = CyberDefenseCombatPolicy.PlayerMaxHp / CyberDefenseCombatPolicy.NormalEnemyBaseDamage;
        Assert.Equal(25, turnsPerDefeat);
        Assert.Equal(SimulationTerminationReason.MaxGameOversReached, result.TerminationReason);
        Assert.Equal(3, result.TotalGameOvers);
        Assert.Equal(3 * turnsPerDefeat, transitions.Count);
        Assert.Equal(transitions.Count, result.TotalExecutedTurns);

        int[] gameOverTurnNumbers = GetGameOverTurnNumbers(transitions);
        Assert.Equal(new[] { 25, 50, 75 }, gameOverTurnNumbers);

        foreach (int turnNumber in gameOverTurnNumbers)
        {
            AssertQualifyingEarlyDefeat(transitions[turnNumber - 1], expectedOpponentIndex: 0);
        }

        // Reboots after game-over 1 and 2 carry no shield; no shield exists anywhere before game-over 3.
        Assert.Equal(CyberDefenseRunState.InitialRun(), transitions[24].NextState);
        Assert.Equal(CyberDefenseRunState.InitialRun(), transitions[49].NextState);
        Assert.All(transitions.Take(74), AssertNoShieldPresent);

        // Third reboot: exactly one temporary emergency shield, full HP, Sector 1 opening opponent.
        var thirdReboot = transitions[74].NextState;
        Assert.Equal(CyberDefenseRunState.InitialRun(CyberDefenseBeginnerAssistancePolicy.EmergencyShieldCount), thirdReboot);
        Assert.Equal(1, thirdReboot.ShieldCapacity);
        Assert.Equal(1, thirdReboot.ActiveShields);
        Assert.Equal(0, thirdReboot.ShieldRechargeUnits);
        Assert.Equal(thirdReboot, result.FinalState);
    }

    [Fact]
    public void TwoQualifyingEarlySector1Defeats_DoNotActivateAssistance()
    {
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        var lowHpState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 1,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp));

        var zeroProfile = new SyntheticPlayerProfile("zero", "Zero", 0.0, 1000, 1000);

        CyberDefenseCombatTransitionResult? observedTransition = null;
        var config = new SimulationRunConfig(
            profile: zeroProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 1,
            startingState: lowHpState,
            enableBeginnerAssistance: true,
            startingConsecutiveEarlyDefeats: 1, // Only 1 prior defeat -> this fatal turn is defeat #2
            onTurnCompleted: t => observedTransition = t);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.NotNull(observedTransition);
        Assert.True(observedTransition.IsGameOver);
        // 2 defeats: assistance must NOT activate
        Assert.Equal(0, observedTransition.NextState.ShieldCapacity);
        Assert.Equal(0, observedTransition.NextState.ActiveShields);
        Assert.Equal(0, result.FinalState.ShieldCapacity);
        Assert.Equal(0, result.FinalState.ActiveShields);
    }

    [Fact]
    public void DisabledAssistanceSetting_PreservesLegacyBehaviorAcrossThreeDefeats()
    {
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        var lowHpState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 1,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp));

        var zeroProfile = new SyntheticPlayerProfile("zero", "Zero", 0.0, 1000, 1000);

        // When enableBeginnerAssistance is false (default), no assistance is triggered
        var config = new SimulationRunConfig(
            profile: zeroProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 1,
            startingState: lowHpState,
            enableBeginnerAssistance: false,
            startingConsecutiveEarlyDefeats: 2);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(0, result.FinalState.ShieldCapacity);
        Assert.Equal(0, result.FinalState.ActiveShields);
    }

    // =========================================================================
    // 3. STREAK RESETS ON DISQUALIFYING PROGRESSION
    // =========================================================================

    [Fact]
    public void Sector1BossDefeat_InterruptsStreakOfTwo_SubsequentEarlyDefeatsRestartFromZero()
    {
        // STRESS FIXTURE (not canonical Profile E): all-incorrect answers make every defeat deterministic.
        // Any game-over after merely reaching the boss is itself a boss defeat, so the boss-defeat
        // trajectory is the externally observable form of the boss disqualification rule.
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);
        var bossState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: bossIndex,
            playerCurrentHp: 1,
            currentOpponent: new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp));

        var transitions = new List<CyberDefenseCombatTransitionResult>();
        var config = new SimulationRunConfig(
            profile: AllIncorrectStressProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 200,
            maxGameOvers: 4,
            startingState: bossState,
            onTurnCompleted: transitions.Add,
            enableBeginnerAssistance: true,
            startingConsecutiveEarlyDefeats: 2,
            permanentFirewallRank: 0);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(SimulationTerminationReason.MaxGameOversReached, result.TerminationReason);
        Assert.Equal(4, result.TotalGameOvers);
        Assert.Equal(76, transitions.Count);
        Assert.Equal(new[] { 1, 26, 51, 76 }, GetGameOverTurnNumbers(transitions));

        // Terminal transition 1: defeat at the Sector 1 boss with a streak of two does not grant a shield.
        var bossDefeat = transitions[0];
        Assert.True(bossDefeat.IsGameOver);
        Assert.NotNull(bossDefeat.TerminalSnapshot);
        Assert.Equal(1, bossDefeat.TerminalSnapshot.Sector);
        Assert.Equal(bossIndex, bossDefeat.TerminalSnapshot.OpponentIndex);
        Assert.Equal(OpponentKind.Boss, bossDefeat.TerminalSnapshot.Kind);
        Assert.Equal(CyberDefenseRunState.InitialRun(), bossDefeat.NextState);

        // Terminal transition 2: first qualifying early defeat after the interruption. It would be the
        // third consecutive early defeat if the boss defeat had not cleared the carried streak.
        var firstEarlyDefeatAfterBoss = transitions[25];
        AssertQualifyingEarlyDefeat(firstEarlyDefeatAfterBoss, expectedOpponentIndex: 0);
        Assert.Equal(CyberDefenseRunState.InitialRun(), firstEarlyDefeatAfterBoss.NextState);

        // Terminal transition 3: second early defeat since the interruption, still no shield.
        AssertQualifyingEarlyDefeat(transitions[50], expectedOpponentIndex: 0);
        Assert.Equal(CyberDefenseRunState.InitialRun(), transitions[50].NextState);
        Assert.All(transitions.Take(75), AssertNoShieldPresent);

        // Terminal transition 4: third early defeat counted from zero activates exactly one emergency shield.
        AssertQualifyingEarlyDefeat(transitions[75], expectedOpponentIndex: 0);
        Assert.Equal(
            CyberDefenseRunState.InitialRun(CyberDefenseBeginnerAssistancePolicy.EmergencyShieldCount),
            transitions[75].NextState);
        Assert.Equal(transitions[75].NextState, result.FinalState);
    }

    [Fact]
    public void DefeatAtSector1Boss_DoesNotQualify_DoesNotActivateAssistance()
    {
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);
        var bossState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: bossIndex,
            playerCurrentHp: 1,
            currentOpponent: new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp));

        var zeroProfile = new SyntheticPlayerProfile("zero", "Zero", 0.0, 1000, 1000);

        var config = new SimulationRunConfig(
            profile: zeroProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 1,
            startingState: bossState,
            enableBeginnerAssistance: true,
            startingConsecutiveEarlyDefeats: 2); // even if starting with 2, defeat AT boss is disqualifying

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(1, result.TotalGameOvers);
        Assert.Equal(0, result.FinalState.ShieldCapacity);
        Assert.Equal(0, result.FinalState.ActiveShields);
    }

    [Fact]
    public void DefeatInSector2_DoesNotQualify_DoesNotActivateAssistance()
    {
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(2, 0);
        var sector2State = CyberDefenseRunState.CreateActive(
            sector: 2,
            opponentIndex: 0,
            playerCurrentHp: 1,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp));

        var zeroProfile = new SyntheticPlayerProfile("zero", "Zero", 0.0, 1000, 1000);

        var config = new SimulationRunConfig(
            profile: zeroProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 1,
            startingState: sector2State,
            enableBeginnerAssistance: true,
            startingConsecutiveEarlyDefeats: 2);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(1, result.TotalGameOvers);
        Assert.Equal(0, result.FinalState.ShieldCapacity);
    }

    // =========================================================================
    // 4. TEMPORARY EMERGENCY SHIELD CONSUMPTION & NON-MULTIPLICATION
    // =========================================================================

    [Fact]
    public void EmergencyShield_AbsorbsSingleHit_DecrementsToZero_AndDoesNotMultiply()
    {
        // Start with assistance active: 1 emergency shield
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        var assistedState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 100,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp),
            shieldCapacity: 1,
            activeShields: 1,
            shieldRechargeUnits: 0);

        var zeroProfile = new SyntheticPlayerProfile("zero", "Zero", 0.0, 1000, 1000);

        var transitions = new List<CyberDefenseCombatTransitionResult>();
        var config = new SimulationRunConfig(
            profile: zeroProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 2,
            startingState: assistedState,
            enableBeginnerAssistance: true,
            startingAssistanceActive: true,
            onTurnCompleted: t => transitions.Add(t));

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(2, transitions.Count);

        // Turn 1: Emergency shield absorbs the hit
        Assert.True(transitions[0].IsShieldAbsorbed);
        Assert.Equal(4, transitions[0].AbsorbedEnemyDamage);
        Assert.Equal(0, transitions[0].AppliedPlayerDamage);
        Assert.Equal(100, transitions[0].NextState.PlayerCurrentHp);
        Assert.Equal(0, transitions[0].NextState.ActiveShields);
        Assert.Equal(1, transitions[0].NextState.ShieldCapacity);

        // Turn 2: Emergency shield is depleted; player takes real damage
        Assert.False(transitions[1].IsShieldAbsorbed);
        Assert.Equal(0, transitions[1].AbsorbedEnemyDamage);
        Assert.Equal(4, transitions[1].AppliedPlayerDamage);
        Assert.Equal(96, transitions[1].NextState.PlayerCurrentHp);

        // Non-multiplication: Total shields absorbed across run is exactly 1
        Assert.Equal(1L, result.Telemetry.TotalShieldsAbsorbed);
    }

    [Fact]
    public void GameOverWhileAssistanceActive_PreservesAssistanceAndRestartsWithSingleShield()
    {
        // Start with assistance active, 1 HP remaining, depleted shield -> next turn causes game-over
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        var dyingState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 1,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp),
            shieldCapacity: 1,
            activeShields: 0,
            shieldRechargeUnits: 0);

        var zeroProfile = new SyntheticPlayerProfile("zero", "Zero", 0.0, 1000, 1000);

        var config = new SimulationRunConfig(
            profile: zeroProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 1,
            startingState: dyingState,
            enableBeginnerAssistance: true,
            startingAssistanceActive: true,
            startingConsecutiveEarlyDefeats: 3);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(1, result.TotalGameOvers);
        // Restarted run state must have exactly 1 fresh emergency shield, not 2
        Assert.Equal(1, result.FinalState.ShieldCapacity);
        Assert.Equal(1, result.FinalState.ActiveShields);
        Assert.Equal(CyberDefenseCombatPolicy.PlayerMaxHp, result.FinalState.PlayerCurrentHp);
    }

    // =========================================================================
    // 5. RETIREMENT ON SECTOR 1 CLEAR & PURCHASE BOUNDARIES
    // =========================================================================

    [Fact]
    public void Sector1Clear_RetiresAssistance_RemovesEmergencyShieldInSector2()
    {
        // At Sector 1 Boss with 1 HP remaining, player has active assistance
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);
        var bossClearState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: bossIndex,
            playerCurrentHp: 100,
            currentOpponent: new OpponentState(OpponentKind.Boss, bossMaxHp, 1),
            shieldCapacity: 1,
            activeShields: 1,
            shieldRechargeUnits: 0);

        var perfectProfile = SyntheticPlayerProfiles.ProfileA_Perfect;

        var transitions = new List<CyberDefenseCombatTransitionResult>();
        var config = new SimulationRunConfig(
            profile: perfectProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 1,
            startingState: bossClearState,
            enableBeginnerAssistance: true,
            startingAssistanceActive: true,
            onTurnCompleted: t => transitions.Add(t));

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Single(transitions);
        Assert.True(transitions[0].IsSectorCompleted);
        Assert.Equal(2, transitions[0].NextState.Sector);

        // Assistance is retired: Sector 2 must have NO borrowed emergency shields
        Assert.Equal(0, transitions[0].NextState.ShieldCapacity);
        Assert.Equal(0, transitions[0].NextState.ActiveShields);
        Assert.Equal(0, transitions[0].NextState.ShieldRechargeUnits);
        Assert.Equal(0, result.FinalState.ShieldCapacity);
        Assert.Equal(0, result.FinalState.ActiveShields);
    }

    [Fact]
    public void PermanentFirewallRank2_ThirdEarlyDefeat_RetainsPermanentCapacity_AndNeverSubstitutesEmergencyShield()
    {
        // Rank-eligibility simulation, not purchase-flow certification: the permanent rank is an
        // authoritative synthetic starting fact. Capacity 2 differs from the emergency capacity 1,
        // so ignoring the rank gate would observably replace the reboot state.
        const int permanentRank = 2;
        int permanentCapacity = CyberDefenseFirewallPolicy.GetShieldCapacity(permanentRank);
        Assert.Equal(2, permanentCapacity);
        Assert.NotEqual(CyberDefenseBeginnerAssistancePolicy.EmergencyShieldCount, permanentCapacity);

        var (rankedResult, rankedFatalTurn) = RunFatalThirdEarlyDefeat(permanentRank, permanentCapacity);

        Assert.Equal(1, rankedResult.TotalGameOvers);
        AssertQualifyingEarlyDefeat(rankedFatalTurn, expectedOpponentIndex: 0);
        Assert.False(rankedFatalTurn.IsShieldAbsorbed);
        Assert.Equal(CyberDefenseRunState.InitialRun(permanentCapacity), rankedFatalTurn.NextState);
        Assert.Equal(2, rankedFatalTurn.NextState.ShieldCapacity);
        Assert.Equal(2, rankedFatalTurn.NextState.ActiveShields);
        Assert.Equal(0, rankedFatalTurn.NextState.ShieldRechargeUnits);
        Assert.NotEqual(
            CyberDefenseRunState.InitialRun(CyberDefenseBeginnerAssistancePolicy.EmergencyShieldCount),
            rankedFatalTurn.NextState);
        Assert.Equal(rankedFatalTurn.NextState, rankedResult.FinalState);

        // Rank-0 control: the identical fatal third early defeat does activate the emergency shield,
        // proving the rank gate is the only reason the ranked run above was not assisted.
        var (controlResult, controlFatalTurn) = RunFatalThirdEarlyDefeat(permanentRank: 0, shieldCapacity: 0);

        Assert.Equal(1, controlResult.TotalGameOvers);
        AssertQualifyingEarlyDefeat(controlFatalTurn, expectedOpponentIndex: 0);
        Assert.Equal(
            CyberDefenseRunState.InitialRun(CyberDefenseBeginnerAssistancePolicy.EmergencyShieldCount),
            controlFatalTurn.NextState);
    }

    // =========================================================================
    // 6. DETERMINISTIC CANDIDATE SEEDS & REPRODUCIBILITY
    // =========================================================================

    [Fact]
    public void ProfileE_WitnessSeed_LowHpEligibleStart_ObservesQualifyingDefeatEmergencyShieldAndIdenticalReplay()
    {
        // Canonical Profile E (0.50 accuracy) on regression witness seed 0xBE6144E. Legal low-HP Sector 1
        // start (8 HP) with a documented pre-existing eligible streak of two; the uninterrupted 0-to-3
        // sequence is proven separately by the stress-fixture trajectory tests.
        var profile = SyntheticPlayerProfiles.ProfileE_Beginner;
        Assert.Equal(0.50, profile.AccuracyProbability);

        int opponentMaxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        var lowHpState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 8,
            currentOpponent: new OpponentState(OpponentKind.Normal, opponentMaxHp, opponentMaxHp));

        SimulationRunConfig CreateConfig(List<CyberDefenseCombatTransitionResult> sink) => new(
            profile: profile,
            masterSeed: CandidateSeedHex,
            maxTurns: 7,
            maxGameOvers: 2,
            startingState: lowHpState,
            onTurnCompleted: sink.Add,
            enableBeginnerAssistance: true,
            startingConsecutiveEarlyDefeats: 2,
            permanentFirewallRank: 0);

        var transitions = new List<CyberDefenseCombatTransitionResult>();
        var result = HeadlessCombatSimulator.Run(CreateConfig(transitions));

        Assert.Equal(SimulationTerminationReason.MaxTurnsReached, result.TerminationReason);
        Assert.Equal(7, transitions.Count);
        Assert.Equal(1, result.TotalGameOvers);

        // Observed sampled path for this seed: incorrect, correct x3, incorrect x3.
        Assert.Equal(
            new[] { false, true, true, true, false, false, false },
            transitions.Select(t => t.IsCorrect).ToArray());
        Assert.True(transitions[2].IsOpponentDefeated);
        Assert.Equal(1, transitions[2].NextState.OpponentIndex);
        Assert.Equal(new[] { 6 }, GetGameOverTurnNumbers(transitions));

        // Turn 6: qualifying early defeat (Sector 1, opponent 1, boss never reached) activates assistance.
        AssertQualifyingEarlyDefeat(transitions[5], expectedOpponentIndex: 1);
        Assert.All(transitions.Take(5), AssertNoShieldPresent);
        Assert.Equal(
            CyberDefenseRunState.InitialRun(CyberDefenseBeginnerAssistancePolicy.EmergencyShieldCount),
            transitions[5].NextState);

        // Turn 7: the single temporary emergency shield absorbs Profile E's next incorrect answer.
        var absorbedTurn = transitions[6];
        Assert.True(absorbedTurn.IsShieldAbsorbed);
        Assert.Equal(CyberDefenseCombatPolicy.NormalEnemyBaseDamage, absorbedTurn.AbsorbedEnemyDamage);
        Assert.Equal(0, absorbedTurn.AppliedPlayerDamage);
        Assert.Equal(CyberDefenseCombatPolicy.PlayerMaxHp, absorbedTurn.NextState.PlayerCurrentHp);
        Assert.Equal(1, absorbedTurn.NextState.ShieldCapacity);
        Assert.Equal(0, absorbedTurn.NextState.ActiveShields);
        Assert.Equal(1L, result.Telemetry.TotalShieldsAbsorbed);

        // Identical seed and configuration replay the identical authoritative trace.
        var replayTransitions = new List<CyberDefenseCombatTransitionResult>();
        var replay = HeadlessCombatSimulator.Run(CreateConfig(replayTransitions));

        Assert.Equal(transitions, replayTransitions);
        Assert.Equal(result.FinalState, replay.FinalState);
        Assert.Equal(result.LastTerminalSnapshot, replay.LastTerminalSnapshot);
        Assert.Equal(result.Telemetry, replay.Telemetry);
        Assert.Equal(result.TerminationReason, replay.TerminationReason);
        Assert.Equal(result.TotalExecutedTurns, replay.TotalExecutedTurns);
        Assert.Equal(result.TotalGameOvers, replay.TotalGameOvers);
        Assert.Equal(result.TotalVirtualTimeMs, replay.TotalVirtualTimeMs);
    }

    [Theory]
    [InlineData(CandidateSeedHex)]
    [InlineData(2001UL)]
    [InlineData(2002UL)]
    [InlineData(2003UL)]
    [InlineData(2004UL)]
    [InlineData(2005UL)]
    public void ProfileE_CandidateSeeds_FullHp20Turns_ReplayDeterministically_DeterminismOnlyWithoutAssistance(ulong seed)
    {
        // Determinism-only: 20 turns x 4 damage cannot exhaust 100 HP, so these runs never reach
        // a game-over and do not exercise beginner assistance.
        var profile = SyntheticPlayerProfiles.ProfileE_Beginner;
        var transitions1 = new List<CyberDefenseCombatTransitionResult>();
        var transitions2 = new List<CyberDefenseCombatTransitionResult>();
        var config1 = new SimulationRunConfig(profile, seed, maxTurns: 20, onTurnCompleted: transitions1.Add, enableBeginnerAssistance: true);
        var config2 = new SimulationRunConfig(profile, seed, maxTurns: 20, onTurnCompleted: transitions2.Add, enableBeginnerAssistance: true);

        var res1 = HeadlessCombatSimulator.Run(config1);
        var res2 = HeadlessCombatSimulator.Run(config2);

        Assert.Equal(SimulationTerminationReason.MaxTurnsReached, res1.TerminationReason);
        Assert.Equal(20, transitions1.Count);
        Assert.Equal(0, res1.TotalGameOvers);
        Assert.Equal(0, res1.FinalState.ShieldCapacity);

        Assert.Equal(transitions1, transitions2);
        Assert.Equal(res1.FinalState, res2.FinalState);
        Assert.Equal(res1.Telemetry, res2.Telemetry);
        Assert.Equal(res1.TotalExecutedTurns, res2.TotalExecutedTurns);
        Assert.Equal(res1.TotalGameOvers, res2.TotalGameOvers);
        Assert.Equal(res1.TotalVirtualTimeMs, res2.TotalVirtualTimeMs);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static (SimulationExecutionResult Result, CyberDefenseCombatTransitionResult FatalTurn) RunFatalThirdEarlyDefeat(
        int permanentRank,
        int shieldCapacity)
    {
        // All shields depleted and 1 HP: the single incorrect answer is an unabsorbed fatal early defeat.
        int opponentMaxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        var fatalState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 1,
            currentOpponent: new OpponentState(OpponentKind.Normal, opponentMaxHp, opponentMaxHp),
            shieldCapacity: shieldCapacity,
            activeShields: 0,
            shieldRechargeUnits: 0);

        var transitions = new List<CyberDefenseCombatTransitionResult>();
        var config = new SimulationRunConfig(
            profile: AllIncorrectStressProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 1,
            maxGameOvers: 1,
            startingState: fatalState,
            onTurnCompleted: transitions.Add,
            enableBeginnerAssistance: true,
            startingConsecutiveEarlyDefeats: 2,
            permanentFirewallRank: permanentRank);

        Assert.Equal(permanentRank, config.PermanentFirewallRank);

        var result = HeadlessCombatSimulator.Run(config);
        return (result, Assert.Single(transitions));
    }

    private static int[] GetGameOverTurnNumbers(IReadOnlyList<CyberDefenseCombatTransitionResult> transitions) =>
        transitions
            .Select((transition, index) => (transition, turnNumber: index + 1))
            .Where(entry => entry.transition.IsGameOver)
            .Select(entry => entry.turnNumber)
            .ToArray();

    private static void AssertQualifyingEarlyDefeat(CyberDefenseCombatTransitionResult transition, int expectedOpponentIndex)
    {
        Assert.True(transition.IsGameOver);
        Assert.NotNull(transition.TerminalSnapshot);
        Assert.Equal(1, transition.TerminalSnapshot.Sector);
        Assert.Equal(expectedOpponentIndex, transition.TerminalSnapshot.OpponentIndex);
        Assert.Equal(OpponentKind.Normal, transition.TerminalSnapshot.Kind);
        Assert.Equal(0, transition.TerminalSnapshot.PlayerCurrentHp);
    }

    private static void AssertNoShieldPresent(CyberDefenseCombatTransitionResult transition)
    {
        Assert.False(transition.IsShieldAbsorbed);
        Assert.Equal(0, transition.NextState.ShieldCapacity);
        Assert.Equal(0, transition.NextState.ActiveShields);
    }
}
