namespace MathFirst.Domain.Curriculum;

public sealed class OperationCurriculum
{
    private readonly Func<int, CurriculumBand?>? _structuredBandFactory;

    public ArithmeticOperation Operation { get; }
    public IReadOnlyList<CurriculumBand> Bands { get; }

    public OperationCurriculum(
        ArithmeticOperation operation,
        IReadOnlyList<CurriculumBand> bands,
        Func<int, CurriculumBand?>? structuredBandFactory = null)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown arithmetic operation.");
        }

        ArgumentNullException.ThrowIfNull(bands);
        var completeBands = bands.ToArray();
        if (completeBands.Length == 0)
        {
            throw new ArgumentException("An operation curriculum must contain at least one complete band.", nameof(bands));
        }

        for (var index = 0; index < completeBands.Length; index++)
        {
            var band = completeBands[index];
            if (band.Operation != operation)
            {
                throw new ArgumentException("Every band must match the curriculum operation.", nameof(bands));
            }

            if (band.BandIndex != index)
            {
                throw new ArgumentException("Bands must use contiguous zero-based canonical indices.", nameof(bands));
            }
        }

        if (completeBands.Select(band => band.Id).Distinct().Count() != completeBands.Length)
        {
            throw new ArgumentException("Curriculum band IDs must be unique within an operation.", nameof(bands));
        }

        Operation = operation;
        Bands = Array.AsReadOnly(completeBands);
        _structuredBandFactory = structuredBandFactory;
    }

    public bool TryGetBand(int bandIndex, out CurriculumBand? band)
    {
        if (bandIndex < 0)
        {
            band = null;
            return false;
        }

        if (bandIndex < Bands.Count)
        {
            band = Bands[bandIndex];
            return true;
        }

        band = _structuredBandFactory?.Invoke(bandIndex);
        if (band is null)
        {
            return false;
        }

        if (band.Operation != Operation || band.BandIndex != bandIndex || band.Kind != CurriculumBandKind.Structured)
        {
            throw new InvalidOperationException("A procedural band must preserve its curriculum operation, index, and structured kind.");
        }

        return true;
    }
}
