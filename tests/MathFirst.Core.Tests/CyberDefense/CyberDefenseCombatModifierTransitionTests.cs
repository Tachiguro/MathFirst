using System.Reflection;
using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseCombatModifierTransitionTests
{
    private static CyberDefenseCombatModifierOutcome Resolve(
        CyberDefenseCombatModifierState state,
        bool isCorrect,
        int criticalRank,
        int overdriveRank = 0)
    {
        return CyberDefenseCombatModifierTransition.ResolveAttempt(state, isCorrect, criticalRank, overdriveRank);
    }

    private static CyberDefenseCombatModifierState Run(
        CyberDefenseCombatModifierState state,
        int criticalRank,
        int overdriveRank,
        params bool[] answers)
    {
        foreach (bool answer in answers)
        {
            state = Resolve(state, answer, criticalRank, overdriveRank).NextState;
        }

        return state;
    }

    private static List<CyberDefenseCombatModifierOutcome> Collect(int criticalRank, int overdriveRank, int correctAttacks)
    {
        return Collect(CyberDefenseCombatModifierState.Initial, criticalRank, overdriveRank, correctAttacks);
    }

    private static List<CyberDefenseCombatModifierOutcome> Collect(
        CyberDefenseCombatModifierState state,
        int criticalRank,
        int overdriveRank,
        int correctAttacks)
    {
        var outcomes = new List<CyberDefenseCombatModifierOutcome>();
        for (int i = 0; i < correctAttacks; i++)
        {
            var outcome = Resolve(state, true, criticalRank, overdriveRank);
            outcomes.Add(outcome);
            state = outcome.NextState;
        }

        return outcomes;
    }

    // ---- State ----

    [Fact]
    public void InitialState_HasZeroCountersAndNoWindow()
    {
        var state = CyberDefenseCombatModifierState.Initial;

        Assert.Equal(0, state.CriticalCorrectAttackCount);
        Assert.Equal(0, state.OverdriveStreak);
        Assert.Equal(0, state.OverdriveRemainingBonusAttacks);
        Assert.Equal(default, state);
    }

    [Fact]
    public void State_HasValueEquality()
    {
        var a = new CyberDefenseCombatModifierState(3, 1, 1);
        var b = new CyberDefenseCombatModifierState(3, 1, 1);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new CyberDefenseCombatModifierState(3, 1, 0));
        Assert.NotEqual(a, new CyberDefenseCombatModifierState(3, 0, 2));
        Assert.NotEqual(a, new CyberDefenseCombatModifierState(4, 1, 1));
    }

    [Fact]
    public void State_TwoArgumentConstructor_HasNoActiveWindow()
    {
        var state = new CyberDefenseCombatModifierState(3, 2);

        Assert.Equal(new CyberDefenseCombatModifierState(3, 2, 0), state);
        Assert.Equal(0, state.OverdriveRemainingBonusAttacks);
    }

    [Fact]
    public void State_ExposesNoMutableMembers()
    {
        foreach (Type type in new[] { typeof(CyberDefenseCombatModifierState), typeof(CyberDefenseCombatModifierOutcome) })
        {
            Assert.True(type.IsValueType);
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.True(
                    property.SetMethod is null || property.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)),
                    $"{type.Name}.{property.Name} must be immutable.");
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Assert.True(field.IsInitOnly, $"{type.Name}.{field.Name} must be readonly.");
            }
        }
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(int.MinValue, 0, 0)]
    [InlineData(0, 0, int.MinValue)]
    public void State_RejectsNegativeCounters(int count, int streak, int remaining)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CyberDefenseCombatModifierState(count, streak, remaining));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void State_RejectsRemainingWindowAboveTwoAttacks(int remaining)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CyberDefenseCombatModifierState(0, 0, remaining));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(int.MaxValue, 0)]
    public void State_AcceptsWindowAndStreakCombinationsReachableByPlay(int streak, int remaining)
    {
        var state = new CyberDefenseCombatModifierState(0, streak, remaining);

        Assert.Equal(streak, state.OverdriveStreak);
        Assert.Equal(remaining, state.OverdriveRemainingBonusAttacks);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 2)]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    [InlineData(int.MaxValue, 2)]
    public void State_RejectsWindowOverlappingAStreakItDidNotStart(int streak, int remaining)
    {
        // An armed window restarts the streak at zero and each window attack advances the streak by
        // one, so while a window is active the streak always equals the window attacks already used.
        Assert.Throws<ArgumentException>(() => new CyberDefenseCombatModifierState(0, streak, remaining));
    }

    // ---- Purity and validation ----

    [Fact]
    public void ResolveAttempt_DoesNotMutateInputState()
    {
        var state = new CyberDefenseCombatModifierState(4, 4, 0);

        var outcome = Resolve(state, true, 1, 1);

        Assert.Equal(new CyberDefenseCombatModifierState(4, 4, 0), state);
        Assert.Equal(new CyberDefenseCombatModifierState(5, 0, 2), outcome.NextState);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void InvalidCriticalRank_IsRejectedForCorrectAndIncorrectAnswers(int rank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolve(CyberDefenseCombatModifierState.Initial, true, rank, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolve(CyberDefenseCombatModifierState.Initial, false, rank, 1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void InvalidOverdriveRank_IsRejectedForCorrectAndIncorrectAnswers(int rank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolve(CyberDefenseCombatModifierState.Initial, true, 1, rank));
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolve(CyberDefenseCombatModifierState.Initial, false, 1, rank));
    }

    // ---- DEC-007-01: Critical Strike ----

    [Fact]
    public void FirstCorrectAnswer_IncrementsBothCountersAndHasNoBonus()
    {
        var outcome = Resolve(CyberDefenseCombatModifierState.Initial, true, 1, 1);

        Assert.False(outcome.IsCriticalStrike);
        Assert.Equal(0, outcome.CriticalBonusPercent);
        Assert.Equal(0, outcome.OverdriveBonusPercent);
        Assert.Equal(new CyberDefenseCombatModifierState(1, 1, 0), outcome.NextState);
    }

    [Fact]
    public void CriticalRank1_TriggersAtAttacksFiveAndTenWithFiftyPercent()
    {
        var outcomes = Collect(1, 0, 10);

        for (int i = 0; i < outcomes.Count; i++)
        {
            bool expected = i + 1 is 5 or 10;
            Assert.Equal(expected, outcomes[i].IsCriticalStrike);
            Assert.Equal(expected ? 50 : 0, outcomes[i].CriticalBonusPercent);
        }

        Assert.Equal(10, outcomes[9].NextState.CriticalCorrectAttackCount);
    }

    [Fact]
    public void CriticalRank2_TriggersAtAttacksFourAndEightWithHundredPercent()
    {
        var outcomes = Collect(2, 0, 8);

        for (int i = 0; i < outcomes.Count; i++)
        {
            bool expected = i + 1 is 4 or 8;
            Assert.Equal(expected, outcomes[i].IsCriticalStrike);
            Assert.Equal(expected ? 100 : 0, outcomes[i].CriticalBonusPercent);
        }
    }

    [Fact]
    public void CriticalRank0_DoesNotAccumulateCorrectAttacks()
    {
        var outcomes = Collect(0, 0, 20);

        Assert.All(outcomes, o =>
        {
            Assert.False(o.IsCriticalStrike);
            Assert.Equal(0, o.CriticalBonusPercent);
            Assert.Equal(0, o.NextState.CriticalCorrectAttackCount);
        });
    }

    [Fact]
    public void CriticalRank0_LeavesAnExistingCountUnchanged()
    {
        var outcome = Resolve(new CyberDefenseCombatModifierState(3, 0, 0), true, 0);

        Assert.Equal(3, outcome.NextState.CriticalCorrectAttackCount);
        Assert.False(outcome.IsCriticalStrike);
    }

    [Fact]
    public void CriticalPurchase_DoesNotCountPrePurchaseAttacksRetroactively()
    {
        var beforePurchase = Run(CyberDefenseCombatModifierState.Initial, 0, 0, Enumerable.Repeat(true, 19).ToArray());

        var afterPurchase = Collect(beforePurchase, 1, 0, 5);

        Assert.All(afterPurchase.Take(4), o => Assert.False(o.IsCriticalStrike));
        Assert.True(afterPurchase[4].IsCriticalStrike);
        Assert.Equal(50, afterPurchase[4].CriticalBonusPercent);
    }

    [Fact]
    public void ResetCriticalPhase_ZeroesOnlyTheCriticalCounter()
    {
        var state = new CyberDefenseCombatModifierState(7, 1, 1);

        var reset = CyberDefenseCombatModifierTransition.ResetCriticalPhase(state);

        Assert.Equal(new CyberDefenseCombatModifierState(0, 1, 1), reset);
        Assert.Equal(new CyberDefenseCombatModifierState(7, 1, 1), state);
    }

    [Fact]
    public void CriticalRankChange_RestartsTriggerPhase()
    {
        // Three rank 1 hits; without the phase reset the rank 2 trigger would land on the first
        // following hit (ordinal 4).
        var atRank1 = Run(CyberDefenseCombatModifierState.Initial, 1, 0, true, true, true);
        var afterRankChange = CyberDefenseCombatModifierTransition.ResetCriticalPhase(atRank1);

        var outcomes = Collect(afterRankChange, 2, 0, 4);

        Assert.All(outcomes.Take(3), o => Assert.False(o.IsCriticalStrike));
        Assert.True(outcomes[3].IsCriticalStrike);
        Assert.Equal(100, outcomes[3].CriticalBonusPercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void IncorrectAnswer_NeverGrantsCriticalOrOverdrive(int rank)
    {
        // Counts 4 (rank 2) and 9 (rank 1) are one short of a trigger, so a wrongly counted
        // incorrect answer would land on a trigger ordinal; the active window would grant Overdrive.
        foreach (int count in new[] { 3, 4, 8, 9, 19 })
        {
            for (int overdriveRank = 0; overdriveRank <= CyberDefenseOverdrivePolicy.MaxRank; overdriveRank++)
            {
                foreach (var window in new[] { (0, 2), (1, 1), (2, 0) })
                {
                    // An armed window without a purchased Overdrive rank is rejected (see the
                    // rank 0 armed-window tests below).
                    if (overdriveRank == 0 && window.Item2 > 0)
                    {
                        continue;
                    }

                    var outcome = Resolve(new CyberDefenseCombatModifierState(count, window.Item1, window.Item2), false, rank, overdriveRank);

                    Assert.False(outcome.IsCriticalStrike);
                    Assert.Equal(0, outcome.CriticalBonusPercent);
                    Assert.Equal(0, outcome.OverdriveBonusPercent);
                }
            }
        }
    }

    [Fact]
    public void IncorrectAnswer_PreservesCriticalCount()
    {
        var outcome = Resolve(new CyberDefenseCombatModifierState(7, 6, 0), false, 1, 1);

        Assert.Equal(7, outcome.NextState.CriticalCorrectAttackCount);
    }

    [Fact]
    public void CriticalCount_IsNotResetByIncorrectAnswers_SoTriggerIsStillOnFourthCorrect()
    {
        var state = Run(CyberDefenseCombatModifierState.Initial, 2, 1, true, true, false, true);

        var fourthCorrect = Resolve(state, true, 2, 1);

        Assert.True(fourthCorrect.IsCriticalStrike);
        Assert.Equal(100, fourthCorrect.CriticalBonusPercent);
        Assert.Equal(4, fourthCorrect.NextState.CriticalCorrectAttackCount);
        Assert.Equal(2, fourthCorrect.NextState.OverdriveStreak);
    }

    [Fact]
    public void CriticalResults_MatchExistingCriticalStrikePolicy()
    {
        for (int rank = 1; rank <= CyberDefenseCriticalStrikePolicy.MaxRank; rank++)
        {
            for (int priorCount = 0; priorCount < 40; priorCount++)
            {
                var outcome = Resolve(new CyberDefenseCombatModifierState(priorCount, 0, 0), true, rank);

                Assert.Equal(
                    CyberDefenseCriticalStrikePolicy.GetCriticalBonusPercent(rank, priorCount + 1),
                    outcome.CriticalBonusPercent);
                Assert.Equal(outcome.CriticalBonusPercent > 0, outcome.IsCriticalStrike);
            }
        }
    }

    [Fact]
    public void CriticalCountOverflow_FailsClosedWhileActive()
    {
        var state = new CyberDefenseCombatModifierState(int.MaxValue, 1, 0);

        Assert.Throws<OverflowException>(() => Resolve(state, true, 1));
        Assert.Throws<OverflowException>(() => Resolve(state, true, 2));
    }

    [Fact]
    public void CriticalCountAtMaxValue_IsUnchangedAtRank0AndByIncorrectAnswers()
    {
        var state = new CyberDefenseCombatModifierState(int.MaxValue, 1, 0);

        Assert.Equal(int.MaxValue, Resolve(state, true, 0).NextState.CriticalCorrectAttackCount);
        Assert.Equal(int.MaxValue, Resolve(state, false, 2).NextState.CriticalCorrectAttackCount);
    }

    [Fact]
    public void CriticalCountAtMaxValueMinusOne_IncrementsToMaxValue()
    {
        var outcome = Resolve(new CyberDefenseCombatModifierState(int.MaxValue - 1, 0, 0), true, 1);

        Assert.Equal(int.MaxValue, outcome.NextState.CriticalCorrectAttackCount);
    }

    // ---- DEC-007-02 / DEC-007-04: Overdrive arming and windows ----

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 4)]
    public void Overdrive_ArmsAfterThresholdConsecutiveCorrectAnswers(int overdriveRank, int threshold)
    {
        var outcomes = Collect(0, overdriveRank, threshold);

        for (int i = 0; i < threshold - 1; i++)
        {
            Assert.Equal(new CyberDefenseCombatModifierState(0, i + 1, 0), outcomes[i].NextState);
        }

        Assert.Equal(new CyberDefenseCombatModifierState(0, 0, 2), outcomes[threshold - 1].NextState);
        Assert.Equal(threshold, CyberDefenseOverdrivePolicy.GetTriggerThreshold(overdriveRank));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 4)]
    public void Overdrive_TriggeringAnswerReceivesNoNewlyArmedBonus(int overdriveRank, int threshold)
    {
        var outcomes = Collect(0, overdriveRank, threshold);

        Assert.All(outcomes, o => Assert.Equal(0, o.OverdriveBonusPercent));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 4)]
    public void Overdrive_NextTwoAttacksGainFiftyPercentAndTheThirdDoesNot(int overdriveRank, int threshold)
    {
        var outcomes = Collect(0, overdriveRank, threshold + 3);

        Assert.Equal(50, outcomes[threshold].OverdriveBonusPercent);
        Assert.Equal(50, outcomes[threshold + 1].OverdriveBonusPercent);
        Assert.Equal(0, outcomes[threshold + 2].OverdriveBonusPercent);
        Assert.Equal(new CyberDefenseCombatModifierState(0, 1, 1), outcomes[threshold].NextState);
        Assert.Equal(new CyberDefenseCombatModifierState(0, 2, 0), outcomes[threshold + 1].NextState);
        Assert.Equal(new CyberDefenseCombatModifierState(0, 3, 0), outcomes[threshold + 2].NextState);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 4)]
    public void Overdrive_RearmsPeriodicallyDuringContinuedCorrectPlay(int overdriveRank, int threshold)
    {
        var outcomes = Collect(0, overdriveRank, 30);

        for (int i = 0; i < outcomes.Count; i++)
        {
            int answer = i + 1;
            bool armsHere = answer % threshold == 0;
            bool boosted = answer > threshold && answer % threshold is 1 or 2;

            Assert.Equal(boosted ? 50 : 0, outcomes[i].OverdriveBonusPercent);
            Assert.Equal(armsHere ? 2 : boosted ? 2 - (answer % threshold) : 0, outcomes[i].NextState.OverdriveRemainingBonusAttacks);
            Assert.Equal(answer % threshold, outcomes[i].NextState.OverdriveStreak);
        }
    }

    [Fact]
    public void Overdrive_Rank2UsesTheSameFiftyPercentTwoAttackWindowAsRank1()
    {
        var rank1 = Collect(0, 1, 7).Skip(5).Select(o => o.OverdriveBonusPercent);
        var rank2 = Collect(0, 2, 6).Skip(4).Select(o => o.OverdriveBonusPercent);

        Assert.Equal(new[] { 50, 50 }, rank1);
        Assert.Equal(new[] { 50, 50 }, rank2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Overdrive_NeverHasMoreThanOneWindowOrMoreThanFiftyPercent(int overdriveRank)
    {
        foreach (var outcome in Collect(0, overdriveRank, 200))
        {
            Assert.InRange(outcome.NextState.OverdriveRemainingBonusAttacks, 0, 2);
            Assert.True(outcome.OverdriveBonusPercent is 0 or 50);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Overdrive_Rank0_NeverAccumulatesStreakArmsOrGrantsBonus(int criticalRank)
    {
        var outcomes = Collect(criticalRank, 0, 20);

        Assert.All(outcomes, o =>
        {
            Assert.Equal(0, o.OverdriveBonusPercent);
            Assert.Equal(0, o.NextState.OverdriveStreak);
            Assert.Equal(0, o.NextState.OverdriveRemainingBonusAttacks);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(int.MaxValue - 1)]
    [InlineData(int.MaxValue)]
    public void Overdrive_Rank0_ResultHasZeroStreakForAnyStoredStreakAndAnswer(int storedStreak)
    {
        foreach (bool correct in new[] { true, false })
        {
            var outcome = Resolve(new CyberDefenseCombatModifierState(1, storedStreak, 0), correct, 1, 0);

            Assert.Equal(0, outcome.NextState.OverdriveStreak);
            Assert.Equal(0, outcome.NextState.OverdriveRemainingBonusAttacks);
            Assert.Equal(0, outcome.OverdriveBonusPercent);
            Assert.Equal(correct ? 2 : 1, outcome.NextState.CriticalCorrectAttackCount);
        }
    }

    // ---- OD-1: no pre-purchase Overdrive streak ----

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(19)]
    [InlineData(100)]
    public void OverdrivePurchase_Rank1_RequiresFiveNewConsecutiveCorrectAnswers(int correctAnswersBeforePurchase)
    {
        var beforePurchase = Run(
            CyberDefenseCombatModifierState.Initial,
            0,
            0,
            Enumerable.Repeat(true, correctAnswersBeforePurchase).ToArray());

        var afterPurchase = Collect(beforePurchase, 0, 1, 8);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(new CyberDefenseCombatModifierState(0, i + 1, 0), afterPurchase[i].NextState);
        }

        Assert.Equal(new CyberDefenseCombatModifierState(0, 0, 2), afterPurchase[4].NextState);
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 50, 50, 0 }, afterPurchase.Select(o => o.OverdriveBonusPercent));
    }

    [Fact]
    public void OverdrivePurchase_Rank1_FromInterruptedPrePurchaseStreakStillRequiresFiveNewAnswers()
    {
        var beforePurchase = Run(CyberDefenseCombatModifierState.Initial, 1, 0, true, true, false, true, true, true, true);

        var afterPurchase = Collect(beforePurchase, 1, 1, 5);

        Assert.All(afterPurchase.Take(4), o => Assert.Equal(0, o.NextState.OverdriveRemainingBonusAttacks));
        Assert.Equal(2, afterPurchase[4].NextState.OverdriveRemainingBonusAttacks);
        Assert.All(afterPurchase, o => Assert.Equal(0, o.OverdriveBonusPercent));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    public void OverdriveUpgrade_Rank1ToRank2_PreservesProgressEarnedWhileActive(int answersAtRank1, int answersToArmAtRank2)
    {
        var atRank1 = Run(CyberDefenseCombatModifierState.Initial, 0, 1, Enumerable.Repeat(true, answersAtRank1).ToArray());
        Assert.Equal(new CyberDefenseCombatModifierState(0, answersAtRank1, 0), atRank1);

        var atRank2 = Collect(atRank1, 0, 2, answersToArmAtRank2 + 2);

        Assert.All(atRank2.Take(answersToArmAtRank2 - 1), o => Assert.Equal(0, o.NextState.OverdriveRemainingBonusAttacks));
        Assert.Equal(new CyberDefenseCombatModifierState(0, 0, 2), atRank2[answersToArmAtRank2 - 1].NextState);
        Assert.Equal(50, atRank2[answersToArmAtRank2].OverdriveBonusPercent);
        Assert.Equal(50, atRank2[answersToArmAtRank2 + 1].OverdriveBonusPercent);
    }

    [Fact]
    public void OverdriveUpgrade_PrePurchaseAnswersDoNotCarryThroughRank1IntoRank2()
    {
        var beforePurchase = Run(CyberDefenseCombatModifierState.Initial, 0, 0, Enumerable.Repeat(true, 10).ToArray());
        var atRank1 = Run(beforePurchase, 0, 1, true, true);

        var atRank2 = Collect(atRank1, 0, 2, 2);

        Assert.Equal(new CyberDefenseCombatModifierState(0, 3, 0), atRank2[0].NextState);
        Assert.Equal(new CyberDefenseCombatModifierState(0, 0, 2), atRank2[1].NextState);
    }

    // ---- Rank 0 armed window fails closed ----

    [Theory]
    [InlineData(0, 2, true)]
    [InlineData(0, 2, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 1, false)]
    public void Overdrive_Rank0ArmedWindow_IsRejectedForCorrectAndIncorrectAnswers(int streak, int remaining, bool isCorrect)
    {
        for (int criticalRank = 0; criticalRank <= CyberDefenseCriticalStrikePolicy.MaxRank; criticalRank++)
        {
            var state = new CyberDefenseCombatModifierState(4, streak, remaining);

            var exception = Assert.Throws<ArgumentException>(() => Resolve(state, isCorrect, criticalRank, 0));

            Assert.Equal("state", exception.ParamName);
        }
    }

    [Fact]
    public void Overdrive_Rank0ArmedWindow_IsRejectedBeforeTheAttemptIsResolved()
    {
        // A resolved correct attempt at this Critical count would overflow; the invalid window must
        // be rejected first.
        var state = new CyberDefenseCombatModifierState(int.MaxValue, 0, 2);

        Assert.Throws<ArgumentException>(() => Resolve(state, true, 1, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Overdrive_ArmedWindow_RemainsValidWhileOverdriveIsPurchased(int overdriveRank)
    {
        var outcome = Resolve(new CyberDefenseCombatModifierState(0, 0, 2), true, 0, overdriveRank);

        Assert.Equal(50, outcome.OverdriveBonusPercent);
        Assert.Equal(new CyberDefenseCombatModifierState(0, 1, 1), outcome.NextState);
    }

    // ---- Trigger threshold versus window length ----

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OverdriveTriggerThreshold_ExceedsBonusWindowLength(int overdriveRank)
    {
        // A window attack advances the streak by one, so a threshold above the window length keeps a
        // new window from arming while one is still active.
        Assert.True(
            CyberDefenseOverdrivePolicy.GetTriggerThreshold(overdriveRank) > CyberDefenseOverdrivePolicy.Rank1BonusAttackCount,
            $"Overdrive rank {overdriveRank} threshold must exceed the bonus window length.");
    }

    [Fact]
    public void OverdriveTriggerThreshold_ExceedsBonusWindowLengthForEveryPurchasableRank()
    {
        for (int rank = 1; rank <= CyberDefenseOverdrivePolicy.MaxRank; rank++)
        {
            Assert.InRange(
                CyberDefenseOverdrivePolicy.GetTriggerThreshold(rank),
                CyberDefenseOverdrivePolicy.Rank1BonusAttackCount + 1,
                int.MaxValue);
        }
    }

    // ---- DEC-007-03: incorrect answers cancel Overdrive ----

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(4, 0)]
    public void IncorrectAnswer_ResetsStreakAndCancelsActiveWindow(int streak, int remaining)
    {
        var outcome = Resolve(new CyberDefenseCombatModifierState(6, streak, remaining), false, 1, 1);

        Assert.Equal(new CyberDefenseCombatModifierState(6, 0, 0), outcome.NextState);
        Assert.Equal(0, outcome.OverdriveBonusPercent);
    }

    [Fact]
    public void IncorrectAnswerDuringWindow_LeavesNoCarriedBonus()
    {
        var armed = Run(CyberDefenseCombatModifierState.Initial, 0, 2, true, true, true, true);
        var afterError = Resolve(armed, false, 0, 2).NextState;

        var following = Collect(afterError, 0, 2, 3);

        Assert.All(following, o => Assert.Equal(0, o.OverdriveBonusPercent));
    }

    [Fact]
    public void ShieldAbsorption_CannotPreserveStreakOrWindow_BecauseOnlyAnswerCorrectnessIsAnInput()
    {
        MethodInfo[] methods = typeof(CyberDefenseCombatModifierTransition)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(m => m.Name == nameof(CyberDefenseCombatModifierTransition.ResolveAttempt))
            .ToArray();

        MethodInfo method = Assert.Single(methods);
        Assert.Equal(
            new[] { typeof(CyberDefenseCombatModifierState), typeof(bool), typeof(int), typeof(int) },
            method.GetParameters().Select(p => p.ParameterType).ToArray());

        var outcome = Resolve(new CyberDefenseCombatModifierState(2, 1, 1), false, 1, 1);
        Assert.Equal(0, outcome.NextState.OverdriveStreak);
        Assert.Equal(0, outcome.NextState.OverdriveRemainingBonusAttacks);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OverdriveStreak_AtMaxValueArmsWithoutOverflowWhileOverdriveIsPurchased(int overdriveRank)
    {
        var outcome = Resolve(new CyberDefenseCombatModifierState(1, int.MaxValue, 0), true, 1, overdriveRank);

        Assert.Equal(new CyberDefenseCombatModifierState(2, 0, 2), outcome.NextState);
        Assert.Equal(0, outcome.OverdriveBonusPercent);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OverdriveStreak_MatchesExistingOverdrivePolicyBelowThresholdWhilePurchased(int overdriveRank)
    {
        int threshold = CyberDefenseOverdrivePolicy.GetTriggerThreshold(overdriveRank);
        for (int streak = 0; streak < threshold - 1; streak++)
        {
            foreach (bool correct in new[] { true, false })
            {
                var outcome = Resolve(new CyberDefenseCombatModifierState(0, streak, 0), correct, 0, overdriveRank);

                Assert.Equal(CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(streak, correct), outcome.NextState.OverdriveStreak);
            }
        }
    }

    // ---- Critical and Overdrive together ----

    [Fact]
    public void CriticalAndOverdriveBonuses_CoexistOnTheSameAttack()
    {
        // Critical rank 1 triggers on correct attack 5; Overdrive rank 2 armed on attack 4 boosts 5 and 6.
        var outcomes = Collect(1, 2, 6);

        Assert.True(outcomes[4].IsCriticalStrike);
        Assert.Equal(50, outcomes[4].CriticalBonusPercent);
        Assert.Equal(50, outcomes[4].OverdriveBonusPercent);
        Assert.False(outcomes[5].IsCriticalStrike);
        Assert.Equal(50, outcomes[5].OverdriveBonusPercent);
    }

    [Fact]
    public void CombinedBonuses_ComposeAdditivelyWithSingleCeiling()
    {
        // Critical rank 2 triggers on attack 12; Overdrive rank 1 armed on attack 10 boosts 11 and 12.
        var outcome = Collect(2, 1, 12)[11];

        Assert.Equal(100, outcome.CriticalBonusPercent);
        Assert.Equal(50, outcome.OverdriveBonusPercent);
        Assert.Equal(3, CyberDefenseDamageCompositionPolicy.GetEffectiveDamage(1, outcome.CriticalBonusPercent, outcome.OverdriveBonusPercent));
        Assert.Equal(8, CyberDefenseDamageCompositionPolicy.GetEffectiveDamage(3, outcome.CriticalBonusPercent, outcome.OverdriveBonusPercent));
        Assert.Equal(25, CyberDefenseDamageCompositionPolicy.GetEffectiveDamage(10, outcome.CriticalBonusPercent, outcome.OverdriveBonusPercent));
    }

    [Fact]
    public void OverdriveOnlyBonus_ComposesWithSingleCeiling()
    {
        var outcome = Collect(0, 1, 6)[5];

        Assert.False(outcome.IsCriticalStrike);
        Assert.Equal(2, CyberDefenseDamageCompositionPolicy.GetEffectiveDamage(1, outcome.CriticalBonusPercent, outcome.OverdriveBonusPercent));
        Assert.Equal(5, CyberDefenseDamageCompositionPolicy.GetEffectiveDamage(3, outcome.CriticalBonusPercent, outcome.OverdriveBonusPercent));
    }

    // ---- Game-over reset ----

    [Fact]
    public void ResetRunModifiers_ClearsAllRunScopedState()
    {
        var played = Run(CyberDefenseCombatModifierState.Initial, 1, 1, true, true, true, true, true, true, false, true, true, true, true, true);
        Assert.NotEqual(CyberDefenseCombatModifierState.Initial, played);

        Assert.Equal(CyberDefenseCombatModifierState.Initial, CyberDefenseCombatModifierTransition.ResetRunModifiers());
    }

    [Fact]
    public void AfterRunReset_TriggersStartFromZeroAgain()
    {
        var reset = CyberDefenseCombatModifierTransition.ResetRunModifiers();

        var outcomes = Collect(reset, 1, 1, 7);

        Assert.Equal(new[] { false, false, false, false, true, false, false }, outcomes.Select(o => o.IsCriticalStrike));
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 50, 50 }, outcomes.Select(o => o.OverdriveBonusPercent));
    }

    // ---- Outcome ----

    [Fact]
    public void Outcome_ValidatesConsistencyAndHasValueEquality()
    {
        var state = new CyberDefenseCombatModifierState(5, 0, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => new CyberDefenseCombatModifierOutcome(true, -50, state));
        Assert.Throws<ArgumentException>(() => new CyberDefenseCombatModifierOutcome(true, 0, state));
        Assert.Throws<ArgumentException>(() => new CyberDefenseCombatModifierOutcome(false, 50, state));
        Assert.Equal(
            new CyberDefenseCombatModifierOutcome(true, 50, 50, state),
            new CyberDefenseCombatModifierOutcome(true, 50, 50, state));
        Assert.NotEqual(
            new CyberDefenseCombatModifierOutcome(true, 50, 50, state),
            new CyberDefenseCombatModifierOutcome(true, 50, 0, state));
    }

    [Fact]
    public void Outcome_ThreeArgumentConstructor_HasNoOverdriveBonus()
    {
        var state = new CyberDefenseCombatModifierState(5, 5);

        Assert.Equal(0, new CyberDefenseCombatModifierOutcome(true, 50, state).OverdriveBonusPercent);
        Assert.Equal(new CyberDefenseCombatModifierOutcome(true, 50, 0, state), new CyberDefenseCombatModifierOutcome(true, 50, state));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(1)]
    [InlineData(49)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void Outcome_RejectsOverdriveBonusOtherThanZeroOrFiftyPercent(int overdriveBonusPercent)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new CyberDefenseCombatModifierOutcome(false, 0, overdriveBonusPercent, CyberDefenseCombatModifierState.Initial));
    }

    // ---- Determinism and timing independence ----

    [Fact]
    public void RepeatedCalculations_AreDeterministic()
    {
        var state = new CyberDefenseCombatModifierState(3, 1, 1);

        var first = Resolve(state, true, 2, 2);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(first, Resolve(state, true, 2, 2));
        }

        bool[] answers = { true, true, false, true, true, true, true, true, true, true, false, true };
        Assert.Equal(Run(CyberDefenseCombatModifierState.Initial, 1, 1, answers), Run(CyberDefenseCombatModifierState.Initial, 1, 1, answers));
    }

    [Fact]
    public void Transition_IsStatelessAndHasNoTimingDependencies()
    {
        Type type = typeof(CyberDefenseCombatModifierTransition);

        Assert.True(type.IsAbstract && type.IsSealed, "Transition must be a static class.");
        Assert.DoesNotContain(
            type.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            f => !f.IsLiteral);

        foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            Assert.DoesNotContain(method.GetParameters(), p =>
                p.ParameterType == typeof(TimeSpan) ||
                p.ParameterType == typeof(DateTime) ||
                p.ParameterType == typeof(DateTimeOffset) ||
                p.ParameterType == typeof(TimeProvider));
        }
    }
}
