namespace MathFirst.Domain;

public static class PracticeOperationPreferencePolicy
{
    public static readonly IReadOnlyList<ArithmeticOperation> AllOperations =
    [
        ArithmeticOperation.Addition,
        ArithmeticOperation.Subtraction,
        ArithmeticOperation.Multiplication,
        ArithmeticOperation.Division
    ];

    public static IReadOnlyList<ArithmeticOperation> NormalizeEnabledOperations(
        IEnumerable<ArithmeticOperation>? enabledOperations)
    {
        if (enabledOperations is null)
        {
            return AllOperations;
        }

        var set = enabledOperations.ToHashSet();
        var canonical = AllOperations.Where(op => set.Contains(op)).ToArray();

        return canonical.Length > 0 ? canonical : AllOperations;
    }
}
