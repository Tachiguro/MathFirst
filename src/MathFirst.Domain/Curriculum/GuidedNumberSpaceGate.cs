namespace MathFirst.Domain.Curriculum;

public sealed class GuidedNumberSpaceGate
{
    public static GuidedNumberSpaceGate Unrestricted { get; } = new(null);

    public bool IsActive => AdditionCeiling.HasValue;
    public int? AdditionCeiling { get; }

    private GuidedNumberSpaceGate(int? additionCeiling)
    {
        AdditionCeiling = additionCeiling;
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
        int unlockedAdditionBandIndex)
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

        return new GuidedNumberSpaceGate(additionCeiling);
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
            ArithmeticOperation.Multiplication => fact.CorrectResult <= AdditionCeiling!.Value,
            ArithmeticOperation.Division => fact.LeftOperand <= AdditionCeiling!.Value,
            _ => throw new ArgumentOutOfRangeException(
                nameof(fact),
                fact.Operation,
                "Guided number-space gating requires a valid arithmetic operation.")
        };
    }
}
