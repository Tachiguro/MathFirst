namespace MathFirst.Domain;

public static class ArithmeticCatalog
{
    public const int DefaultInitialMaxOperand = 1;
    public const int MaxV1Operand = 10;

    public static IReadOnlyList<ArithmeticFact> GetFacts(ArithmeticOperation operation, int maxOperand)
    {
        if (maxOperand < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOperand), maxOperand, "Max operand must be non-negative.");
        }

        var clampedMax = Math.Min(maxOperand, MaxV1Operand);
        var list = new List<ArithmeticFact>();

        switch (operation)
        {
            case ArithmeticOperation.Addition:
                for (var l = 0; l <= clampedMax; l++)
                {
                    for (var r = 0; r <= clampedMax; r++)
                    {
                        list.Add(new ArithmeticFact(ArithmeticOperation.Addition, l, r));
                    }
                }
                break;

            case ArithmeticOperation.Subtraction:
                for (var l = 0; l <= clampedMax; l++)
                {
                    for (var r = 0; r <= l; r++)
                    {
                        list.Add(new ArithmeticFact(ArithmeticOperation.Subtraction, l, r));
                    }
                }
                break;

            case ArithmeticOperation.Multiplication:
                for (var l = 0; l <= clampedMax; l++)
                {
                    for (var r = 0; r <= clampedMax; r++)
                    {
                        list.Add(new ArithmeticFact(ArithmeticOperation.Multiplication, l, r));
                    }
                }
                break;

            case ArithmeticOperation.Division:
                // No division by zero: divisor r >= 1
                for (var r = 1; r <= clampedMax; r++)
                {
                    for (var quotient = 0; quotient <= clampedMax; quotient++)
                    {
                        var dividend = r * quotient;
                        list.Add(new ArithmeticFact(ArithmeticOperation.Division, dividend, r));
                    }
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown arithmetic operation.");
        }

        return list;
    }

    public static IReadOnlyList<ArithmeticFact> GetAllFacts(int maxOperand)
    {
        var list = new List<ArithmeticFact>();
        list.AddRange(GetFacts(ArithmeticOperation.Addition, maxOperand));
        list.AddRange(GetFacts(ArithmeticOperation.Subtraction, maxOperand));
        list.AddRange(GetFacts(ArithmeticOperation.Multiplication, maxOperand));
        list.AddRange(GetFacts(ArithmeticOperation.Division, maxOperand));
        return list;
    }

    public static IReadOnlyList<ArithmeticFact> GetActiveFacts(LearnerProgression progression)
    {
        ArgumentNullException.ThrowIfNull(progression);
        return GetActiveFacts(progression.OperationMaxOperands);
    }

    public static IReadOnlyList<ArithmeticFact> GetActiveFacts(IReadOnlyDictionary<ArithmeticOperation, int> maxOperands)
    {
        ArgumentNullException.ThrowIfNull(maxOperands);

        var list = new List<ArithmeticFact>();
        var addMax = maxOperands.TryGetValue(ArithmeticOperation.Addition, out var a) ? a : DefaultInitialMaxOperand;
        var subMax = maxOperands.TryGetValue(ArithmeticOperation.Subtraction, out var s) ? s : DefaultInitialMaxOperand;
        var mulMax = maxOperands.TryGetValue(ArithmeticOperation.Multiplication, out var m) ? m : DefaultInitialMaxOperand;
        var divMax = maxOperands.TryGetValue(ArithmeticOperation.Division, out var d) ? d : DefaultInitialMaxOperand;

        list.AddRange(GetFacts(ArithmeticOperation.Addition, addMax));
        list.AddRange(GetFacts(ArithmeticOperation.Subtraction, subMax));
        list.AddRange(GetFacts(ArithmeticOperation.Multiplication, mulMax));
        list.AddRange(GetFacts(ArithmeticOperation.Division, divMax));
        return list;
    }

    public static IReadOnlyList<ArithmeticFact> GetNewlyUnlockedFacts(ArithmeticOperation operation, int newlyUnlockedOperand)
    {
        if (newlyUnlockedOperand <= DefaultInitialMaxOperand)
        {
            // At the fresh initial range (0..1), all 0..1 facts are considered new for introduction
            return GetFacts(operation, DefaultInitialMaxOperand);
        }

        var allUpToNew = GetFacts(operation, newlyUnlockedOperand);
        var allPrior = GetFacts(operation, newlyUnlockedOperand - 1);
        var priorIds = allPrior.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);

        return allUpToNew.Where(f => !priorIds.Contains(f.Id)).ToList();
    }
}
