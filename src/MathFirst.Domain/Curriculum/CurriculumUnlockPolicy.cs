namespace MathFirst.Domain.Curriculum;

public static class CurriculumUnlockPolicy
{
    private static readonly IReadOnlyList<ArithmeticOperation> Stage1Operations =
        Array.AsReadOnly([ArithmeticOperation.Addition]);

    private static readonly IReadOnlyList<ArithmeticOperation> Stage2Operations =
        Array.AsReadOnly([ArithmeticOperation.Addition, ArithmeticOperation.Subtraction]);

    private static readonly IReadOnlyList<ArithmeticOperation> Stage3Operations =
        Array.AsReadOnly([ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication]);

    private static readonly IReadOnlyList<ArithmeticOperation> Stage4Operations =
        Array.AsReadOnly([
            ArithmeticOperation.Addition,
            ArithmeticOperation.Subtraction,
            ArithmeticOperation.Multiplication,
            ArithmeticOperation.Division
        ]);

    private static readonly IReadOnlyList<string> Stage1PrerequisiteFacts =
        Array.AsReadOnly(["add:0+0", "add:0+1", "add:1+0", "add:1+1"]);

    private static readonly IReadOnlyList<string> Stage2PrerequisiteFacts =
        Array.AsReadOnly(["sub:0-0", "sub:1-0", "sub:1-1"]);

    private static readonly IReadOnlyList<string> Stage3PrerequisiteFacts =
        Array.AsReadOnly(["mul:0*0", "mul:0*1", "mul:1*0", "mul:1*1"]);

    private static readonly IReadOnlyList<string> EmptyFactList =
        Array.AsReadOnly(Array.Empty<string>());

    public static IReadOnlyList<ArithmeticOperation> GetUnlockedOperations(CurriculumStage stage) => stage switch
    {
        CurriculumStage.Stage1_Addition => Stage1Operations,
        CurriculumStage.Stage2_Subtraction => Stage2Operations,
        CurriculumStage.Stage3_Multiplication => Stage3Operations,
        CurriculumStage.Stage4_Division => Stage4Operations,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown curriculum stage.")
    };

    public static IReadOnlyList<string> GetPrerequisiteFactIds(CurriculumStage stage) => stage switch
    {
        CurriculumStage.Stage1_Addition => Stage1PrerequisiteFacts,
        CurriculumStage.Stage2_Subtraction => Stage2PrerequisiteFacts,
        CurriculumStage.Stage3_Multiplication => Stage3PrerequisiteFacts,
        CurriculumStage.Stage4_Division => EmptyFactList,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown curriculum stage.")
    };

    public static ArithmeticOperation? GetPrerequisiteOperation(CurriculumStage stage) => stage switch
    {
        CurriculumStage.Stage1_Addition => ArithmeticOperation.Addition,
        CurriculumStage.Stage2_Subtraction => ArithmeticOperation.Subtraction,
        CurriculumStage.Stage3_Multiplication => ArithmeticOperation.Multiplication,
        CurriculumStage.Stage4_Division => null,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown curriculum stage.")
    };

    public static bool IsPrerequisiteFullyIntroduced(
        CurriculumStage currentStage,
        IReadOnlyDictionary<string, ItemLearningState> itemStates,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? progressions = null)
    {
        ArgumentNullException.ThrowIfNull(itemStates);

        if (!Enum.IsDefined(currentStage))
        {
            throw new ArgumentOutOfRangeException(nameof(currentStage), currentStage, "Unknown curriculum stage.");
        }

        if (currentStage == CurriculumStage.Stage4_Division)
        {
            return false;
        }

        var prerequisiteOperation = GetPrerequisiteOperation(currentStage);
        if (prerequisiteOperation.HasValue &&
            progressions is not null &&
            progressions.TryGetValue(prerequisiteOperation.Value, out var progression) &&
            progression.BandIndex >= 1)
        {
            return true;
        }

        var prerequisiteFacts = GetPrerequisiteFactIds(currentStage);
        if (prerequisiteFacts.Count == 0)
        {
            return false;
        }

        foreach (var factId in prerequisiteFacts)
        {
            if (!itemStates.TryGetValue(factId, out var state) || state.TotalAttempts <= 0)
            {
                return false;
            }
        }

        return true;
    }

