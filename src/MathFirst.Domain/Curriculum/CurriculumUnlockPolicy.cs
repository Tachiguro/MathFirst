namespace MathFirst.Domain.Curriculum;

public static class CurriculumUnlockPolicy
{
    /// <summary>
    /// Number of leading Dense bands (D01..D10) whose canonical facts a prerequisite operation must master
    /// before the next operation unlocks (ADR-0013). BandIndex is zero-based, so completing D10 means BandIndex &gt;= 10.
    /// </summary>
    public const int PrerequisiteMasteryBandCount = 10;

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

    private static readonly IReadOnlyList<string> Stage1BoundaryFacts =
        Array.AsReadOnly(["add:10+10"]);

    private static readonly IReadOnlyList<string> Stage2BoundaryFacts =
        Array.AsReadOnly(["sub:10-9", "sub:10-10"]);

    private static readonly IReadOnlyList<string> Stage3BoundaryFacts =
        Array.AsReadOnly(["mul:10*10"]);

    private static readonly IReadOnlyList<string> EmptyFactList =
        Array.AsReadOnly(Array.Empty<string>());

    private static readonly IReadOnlyDictionary<ArithmeticOperation, IReadOnlyList<string>> MasteryFactsByOperation =
        CreateMasteryFactsByOperation();

    public static IReadOnlyList<ArithmeticOperation> GetUnlockedOperations(CurriculumStage stage) => stage switch
    {
        CurriculumStage.Stage1_Addition => Stage1Operations,
        CurriculumStage.Stage2_Subtraction => Stage2Operations,
        CurriculumStage.Stage3_Multiplication => Stage3Operations,
        CurriculumStage.Stage4_Division => Stage4Operations,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown curriculum stage.")
    };

    /// <summary>
    /// Legacy ADR-0012 introductory (D01) prerequisite facts. Retained only for the conservative Schema V8 to V9
    /// migration; live stage advancement uses <see cref="EvaluatePrerequisiteMastery"/>.
    /// </summary>
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

    /// <summary>
    /// Explicit D10 boundary facts that must carry authoritative mastery evidence before leaving <paramref name="stage"/>.
    /// They are also members of <see cref="GetMasteryFactIds"/>; listing them separately pins the owner-approved boundary.
    /// </summary>
    public static IReadOnlyList<string> GetRequiredBoundaryFactIds(CurriculumStage stage) => stage switch
    {
        CurriculumStage.Stage1_Addition => Stage1BoundaryFacts,
        CurriculumStage.Stage2_Subtraction => Stage2BoundaryFacts,
        CurriculumStage.Stage3_Multiplication => Stage3BoundaryFacts,
        CurriculumStage.Stage4_Division => EmptyFactList,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown curriculum stage.")
    };

