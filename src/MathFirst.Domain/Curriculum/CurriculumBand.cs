namespace MathFirst.Domain.Curriculum;

public sealed class CurriculumBand
{
    public ArithmeticOperation Operation { get; }
    public int BandIndex { get; }
    public CurriculumBandId Id { get; }
    public CurriculumBandKind Kind { get; }
    public IReadOnlyList<ArithmeticFact> Frontier { get; }

    public CurriculumBand(
        ArithmeticOperation operation,
        int bandIndex,
        CurriculumBandId id,
        CurriculumBandKind kind,
        IReadOnlyList<ArithmeticFact> frontier)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown arithmetic operation.");
        }

        if (bandIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bandIndex), bandIndex, "Band index must be non-negative.");
        }

        ArgumentNullException.ThrowIfNull(id);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown curriculum band kind.");
        }

        ArgumentNullException.ThrowIfNull(frontier);
        var facts = frontier.ToArray();
        if (facts.Length == 0)
        {
            throw new ArgumentException("A complete curriculum band must have a non-empty frontier.", nameof(frontier));
        }

        if (facts.Any(fact => fact.Operation != operation))
        {
            throw new ArgumentException("Every frontier fact must match the curriculum band's operation.", nameof(frontier));
        }

        if (facts.Select(fact => fact.Id).Distinct(StringComparer.Ordinal).Count() != facts.Length)
        {
            throw new ArgumentException("A curriculum band frontier cannot contain duplicate FactIds.", nameof(frontier));
        }

        Operation = operation;
        BandIndex = bandIndex;
        Id = id;
        Kind = kind;
        Frontier = Array.AsReadOnly(facts);
    }
}
