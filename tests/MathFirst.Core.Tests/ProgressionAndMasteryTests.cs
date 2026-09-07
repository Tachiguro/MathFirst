namespace MathFirst.Core.Tests;

using MathFirst.Application.Practice;
using MathFirst.Domain;
using Xunit;

public sealed class ProgressionAndMasteryTests
{
    [Fact]
    public void EvaluateItemMastery_RequiresThreeAttemptsThreeStreakAndFluentLatency()
    {
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var state = ItemLearningState.CreateNew(fact);

        // Initially not mastered
        Assert.False(LearningPolicy.EvaluateItemMastery(state));

        // 2 attempts, fast and correct -> not yet mastered
        state.TotalAttempts = 2;
        state.CorrectAttempts = 2;
        state.ConsecutiveCorrectStreak = 2;
        state.LastLatencyMs = 1200;
        Assert.False(LearningPolicy.EvaluateItemMastery(state));

        // 3rd attempt, fast and correct -> mastered!
        state.TotalAttempts = 3;
        state.CorrectAttempts = 3;
        state.ConsecutiveCorrectStreak = 3;
        state.LastLatencyMs = 1500;
        Assert.True(LearningPolicy.EvaluateItemMastery(state));

        // Slow latency (> 2500ms) -> not mastered
        state.LastLatencyMs = 2800;
        Assert.False(LearningPolicy.EvaluateItemMastery(state));

        // Unresolved remediation -> not mastered
        state.LastLatencyMs = 1500;
        state.NeedsRemediation = true;
        Assert.False(LearningPolicy.EvaluateItemMastery(state));
    }

    [Fact]
    public void DetermineProgressionPhase_FollowsStrictExposureSequence()
    {
        var progression = LearnerProgression.CreateFresh();
        var itemStates = new Dictionary<string, ItemLearningState>();

        // 1. Fresh state -> IntroducingAddition at 0..1
        var (phase1, turn1, max1, unexposed1) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
        Assert.Equal(ProgressionPhase.IntroducingAddition, phase1);
        Assert.Equal(ArithmeticOperation.Addition, turn1);
        Assert.Equal(1, max1);
        Assert.Equal(4, unexposed1.Count); // 0+0, 0+1, 1+0, 1+1

        // Expose 4 Addition facts (even with mistakes / 1 attempt)
        var addFacts = ArithmeticCatalog.GetFacts(ArithmeticOperation.Addition, 1);
        foreach (var f in addFacts)
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 1; // Exposed!
            itemStates[f.Id] = st;
        }

        // 2. Next -> IntroducingSubtraction at 0..1
        var (phase2, turn2, max2, unexposed2) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
        Assert.Equal(ProgressionPhase.IntroducingSubtraction, phase2);
        Assert.Equal(ArithmeticOperation.Subtraction, turn2);
        Assert.Equal(1, max2);
        Assert.Equal(3, unexposed2.Count); // 0-0, 1-0, 1-1

