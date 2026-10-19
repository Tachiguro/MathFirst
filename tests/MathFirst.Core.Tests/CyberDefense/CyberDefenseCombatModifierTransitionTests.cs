using System.Reflection;
using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseCombatModifierTransitionTests
{
    private static CyberDefenseCombatModifierState Run(
        CyberDefenseCombatModifierState state,
        int rank,
        params bool[] answers)
    {
        foreach (bool answer in answers)
        {
            state = CyberDefenseCombatModifierTransition.ResolveAttempt(state, answer, rank).NextState;
        }

        return state;
    }

    private static List<CyberDefenseCombatModifierOutcome> Collect(int rank, int correctAttacks)
    {
        var outcomes = new List<CyberDefenseCombatModifierOutcome>();
        var state = CyberDefenseCombatModifierState.Initial;
        for (int i = 0; i < correctAttacks; i++)
        {
            var outcome = CyberDefenseCombatModifierTransition.ResolveAttempt(state, true, rank);
            outcomes.Add(outcome);
            state = outcome.NextState;
        }

        return outcomes;
    }

    [Fact]
    public void InitialState_HasZeroCounters()
    {
        var state = CyberDefenseCombatModifierState.Initial;

        Assert.Equal(0, state.CriticalCorrectAttackCount);
        Assert.Equal(0, state.OverdriveStreak);
        Assert.Equal(default, state);
    }

    [Fact]
    public void State_HasValueEquality()
    {
        var a = new CyberDefenseCombatModifierState(3, 2);
        var b = new CyberDefenseCombatModifierState(3, 2);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new CyberDefenseCombatModifierState(3, 1));
        Assert.NotEqual(a, new CyberDefenseCombatModifierState(4, 2));
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

    [Fact]
    public void ResolveAttempt_DoesNotMutateInputState()
    {
        var state = new CyberDefenseCombatModifierState(4, 3);

        var outcome = CyberDefenseCombatModifierTransition.ResolveAttempt(state, true, 1);

        Assert.Equal(new CyberDefenseCombatModifierState(4, 3), state);
        Assert.Equal(new CyberDefenseCombatModifierState(5, 4), outcome.NextState);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(int.MinValue, 0)]
    public void State_RejectsNegativeCounters(int count, int streak)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CyberDefenseCombatModifierState(count, streak));
    }

    [Fact]
    public void FirstCorrectAnswer_IncrementsBothCountersAndIsNotCritical()
    {
        var outcome = CyberDefenseCombatModifierTransition.ResolveAttempt(CyberDefenseCombatModifierState.Initial, true, 1);

        Assert.False(outcome.IsCriticalStrike);
        Assert.Equal(0, outcome.CriticalBonusPercent);
        Assert.Equal(new CyberDefenseCombatModifierState(1, 1), outcome.NextState);
    }

    [Fact]
    public void Rank1_EveryFifthCorrectAttackIsCriticalFiftyPercent()
    {
        var outcomes = Collect(1, 10);

        for (int i = 0; i < outcomes.Count; i++)
        {
            bool expected = (i + 1) % 5 == 0;
            Assert.Equal(expected, outcomes[i].IsCriticalStrike);
            Assert.Equal(expected ? 50 : 0, outcomes[i].CriticalBonusPercent);
        }

        Assert.Equal(new CyberDefenseCombatModifierState(10, 10), outcomes[9].NextState);
    }

    [Fact]
    public void Rank2_EveryFourthCorrectAttackIsCriticalHundredPercent()
    {
        var outcomes = Collect(2, 8);

        for (int i = 0; i < outcomes.Count; i++)
        {
            bool expected = (i + 1) % 4 == 0;
            Assert.Equal(expected, outcomes[i].IsCriticalStrike);
            Assert.Equal(expected ? 100 : 0, outcomes[i].CriticalBonusPercent);
        }
    }

    [Fact]
    public void Rank0_NeverGrantsCriticalButStillCounts()
    {
        var outcomes = Collect(0, 20);

        Assert.All(outcomes, o =>
        {
            Assert.False(o.IsCriticalStrike);
            Assert.Equal(0, o.CriticalBonusPercent);
        });
        Assert.Equal(new CyberDefenseCombatModifierState(20, 20), outcomes[19].NextState);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void IncorrectAnswer_NeverGrantsCritical(int rank)
    {
        // Counts 4 (rank 2) and 9 (rank 1) are one short of a trigger, so a wrongly counted
        // incorrect answer would land on a trigger ordinal.
        foreach (int count in new[] { 3, 4, 8, 9, 19 })
        {
            var outcome = CyberDefenseCombatModifierTransition.ResolveAttempt(
                new CyberDefenseCombatModifierState(count, 2), false, rank);

            Assert.False(outcome.IsCriticalStrike);
            Assert.Equal(0, outcome.CriticalBonusPercent);
        }
    }

    [Fact]
    public void IncorrectAnswer_ResetsStreakAndPreservesCriticalCount()
    {
        var outcome = CyberDefenseCombatModifierTransition.ResolveAttempt(
            new CyberDefenseCombatModifierState(7, 6), false, 1);

        Assert.Equal(new CyberDefenseCombatModifierState(7, 0), outcome.NextState);
    }

    [Fact]
    public void CorrectIncorrectCorrectSequence_CountsOnlyCorrectAndRestartsStreak()
    {
        var first = CyberDefenseCombatModifierTransition.ResolveAttempt(CyberDefenseCombatModifierState.Initial, true, 2);
        var wrong = CyberDefenseCombatModifierTransition.ResolveAttempt(first.NextState, false, 2);
        var third = CyberDefenseCombatModifierTransition.ResolveAttempt(wrong.NextState, true, 2);

        Assert.Equal(new CyberDefenseCombatModifierState(1, 1), first.NextState);
        Assert.Equal(new CyberDefenseCombatModifierState(1, 0), wrong.NextState);
        Assert.Equal(new CyberDefenseCombatModifierState(2, 1), third.NextState);
    }

    [Fact]
    public void CriticalCount_IsNotResetByIncorrectAnswers_SoTriggerIsStillOnFourthCorrect()
    {
        var state = Run(CyberDefenseCombatModifierState.Initial, 2, true, true, false, true);

        var fourthCorrect = CyberDefenseCombatModifierTransition.ResolveAttempt(state, true, 2);

        Assert.True(fourthCorrect.IsCriticalStrike);
        Assert.Equal(100, fourthCorrect.CriticalBonusPercent);
        Assert.Equal(new CyberDefenseCombatModifierState(4, 2), fourthCorrect.NextState);
    }

    [Fact]
    public void ShieldAbsorption_CannotPreserveStreak_BecauseOnlyAnswerCorrectnessIsAnInput()
    {
        MethodInfo method = typeof(CyberDefenseCombatModifierTransition).GetMethod(nameof(CyberDefenseCombatModifierTransition.ResolveAttempt))!;
        Type[] parameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Equal(new[] { typeof(CyberDefenseCombatModifierState), typeof(bool), typeof(int) }, parameterTypes);

        var outcome = CyberDefenseCombatModifierTransition.ResolveAttempt(
            new CyberDefenseCombatModifierState(2, 9), false, 1);
        Assert.Equal(0, outcome.NextState.OverdriveStreak);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void InvalidCriticalRank_IsRejectedForCorrectAndIncorrectAnswers(int rank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseCombatModifierTransition.ResolveAttempt(CyberDefenseCombatModifierState.Initial, true, rank));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseCombatModifierTransition.ResolveAttempt(CyberDefenseCombatModifierState.Initial, false, rank));
    }

    [Fact]
    public void CriticalCountOverflow_FailsClosed()
    {
        var state = new CyberDefenseCombatModifierState(int.MaxValue, 1);

        Assert.Throws<OverflowException>(() => CyberDefenseCombatModifierTransition.ResolveAttempt(state, true, 1));
        Assert.Throws<OverflowException>(() => CyberDefenseCombatModifierTransition.ResolveAttempt(state, true, 0));
    }

    [Fact]
    public void CriticalCountAtMaxValueMinusOne_IncrementsToMaxValue()
    {
        var outcome = CyberDefenseCombatModifierTransition.ResolveAttempt(
            new CyberDefenseCombatModifierState(int.MaxValue - 1, 0), true, 0);

        Assert.Equal(int.MaxValue, outcome.NextState.CriticalCorrectAttackCount);
    }

    [Fact]
    public void IncorrectAnswerAtMaxCriticalCount_DoesNotOverflow()
    {
        var outcome = CyberDefenseCombatModifierTransition.ResolveAttempt(
            new CyberDefenseCombatModifierState(int.MaxValue, 5), false, 2);

        Assert.Equal(new CyberDefenseCombatModifierState(int.MaxValue, 0), outcome.NextState);
    }

    [Fact]
    public void OverdriveStreak_SaturatesAtMaxValue()
    {
        var outcome = CyberDefenseCombatModifierTransition.ResolveAttempt(
            new CyberDefenseCombatModifierState(1, int.MaxValue), true, 1);

        Assert.Equal(int.MaxValue, outcome.NextState.OverdriveStreak);
        Assert.Equal(2, outcome.NextState.CriticalCorrectAttackCount);
    }

    [Fact]
    public void OverdriveStreak_MatchesExistingOverdrivePolicy()
    {
        foreach (int streak in new[] { 0, 1, 4, 5, 100, int.MaxValue - 1, int.MaxValue })
        {
            foreach (bool correct in new[] { true, false })
            {
                var outcome = CyberDefenseCombatModifierTransition.ResolveAttempt(
                    new CyberDefenseCombatModifierState(0, streak), correct, 0);

                Assert.Equal(CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(streak, correct), outcome.NextState.OverdriveStreak);
            }
        }
    }

    [Fact]
    public void CriticalResults_MatchExistingCriticalStrikePolicy()
    {
        for (int rank = 0; rank <= CyberDefenseCriticalStrikePolicy.MaxRank; rank++)
        {
            for (int priorCount = 0; priorCount < 40; priorCount++)
            {
                var outcome = CyberDefenseCombatModifierTransition.ResolveAttempt(
                    new CyberDefenseCombatModifierState(priorCount, 0), true, rank);

                Assert.Equal(
                    CyberDefenseCriticalStrikePolicy.GetCriticalBonusPercent(rank, priorCount + 1),
                    outcome.CriticalBonusPercent);
                Assert.Equal(outcome.CriticalBonusPercent > 0, outcome.IsCriticalStrike);
            }
        }
    }

    [Fact]
    public void Outcome_ValidatesConsistencyAndHasValueEquality()
    {
        var state = new CyberDefenseCombatModifierState(5, 5);

        Assert.Throws<ArgumentOutOfRangeException>(() => new CyberDefenseCombatModifierOutcome(true, -50, state));
        Assert.Throws<ArgumentException>(() => new CyberDefenseCombatModifierOutcome(true, 0, state));
        Assert.Throws<ArgumentException>(() => new CyberDefenseCombatModifierOutcome(false, 50, state));
        Assert.Equal(
            new CyberDefenseCombatModifierOutcome(true, 50, state),
            new CyberDefenseCombatModifierOutcome(true, 50, state));
    }

    [Fact]
    public void RepeatedCalculations_AreDeterministic()
    {
        var state = new CyberDefenseCombatModifierState(3, 2);

        var first = CyberDefenseCombatModifierTransition.ResolveAttempt(state, true, 2);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(first, CyberDefenseCombatModifierTransition.ResolveAttempt(state, true, 2));
        }

        var sequenceA = Run(CyberDefenseCombatModifierState.Initial, 1, true, true, false, true, true, true, true, true);
        var sequenceB = Run(CyberDefenseCombatModifierState.Initial, 1, true, true, false, true, true, true, true, true);
        Assert.Equal(sequenceA, sequenceB);
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
