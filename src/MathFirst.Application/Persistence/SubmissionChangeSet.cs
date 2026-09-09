namespace MathFirst.Application.Persistence;

using MathFirst.Application.Scheduling;
using MathFirst.Domain;

public sealed record SubmissionChangeSet(
    string SubmissionId,
    long ExpectedRevision,
    AttemptRecord Attempt,
    ItemLearningState UpdatedItemState,
    LearnerProgression UpdatedProgression,
    FsrsCardState? UpdatedFsrsState = null,
    IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? OperationProgressions = null);
