namespace MathFirst.Application.Persistence;

using MathFirst.Application.Scheduling;
using MathFirst.Domain;

public sealed record SubmissionChangeSet
{
    public SubmissionChangeSet(
        string submissionId,
        long ExpectedRevision,
        AttemptRecord attempt,
        ItemLearningState updatedItemState,
        LearnerProgression updatedProgression,
        FsrsCardState? updatedFsrsState = null,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? operationProgressions = null)
    {
        SubmissionId = submissionId;
        this.ExpectedRevision = ExpectedRevision;
        Attempt = attempt with { };
        UpdatedItemState = CloneItemState(updatedItemState);
        UpdatedProgression = CloneProgression(updatedProgression);
        UpdatedFsrsState = updatedFsrsState;
        OperationProgressions = new System.Collections.ObjectModel.ReadOnlyDictionary<ArithmeticOperation, OperationProgression>(
            (operationProgressions ?? updatedProgression.OperationProgressions).ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    public string SubmissionId { get; }
    public long ExpectedRevision { get; }
    public AttemptRecord Attempt { get; }
    public ItemLearningState UpdatedItemState { get; }
    public LearnerProgression UpdatedProgression { get; }
    public FsrsCardState? UpdatedFsrsState { get; }
    public IReadOnlyDictionary<ArithmeticOperation, OperationProgression> OperationProgressions { get; }

    private static LearnerProgression CloneProgression(LearnerProgression source) => new()
    {
        PracticePosition = source.PracticePosition,
        StoreRevision = source.StoreRevision,
        SchemaVersion = source.SchemaVersion,
        UpdatedAt = source.UpdatedAt,
        OperationProgressions = source.OperationProgressions.ToDictionary(pair => pair.Key, pair => pair.Value)
    };

    private static ItemLearningState CloneItemState(ItemLearningState source) => new()
    {
        FactId = source.FactId,
        Operation = source.Operation,
        LeftOperand = source.LeftOperand,
        RightOperand = source.RightOperand,
        TotalAttempts = source.TotalAttempts,
        CorrectAttempts = source.CorrectAttempts,
        IncorrectAttempts = source.IncorrectAttempts,
        ConsecutiveCorrectStreak = source.ConsecutiveCorrectStreak,
        LastLatencyMs = source.LastLatencyMs,
        RollingLatencyMs = source.RollingLatencyMs,
        FluentStreak = source.FluentStreak,
        IsProvisionallyMastered = source.IsProvisionallyMastered,
        NeedsRemediation = source.NeedsRemediation,
        RemediationDueOrder = source.RemediationDueOrder,
        LastPracticedOrder = source.LastPracticedOrder,
        LastPracticedAt = source.LastPracticedAt
    };
}
