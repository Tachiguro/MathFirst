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

    public static bool CanToggleOperationOff(
        IReadOnlyCollection<ArithmeticOperation> currentEnabled,
        ArithmeticOperation operation)
    {
        ArgumentNullException.ThrowIfNull(currentEnabled);
        if (!currentEnabled.Contains(operation))
        {
            return true;
        }

        return currentEnabled.Count > 1;
    }

    public static bool TryToggleOperation(
        Action<ArithmeticOperation, bool> setOperationEnabled,
        Func<IReadOnlyList<ArithmeticOperation>> getEnabledOperations,
        IReadOnlyCollection<ArithmeticOperation> currentEnabled,
        ArithmeticOperation operation,
        out IReadOnlyList<ArithmeticOperation> resultingEnabled)
    {
        ArgumentNullException.ThrowIfNull(setOperationEnabled);
        ArgumentNullException.ThrowIfNull(getEnabledOperations);
        ArgumentNullException.ThrowIfNull(currentEnabled);

        if (!CanToggleOperationOff(currentEnabled, operation))
        {
            resultingEnabled = NormalizeEnabledOperations(currentEnabled);
            return false;
        }

        var nextState = !currentEnabled.Contains(operation);
        setOperationEnabled(operation, nextState);
        resultingEnabled = getEnabledOperations();
        return true;
    }
}
