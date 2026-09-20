namespace MathFirst.Application.Persistence;

using MathFirst.Application.Scheduling;
using MathFirst.Domain;

public sealed record LearnerSnapshot
{
    public LearnerProgression Progression { get; }
    public IReadOnlyDictionary<string, ItemLearningState> ItemStates { get; }
    public IReadOnlyDictionary<string, FsrsCardState> FsrsStates { get; }
    public IReadOnlyList<AttemptRecord> RecentAttempts { get; }
    public long Revision { get; }
    public int SchemaVersion { get; }
    public IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? OperationProgressions { get; }
    public DateTimeOffset? LatestAcceptedPracticeAt { get; }
    public IReadOnlyDictionary<ArithmeticOperation, long> OperationAcceptedAttemptCounts { get; }

    public LearnerSnapshot(
        LearnerProgression progression,
        IReadOnlyDictionary<string, ItemLearningState> itemStates,
        IReadOnlyDictionary<string, FsrsCardState> fsrsStates,
        IReadOnlyList<AttemptRecord> recentAttempts,
        long revision,
        int schemaVersion,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? operationProgressions = null,
        DateTimeOffset? latestAcceptedPracticeAt = null,
        IReadOnlyDictionary<ArithmeticOperation, long>? operationAcceptedAttemptCounts = null)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(itemStates);
        ArgumentNullException.ThrowIfNull(fsrsStates);
        ArgumentNullException.ThrowIfNull(recentAttempts);

        Progression = progression;
        ItemStates = itemStates;
        FsrsStates = fsrsStates;
        RecentAttempts = recentAttempts;
        Revision = revision;
        SchemaVersion = schemaVersion;
        OperationProgressions = operationProgressions;
        LatestAcceptedPracticeAt = latestAcceptedPracticeAt;

        if (operationAcceptedAttemptCounts is not null)
        {
            ValidateOperationAcceptedAttemptCounts(operationAcceptedAttemptCounts);
            OperationAcceptedAttemptCounts = operationAcceptedAttemptCounts;
        }
        else if (itemStates.Count > 0)
        {
            OperationAcceptedAttemptCounts = Enum.GetValues<ArithmeticOperation>()
                .ToDictionary(
                    op => op,
                    op => checked(itemStates.Values.Where(state => state.Operation == op).Sum(state => (long)state.TotalAttempts)));
        }
        else if (progression.PracticePosition == 0 && recentAttempts.Count == 0 && latestAcceptedPracticeAt == null)
        {
            OperationAcceptedAttemptCounts = Enum.GetValues<ArithmeticOperation>()
                .ToDictionary(op => op, _ => 0L);
        }
        else
        {
            throw new InvalidOperationException("Authoritative operation accepted attempt counts are required when practice history exists and item states are empty.");
        }
    }

    public LearnerSnapshot(
        LearnerProgression progression,
        IReadOnlyDictionary<string, ItemLearningState> itemStates,
        IReadOnlyList<AttemptRecord> recentAttempts,
        long revision,
        int schemaVersion)
        : this(progression, itemStates, new Dictionary<string, FsrsCardState>(StringComparer.Ordinal), recentAttempts, revision, schemaVersion)
    {
    }

    private static void ValidateOperationAcceptedAttemptCounts(IReadOnlyDictionary<ArithmeticOperation, long> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        foreach (var op in Enum.GetValues<ArithmeticOperation>())
        {
            if (!counts.TryGetValue(op, out var count))
            {
                throw new ArgumentException($"Missing operation accepted attempt count for {op}.", nameof(counts));
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(counts), count, $"Operation accepted attempt count for {op} cannot be negative.");
            }
        }
    }
}
