namespace MathFirst.Core.Tests;

using MathFirst.Application.Persistence;
using MathFirst.Domain;
using Xunit;

public sealed class SqlitePersistenceConformanceTests : IDisposable
{
    private readonly string _testDbDir;

    public SqlitePersistenceConformanceTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbDir))
            {
                Directory.Delete(_testDbDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup in temp directory
        }
    }

    private string GetTempDbPath() => Path.Combine(_testDbDir, $"test_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Conformance_1_EmptyStoreInitializesCleanly()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);

        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot.Revision);
        Assert.Equal(LearnerProgression.DefaultSchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(ArithmeticOperation.Addition, snapshot.Progression.CurrentOperation);
        Assert.Equal(1, snapshot.Progression.CurrentMaxOperand);
        Assert.Empty(snapshot.ItemStates);
        Assert.Empty(snapshot.RecentAttempts);
    }

    [Fact]
    public async Task Conformance_2_AtomicSubmission_UpdatesItemProgressionAndAttemptAtomically()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, 1200, DateTimeOffset.UtcNow);

        var itemState = ItemLearningState.CreateNew(fact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 1;
        itemState.LastLatencyMs = 1200;

        var progression = LearnerProgression.CreateFresh();
        progression.CurrentMaxOperand = 1;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, progression);

        var result = await store.CommitSubmissionAsync(changeSet);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.NewRevision);

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(2, snapshot.Revision);
        Assert.Single(snapshot.ItemStates);
        Assert.True(snapshot.ItemStates.ContainsKey(fact.Id));
        Assert.Equal(1, snapshot.ItemStates[fact.Id].TotalAttempts);
        Assert.Equal(1200, snapshot.ItemStates[fact.Id].LastLatencyMs);
        Assert.Single(snapshot.RecentAttempts);
        Assert.Equal(subId, snapshot.RecentAttempts[0].SubmissionId);
    }

    [Fact]
    public async Task Conformance_3_StaleRevisionConflict_RejectsWrite()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId1 = Guid.NewGuid().ToString("N");
        var attempt1 = new AttemptRecord(subId1, fact.Id, fact.Operation, 0, 1, 1, 1, true, 1200, DateTimeOffset.UtcNow);
        var itemState1 = ItemLearningState.CreateNew(fact);
        var prog1 = LearnerProgression.CreateFresh();

        var changeSet1 = new SubmissionChangeSet(subId1, ExpectedRevision: 1, attempt1, itemState1, prog1);
        var res1 = await store.CommitSubmissionAsync(changeSet1);
        Assert.True(res1.IsSuccess);
        Assert.Equal(2, res1.NewRevision);

        // Attempt second submission with stale revision 1 (when DB is now at revision 2)
        var subId2 = Guid.NewGuid().ToString("N");
        var attempt2 = new AttemptRecord(subId2, fact.Id, fact.Operation, 0, 1, 1, 1, true, 1100, DateTimeOffset.UtcNow);
        var changeSet2 = new SubmissionChangeSet(subId2, ExpectedRevision: 1, attempt2, itemState1, prog1);

        var res2 = await store.CommitSubmissionAsync(changeSet2);
        Assert.False(res2.IsSuccess);
        Assert.Equal(PersistenceStatus.RevisionConflict, res2.Status);
    }

    [Fact]
    public async Task Conformance_4_DuplicateSubmission_IsIdempotent()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, 1200, DateTimeOffset.UtcNow);
        var itemState = ItemLearningState.CreateNew(fact);
        var prog = LearnerProgression.CreateFresh();

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, prog);

        var res1 = await store.CommitSubmissionAsync(changeSet);
        Assert.True(res1.IsSuccess);
        Assert.Equal(2, res1.NewRevision);

        // Retrying identical submission ID returns success idempotently without double recording
        var res2 = await store.CommitSubmissionAsync(changeSet);
        Assert.True(res2.IsSuccess);

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Single(snapshot.RecentAttempts);
    }

    [Fact]
    public async Task Conformance_5_UnsupportedNewerVersion_IsRejected()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            // Manually write higher schema version into database
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE schema_info SET value = '99' WHERE key = 'schema_version';";
            await cmd.ExecuteNonQueryAsync();
        }

        // Reopening store must reject unsupported version
        using var store2 = new SqliteLearnerStore(dbPath);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store2.InitializeAsync());
    }

    [Fact]
    public async Task Conformance_6_CorruptedDatabase_RejectsGracefully()
    {
        var dbPath = GetTempDbPath();
        await File.WriteAllBytesAsync(dbPath, new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77 });

        using var store = new SqliteLearnerStore(dbPath);
        // SQLite will reject invalid header
        await Assert.ThrowsAnyAsync<Exception>(async () => await store.InitializeAsync());
    }

    [Fact]
    public async Task Conformance_7_CloseAndReopen_PreservesState()
    {
        var dbPath = GetTempDbPath();
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");

        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, 1300, DateTimeOffset.UtcNow);
            var itemState = ItemLearningState.CreateNew(fact);
            itemState.TotalAttempts = 5;
            itemState.CorrectAttempts = 5;
            itemState.ConsecutiveCorrectStreak = 5;
            itemState.IsProvisionallyMastered = true;
            itemState.LastLatencyMs = 1300;

            var prog = LearnerProgression.CreateFresh();
            prog.CurrentMaxOperand = 2;

            var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, prog);
            await store.CommitSubmissionAsync(changeSet);
            await store.CloseAsync();
        }

        // Reopen from disk
        using (var storeReopened = new SqliteLearnerStore(dbPath))
        {
            await storeReopened.InitializeAsync();
            var snapshot = await storeReopened.LoadSnapshotAsync();

            Assert.Equal(2, snapshot.Revision);
            Assert.Equal(2, snapshot.Progression.CurrentMaxOperand);
            Assert.Single(snapshot.ItemStates);
            Assert.True(snapshot.ItemStates[fact.Id].IsProvisionallyMastered);
            Assert.Equal(5, snapshot.ItemStates[fact.Id].TotalAttempts);
        }
    }

    [Fact]
    public async Task Conformance_8_ResetLearningProgress_RestoresFreshProgression()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, 1200, DateTimeOffset.UtcNow);
        var itemState = ItemLearningState.CreateNew(fact);
        var prog = LearnerProgression.CreateFresh();
        prog.CurrentMaxOperand = 5;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, prog);
        await store.CommitSubmissionAsync(changeSet);

        // Reset
        await store.ResetLearningProgressAsync();

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(1, snapshot.Revision);
        Assert.Equal(ArithmeticOperation.Addition, snapshot.Progression.CurrentOperation);
        Assert.Equal(1, snapshot.Progression.CurrentMaxOperand);
        Assert.Empty(snapshot.ItemStates);
        Assert.Empty(snapshot.RecentAttempts);
    }

    [Fact]
    public async Task Conformance_9_PerOperationMaxOperands_PreservedAcrossReopen()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();

            var prog = LearnerProgression.CreateFresh();
            prog.SetMaxOperand(ArithmeticOperation.Addition, 5);
            prog.SetMaxOperand(ArithmeticOperation.Subtraction, 4);
            prog.SetMaxOperand(ArithmeticOperation.Multiplication, 3);
            prog.SetMaxOperand(ArithmeticOperation.Division, 2);

            var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
            var subId = Guid.NewGuid().ToString("N");
            var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, 800, DateTimeOffset.UtcNow);
            var itemState = ItemLearningState.CreateNew(fact);

            var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, prog);
            await store.CommitSubmissionAsync(changeSet);
            await store.CloseAsync();
        }

        using (var storeReopened = new SqliteLearnerStore(dbPath))
        {
            await storeReopened.InitializeAsync();
            var snapshot = await storeReopened.LoadSnapshotAsync();

            Assert.Equal(5, snapshot.Progression.GetMaxOperand(ArithmeticOperation.Addition));
            Assert.Equal(4, snapshot.Progression.GetMaxOperand(ArithmeticOperation.Subtraction));
            Assert.Equal(3, snapshot.Progression.GetMaxOperand(ArithmeticOperation.Multiplication));
            Assert.Equal(2, snapshot.Progression.GetMaxOperand(ArithmeticOperation.Division));
        }
    }
}
