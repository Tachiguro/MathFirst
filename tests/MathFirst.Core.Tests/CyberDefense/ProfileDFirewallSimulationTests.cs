namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using MathFirst.Core.Tests.CyberDefense.Simulator;
using MathFirst.Domain.CyberDefense;
using Xunit;

/// <summary>
/// Authoritative deterministic simulation verification for permanent Firewall shields (GDD §8.2)
/// exercising Synthetic Profile D (Learner, ~70% accuracy).
///
/// Invariants verified:
/// - Maximum 3 permanent shield slots (Ranks 1/2/3 unlocked at Player Levels 5/20/50).
/// - Correct answer +2 recharge units, incorrect answer +1, recharge threshold 30 units.
/// - Shield absorption consumes 1 active shield for eligible incoming attack, absorbs full damage, and protects HP.
/// - Unshielded attempts take authoritative damage.
/// - Upgrade preferences on Profile D are descriptive metadata only; purchases are not simulated here.
/// - Deterministic seed reproducibility and identical attempt sampling across configurations.
/// </summary>
public sealed class ProfileDFirewallSimulationTests
{
    // Proposed candidate seeds from task specification
    private const ulong CandidateSeedHex = 0xD15EFUL;
    private static readonly ulong[] CandidateSeeds = { 1001UL, 1002UL, 1003UL, 1004UL, 1005UL };

    // =========================================================================
    // 1. DESCRIPTIVE METADATA & LEGAL STARTING STATE INVARIANTS
    // =========================================================================

    [Fact]
    public void ProfileD_UpgradePreference_IsDescriptiveMetadata_NotExecutablePurchase()
    {
        var profile = SyntheticPlayerProfiles.ProfileD_Learner;

        Assert.Equal(SyntheticPlayerProfiles.ProfileDId, profile.Id);
        Assert.Equal(0.70, profile.AccuracyProbability);
        Assert.Equal(SyntheticUpgradePreference.Defensive, profile.UpgradePreference);
        Assert.False(profile.IsUpgradeStrategyExecutable, "Profile D upgrade preference is descriptive metadata and must not claim executable purchases.");
    }

    [Theory]
    [InlineData(0, "Unupgraded baseline: zero shields")]
    [InlineData(1, "Rank 1 Firewall: 1 slot (Level 5 unlock, 3 SP)")]
    [InlineData(2, "Rank 2 Firewall: 2 slots (Level 20 unlock, 8 SP)")]
    [InlineData(3, "Rank 3 Firewall: 3 slots (Level 50 unlock, 15 SP)")]
    public void LegalSyntheticStartingStates_ConformToAuthoritativeInvariants(int rank, string description)
    {
        Assert.NotNull(description);
        var state = CyberDefenseRunState.InitialRun(shieldCapacity: rank);

        Assert.Equal(1, state.Sector);
        Assert.Equal(0, state.OpponentIndex);
        Assert.Equal(CyberDefenseCombatPolicy.PlayerMaxHp, state.PlayerCurrentHp);
        Assert.Equal(rank, state.ShieldCapacity);
        Assert.Equal(rank, state.ActiveShields);
        Assert.Equal(0, state.ShieldRechargeUnits);
        Assert.True(state.ShieldCapacity <= CyberDefenseFirewallPolicy.MaxShieldCapacity);
        Assert.True(state.ActiveShields <= state.ShieldCapacity);
    }

