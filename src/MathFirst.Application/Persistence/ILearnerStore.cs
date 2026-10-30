namespace MathFirst.Application.Persistence;

using MathFirst.Domain;

public interface ILearnerStore : IDisposable
{
    string StoragePath { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default);
    async Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken cancellationToken = default) =>
        await LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
    async Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(
        PracticeSelectionEvidenceRequest request,
        CancellationToken cancellationToken = default) =>
        PracticeSelectionEvidence.FromSnapshot(
            await LoadSnapshotAsync(cancellationToken).ConfigureAwait(false),
            request);
    /// <summary>
    /// Reads the durable item states for the requested fact IDs (unknown IDs are absent from the result).
    /// ADR-0013 §4 uses it to evaluate D10 prerequisite mastery after a restart.
    /// </summary>
    /// <remarks>
    /// The default implementation throws <see cref="NotSupportedException"/>; there is no <c>LoadSnapshotAsync</c>
    /// fallback. An adapter whose <see cref="LoadRuntimeSnapshotAsync"/> is sparse (<c>SqliteLearnerStore</c>
    /// in production) MUST implement this query to unlock operations after a restart. Without it, and also while
    /// it fails, <c>TrainingSession</c> evaluates only in-session item states: the unlock is withheld, never
    /// granted, and the earned stage is not lowered. An adapter that keeps the default (complete)
    /// <see cref="LoadRuntimeSnapshotAsync"/> already carries every durable item state and needs no override.
    /// </remarks>
    Task<IReadOnlyDictionary<string, ItemLearningState>> LoadItemStatesAsync(
        IReadOnlyCollection<string> factIds,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Authoritative item-state query requires explicit store support.");
    Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
        ArithmeticOperation operation,
        long bandStartedPracticePosition,
        IReadOnlyList<string> frontierFactIds,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Authoritative latest-per-frontier query requires explicit store support.");
    Task<IReadOnlyList<AttemptRecord>> LoadCompleteAttemptTelemetryAsync(
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Complete attempt telemetry query requires explicit store support.");
    Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default);
    Task<CommittedLearnerAttemptEvidence?> GetCommittedAttemptEvidenceAsync(string submissionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Committed learner attempt evidence query requires explicit store support.");
    Task ResetLearningProgressAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
