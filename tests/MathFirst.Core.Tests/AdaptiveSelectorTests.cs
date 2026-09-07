namespace MathFirst.Core.Tests;

using MathFirst.Application.Practice;
using MathFirst.Domain;
using Xunit;

public sealed class AdaptiveSelectorTests
{
    [Fact]
    public void SelectNextFact_Introduction_PresentsAllOperationsInSequence()
    {
        var rng = new Random(42);
        var selector = new AdaptivePracticeSelector(rng);
        var progression = LearnerProgression.CreateFresh();
        var itemStates = new Dictionary<string, ItemLearningState>();
        var order = 1;

        // 1. Initial 4 Addition facts (0..1)
        for (var i = 0; i < 4; i++)
        {
            var (phase, turn, max, _) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
            Assert.Equal(ProgressionPhase.IntroducingAddition, phase);
            Assert.Equal(ArithmeticOperation.Addition, turn);
            Assert.Equal(1, max);

            var fact = selector.SelectNextFact(progression, itemStates, order++);
            Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
            var st = ItemLearningState.CreateNew(fact);
            st.TotalAttempts = 1;
            itemStates[fact.Id] = st;
        }

        // 2. Next 3 Subtraction facts (0..1)
        for (var i = 0; i < 3; i++)
        {
            var (phase, turn, max, _) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
            Assert.Equal(ProgressionPhase.IntroducingSubtraction, phase);
            Assert.Equal(ArithmeticOperation.Subtraction, turn);
            Assert.Equal(1, max);

            var fact = selector.SelectNextFact(progression, itemStates, order++);
            Assert.Equal(ArithmeticOperation.Subtraction, fact.Operation);
            var st = ItemLearningState.CreateNew(fact);
            st.TotalAttempts = 1;
            itemStates[fact.Id] = st;
        }

        // 3. Next 4 Multiplication facts (0..1)
        for (var i = 0; i < 4; i++)
        {
            var (phase, turn, max, _) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
            Assert.Equal(ProgressionPhase.IntroducingMultiplication, phase);
            Assert.Equal(ArithmeticOperation.Multiplication, turn);
            Assert.Equal(1, max);

            var fact = selector.SelectNextFact(progression, itemStates, order++);
            Assert.Equal(ArithmeticOperation.Multiplication, fact.Operation);
            var st = ItemLearningState.CreateNew(fact);
            st.TotalAttempts = 1;
            itemStates[fact.Id] = st;
        }

        // 4. Next 2 Division facts (0..1)
        for (var i = 0; i < 2; i++)
        {
            var (phase, turn, max, _) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
            Assert.Equal(ProgressionPhase.IntroducingDivision, phase);
            Assert.Equal(ArithmeticOperation.Division, turn);
            Assert.Equal(1, max);

            var fact = selector.SelectNextFact(progression, itemStates, order++);
            Assert.Equal(ArithmeticOperation.Division, fact.Operation);
            var st = ItemLearningState.CreateNew(fact);
            st.TotalAttempts = 1;
            itemStates[fact.Id] = st;
        }

        // 5. All 4 operations at 0..1 exposed -> Checkpoint 1 phase!
        var (chkPhase, _, chkMax, _) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
        Assert.Equal(ProgressionPhase.Checkpoint, chkPhase);
        Assert.Equal(1, chkMax);

        // Complete Checkpoint 1 -> Addition advances to 0..2!
        progression.CompletedCheckpointLevel = 1;
        var (nextPhase, nextTurn, nextMax, unexposedNext) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
        Assert.Equal(ProgressionPhase.IntroducingAddition, nextPhase);
        Assert.Equal(ArithmeticOperation.Addition, nextTurn);
        Assert.Equal(2, nextMax);
        Assert.Equal(5, unexposedNext.Count);
    }

    [Fact]
    public void SelectNextFact_MixedPractice_SelectsAcrossAllFourOperations()
    {
        var rng = new Random(12345);
        var selector = new AdaptivePracticeSelector(rng);
        var progression = new LearnerProgression
        {
            OperationMaxOperands = new Dictionary<ArithmeticOperation, int>
            {
                [ArithmeticOperation.Addition] = 10,
                [ArithmeticOperation.Subtraction] = 10,
                [ArithmeticOperation.Multiplication] = 10,
                [ArithmeticOperation.Division] = 10
            }
        };
        var allActiveFacts = ArithmeticCatalog.GetActiveFacts(progression); // 418 facts
        var itemStates = allActiveFacts.ToDictionary(f => f.Id, f =>
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 2;
            st.CorrectAttempts = 2;
            st.ConsecutiveCorrectStreak = 2;
            st.LastLatencyMs = 1500;
            return st;
        }, StringComparer.Ordinal);

        var seenOperations = new HashSet<ArithmeticOperation>();

        for (var order = 1; order <= 40; order++)
        {
            var fact = selector.SelectNextFact(progression, itemStates, order);
            seenOperations.Add(fact.Operation);
        }

        // In mixed practice, all 4 operations are actively surfaced
        Assert.Contains(ArithmeticOperation.Addition, seenOperations);
        Assert.Contains(ArithmeticOperation.Subtraction, seenOperations);
        Assert.Contains(ArithmeticOperation.Multiplication, seenOperations);
        Assert.Contains(ArithmeticOperation.Division, seenOperations);
    }

    [Fact]
    public void SelectNextFact_SpacedRemediation_SurfacesAfterDelay()
    {
        var rng = new Random(42);
        var selector = new AdaptivePracticeSelector(rng);
        var progression = new LearnerProgression { CurrentMaxOperand = 1 };
        var facts = ArithmeticCatalog.GetAllFacts(1);
        var itemStates = facts.ToDictionary(f => f.Id, f =>
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 1;
            return st;
        }, StringComparer.Ordinal);

        // Mark a division fact as needing remediation due at order 5
        var wrongFact = facts.First(f => f.Operation == ArithmeticOperation.Division);
        itemStates[wrongFact.Id].NeedsRemediation = true;
        itemStates[wrongFact.Id].RemediationDueOrder = 5;

        // At order 5 -> due remediation is picked
        selector.ResetLastSelected();
        var dueFact = selector.SelectNextFact(progression, itemStates, 5);
        Assert.Equal(wrongFact.Id, dueFact.Id);
    }

    [Fact]
    public void SelectNextFact_AvoidsImmediateConsecutiveRepetition()
    {
        var rng = new Random(9876);
        var selector = new AdaptivePracticeSelector(rng);
        var progression = new LearnerProgression { CurrentMaxOperand = 1 };
        var facts = ArithmeticCatalog.GetAllFacts(1);
        var itemStates = facts.ToDictionary(f => f.Id, f =>
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 1;
            return st;
        }, StringComparer.Ordinal);

        string? previousId = null;
        for (var i = 1; i <= 30; i++)
        {
            var next = selector.SelectNextFact(progression, itemStates, i);
            if (previousId is not null)
            {
                Assert.NotEqual(previousId, next.Id);
            }
            previousId = next.Id;
        }
    }
}
