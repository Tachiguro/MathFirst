namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
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
/// - Defeat at boss or reaching the boss resets the early defeat streak per production policy.
/// - Defeat in Sector 2 does not qualify and resets streak.
/// - The emergency shield is temporary (capacity 1, active 1), absorbs only its permitted eligible hit,
///   and is not multiplied across retries.
/// - Clearing Sector 1 retires assistance, resetting capacity and active shields to 0 upon entering Sector 2.
/// - If permanent firewall rank is >= 1, assistance is ineligible and never activates.
///   (Note: in-game upgrade purchase engine is tested in domain and store tests; the simulator respects
///   authoritative starting firewall rank without inventing an artificial purchase engine).
/// - Matched deterministic candidate seeds produce reproducible traces and preserve virtual time.
/// </summary>
public sealed class ProfileEBeginnerAssistanceSimulationTests
{
    private const ulong CandidateSeedHex = 0xBE6144EUL;
    private static readonly ulong[] CandidateSeeds = { 2001UL, 2002UL, 2003UL, 2004UL, 2005UL };

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
    public void ThreeQualifyingEarlySector1Defeats_ActivatesAssistance_AndGrantsEmergencyShield()
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
            startingConsecutiveEarlyDefeats: 2,
            onTurnCompleted: t => observedTransition = t);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.NotNull(observedTransition);
        Assert.True(observedTransition.IsGameOver);
        // After defeat #3, assistance activates and next rebooted run starts with exactly 1 emergency shield
        Assert.Equal(1, observedTransition.NextState.ShieldCapacity);
        Assert.Equal(1, observedTransition.NextState.ActiveShields);
        Assert.Equal(0, observedTransition.NextState.ShieldRechargeUnits);
        Assert.Equal(1, result.FinalState.ShieldCapacity);
        Assert.Equal(1, result.FinalState.ActiveShields);
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
    public void ReachingSector1Boss_ResetsConsecutiveEarlyDefeatStreak()
    {
        // OpponentIndex 4 is the last Normal opponent in Sector 1. Defeating it transitions to Boss (Index 5).
        int oppHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 4);
        var preBossState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 4,
            playerCurrentHp: 100,
            currentOpponent: new OpponentState(OpponentKind.Normal, oppHp, 1)); // 1 HP left

        var perfectProfile = SyntheticPlayerProfiles.ProfileA_Perfect;

        // Run 2 turns: Turn 1 defeats Normal #4 and reaches Boss. Turn 2 is at the Boss.
        // Then subsequent defeat must NOT activate assistance because streak was reset at Boss.
        var transitions = new List<CyberDefenseCombatTransitionResult>();
        var config = new SimulationRunConfig(
            profile: perfectProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 1,
            startingState: preBossState,
            enableBeginnerAssistance: true,
            startingConsecutiveEarlyDefeats: 2,
            onTurnCompleted: t => transitions.Add(t));

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Single(transitions);
        Assert.True(transitions[0].IsOpponentDefeated);
        Assert.Equal(OpponentKind.Boss, transitions[0].NextState.CurrentOpponent.Kind);

        // Now, a subsequent run starting from this Boss state with a fatal defeat should NOT trigger assistance
        var zeroProfile = new SyntheticPlayerProfile("zero", "Zero", 0.0, 1000, 1000);
        var bossFatalState = CyberDefenseRunState.CreateActive(
            transitions[0].NextState.Sector,
            transitions[0].NextState.OpponentIndex,
            playerCurrentHp: 1,
            currentOpponent: transitions[0].NextState.CurrentOpponent);

        var fatalConfig = new SimulationRunConfig(
            profile: zeroProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 1,
            startingState: bossFatalState,
            enableBeginnerAssistance: true,
            startingConsecutiveEarlyDefeats: 0); // streak was reset upon reaching boss

        var fatalResult = HeadlessCombatSimulator.Run(fatalConfig);
        Assert.Equal(0, fatalResult.FinalState.ShieldCapacity);
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
    public void PermanentFirewallRank_PreventsAssistanceActivation()
    {
        // Player has permanent Firewall Rank 1 (purchased earlier)
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        var lowHpState = CyberDefenseRunState.CreateActive(
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
            startingState: lowHpState,
            enableBeginnerAssistance: true,
            startingConsecutiveEarlyDefeats: 2,
            permanentFirewallRank: 1); // Rank >= 1 makes assistance ineligible

        var result = HeadlessCombatSimulator.Run(config);

        Assert.True(result.TotalGameOvers > 0);
        // Rebooted state retains legitimate permanent capacity 1, but assistance was not activated
        Assert.Equal(1, result.FinalState.ShieldCapacity);
        Assert.Equal(1, result.FinalState.ActiveShields);
    }

    // =========================================================================
    // 6. DETERMINISTIC CANDIDATE SEEDS & REPRODUCIBILITY
    // =========================================================================

    [Theory]
    [InlineData(CandidateSeedHex)]
    [InlineData(2001UL)]
    [InlineData(2002UL)]
    [InlineData(2003UL)]
    [InlineData(2004UL)]
    [InlineData(2005UL)]
    public void ProfileE_CandidateSeeds_ExecuteDeterministically(ulong seed)
    {
        var profile = SyntheticPlayerProfiles.ProfileE_Beginner;
        var config1 = new SimulationRunConfig(profile, seed, maxTurns: 20, enableBeginnerAssistance: true);
        var config2 = new SimulationRunConfig(profile, seed, maxTurns: 20, enableBeginnerAssistance: true);

        var res1 = HeadlessCombatSimulator.Run(config1);
        var res2 = HeadlessCombatSimulator.Run(config2);

        Assert.Equal(res1.FinalState, res2.FinalState);
        Assert.Equal(res1.Telemetry, res2.Telemetry);
        Assert.Equal(res1.TotalExecutedTurns, res2.TotalExecutedTurns);
        Assert.Equal(res1.TotalGameOvers, res2.TotalGameOvers);
        Assert.Equal(res1.TotalVirtualTimeMs, res2.TotalVirtualTimeMs);
    }
}