    public static int CountPrerequisiteWeakFacts(
        CurriculumStage currentStage,
        IReadOnlyDictionary<string, ItemLearningState> itemStates)
    {
        ArgumentNullException.ThrowIfNull(itemStates);

        if (!Enum.IsDefined(currentStage))
        {
            throw new ArgumentOutOfRangeException(nameof(currentStage), currentStage, "Unknown curriculum stage.");
        }

        if (currentStage == CurriculumStage.Stage4_Division)
        {
            return 0;
        }

        var prerequisiteFacts = GetPrerequisiteFactIds(currentStage);
        var weakCount = 0;

        foreach (var factId in prerequisiteFacts)
        {
            if (itemStates.TryGetValue(factId, out var state) && state.NeedsRemediation)
            {
                weakCount++;
            }
        }

        return weakCount;
    }

    public static bool CanAdvance(
        CurriculumStage currentStage,
        bool isPrerequisiteIntroduced,
        int prerequisiteWeakCount,
        bool hasBroadWeakness)
    {
        if (!Enum.IsDefined(currentStage))
        {
            throw new ArgumentOutOfRangeException(nameof(currentStage), currentStage, "Unknown curriculum stage.");
        }

        if (currentStage == CurriculumStage.Stage4_Division)
        {
            return false;
        }

        if (hasBroadWeakness)
        {
            return false;
        }

        if (!isPrerequisiteIntroduced)
        {
            return false;
        }

        if (prerequisiteWeakCount > 1)
        {
            return false;
        }

        return true;
    }

    public static bool CanAdvance(
        CurriculumStage currentStage,
        IReadOnlyDictionary<string, ItemLearningState> itemStates,
        bool hasBroadWeakness,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? progressions = null)
    {
        ArgumentNullException.ThrowIfNull(itemStates);

        if (!Enum.IsDefined(currentStage))
        {
            throw new ArgumentOutOfRangeException(nameof(currentStage), currentStage, "Unknown curriculum stage.");
        }

        if (currentStage == CurriculumStage.Stage4_Division)
        {
            return false;
        }

        var isIntroduced = IsPrerequisiteFullyIntroduced(currentStage, itemStates, progressions);
        var weakCount = CountPrerequisiteWeakFacts(currentStage, itemStates);

        return CanAdvance(currentStage, isIntroduced, weakCount, hasBroadWeakness);
    }

    public static CurriculumStage EvaluateNextStage(
        CurriculumStage currentStage,
        bool isPrerequisiteIntroduced,
        int prerequisiteWeakCount,
        bool hasBroadWeakness)
    {
        if (!Enum.IsDefined(currentStage))
        {
            throw new ArgumentOutOfRangeException(nameof(currentStage), currentStage, "Unknown curriculum stage.");
        }

        if (currentStage == CurriculumStage.Stage4_Division)
        {
            return CurriculumStage.Stage4_Division;
        }

        if (CanAdvance(currentStage, isPrerequisiteIntroduced, prerequisiteWeakCount, hasBroadWeakness))
        {
            return currentStage switch
            {
                CurriculumStage.Stage1_Addition => CurriculumStage.Stage2_Subtraction,
                CurriculumStage.Stage2_Subtraction => CurriculumStage.Stage3_Multiplication,
                CurriculumStage.Stage3_Multiplication => CurriculumStage.Stage4_Division,
                _ => currentStage
            };
        }

        return currentStage;
    }

    public static CurriculumStage EvaluateNextStage(
        CurriculumStage currentStage,
        IReadOnlyDictionary<string, ItemLearningState> itemStates,
        bool hasBroadWeakness,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? progressions = null)
    {
        ArgumentNullException.ThrowIfNull(itemStates);

        if (!Enum.IsDefined(currentStage))
        {
            throw new ArgumentOutOfRangeException(nameof(currentStage), currentStage, "Unknown curriculum stage.");
        }

        if (currentStage == CurriculumStage.Stage4_Division)
        {
            return CurriculumStage.Stage4_Division;
        }

        var isIntroduced = IsPrerequisiteFullyIntroduced(currentStage, itemStates, progressions);
        var weakCount = CountPrerequisiteWeakFacts(currentStage, itemStates);

        return EvaluateNextStage(currentStage, isIntroduced, weakCount, hasBroadWeakness);
    }
}
