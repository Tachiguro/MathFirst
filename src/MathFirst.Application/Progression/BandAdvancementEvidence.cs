namespace MathFirst.Application.Progression;

using System.Collections.Frozen;

public sealed class BandAdvancementEvidence
{
    public IReadOnlyList<BandAttemptEvidence> AcceptedAttempts { get; }
    public IReadOnlySet<string> LifetimeAttemptedFactIds { get; }
    public IReadOnlySet<string> CurrentBandIntroducedFactIds { get; }

    public BandAdvancementEvidence(
        IEnumerable<BandAttemptEvidence> acceptedAttempts,
        IEnumerable<string> lifetimeAttemptedFactIds,
        IEnumerable<string> currentBandIntroducedFactIds)
    {
        ArgumentNullException.ThrowIfNull(acceptedAttempts);
        ArgumentNullException.ThrowIfNull(lifetimeAttemptedFactIds);
        ArgumentNullException.ThrowIfNull(currentBandIntroducedFactIds);

        var attempts = acceptedAttempts.ToArray();
        if (attempts.Any(attempt => attempt is null))
        {
            throw new ArgumentException("Attempt evidence cannot contain null entries.", nameof(acceptedAttempts));
        }

        AcceptedAttempts = Array.AsReadOnly(attempts);
        LifetimeAttemptedFactIds = CopyFactIds(lifetimeAttemptedFactIds, nameof(lifetimeAttemptedFactIds));
        CurrentBandIntroducedFactIds = CopyFactIds(currentBandIntroducedFactIds, nameof(currentBandIntroducedFactIds));
    }

    private static IReadOnlySet<string> CopyFactIds(IEnumerable<string> factIds, string parameterName)
    {
        var copied = new List<string>();
        foreach (var factId in factIds)
        {
            if (string.IsNullOrWhiteSpace(factId))
            {
                throw new ArgumentException("FactId evidence cannot contain null or whitespace values.", parameterName);
            }

            copied.Add(factId);
        }

        return copied.ToFrozenSet(StringComparer.Ordinal);
    }
}
