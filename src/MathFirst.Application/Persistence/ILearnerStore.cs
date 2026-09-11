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
    Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
        ArithmeticOperation operation,
        long bandStartedPracticePosition,
        IReadOnlyList<string> frontierFactIds,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Authoritative latest-per-frontier query requires explicit store support.");
    Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default);
    Task ResetLearningProgressAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
