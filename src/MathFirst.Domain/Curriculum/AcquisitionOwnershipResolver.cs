namespace MathFirst.Domain.Curriculum;

public sealed class AcquisitionOwnershipResolver
{
    private readonly OperationCurriculum _curriculum;
    private readonly Dictionary<string, int> _owners = new(StringComparer.Ordinal);
    private readonly List<IReadOnlyList<ArithmeticFact>> _ownedFrontiers = [];

    public AcquisitionOwnershipResolver(OperationCurriculum curriculum)
    {
        ArgumentNullException.ThrowIfNull(curriculum);
        _curriculum = curriculum;
    }

    public IReadOnlyList<ArithmeticFact> GetOwnedFrontier(int bandIndex)
    {
        if (!TryGetOwnedFrontier(bandIndex, out var frontier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bandIndex),
                bandIndex,
                "The requested complete curriculum band is unavailable.");
        }

        return frontier!;
    }

    public bool TryGetOwnedFrontier(int bandIndex, out IReadOnlyList<ArithmeticFact>? frontier)
    {
        if (bandIndex < 0 || !EnsurePrefixThrough(bandIndex))
        {
            frontier = null;
            return false;
        }

        frontier = _ownedFrontiers[bandIndex];
        return true;
    }

    public bool TryGetOwner(string factId, int throughBandIndex, out int ownerBandIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factId);
        if (throughBandIndex < 0 || !EnsurePrefixThrough(throughBandIndex))
        {
            ownerBandIndex = -1;
            return false;
        }

        return _owners.TryGetValue(factId, out ownerBandIndex);
    }

    private bool EnsurePrefixThrough(int requestedBandIndex)
    {
        while (_ownedFrontiers.Count <= requestedBandIndex)
        {
            var bandIndex = _ownedFrontiers.Count;
            if (!_curriculum.TryGetBand(bandIndex, out var band))
            {
                return false;
            }

            var owned = new List<ArithmeticFact>();
            foreach (var fact in band!.Frontier)
            {
                if (_owners.TryAdd(fact.Id, bandIndex))
                {
                    owned.Add(fact);
                }
            }

            _ownedFrontiers.Add(Array.AsReadOnly(owned.ToArray()));
        }

        return true;
    }
}