    [Fact]
    public void IllegalStartingCapacity_AboveMaxRank_IsRejectedByDomain()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.InitialRun(shieldCapacity: CyberDefenseFirewallPolicy.MaxShieldCapacity + 1));
    }

    // =========================================================================
    // 2. SHIELD ABSORPTION AND DAMAGE PREVENTION
    // =========================================================================

    [Fact]
    public void Shield_AbsorbsIncomingDamage_ProtectsPlayerHp_AndRecordsTelemetry()
    {
        var startingState = CyberDefenseRunState.InitialRun(shieldCapacity: 1);
        var zeroProfile = new SyntheticPlayerProfile(
            id: "zero_acc",
            name: "Zero Accuracy",
            accuracyProbability: 0.0,
            minSyntheticLatencyMilliseconds: 1000,
            maxSyntheticLatencyMilliseconds: 1000);

        CyberDefenseCombatTransitionResult? turnResult = null;
        var config = new SimulationRunConfig(
            profile: zeroProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 1,
            startingState: startingState,
            onTurnCompleted: t => turnResult = t);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.NotNull(turnResult);
        Assert.False(turnResult.IsCorrect);
        Assert.True(turnResult.IsShieldAbsorbed);
        Assert.Equal(4, turnResult.AbsorbedEnemyDamage); // Sector 1 Normal enemy damage is 4
        Assert.Equal(0, turnResult.AppliedPlayerDamage);
        Assert.Equal(CyberDefenseCombatPolicy.PlayerMaxHp, turnResult.NextState.PlayerCurrentHp);
        Assert.Equal(0, turnResult.NextState.ActiveShields);
        Assert.Equal(1, turnResult.NextState.ShieldCapacity);
        Assert.Equal(CyberDefenseFirewallPolicy.IncorrectAttemptRechargeUnits, turnResult.NextState.ShieldRechargeUnits);

        // Telemetry must authoritatively record exactly 1 shield absorbed
        Assert.Equal(1L, result.Telemetry.TotalShieldsAbsorbed);
        Assert.Equal(1L, result.Telemetry.ShieldsAbsorbed);
        Assert.Equal(0L, result.Telemetry.TotalAppliedPlayerDamage);
    }

    [Fact]
    public void DepletedShield_DoesNotAbsorb_PlayerTakesAuthoritativeDamage()
    {
        // Start with capacity 1, but 0 active shields
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        var depletedState = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: CyberDefenseCombatPolicy.PlayerMaxHp,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp),
            shieldCapacity: 1,
            activeShields: 0,
            shieldRechargeUnits: 10);

        var zeroProfile = new SyntheticPlayerProfile(
            id: "zero_acc",
            name: "Zero Accuracy",
            accuracyProbability: 0.0,
            minSyntheticLatencyMilliseconds: 1000,
            maxSyntheticLatencyMilliseconds: 1000);

        CyberDefenseCombatTransitionResult? turnResult = null;
        var config = new SimulationRunConfig(
            profile: zeroProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: 1,
            startingState: depletedState,
            onTurnCompleted: t => turnResult = t);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.NotNull(turnResult);
        Assert.False(turnResult.IsShieldAbsorbed);
        Assert.Equal(0, turnResult.AbsorbedEnemyDamage);
        Assert.Equal(4, turnResult.AppliedPlayerDamage);
        Assert.Equal(CyberDefenseCombatPolicy.PlayerMaxHp - 4, turnResult.NextState.PlayerCurrentHp);
        Assert.Equal(0, turnResult.NextState.ActiveShields);
        Assert.Equal(11, turnResult.NextState.ShieldRechargeUnits); // 10 + 1 for incorrect answer

        Assert.Equal(0L, result.Telemetry.TotalShieldsAbsorbed);
        Assert.Equal(4L, result.Telemetry.TotalAppliedPlayerDamage);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void MultiSlotFirewall_AbsorbsConsecutiveAttacksUpToCapacity(int capacity)
    {
        var startingState = CyberDefenseRunState.InitialRun(shieldCapacity: capacity);
        var zeroProfile = new SyntheticPlayerProfile(
            id: "zero_acc",
            name: "Zero Accuracy",
            accuracyProbability: 0.0,
            minSyntheticLatencyMilliseconds: 1000,
            maxSyntheticLatencyMilliseconds: 1000);

        var transitions = new List<CyberDefenseCombatTransitionResult>();
        var config = new SimulationRunConfig(
            profile: zeroProfile,
            masterSeed: CandidateSeedHex,
            maxTurns: capacity,
            startingState: startingState,
            onTurnCompleted: t => transitions.Add(t));

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(capacity, transitions.Count);
        for (int i = 0; i < capacity; i++)
        {
            Assert.True(transitions[i].IsShieldAbsorbed, $"Turn {i + 1} should be absorbed.");
            Assert.Equal(4, transitions[i].AbsorbedEnemyDamage);
            Assert.Equal(0, transitions[i].AppliedPlayerDamage);
            Assert.Equal(capacity - (i + 1), transitions[i].NextState.ActiveShields);
        }

        Assert.Equal(CyberDefenseCombatPolicy.PlayerMaxHp, result.FinalState.PlayerCurrentHp);
        Assert.Equal(0, result.FinalState.ActiveShields);
        Assert.Equal(capacity, result.Telemetry.TotalShieldsAbsorbed);
        Assert.Equal(0L, result.Telemetry.TotalAppliedPlayerDamage);
    }

    // =========================================================================
    // 3. RECHARGE ACCOUNTING AND CLAMPING (+2 CORRECT, +1 INCORRECT, THRESHOLD 30)
    // =========================================================================

    [Fact]
    public void Recharge_CorrectAnswer_AddsTwoUnits()
    {
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        var state = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 100,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp),
            shieldCapacity: 1,
            activeShields: 0,
            shieldRechargeUnits: 10);

        var perfectProfile = SyntheticPlayerProfiles.ProfileA_Perfect;
        var config = new SimulationRunConfig(perfectProfile, CandidateSeedHex, maxTurns: 1, startingState: state);

        var result = HeadlessCombatSimulator.Run(config);

        // 10 + 2 = 12
        Assert.Equal(12, result.FinalState.ShieldRechargeUnits);
        Assert.Equal(0, result.FinalState.ActiveShields);
    }

    [Fact]
    public void Recharge_IncorrectAnswer_AddsOneUnit()
    {
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        var state = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 100,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp),
            shieldCapacity: 1,
            activeShields: 0,
            shieldRechargeUnits: 10);

        var zeroProfile = new SyntheticPlayerProfile("z", "z", 0.0, 1000, 1000);
        var config = new SimulationRunConfig(zeroProfile, CandidateSeedHex, maxTurns: 1, startingState: state);

        var result = HeadlessCombatSimulator.Run(config);

        // 10 + 1 = 11
        Assert.Equal(11, result.FinalState.ShieldRechargeUnits);
        Assert.Equal(0, result.FinalState.ActiveShields);
    }

    [Fact]
    public void Recharge_AtThreshold30_RestoresShield_AndClampsAtCapacity()
    {
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        // Capacity 1, active 0, recharge units 29: +2 from correct answer gives 31 >= 30
        var state = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 100,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp),
            shieldCapacity: 1,
            activeShields: 0,
            shieldRechargeUnits: 29);

        var perfectProfile = SyntheticPlayerProfiles.ProfileA_Perfect;
        var config = new SimulationRunConfig(perfectProfile, CandidateSeedHex, maxTurns: 1, startingState: state);

        var result = HeadlessCombatSimulator.Run(config);

        // Shield restored to 1 == capacity; recharge units reset to 0
        Assert.Equal(1, result.FinalState.ActiveShields);
        Assert.Equal(0, result.FinalState.ShieldRechargeUnits);
    }

    [Fact]
    public void Recharge_AtThreshold30_RestoresShield_AndCarriesSurplusWhenBelowCapacity()
    {
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        // Capacity 2, active 0, recharge units 29: +2 gives 31 >= 30. Restores to 1 < 2, surplus is 1
        var state = CyberDefenseRunState.CreateActive(
            sector: 1,
            opponentIndex: 0,
            playerCurrentHp: 100,
            currentOpponent: new OpponentState(OpponentKind.Normal, maxHp, maxHp),
            shieldCapacity: 2,
            activeShields: 0,
            shieldRechargeUnits: 29);

        var perfectProfile = SyntheticPlayerProfiles.ProfileA_Perfect;
        var config = new SimulationRunConfig(perfectProfile, CandidateSeedHex, maxTurns: 1, startingState: state);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(1, result.FinalState.ActiveShields);
        Assert.Equal(1, result.FinalState.ShieldRechargeUnits);
    }

    [Fact]
    public void Recharge_AtFullCapacity_RemainsZeroUnits()
    {
        var state = CyberDefenseRunState.InitialRun(shieldCapacity: 2);
        var perfectProfile = SyntheticPlayerProfiles.ProfileA_Perfect;
        var config = new SimulationRunConfig(perfectProfile, CandidateSeedHex, maxTurns: 5, startingState: state);

        var result = HeadlessCombatSimulator.Run(config);

        Assert.Equal(2, result.FinalState.ActiveShields);
        Assert.Equal(0, result.FinalState.ShieldRechargeUnits);
    }

    // =========================================================================
    // 4. DETERMINISTIC MATCHED SEEDS AND COMPARATIVE TRACES
    // =========================================================================

    [Theory]
    [InlineData(CandidateSeedHex)]
    [InlineData(1001UL)]
    [InlineData(1002UL)]
    [InlineData(1003UL)]
    [InlineData(1004UL)]
    [InlineData(1005UL)]
    public void MatchedSeeds_ProduceIdenticalAttemptSequences_WithDifferentialShieldOutcomes(ulong seed)
    {
        var profile = SyntheticPlayerProfiles.ProfileD_Learner;
        const long turns = 30;

        // Run 1: Unshielded baseline (Capacity 0)
        var unshieldedConfig = new SimulationRunConfig(
            profile,
            masterSeed: seed,
            maxTurns: turns,
            startingState: CyberDefenseRunState.InitialRun(shieldCapacity: 0),
            trackShieldTelemetry: true);

        // Run 2: Shielded (Capacity 1)
        var shieldedConfig = new SimulationRunConfig(
            profile,
            masterSeed: seed,
            maxTurns: turns,
            startingState: CyberDefenseRunState.InitialRun(shieldCapacity: 1));

        var unshieldedResult = HeadlessCombatSimulator.Run(unshieldedConfig);
        var shieldedResult = HeadlessCombatSimulator.Run(shieldedConfig);

        // Invariant: Both runs sample the exact same attempts and virtual latency
        Assert.Equal(unshieldedResult.TotalExecutedTurns, shieldedResult.TotalExecutedTurns);
        Assert.Equal(unshieldedResult.Telemetry.CorrectAnswers, shieldedResult.Telemetry.CorrectAnswers);
        Assert.Equal(unshieldedResult.Telemetry.IncorrectAnswers, shieldedResult.Telemetry.IncorrectAnswers);
        Assert.Equal(unshieldedResult.TotalVirtualTimeMs, shieldedResult.TotalVirtualTimeMs);

        // Telemetry shield comparison:
        Assert.Equal(0L, unshieldedResult.Telemetry.TotalShieldsAbsorbed);
        if (shieldedResult.Telemetry.IncorrectAnswers > 0)
        {
            // When mistakes occurred, the shielded run must have absorbed at least one hit
            Assert.True(shieldedResult.Telemetry.TotalShieldsAbsorbed >= 1L);
            Assert.True(shieldedResult.Telemetry.TotalAppliedPlayerDamage <= unshieldedResult.Telemetry.TotalAppliedPlayerDamage);
        }
    }

    [Fact]
    public void RepeatedIdenticalRuns_ProduceBitExactStateAndTelemetry()
    {
        var profile = SyntheticPlayerProfiles.ProfileD_Learner;
        var config1 = new SimulationRunConfig(profile, CandidateSeedHex, maxTurns: 50, startingState: CyberDefenseRunState.InitialRun(shieldCapacity: 2));
        var config2 = new SimulationRunConfig(profile, CandidateSeedHex, maxTurns: 50, startingState: CyberDefenseRunState.InitialRun(shieldCapacity: 2));

        var res1 = HeadlessCombatSimulator.Run(config1);
        var res2 = HeadlessCombatSimulator.Run(config2);

        Assert.Equal(res1.FinalState, res2.FinalState);
        Assert.Equal(res1.Telemetry, res2.Telemetry);
        Assert.Equal(res1.TotalExecutedTurns, res2.TotalExecutedTurns);
        Assert.Equal(res1.TotalGameOvers, res2.TotalGameOvers);
        Assert.Equal(res1.TotalVirtualTimeMs, res2.TotalVirtualTimeMs);
        Assert.Equal(res1.Telemetry.TotalShieldsAbsorbed, res2.Telemetry.TotalShieldsAbsorbed);
    }
}
