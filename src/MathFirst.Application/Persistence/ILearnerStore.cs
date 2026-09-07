namespace MathFirst.Application.Persistence;

public interface ILearnerStore : IDisposable
{
    string StoragePath { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default);
    Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default);
    Task ResetLearningProgressAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
