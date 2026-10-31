namespace MathFirst.Domain.Curriculum;

public sealed class AcquisitionOwnershipResolver
{
    private readonly OperationCurriculum _curriculum;
    private readonly Dictionary<string, int> _owners = new(StringComparer.Ordinal);
    private readonly List<IReadOnlyList<ArithmeticFact>> _ownedFrontiers = [];
    private int _availableAnalyticThroughBandIndex = -1;
    private int _cachedAnalyticBandIndex = -1;
    private IReadOnlyList<ArithmeticFact>? _cachedAnalyticFrontier;

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
        if (_curriculum.AnalyticModel is not null)
        {
            // Analytic frontiers are disjoint by construction: the owned frontier is the band itself, resolved
            // without materializing any earlier band (Issue #126).
            if (bandIndex >= 0 && bandIndex == _cachedAnalyticBandIndex)
            {
                frontier = _cachedAnalyticFrontier;
                return true;
            }

            if (bandIndex < 0 || !_curriculum.TryGetBand(bandIndex, out var band))
            {
                frontier = null;
                return false;
            }

            _cachedAnalyticBandIndex = bandIndex;
            _cachedAnalyticFrontier = band!.Frontier;
            _availableAnalyticThroughBandIndex = Math.Max(_availableAnalyticThroughBandIndex, bandIndex);
            frontier = _cachedAnalyticFrontier;
            return true;
        }

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
        if (_curriculum.AnalyticModel is not null)
        {
            // Same answer a fresh prefix resolver gives: only owners inside the complete prefix through the band.
            if (throughBandIndex >= 0
                && _curriculum.AnalyticModel.TryGetOwnerBandIndex(factId, out ownerBandIndex)
                && ownerBandIndex <= throughBandIndex
                && IsAnalyticBandAvailable(throughBandIndex))
            {
                return true;
            }

            ownerBandIndex = -1;
            return false;
        }

        if (throughBandIndex < 0 || !EnsurePrefixThrough(throughBandIndex))
        {
            ownerBandIndex = -1;
            return false;
        }

        return _owners.TryGetValue(factId, out ownerBandIndex);
    }

    public bool IsEligible(string factId, int throughBandIndex)
    {
        if (string.IsNullOrWhiteSpace(factId) || throughBandIndex < 0)
        {
            return false;
        }

        return TryGetOwner(factId, throughBandIndex, out var owner)
            && owner <= throughBandIndex;
    }

    private bool IsAnalyticBandAvailable(int bandIndex)
    {
        // Complete bands are contiguous, so one successful lookup proves every lower band; repeated eligibility checks
        // against the same progression therefore generate the procedural band at most once per resolver.
        if (bandIndex <= _availableAnalyticThroughBandIndex)
        {
            return true;
        }

        if (!_curriculum.TryGetBand(bandIndex, out _))
        {
            return false;
        }

        _availableAnalyticThroughBandIndex = bandIndex;
        return true;
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
