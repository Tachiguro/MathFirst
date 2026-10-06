namespace MathFirst.Domain.Curriculum;

public static class BroadWeaknessPolicy
{
    public static int CountEligibleWeakFacts(
        IEnumerable<ItemLearningState> itemStates,
        IReadOnlyCollection<ArithmeticOperation> activeOperations,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression> progressions,
        ArithmeticCurriculum curriculum,
        GuidedNumberSpaceGate effectiveGate)
    {
        ArgumentNullException.ThrowIfNull(itemStates);
        ArgumentNullException.ThrowIfNull(activeOperations);
        ArgumentNullException.ThrowIfNull(progressions);
        ArgumentNullException.ThrowIfNull(curriculum);
        ArgumentNullException.ThrowIfNull(effectiveGate);

        var count = 0;
        Dictionary<ArithmeticOperation, AcquisitionOwnershipResolver>? resolvers = null;

        foreach (var itemState in itemStates)
        {
            if (!itemState.NeedsRemediation)
            {
                continue;
            }

            if (!activeOperations.Contains(itemState.Operation))
            {
                continue;
            }

            if (!progressions.TryGetValue(itemState.Operation, out var progression))
            {
                continue;
            }

            resolvers ??= new Dictionary<ArithmeticOperation, AcquisitionOwnershipResolver>(4);
            if (!resolvers.TryGetValue(itemState.Operation, out var ownership))
            {
                var operationCurriculum = curriculum.GetCurriculum(itemState.Operation);
                ownership = new AcquisitionOwnershipResolver(operationCurriculum);
                resolvers[itemState.Operation] = ownership;
            }

            if (!ownership.IsEligible(itemState.FactId, progression.BandIndex))
            {
                continue;
            }

            var fact = new ArithmeticFact(itemState.Operation, itemState.LeftOperand, itemState.RightOperand);
            if (!effectiveGate.Allows(fact))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    public static bool HasBroadWeakness(
        IEnumerable<ItemLearningState> itemStates,
        IReadOnlyCollection<ArithmeticOperation> activeOperations,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression> progressions,
        ArithmeticCurriculum curriculum,
        GuidedNumberSpaceGate effectiveGate)
    {
        ArgumentNullException.ThrowIfNull(itemStates);
        ArgumentNullException.ThrowIfNull(activeOperations);
        ArgumentNullException.ThrowIfNull(progressions);
        ArgumentNullException.ThrowIfNull(curriculum);
        ArgumentNullException.ThrowIfNull(effectiveGate);

        var count = 0;
        Dictionary<ArithmeticOperation, AcquisitionOwnershipResolver>? resolvers = null;

        foreach (var itemState in itemStates)
        {
            if (!itemState.NeedsRemediation)
            {
                continue;
            }

            if (!activeOperations.Contains(itemState.Operation))
            {
                continue;
            }

            if (!progressions.TryGetValue(itemState.Operation, out var progression))
            {
                continue;
            }

            resolvers ??= new Dictionary<ArithmeticOperation, AcquisitionOwnershipResolver>(4);
            if (!resolvers.TryGetValue(itemState.Operation, out var ownership))
            {
                var operationCurriculum = curriculum.GetCurriculum(itemState.Operation);
                ownership = new AcquisitionOwnershipResolver(operationCurriculum);
                resolvers[itemState.Operation] = ownership;
            }

            if (!ownership.IsEligible(itemState.FactId, progression.BandIndex))
            {
                continue;
            }

            var fact = new ArithmeticFact(itemState.Operation, itemState.LeftOperand, itemState.RightOperand);
            if (!effectiveGate.Allows(fact))
            {
                continue;
            }

            count++;
            if (count >= LearningPolicy.BroadWeaknessThreshold)
            {
                return true;
            }
        }

        return false;
    }
}
