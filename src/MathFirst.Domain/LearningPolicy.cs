namespace MathFirst.Domain;

public static class LearningPolicy
{
    public const int InitialMaxOperand = 1;
    public const int MaxV1Operand = 10;
    public const long DefaultEasyResponseThresholdMs = 1000;
    public const long DefaultFluentResponseThresholdMs = 2500;
    public const int MinMasteryAttempts = 3;
    public const int MinConsecutiveCorrectForMastery = 3;
    public const double RangeMasteryThresholdRatio = 0.90;
    public const int RemediationInterveningCount = 3;

    public const int DefaultCheckpointAttemptCount = 12;
    public const int ExactFactCooldownDistance = 3;
    public const int MirrorFactCooldownDistance = 3;
    public const int MaxPreferredOperationStreak = 2;

    public const long DeadlineStreak0Ms = 30000;
    public const long DeadlineStreak1Ms = 20000;
    public const long DeadlineStreak2Ms = 15000;
    public const long DeadlineStreak3PlusMs = 10000;

    public static long GetAnswerDeadlineMs(int consecutiveCorrectStreak) =>
        consecutiveCorrectStreak switch
        {
            <= 0 => DeadlineStreak0Ms,
            1 => DeadlineStreak1Ms,
            2 => DeadlineStreak2Ms,
            _ => DeadlineStreak3PlusMs
        };

    public static double GetAnswerDeadlineSeconds(int consecutiveCorrectStreak) =>
        GetAnswerDeadlineMs(consecutiveCorrectStreak) / 1000.0;

    public static string FormatTimerDisplay(long remainingMs, long deadlineMs = DeadlineStreak0Ms)
    {
        var clampedMs = Math.Clamp(remainingMs, 0, Math.Max(0, deadlineMs));
        var seconds = clampedMs / 1000.0;
        return seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " s";
    }

    public static string FormatTimerDisplay(double remainingSeconds, double deadlineSeconds = 30.0)
    {
        if (double.IsNaN(remainingSeconds) || double.IsInfinity(remainingSeconds) || remainingSeconds <= 0.0)
        {
            return "0.000 s";
        }

        var clamped = Math.Clamp(remainingSeconds, 0.0, Math.Max(0.0, deadlineSeconds));
        return clamped.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " s";
    }

    public static ArithmeticOperation GetNextOperation(ArithmeticOperation operation) =>
        operation switch
        {
            ArithmeticOperation.Addition => ArithmeticOperation.Subtraction,
            ArithmeticOperation.Subtraction => ArithmeticOperation.Multiplication,
            ArithmeticOperation.Multiplication => ArithmeticOperation.Division,
            ArithmeticOperation.Division => ArithmeticOperation.Addition,
            _ => ArithmeticOperation.Addition
        };

    public static ProgressionPhase GetPhaseForOperation(ArithmeticOperation operation) =>
        operation switch
        {
            ArithmeticOperation.Addition => ProgressionPhase.IntroducingAddition,
            ArithmeticOperation.Subtraction => ProgressionPhase.IntroducingSubtraction,
            ArithmeticOperation.Multiplication => ProgressionPhase.IntroducingMultiplication,
            ArithmeticOperation.Division => ProgressionPhase.IntroducingDivision,
            _ => ProgressionPhase.MixedPractice
        };

    public static bool EvaluateItemMastery(
        ItemLearningState state,
        long fluentThresholdMs = DefaultFluentResponseThresholdMs)
    {
        if (state.NeedsRemediation)
        {
            return false;
        }

        return state.TotalAttempts >= MinMasteryAttempts
            && state.ConsecutiveCorrectStreak >= MinConsecutiveCorrectForMastery
            && state.LastLatencyMs > 0
            && state.LastLatencyMs <= fluentThresholdMs;
    }

    public static (ProgressionPhase Phase, ArithmeticOperation CurrentTurn, int TurnMaxOperand, IReadOnlyList<ArithmeticFact> UnexposedFacts) DetermineProgressionPhase(
        LearnerProgression progression,
        IReadOnlyDictionary<string, ItemLearningState> itemStates)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(itemStates);

        var mAdd = progression.GetMaxOperand(ArithmeticOperation.Addition);
        var mSub = progression.GetMaxOperand(ArithmeticOperation.Subtraction);
        var mMul = progression.GetMaxOperand(ArithmeticOperation.Multiplication);
        var mDiv = progression.GetMaxOperand(ArithmeticOperation.Division);

        // 1. Check Addition unexposed facts for its current range
        var unexposedAdd = GetUnexposedFacts(ArithmeticOperation.Addition, mAdd, itemStates);
        if (unexposedAdd.Count > 0)
        {
            return (ProgressionPhase.IntroducingAddition, ArithmeticOperation.Addition, mAdd, unexposedAdd);
        }

        // 2. Check Subtraction
        var unexposedSub = GetUnexposedFacts(ArithmeticOperation.Subtraction, mSub, itemStates);
        if (unexposedSub.Count > 0)
        {
            return (ProgressionPhase.IntroducingSubtraction, ArithmeticOperation.Subtraction, mSub, unexposedSub);
        }
        if (mSub < mAdd)
        {
            var newSubFacts = GetUnexposedFacts(ArithmeticOperation.Subtraction, mAdd, itemStates);
            return (ProgressionPhase.IntroducingSubtraction, ArithmeticOperation.Subtraction, mAdd, newSubFacts);
        }

