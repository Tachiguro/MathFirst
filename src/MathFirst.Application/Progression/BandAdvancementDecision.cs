namespace MathFirst.Application.Progression;

using MathFirst.Domain;

public sealed record BandAdvancementDecision
{
    public bool Advances { get; }
    public OperationProgression ResultingProgression { get; }

    internal BandAdvancementDecision(bool advances, OperationProgression resultingProgression)
    {
        ArgumentNullException.ThrowIfNull(resultingProgression);
        Advances = advances;
        ResultingProgression = resultingProgression;
    }
}
