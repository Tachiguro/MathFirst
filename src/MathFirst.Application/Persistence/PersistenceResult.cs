namespace MathFirst.Application.Persistence;

public enum PersistenceStatus
{
    Success = 1,
    RevisionConflict = 2,
    SchemaVersionMismatch = 3,
    Corrupted = 4,
    StorageUnavailable = 5
}

public sealed record PersistenceResult(
    PersistenceStatus Status,
    long? NewRevision = null,
    string? Message = null)
{
    public bool IsSuccess => Status == PersistenceStatus.Success;

    public static PersistenceResult Success(long newRevision) =>
        new(PersistenceStatus.Success, NewRevision: newRevision);

    public static PersistenceResult Conflict(string message) =>
        new(PersistenceStatus.RevisionConflict, Message: message);

    public static PersistenceResult UnsupportedVersion(string message) =>
        new(PersistenceStatus.SchemaVersionMismatch, Message: message);

    public static PersistenceResult Corrupt(string message) =>
        new(PersistenceStatus.Corrupted, Message: message);

    public static PersistenceResult Unavailable(string message) =>
        new(PersistenceStatus.StorageUnavailable, Message: message);
}