        // Expose 3 Subtraction facts
        var subFacts = ArithmeticCatalog.GetFacts(ArithmeticOperation.Subtraction, 1);
        foreach (var f in subFacts)
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 1;
            itemStates[f.Id] = st;
        }

        // 3. Next -> IntroducingMultiplication at 0..1
        var (phase3, turn3, max3, unexposed3) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
        Assert.Equal(ProgressionPhase.IntroducingMultiplication, phase3);
        Assert.Equal(ArithmeticOperation.Multiplication, turn3);
        Assert.Equal(1, max3);
        Assert.Equal(4, unexposed3.Count); // 0*0, 0*1, 1*0, 1*1

        // Expose 4 Multiplication facts
        var mulFacts = ArithmeticCatalog.GetFacts(ArithmeticOperation.Multiplication, 1);
        foreach (var f in mulFacts)
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 1;
            itemStates[f.Id] = st;
        }

        // 4. Next -> IntroducingDivision at 0..1
        var (phase4, turn4, max4, unexposed4) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
        Assert.Equal(ProgressionPhase.IntroducingDivision, phase4);
        Assert.Equal(ArithmeticOperation.Division, turn4);
        Assert.Equal(1, max4);
        Assert.Equal(2, unexposed4.Count); // 0/1, 1/1

        // Expose 2 Division facts
        var divFacts = ArithmeticCatalog.GetFacts(ArithmeticOperation.Division, 1);
        foreach (var f in divFacts)
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 1;
            itemStates[f.Id] = st;
        }

        // 5. All 4 operations at 0..1 exposed -> Transitions to Checkpoint 1!
        var (phase5, turn5, max5, unexposed5) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
        Assert.Equal(ProgressionPhase.Checkpoint, phase5);
        Assert.Equal(1, max5);
        Assert.Empty(unexposed5);

        // 6. Complete Checkpoint 1 -> Addition advances to 0..2!
        progression.CompletedCheckpointLevel = 1;
        var (phase6, turn6, max6, unexposed6) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
        Assert.Equal(ProgressionPhase.IntroducingAddition, phase6);
        Assert.Equal(ArithmeticOperation.Addition, turn6);
        Assert.Equal(2, max6);
        Assert.Equal(5, unexposed6.Count); // 0+2, 1+2, 2+0, 2+1, 2+2
    }

    [Fact]
    public void Introduction_IsExposureBasedAndNotBlockedByIncorrectAnswers()
    {
        var progression = LearnerProgression.CreateFresh();
        var itemStates = new Dictionary<string, ItemLearningState>();

        // Expose all addition 0..1 facts with wrong answers (0 correct streak, needs remediation)
        var addFacts = ArithmeticCatalog.GetFacts(ArithmeticOperation.Addition, 1);
        foreach (var f in addFacts)
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 1;
            st.IncorrectAttempts = 1;
            st.ConsecutiveCorrectStreak = 0;
            st.NeedsRemediation = true;
            itemStates[f.Id] = st;
        }

        // Must still transition to Subtraction introduction!
        var (phase, turn, maxOp, unexposed) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
        Assert.Equal(ProgressionPhase.IntroducingSubtraction, phase);
        Assert.Equal(ArithmeticOperation.Subtraction, turn);
        Assert.Equal(1, maxOp);
        Assert.Equal(3, unexposed.Count);
    }

    [Fact]
    public void SynchronizeProgression_AdvancesPerOperationRangesAndSetsIntroductionTurn()
    {
        var progression = LearnerProgression.CreateFresh();
        var itemStates = new Dictionary<string, ItemLearningState>();

        // Initially all at 1, turn Addition
        var advanced0 = LearningPolicy.SynchronizeProgression(progression, itemStates);
        Assert.False(advanced0);
        Assert.Equal(1, progression.GetMaxOperand(ArithmeticOperation.Addition));
        Assert.Equal(ArithmeticOperation.Addition, progression.CurrentIntroductionTurn);

        // Expose all Addition 0..1 facts
        foreach (var f in ArithmeticCatalog.GetFacts(ArithmeticOperation.Addition, 1))
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 1;
            itemStates[f.Id] = st;
        }

        // Synchronize -> Turn becomes Subtraction, no range increment yet
        var advanced1 = LearningPolicy.SynchronizeProgression(progression, itemStates);
        Assert.False(advanced1);
        Assert.Equal(ArithmeticOperation.Subtraction, progression.CurrentIntroductionTurn);
        Assert.Equal(1, progression.GetMaxOperand(ArithmeticOperation.Subtraction));

        // Expose Subtraction, Multiplication, Division 0..1 facts
        foreach (var f in ArithmeticCatalog.GetFacts(ArithmeticOperation.Subtraction, 1))
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 1;
            itemStates[f.Id] = st;
        }
        foreach (var f in ArithmeticCatalog.GetFacts(ArithmeticOperation.Multiplication, 1))
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 1;
            itemStates[f.Id] = st;
        }
        foreach (var f in ArithmeticCatalog.GetFacts(ArithmeticOperation.Division, 1))
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 1;
            itemStates[f.Id] = st;
        }

        // Synchronize -> Enters Checkpoint 1 (no range increment yet)
        var advanced2 = LearningPolicy.SynchronizeProgression(progression, itemStates);
        Assert.False(advanced2);
        Assert.Equal(1, progression.ActiveCheckpointLevel);

        // Complete Checkpoint 1 -> Synchronize advances Addition to 2!
        progression.CompletedCheckpointLevel = 1;
        var advanced3 = LearningPolicy.SynchronizeProgression(progression, itemStates);
        Assert.True(advanced3);
        Assert.Equal(ArithmeticOperation.Addition, progression.CurrentIntroductionTurn);
        Assert.Equal(2, progression.GetMaxOperand(ArithmeticOperation.Addition));
        Assert.Equal(1, progression.GetMaxOperand(ArithmeticOperation.Subtraction));
        Assert.Equal(1, progression.GetMaxOperand(ArithmeticOperation.Multiplication));
        Assert.Equal(1, progression.GetMaxOperand(ArithmeticOperation.Division));
    }

    [Fact]
    public void FullExposure_TransitionsToMixedPracticeWhenAllOperationsReachTen()
    {
        var progression = new LearnerProgression
        {
            OperationMaxOperands = new Dictionary<ArithmeticOperation, int>
            {
                [ArithmeticOperation.Addition] = 10,
                [ArithmeticOperation.Subtraction] = 10,
                [ArithmeticOperation.Multiplication] = 10,
                [ArithmeticOperation.Division] = 10
            },
            CompletedCheckpointLevel = 10
        };

        var itemStates = new Dictionary<string, ItemLearningState>();
        var allActive = ArithmeticCatalog.GetActiveFacts(progression);

        // Expose all 418 facts
        foreach (var f in allActive)
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 1;
            itemStates[f.Id] = st;
        }

        var (phase, turn, maxOp, unexposed) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
        Assert.Equal(ProgressionPhase.MixedPractice, phase);
        Assert.Empty(unexposed);
    }

    [Fact]
    public void Operands_NeverExceedActiveMaxOperand()
    {
        for (var maxOp = 1; maxOp <= 10; maxOp++)
        {
            var allFacts = ArithmeticCatalog.GetAllFacts(maxOp);
            foreach (var fact in allFacts)
            {
                switch (fact.Operation)
                {
                    case ArithmeticOperation.Addition:
                    case ArithmeticOperation.Multiplication:
                        Assert.True(fact.LeftOperand <= maxOp, $"Left operand {fact.LeftOperand} exceeds max {maxOp}");
                        Assert.True(fact.RightOperand <= maxOp, $"Right operand {fact.RightOperand} exceeds max {maxOp}");
                        break;
                    case ArithmeticOperation.Subtraction:
                        Assert.True(fact.LeftOperand <= maxOp, $"Left operand {fact.LeftOperand} exceeds max {maxOp}");
                        Assert.True(fact.RightOperand <= fact.LeftOperand, $"Right operand {fact.RightOperand} exceeds left {fact.LeftOperand}");
                        break;
                    case ArithmeticOperation.Division:
                        Assert.True(fact.RightOperand <= maxOp, $"Divisor {fact.RightOperand} exceeds max {maxOp}");
                        Assert.True(fact.CorrectResult <= maxOp, $"Quotient {fact.CorrectResult} exceeds max {maxOp}");
                        break;
                }
            }
        }
    }

    [Fact]
    public void EndToEnd_StaircaseProgression_CyclesThroughAllFourOperationsStepByStep()
    {
        var rng = new Random(1337);
        var selector = new AdaptivePracticeSelector(rng);
        var progression = LearnerProgression.CreateFresh();
        var itemStates = new Dictionary<string, ItemLearningState>();
        var order = 1;

        // Level 1:
        // Add 0..1 (4 facts)
        for (var i = 0; i < 4; i++)
        {
            var (phase, turn, max, unexposed) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
            Assert.Equal(ArithmeticOperation.Addition, turn);
            Assert.Equal(1, max);
            var fact = selector.SelectNextFact(progression, itemStates, order++);
            Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
            var st = ItemLearningState.CreateNew(fact);
            st.TotalAttempts = 1;
            itemStates[fact.Id] = st;
            LearningPolicy.SynchronizeProgression(progression, itemStates);
        }

        // Sub 0..1 (3 facts)
        for (var i = 0; i < 3; i++)
        {
            var (phase, turn, max, unexposed) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
            Assert.Equal(ArithmeticOperation.Subtraction, turn);
            Assert.Equal(1, max);
            var fact = selector.SelectNextFact(progression, itemStates, order++);
            Assert.Equal(ArithmeticOperation.Subtraction, fact.Operation);
            var st = ItemLearningState.CreateNew(fact);
            st.TotalAttempts = 1;
            itemStates[fact.Id] = st;
            LearningPolicy.SynchronizeProgression(progression, itemStates);
        }

        // Mul 0..1 (4 facts)
        for (var i = 0; i < 4; i++)
        {
            var (phase, turn, max, unexposed) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
            Assert.Equal(ArithmeticOperation.Multiplication, turn);
            Assert.Equal(1, max);
            var fact = selector.SelectNextFact(progression, itemStates, order++);
            Assert.Equal(ArithmeticOperation.Multiplication, fact.Operation);
            var st = ItemLearningState.CreateNew(fact);
            st.TotalAttempts = 1;
            itemStates[fact.Id] = st;
            LearningPolicy.SynchronizeProgression(progression, itemStates);
        }

        // Div 0..1 (2 facts)
        for (var i = 0; i < 2; i++)
        {
            var (phase, turn, max, unexposed) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
            Assert.Equal(ArithmeticOperation.Division, turn);
            Assert.Equal(1, max);
            var fact = selector.SelectNextFact(progression, itemStates, order++);
            Assert.Equal(ArithmeticOperation.Division, fact.Operation);
            var st = ItemLearningState.CreateNew(fact);
            st.TotalAttempts = 1;
            itemStates[fact.Id] = st;
            LearningPolicy.SynchronizeProgression(progression, itemStates);
        }

        // Checkpoint Level 1 (12 attempts)
        for (var i = 0; i < 12; i++)
        {
            var (phase, turn, max, unexposed) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
            Assert.Equal(ProgressionPhase.Checkpoint, phase);
            Assert.Equal(1, max);
            var fact = selector.SelectNextFact(progression, itemStates, order++);
            Assert.NotNull(fact);
            progression.CheckpointAttemptCount++;
        }
        progression.CompletedCheckpointLevel = 1;
        progression.CheckpointAttemptCount = 0;
        LearningPolicy.SynchronizeProgression(progression, itemStates);

        // Level 2: Addition (5 facts)
        for (var i = 0; i < 5; i++)
        {
            var (phase, turn, max, unexposed) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
            Assert.Equal(ArithmeticOperation.Addition, turn);
            Assert.Equal(2, max);
            var fact = selector.SelectNextFact(progression, itemStates, order++);
            Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
            var st = ItemLearningState.CreateNew(fact);
            st.TotalAttempts = 1;
            itemStates[fact.Id] = st;
            LearningPolicy.SynchronizeProgression(progression, itemStates);
        }
        Assert.Equal(2, progression.GetMaxOperand(ArithmeticOperation.Addition));

        // Sub 0..2 (3 facts)
        for (var i = 0; i < 3; i++)
        {
            var (phase, turn, max, unexposed) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
            Assert.Equal(ArithmeticOperation.Subtraction, turn);
            Assert.Equal(2, max);
            var fact = selector.SelectNextFact(progression, itemStates, order++);
            Assert.Equal(ArithmeticOperation.Subtraction, fact.Operation);
            var st = ItemLearningState.CreateNew(fact);
            st.TotalAttempts = 1;
            itemStates[fact.Id] = st;
            LearningPolicy.SynchronizeProgression(progression, itemStates);
        }
        Assert.Equal(2, progression.GetMaxOperand(ArithmeticOperation.Subtraction));
    }
}