    /// <summary>
    /// Canonical facts owned by the D01..D10 Dense bands of a prerequisite operation, in curriculum order.
    /// Division is never a prerequisite and has no mastery set.
    /// </summary>
    public static IReadOnlyList<string> GetMasteryFactIds(ArithmeticOperation operation)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown arithmetic operation.");
        }

        return MasteryFactsByOperation.TryGetValue(operation, out var factIds) ? factIds : EmptyFactList;
    }

    /// <summary>
    /// A fact carries authoritative mastery evidence when it has been attempted, answered correctly, is not awaiting
    /// remediation, and its latest accepted outcome is Correct. A missed fact therefore stays unresolved until a later,
    /// remediation-spaced presentation is answered correctly. Latency and fluency are deliberately ignored: slow
    /// correct answers count fully.
    /// </summary>
    public static bool IsFactMasteryDemonstrated(ItemLearningState? state) =>
        state is not null
        && state.TotalAttempts > 0
        && state.CorrectAttempts > 0
        && !state.NeedsRemediation
        && state.ConsecutiveCorrectStreak > 0;

    public static PrerequisiteMasteryEvidence EvaluatePrerequisiteMastery(
        CurriculumStage currentStage,
        IReadOnlyDictionary<string, ItemLearningState> itemStates,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? progressions)
    {
        ArgumentNullException.ThrowIfNull(itemStates);

        if (!Enum.IsDefined(currentStage))
        {
            throw new ArgumentOutOfRangeException(nameof(currentStage), currentStage, "Unknown curriculum stage.");
        }

        var prerequisiteOperation = GetPrerequisiteOperation(currentStage);
        if (!prerequisiteOperation.HasValue)
        {
            return new PrerequisiteMasteryEvidence(currentStage, null, -1, 0, EmptyFactList, EmptyFactList);
        }

        var bandIndex = progressions is not null
            && progressions.TryGetValue(prerequisiteOperation.Value, out var progression)
            && progression is not null
                ? progression.BandIndex
                : -1;

        var masteryFacts = GetMasteryFactIds(prerequisiteOperation.Value);
        var unresolved = masteryFacts
            .Where(factId => !IsFactMasteryDemonstrated(itemStates.GetValueOrDefault(factId)))
            .ToArray();
        var unresolvedBoundary = GetRequiredBoundaryFactIds(currentStage)
            .Where(factId => !IsFactMasteryDemonstrated(itemStates.GetValueOrDefault(factId)))
            .ToArray();

        return new PrerequisiteMasteryEvidence(
            currentStage,
            prerequisiteOperation,
            bandIndex,
            masteryFacts.Count,
            Array.AsReadOnly(unresolved),
            Array.AsReadOnly(unresolvedBoundary));
    }

    public static bool IsPrerequisiteMastered(
        CurriculumStage currentStage,
        IReadOnlyDictionary<string, ItemLearningState> itemStates,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? progressions) =>
        EvaluatePrerequisiteMastery(currentStage, itemStates, progressions).IsSatisfied;

    /// <summary>
    /// Legacy ADR-0012 introductory readiness predicate. Retained only for the conservative Schema V8 to V9
    /// migration; it no longer authorizes live stage advancement.
    /// </summary>
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

    /// <summary>
    /// Legacy ADR-0012 introductory weak-fact count. Retained only for the conservative Schema V8 to V9 migration.
    /// </summary>
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
        bool isPrerequisiteMastered,
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

        return isPrerequisiteMastered;
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

        var isMastered = IsPrerequisiteMastered(currentStage, itemStates, progressions);

        return CanAdvance(currentStage, isMastered, hasBroadWeakness);
    }

    public static CurriculumStage EvaluateNextStage(
        CurriculumStage currentStage,
        bool isPrerequisiteMastered,
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

        if (CanAdvance(currentStage, isPrerequisiteMastered, hasBroadWeakness))
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

        var isMastered = IsPrerequisiteMastered(currentStage, itemStates, progressions);

        return EvaluateNextStage(currentStage, isMastered, hasBroadWeakness);
    }

    private static IReadOnlyDictionary<ArithmeticOperation, IReadOnlyList<string>> CreateMasteryFactsByOperation()
    {
        var curriculum = new ArithmeticCurriculum();
        var result = new Dictionary<ArithmeticOperation, IReadOnlyList<string>>();

        foreach (var operation in new[]
                 {
                     ArithmeticOperation.Addition,
                     ArithmeticOperation.Subtraction,
                     ArithmeticOperation.Multiplication
                 })
        {
            var ownership = new AcquisitionOwnershipResolver(curriculum.GetCurriculum(operation));
            var factIds = new List<string>();
            for (var bandIndex = 0; bandIndex < PrerequisiteMasteryBandCount; bandIndex++)
            {
                factIds.AddRange(ownership.GetOwnedFrontier(bandIndex).Select(fact => fact.Id));
            }

            result[operation] = Array.AsReadOnly(factIds.ToArray());
        }

        return result;
    }
}