        // 3. Check Multiplication
        var unexposedMul = GetUnexposedFacts(ArithmeticOperation.Multiplication, mMul, itemStates);
        if (unexposedMul.Count > 0)
        {
            return (ProgressionPhase.IntroducingMultiplication, ArithmeticOperation.Multiplication, mMul, unexposedMul);
        }
        if (mMul < mSub)
        {
            var newMulFacts = GetUnexposedFacts(ArithmeticOperation.Multiplication, mSub, itemStates);
            return (ProgressionPhase.IntroducingMultiplication, ArithmeticOperation.Multiplication, mSub, newMulFacts);
        }

        // 4. Check Division
        var unexposedDiv = GetUnexposedFacts(ArithmeticOperation.Division, mDiv, itemStates);
        if (unexposedDiv.Count > 0)
        {
            return (ProgressionPhase.IntroducingDivision, ArithmeticOperation.Division, mDiv, unexposedDiv);
        }
        if (mDiv < mMul)
        {
            var newDivFacts = GetUnexposedFacts(ArithmeticOperation.Division, mMul, itemStates);
            return (ProgressionPhase.IntroducingDivision, ArithmeticOperation.Division, mMul, newDivFacts);
        }

        // 5. All 4 operations have completed their new fact batches for the current level (mAdd == mSub == mMul == mDiv)
        if (mAdd == mSub && mSub == mMul && mMul == mDiv)
        {
            var currentLevel = mAdd;

            // Has this level's checkpoint completed?
            if (progression.CompletedCheckpointLevel < currentLevel)
            {
                return (ProgressionPhase.Checkpoint, ArithmeticOperation.Addition, currentLevel, Array.Empty<ArithmeticFact>());
            }

            // Checkpoint completed for currentLevel. If not at max level (10), advance to next level Addition!
            if (currentLevel < MaxV1Operand)
            {
                var nextAddMax = currentLevel + 1;
                var nextAddFacts = GetUnexposedFacts(ArithmeticOperation.Addition, nextAddMax, itemStates);
                return (ProgressionPhase.IntroducingAddition, ArithmeticOperation.Addition, nextAddMax, nextAddFacts);
            }

            // All 10 levels and Level 10 checkpoint have completed!
            return (ProgressionPhase.MixedPractice, ArithmeticOperation.Addition, MaxV1Operand, Array.Empty<ArithmeticFact>());
        }

        // Fallback: Mixed Practice
        return (ProgressionPhase.MixedPractice, ArithmeticOperation.Addition, MaxV1Operand, Array.Empty<ArithmeticFact>());
    }

    public static bool SynchronizeProgression(
        LearnerProgression progression,
        IReadOnlyDictionary<string, ItemLearningState> itemStates)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(itemStates);

        var (phase, currentTurn, turnMaxOp, _) = DetermineProgressionPhase(progression, itemStates);
        var rangeAdvanced = false;

        if (phase == ProgressionPhase.Checkpoint)
        {
            progression.ActiveCheckpointLevel = turnMaxOp;
        }
        else
        {
            progression.ActiveCheckpointLevel = null;
            progression.CurrentIntroductionTurn = currentTurn;

            if (progression.GetMaxOperand(currentTurn) != turnMaxOp)
            {
                progression.SetMaxOperand(currentTurn, turnMaxOp);
                rangeAdvanced = true;
            }
        }

        return rangeAdvanced;
    }

    public static (ProgressionPhase Phase, IReadOnlyList<ArithmeticFact> UnexposedFacts) DetermineProgressionPhase(
        int currentMaxOperand,
        IReadOnlyDictionary<string, ItemLearningState> itemStates)
    {
        var tempProgression = new LearnerProgression
        {
            OperationMaxOperands = new Dictionary<ArithmeticOperation, int>
            {
                [ArithmeticOperation.Addition] = currentMaxOperand,
                [ArithmeticOperation.Subtraction] = currentMaxOperand,
                [ArithmeticOperation.Multiplication] = currentMaxOperand,
                [ArithmeticOperation.Division] = currentMaxOperand
            },
            CompletedCheckpointLevel = currentMaxOperand // Assume past checkpoints complete when checking by operand
        };

        var (phase, _, _, unexposed) = DetermineProgressionPhase(tempProgression, itemStates);
        return (phase, unexposed);
    }

    public static int GetIntroducedOperationsCount(
        LearnerProgression progression,
        IReadOnlyDictionary<string, ItemLearningState> itemStates)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(itemStates);

        var count = 0;
        foreach (var op in Enum.GetValues<ArithmeticOperation>())
        {
            var max = progression.GetMaxOperand(op);
            var newFacts = ArithmeticCatalog.GetNewlyUnlockedFacts(op, max);
            if (newFacts.All(f => itemStates.TryGetValue(f.Id, out var s) && s.TotalAttempts > 0))
            {
                count++;
            }
        }
        return count;
    }

    public static int GetIntroducedOperationsCount(
        int currentMaxOperand,
        IReadOnlyDictionary<string, ItemLearningState> itemStates)
    {
        var count = 0;
        foreach (var op in Enum.GetValues<ArithmeticOperation>())
        {
            var newFacts = ArithmeticCatalog.GetNewlyUnlockedFacts(op, currentMaxOperand);
            if (newFacts.All(f => itemStates.TryGetValue(f.Id, out var s) && s.TotalAttempts > 0))
            {
                count++;
            }
        }
        return count;
    }

    private static IReadOnlyList<ArithmeticFact> GetUnexposedFacts(
        ArithmeticOperation operation,
        int maxOperand,
        IReadOnlyDictionary<string, ItemLearningState> itemStates)
    {
        var newlyUnlocked = ArithmeticCatalog.GetNewlyUnlockedFacts(operation, maxOperand);
        return newlyUnlocked
            .Where(f => !itemStates.TryGetValue(f.Id, out var s) || s.TotalAttempts == 0)
            .ToList();
    }
}
