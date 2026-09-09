namespace MathFirst.Application.Persistence;

using MathFirst.Application.Scheduling;
using MathFirst.Domain;

public sealed record LearnerSnapshot(
    LearnerProgression Progression,
    IReadOnlyDictionary<string, ItemLearningState> ItemStates,
    IReadOnlyDictionary<string, FsrsCardState> FsrsStates,
    IReadOnlyList<AttemptRecord> RecentAttempts,
    long Revision,
    int SchemaVersion,
    IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? OperationProgressions = null,
    DateTimeOffset? LatestAcceptedPracticeAt = null)
{
    public LearnerSnapshot(
        LearnerProgression progression,
        IReadOnlyDictionary<string, ItemLearningState> itemStates,
        IReadOnlyList<AttemptRecord> recentAttempts,
        long revision,
        int schemaVersion)
        : this(progression, itemStates, new Dictionary<string, FsrsCardState>(StringComparer.Ordinal), recentAttempts, revision, schemaVersion)
    {
    }
}
