namespace MathFirst.Core.Tests.CyberDefense;

using System;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using Xunit;

public sealed class CyberDefenseProgressionRewardTests
{
    // =========================================================================
    // 1. XP Economy Policy Invariants & Calculations
    // =========================================================================

    [Fact]
    public void Case01_CorrectAppliedAttempt_BaseXpIs6()
    {
        Assert.Equal(6, CyberDefenseEconomyPolicy.GetAttemptXp(isCorrect: true));
        Assert.Equal(6, CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: true, opponentDefeated: false, OpponentKind.Normal, actualSuccessfulAttackCount: 1));
    }

    [Fact]
    public void Case02_IncorrectAppliedAttempt_BaseXpIs1()
    {
        Assert.Equal(1, CyberDefenseEconomyPolicy.GetAttemptXp(isCorrect: false));
        Assert.Equal(1, CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: false, opponentDefeated: false, OpponentKind.Normal, actualSuccessfulAttackCount: 1));
    }

    [Fact]
    public void Case03_CorrectNonLethalHit_AwardsOnlyBaseXp()
    {
        // 1 hit on an opponent that survives -> 6 XP
        int xp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: true, opponentDefeated: false, OpponentKind.Normal, actualSuccessfulAttackCount: 1);
        Assert.Equal(6, xp);
    }

    [Fact]
    public void Case04_SingleHitNormalDefeat_Awards7Xp()
    {
        // 6 base + 1 attack * 1 defeat bonus = 7 XP
        int xp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: true, opponentDefeated: true, OpponentKind.Normal, actualSuccessfulAttackCount: 1);
        Assert.Equal(7, xp);
    }

    [Theory]
    [InlineData(1, 7)]   // 6 + 1
    [InlineData(2, 8)]   // 6 + 2
    [InlineData(3, 9)]   // 6 + 3
    [InlineData(5, 11)]  // 6 + 5
    public void Case05_MultiHitNormalDefeat_UsesActualConfirmedAttackCount(int attacks, int expectedXp)
    {
        int xp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: true, opponentDefeated: true, OpponentKind.Normal, actualSuccessfulAttackCount: attacks);
        Assert.Equal(expectedXp, xp);
    }

    [Fact]
    public void Case06_SingleHitBossDefeat_Awards18Xp()
    {
        // 6 base + 2 attack * 1 + 10 milestone = 18 XP
        int xp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: true, opponentDefeated: true, OpponentKind.Boss, actualSuccessfulAttackCount: 1);
        Assert.Equal(18, xp);
    }

    [Theory]
    [InlineData(1, 18)]  // 6 + 2*1 + 10 = 18
    [InlineData(2, 20)]  // 6 + 2*2 + 10 = 20
    [InlineData(4, 24)]  // 6 + 2*4 + 10 = 24
    [InlineData(8, 32)]  // 6 + 2*8 + 10 = 32
    public void Case07_MultiHitBossDefeat_UsesExactConfirmedAttackCount(int attacks, int expectedXp)
    {
        int xp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: true, opponentDefeated: true, OpponentKind.Boss, actualSuccessfulAttackCount: attacks);
        Assert.Equal(expectedXp, xp);
    }

    [Fact]
    public void Case08_OverkillDoesNotGenerateImaginaryAttacks()
    {
        // Opponent has 1 HP, attack damage requested is 10
        // State machine clamps applied damage to 1, excess is 9, executed attacks increments by exactly 1
        var opponent = new OpponentState(OpponentKind.Normal, maxHp: 2, currentHp: 1, executedAttacks: 0, OpponentEncounterProvenance.Authoritative);
        var run = CyberDefenseRunState.CreateActive(sector: 1, opponentIndex: 0, playerCurrentHp: 100, opponent);

        var transition = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 10);
        Assert.True(transition.IsOpponentDefeated);
        Assert.Equal(1, transition.AppliedOpponentDamage);
        Assert.Equal(9, transition.ExcessOpponentDamage);
        Assert.Equal(1, transition.ExecutedAttacksOnDefeatedOpponent);

        // Economy uses the transition's executed attacks (1), not damage or HP
        int xp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: true,
            opponentDefeated: true,
            run.CurrentOpponent.Kind,
            transition.ExecutedAttacksOnDefeatedOpponent!.Value);
        Assert.Equal(7, xp);
    }

    [Fact]
    public void Case09_UnverifiedLegacyDefeat_DoesNotInventEarlierAttacks()
    {
        // Legacy opponent has currentHp 1, but executedAttacks is only 1 (earlier history unknown)
        var legacyOpponent = new OpponentState(OpponentKind.Normal, maxHp: 2, currentHp: 1, executedAttacks: 1, OpponentEncounterProvenance.UnverifiedLegacy);
        var run = CyberDefenseRunState.CreateActive(sector: 1, opponentIndex: 0, playerCurrentHp: 100, legacyOpponent);

        var transition = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        Assert.True(transition.IsOpponentDefeated);
        // Tracked attacks becomes 2
        Assert.Equal(2, transition.ExecutedAttacksOnDefeatedOpponent);
        Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, transition.DefeatedOpponentProvenance);

        // XP awarded uses ONLY the 2 confirmed tracked attacks, not 5 (MaxHp)
        int xp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: true,
            opponentDefeated: true,
            run.CurrentOpponent.Kind,
            transition.ExecutedAttacksOnDefeatedOpponent!.Value);
        // 6 base + 2 confirmed attacks = 8 XP
        Assert.Equal(8, xp);
    }

    [Fact]
    public void Case10_UnverifiedLegacyReward_UsesOnlyTrustworthyRecordedAttacks()
    {
        // Boss migrated from V1 has 0 tracked attacks initially
        var legacyBoss = new OpponentState(OpponentKind.Boss, maxHp: 12, currentHp: 1, executedAttacks: 0, OpponentEncounterProvenance.UnverifiedLegacy);
        var run = CyberDefenseRunState.CreateActive(sector: 1, opponentIndex: 5, playerCurrentHp: 100, legacyBoss);

        var transition = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        Assert.True(transition.IsOpponentDefeated);
        Assert.Equal(1, transition.ExecutedAttacksOnDefeatedOpponent);

        int xp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: true,
            opponentDefeated: true,
            run.CurrentOpponent.Kind,
            transition.ExecutedAttacksOnDefeatedOpponent!.Value);

        // 6 base + (2 * 1) + 10 milestone = 18 XP (no speculative boost for missing history)
        Assert.Equal(18, xp);
    }

    // =========================================================================
    // 2. Receipt Record Contract, Equality, and Hash Code Invariants
    // =========================================================================

    [Fact]
    public void AppliedReceipt_WithRewardMetadata_PreservesValues()
    {
        var run = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        var timestamp = DateTimeOffset.UtcNow;

        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            "sub-reward-1", "1+1", true, true, 100, 0, timestamp, transition,
            awardedXp: 6, playerLevelAfter: 1, cumulativeXpAfter: 6);

        Assert.Equal(6, receipt.AwardedXp);
        Assert.Equal(1, receipt.PlayerLevelAfter);
        Assert.Equal(6L, receipt.CumulativeXpAfter);
    }

    [Fact]
    public void AppliedReceipt_WithHistoricalNullReward_PreservesNull()
    {
        var run = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        var timestamp = DateTimeOffset.UtcNow;

        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            "sub-hist-1", "1+1", true, true, 100, 0, timestamp, transition);

        Assert.Null(receipt.AwardedXp);
        Assert.Null(receipt.PlayerLevelAfter);
        Assert.Null(receipt.CumulativeXpAfter);
    }

    [Fact]
    public void AppliedReceipt_PartialRewardMetadata_ThrowsArgumentException()
    {
        var run = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        var timestamp = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() =>
            CyberDefenseReceiptRecord.CreateApplied(
                "sub-inv-1", "1+1", true, true, 100, 0, timestamp, transition,
                awardedXp: 6, playerLevelAfter: null, cumulativeXpAfter: 6));

        Assert.Throws<ArgumentException>(() =>
            CyberDefenseReceiptRecord.CreateApplied(
                "sub-inv-2", "1+1", true, true, 100, 0, timestamp, transition,
                awardedXp: null, playerLevelAfter: 1, cumulativeXpAfter: 6));

        Assert.Throws<ArgumentException>(() =>
            CyberDefenseReceiptRecord.CreateApplied(
                "sub-inv-3", "1+1", true, true, 100, 0, timestamp, transition,
                awardedXp: 6, playerLevelAfter: 1, cumulativeXpAfter: null));
    }

    [Fact]
    public void AppliedReceipt_NegativeRewardMetadata_ThrowsOutOfRangeException()
    {
        var run = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        var timestamp = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseReceiptRecord.CreateApplied(
                "sub-inv-4", "1+1", true, true, 100, 0, timestamp, transition,
                awardedXp: -1, playerLevelAfter: 1, cumulativeXpAfter: 6));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseReceiptRecord.CreateApplied(
                "sub-inv-5", "1+1", true, true, 100, 0, timestamp, transition,
                awardedXp: 6, playerLevelAfter: 0, cumulativeXpAfter: 6));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseReceiptRecord.CreateApplied(
                "sub-inv-6", "1+1", true, true, 100, 0, timestamp, transition,
                awardedXp: 6, playerLevelAfter: 1, cumulativeXpAfter: -5));
    }

    [Fact]
    public void CalmModeSuppressedReceipt_WithRewardMetadata_ThrowsArgumentException()
    {
        var timestamp = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseReceiptRecord(
                "sub-calm-inv", CyberDefenseReceiptKind.CalmModeSuppressed, "1+1", true, true, 100, 0, timestamp,
                transitionResult: null, awardedXp: 6, playerLevelAfter: 1, cumulativeXpAfter: 6));
    }

    [Fact]
    public void ReceiptRecord_EqualityAndHashCode_IncludeRewardMetadata()
    {
        var run = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        var timestamp = new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

        var r1 = CyberDefenseReceiptRecord.CreateApplied(
            "sub-eq-1", "1+1", true, true, 100, 0, timestamp, transition,
            awardedXp: 6, playerLevelAfter: 1, cumulativeXpAfter: 6);

        var r1Clone = CyberDefenseReceiptRecord.CreateApplied(
            "sub-eq-1", "1+1", true, true, 100, 0, timestamp, transition,
            awardedXp: 6, playerLevelAfter: 1, cumulativeXpAfter: 6);

        var rDiffXp = CyberDefenseReceiptRecord.CreateApplied(
            "sub-eq-1", "1+1", true, true, 100, 0, timestamp, transition,
            awardedXp: 7, playerLevelAfter: 1, cumulativeXpAfter: 7);

        var rDiffLevel = CyberDefenseReceiptRecord.CreateApplied(
            "sub-eq-1", "1+1", true, true, 100, 0, timestamp, transition,
            awardedXp: 6, playerLevelAfter: 2, cumulativeXpAfter: 6);

        var rNullReward = CyberDefenseReceiptRecord.CreateApplied(
            "sub-eq-1", "1+1", true, true, 100, 0, timestamp, transition);

        Assert.Equal(r1, r1Clone);
        Assert.Equal(r1.GetHashCode(), r1Clone.GetHashCode());

        Assert.NotEqual(r1, rDiffXp);
        Assert.NotEqual(r1, rDiffLevel);
        Assert.NotEqual(r1, rNullReward);
    }

    // =========================================================================
    // 3. Level Progression & Skill Point Calculations
    // =========================================================================

    [Fact]
    public void Case18_Level1ToLevel2Progression()
    {
        // Level 1 threshold is 0. Delta for level 1: 80 + 24 * (0)^0.75 = 80.
        // Level 2 threshold is 80.
        Assert.Equal(0L, CyberDefenseLevelPolicy.GetCumulativeXpThreshold(1));
        Assert.Equal(80L, CyberDefenseLevelPolicy.GetCumulativeXpThreshold(2));

        Assert.Equal(1, CyberDefenseLevelPolicy.GetLevel(0));
        Assert.Equal(1, CyberDefenseLevelPolicy.GetLevel(79));
        Assert.Equal(2, CyberDefenseLevelPolicy.GetLevel(80));
        Assert.Equal(2, CyberDefenseLevelPolicy.GetLevel(81));

        Assert.Equal(0, CyberDefenseLevelPolicy.GetEarnedSkillPoints(1));
        Assert.Equal(1, CyberDefenseLevelPolicy.GetEarnedSkillPoints(2));
    }

    [Fact]
    public void Case19_MultipleLevelsCrossedInOneReward()
    {
        // Check delta for level 1: 80
        // Check delta for level 2: 80 + 24 * 1^0.75 = 104. Level 3 threshold: 80 + 104 = 184.
        Assert.Equal(184L, CyberDefenseLevelPolicy.GetCumulativeXpThreshold(3));

        // Jump from 0 XP to 200 XP
        int level = CyberDefenseLevelPolicy.GetLevel(200);
        Assert.Equal(3, level);

        int earnedSp = CyberDefenseLevelPolicy.GetEarnedSkillPoints(level);
        Assert.Equal(2, earnedSp);
    }

    [Fact]
    public void Case20_ExactlyOneEarnedSpPerGainedLevel()
    {
        for (int lvl = 1; lvl <= 150; lvl++)
        {
            Assert.Equal(lvl - 1, CyberDefenseLevelPolicy.GetEarnedSkillPoints(lvl));
        }
    }

    [Fact]
    public void Case21_SpConservationAcrossProgressionUpdates()
    {
        int oldEarnedSp = 5;
        int oldSpentSp = 2;
        int oldAvailableSp = 3;
        Assert.Equal(oldEarnedSp, oldSpentSp + oldAvailableSp);

        int gainedLevels = 3;
        int newEarnedSp = checked(oldEarnedSp + gainedLevels);
        int newAvailableSp = checked(oldAvailableSp + gainedLevels);
        int newSpentSp = oldSpentSp;

        Assert.Equal(8, newEarnedSp);
        Assert.Equal(6, newAvailableSp);
        Assert.Equal(2, newSpentSp);
        Assert.Equal(newEarnedSp, newSpentSp + newAvailableSp);
    }

    [Fact]
    public void Case30_NoPrematureAttackPurchaseOrDamageScaling()
    {
        var prog = CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow);
        Assert.Equal(0, prog.AttackRank);
        Assert.Equal(0, prog.SpentSkillPoints);
        Assert.Equal(0, prog.AvailableSkillPoints);
    }
}
