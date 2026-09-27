namespace MathFirst.Domain.Curriculum;

public sealed class GuidedNumberSpaceGate
{
    public const int DecouplingBandIndexThreshold = 3;

    public static GuidedNumberSpaceGate Unrestricted { get; } = new(null, isMultiplicationDecoupled: false, isDivisionDecoupled: false);

    public bool IsActive => AdditionCeiling.HasValue;
    public int? AdditionCeiling { get; }
    public bool IsMultiplicationDecoupled { get; }
    public bool IsDivisionDecoupled { get; }

    private GuidedNumberSpaceGate(
        int? additionCeiling,
        bool isMultiplicationDecoupled = false,
        bool isDivisionDecoupled = false)
    {
        AdditionCeiling = additionCeiling;
        IsMultiplicationDecoupled = isMultiplicationDecoupled;
        IsDivisionDecoupled = isDivisionDecoupled;
    }

    public static bool IsGuidedMode(IEnumerable<ArithmeticOperation>? enabledOperations)
    {
        var suppliedOperations = enabledOperations?.ToArray();
        if (suppliedOperations?.Any(operation => !Enum.IsDefined(operation)) == true)
        {
            var invalidOperation = suppliedOperations.First(operation => !Enum.IsDefined(operation));
            throw new ArgumentOutOfRangeException(
                nameof(enabledOperations),
                invalidOperation,
                "Enabled operations must contain only valid arithmetic operations.");
        }

        var normalized = PracticeOperationPreferencePolicy.NormalizeEnabledOperations(suppliedOperations);
        return normalized.SequenceEqual(PracticeOperationPreferencePolicy.AllOperations);
    }

    public static GuidedNumberSpaceGate ForGuided(
        OperationCurriculum additionCurriculum,
        int unlockedAdditionBandIndex,
        int multiplicationBandIndex = 0,
        int divisionBandIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(additionCurriculum);
        if (additionCurriculum.Operation != ArithmeticOperation.Addition)
        {
            throw new ArgumentException(
                "Guided number-space gating requires the Addition curriculum.",
                nameof(additionCurriculum));
        }

        if (unlockedAdditionBandIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unlockedAdditionBandIndex),
                unlockedAdditionBandIndex,
                "Unlocked Addition band index must be non-negative.");
        }

        if (multiplicationBandIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(multiplicationBandIndex),
                multiplicationBandIndex,
                "Multiplication band index must be non-negative.");
        }

        if (divisionBandIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(divisionBandIndex),
                divisionBandIndex,
                "Division band index must be non-negative.");
        }

        var additionCeiling = 0;
        for (var bandIndex = 0; bandIndex <= unlockedAdditionBandIndex; bandIndex++)
        {
            if (!additionCurriculum.TryGetBand(bandIndex, out var band))
            {
                throw new InvalidOperationException(
                    $"Addition curriculum prefix band {bandIndex} is unavailable.");
            }

            foreach (var fact in band!.Frontier)
            {
                additionCeiling = Math.Max(additionCeiling, fact.LeftOperand);
                additionCeiling = Math.Max(additionCeiling, fact.RightOperand);
                additionCeiling = Math.Max(additionCeiling, fact.CorrectResult);
            }
        }

        var isMultiplicationDecoupled = multiplicationBandIndex >= DecouplingBandIndexThreshold;
        var isDivisionDecoupled = divisionBandIndex >= DecouplingBandIndexThreshold;

        return new GuidedNumberSpaceGate(
            additionCeiling,
            isMultiplicationDecoupled,
            isDivisionDecoupled);
    }

    public static GuidedNumberSpaceGate ForGuided(
        OperationCurriculum additionCurriculum,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression> progressions)
    {
        ArgumentNullException.ThrowIfNull(additionCurriculum);
        ArgumentNullException.ThrowIfNull(progressions);

        if (!progressions.TryGetValue(ArithmeticOperation.Addition, out var additionProgression))
        {
            throw new ArgumentException(
                "Operation progressions must contain Addition progression.",
                nameof(progressions));
        }

        var multiplicationBandIndex = progressions.TryGetValue(ArithmeticOperation.Multiplication, out var mulProgression)
            ? mulProgression.BandIndex
            : 0;

        var divisionBandIndex = progressions.TryGetValue(ArithmeticOperation.Division, out var divProgression)
            ? divProgression.BandIndex
            : 0;

        return ForGuided(
            additionCurriculum,
            additionProgression.BandIndex,
            multiplicationBandIndex,
            divisionBandIndex);
    }

    public bool Allows(ArithmeticFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (!Enum.IsDefined(fact.Operation))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fact),
                fact.Operation,
                "Guided number-space gating requires a valid arithmetic operation.");
        }

        if (!IsActive)
        {
            return true;
        }

        return fact.Operation switch
        {
            ArithmeticOperation.Addition => true,
            ArithmeticOperation.Subtraction => true,
            ArithmeticOperation.Multiplication => IsMultiplicationDecoupled || fact.CorrectResult <= AdditionCeiling!.Value,
            ArithmeticOperation.Division => IsDivisionDecoupled || fact.LeftOperand <= AdditionCeiling!.Value,
            _ => throw new ArgumentOutOfRangeException(
                nameof(fact),
                fact.Operation,
                "Guided number-space gating requires a valid arithmetic operation.")
        };
    }
}
