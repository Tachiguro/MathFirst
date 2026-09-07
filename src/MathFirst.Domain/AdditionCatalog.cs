namespace MathFirst.Domain;

public static class AdditionCatalog
{
    public const int MinOperand = 0;
    public const int MaxOperand = 10;
    public const int TotalFactsCount = (MaxOperand - MinOperand + 1) * (MaxOperand - MinOperand + 1); // 121

    public static IReadOnlyList<AdditionFact> CreateFullCatalog()
    {
        var facts = new List<AdditionFact>(TotalFactsCount);
        for (var left = MinOperand; left <= MaxOperand; left++)
        {
            for (var right = MinOperand; right <= MaxOperand; right++)
            {
                facts.Add(new AdditionFact(left, right));
            }
        }
        return facts.AsReadOnly();
    }
}
