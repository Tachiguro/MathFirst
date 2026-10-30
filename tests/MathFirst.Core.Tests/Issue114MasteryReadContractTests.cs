namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>
/// ADR-0013 §4 store-capability contract for the durable prerequisite mastery read
/// (<see cref="ILearnerStore.LoadItemStatesAsync"/>), over synthetic temporary SQLite databases only.
/// There is no <c>LoadSnapshotAsync</c> fallback for the query. An adapter whose runtime snapshot is sparse must
/// implement it; if the read is unsupported or fails, the unlock is withheld (fail closed), never granted.
/// </summary>
public sealed class Issue114MasteryReadContractTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue114MasteryRead_" + Guid.NewGuid().ToString("N"));

    public Issue114MasteryReadContractTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task M01_UnsupportedQuery_SparseRuntimeSnapshot_NeverUnlocks_AndSupportedStoreRecoversAfterRestart()
    {
        var path = await CreateRestartedLearnerAsync("m01");

        // First process: the adapter loads a sparse runtime snapshot and cannot answer the durable mastery read.
        using (var inner = new SqliteLearnerStore(path))
        {
            using var unsupported = new MasteryReadAdapter(inner, MasteryRead.Unsupported, sparseRuntimeSnapshot: true);
            var session = new TrainingSession(unsupported, practiceMode: PracticeMode.CurriculumManaged);
            await session.InitializeAsync(startTiming: false);

            for (var attempt = 1; attempt <= 12; attempt++)
            {
                var evaluation = session.SubmitAnswer(session.CurrentFact.CorrectResult);
                Assert.Equal(CurriculumStage.Stage1_Addition, evaluation.ChangeSet!.UpdatedProgression.CurriculumStage);
                Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
                Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
                await session.AdvanceToNextFactAsync(startTiming: false);
            }

            Assert.True(unsupported.MasteryReadCalls >= 1);
        }

        // Restart with an adapter that implements the query: the same durable evidence now unlocks Subtraction.
        using (var inner = new SqliteLearnerStore(path))
        {
            using var supported = new MasteryReadAdapter(inner, MasteryRead.Supported, sparseRuntimeSnapshot: true);
            var session = new TrainingSession(supported, practiceMode: PracticeMode.CurriculumManaged);
            await session.InitializeAsync(startTiming: false);

            var evaluation = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.Equal(CurriculumStage.Stage2_Subtraction, evaluation.ChangeSet!.UpdatedProgression.CurriculumStage);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
            Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
        }

        using var reopened = new SqliteLearnerStore(path);
        var persisted = await reopened.LoadRuntimeSnapshotAsync();
        Assert.Equal(CurriculumStage.Stage2_Subtraction, persisted.Progression.CurriculumStage);
    }

    [Fact]
    public async Task M02_UnsupportedQuery_CompleteRuntimeSnapshot_UnlocksFromSnapshotItemStates()
    {
        // The default LoadRuntimeSnapshotAsync is complete (LoadSnapshotAsync). Such adapters carry every durable
        // item state in the session, so the missing query costs nothing; this is the only snapshot-based path.
        var path = await CreateRestartedLearnerAsync("m02");

        using var inner = new SqliteLearnerStore(path);
        using var adapter = new MasteryReadAdapter(inner, MasteryRead.Unsupported, sparseRuntimeSnapshot: false);
        var session = new TrainingSession(adapter, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var evaluation = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, evaluation.ChangeSet!.UpdatedProgression.CurriculumStage);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
        Assert.True(adapter.MasteryReadCalls >= 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task M03_TransientMasteryReadFailure_WithholdsUnlockThenRecoversOnNextCommit(int failingReads)
    {
        var path = await CreateRestartedLearnerAsync("m03_" + failingReads);

        using var inner = new SqliteLearnerStore(path);
        using var adapter = new MasteryReadAdapter(inner, MasteryRead.Supported, sparseRuntimeSnapshot: true)
        {
            RemainingTransientFailures = failingReads
        };
        var session = new TrainingSession(adapter, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        // Reads 1..failingReads fail (initialization, then one retry per commit); each failure leaves the overlay
        // empty, so the unlock is withheld while practice itself keeps working.
        for (var attempt = 1; attempt <= failingReads; attempt++)
        {
            var evaluation = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.Equal(CurriculumStage.Stage1_Addition, evaluation.ChangeSet!.UpdatedProgression.CurriculumStage);
            var commit = await session.CommitCurrentEvaluationAsync();
            Assert.True(commit.IsSuccess);
            Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
            await session.AdvanceToNextFactAsync(startTiming: false);
        }

        Assert.Equal(0, adapter.RemainingTransientFailures);
        Assert.Equal(failingReads + 1, adapter.MasteryReadCalls);

        // The retry after the last failure succeeded, so the next accepted submission earns the unlock.
        var unlocking = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, unlocking.ChangeSet!.UpdatedProgression.CurriculumStage);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
    }

    [Fact]
    public async Task M04_SuccessfulMasteryRead_IsNotRepeatedWithinSameStageAndGeneration()
    {
        var path = await CreateRestartedLearnerAsync("m04");

        using var inner = new SqliteLearnerStore(path);
        using var adapter = new MasteryReadAdapter(inner, MasteryRead.Unsupported, sparseRuntimeSnapshot: true);
        var session = new TrainingSession(adapter, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);
        var readsAfterStart = adapter.MasteryReadCalls;

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync(startTiming: false);
        }

        // "Unsupported" is a static store capability: it is learned once per (generation, stage), not retried
        // on every commit, and it is not a transient failure.
        Assert.Equal(1, readsAfterStart);
        Assert.Equal(readsAfterStart, adapter.MasteryReadCalls);
    }

    [Theory]
    [InlineData(MasteryRead.Supported, true, 0)]
    [InlineData(MasteryRead.Unsupported, false, 0)]
    [InlineData(MasteryRead.Supported, true, 2)]
    public async Task M05_IncompleteEvidence_NeverUnlocks_ForAnyStoreCapability(
        MasteryRead read,
        bool sparseRuntimeSnapshot,
        int transientFailures)
    {
        var path = Path.Combine(_directory, $"m05_{read}_{sparseRuntimeSnapshot}_{transientFailures}.db");
        using (var seedStore = new SqliteLearnerStore(path))
        {
            await seedStore.InitializeAsync();
            await SaveAdditionMasteredBandAsync(seedStore);
        }

        // One D10 fact stays unattempted: failures or missing capability must never fill that gap.
        await D10MasteryTestSeed.SeedMasteredAsync(path, ArithmeticOperation.Addition, exceptFactId: "add:1+1");

        using var inner = new SqliteLearnerStore(path);
        using var adapter = new MasteryReadAdapter(inner, read, sparseRuntimeSnapshot)
        {
            RemainingTransientFailures = transientFailures
        };
        var session = new TrainingSession(adapter, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        for (var attempt = 1; attempt <= 12; attempt++)
        {
            if (session.CurrentFact.Id == "add:1+1")
            {
                // A genuinely incorrect answer is still evidence of a missed fact, never of mastery.
                session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
                Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
                await session.AcknowledgeFeedbackAsync(startTiming: false);
            }
            else
            {
                var evaluation = session.SubmitAnswer(session.CurrentFact.CorrectResult);
                Assert.Equal(CurriculumStage.Stage1_Addition, evaluation.ChangeSet!.UpdatedProgression.CurriculumStage);
                Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
                await session.AdvanceToNextFactAsync(startTiming: false);
            }

            Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
        }
    }

    [Fact]
    public async Task M06_EarnedStage_IsMonotonic_WhenMasteryReadIsUnsupportedOrFailing()
    {
        var path = Path.Combine(_directory, "m06.db");
        using (var seedStore = new SqliteLearnerStore(path))
        {
            await seedStore.InitializeAsync();
            var progression = LearnerProgression.CreateFresh();
            progression.CurriculumStage = CurriculumStage.Stage2_Subtraction;
            await seedStore.SaveProgressionAsync(progression);
        }

        foreach (var read in new[] { MasteryRead.Unsupported, MasteryRead.Failing })
        {
            using var inner = new SqliteLearnerStore(path);
            using var adapter = new MasteryReadAdapter(inner, read, sparseRuntimeSnapshot: true);
            var session = new TrainingSession(adapter, practiceMode: PracticeMode.CurriculumManaged);
            await session.InitializeAsync(startTiming: false);

            for (var attempt = 1; attempt <= 5; attempt++)
            {
                session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
                Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
                Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
                await session.AcknowledgeFeedbackAsync(startTiming: false);
            }
        }

        using var reopened = new SqliteLearnerStore(path);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, (await reopened.LoadRuntimeSnapshotAsync()).Progression.CurriculumStage);
    }

    private async Task<string> CreateRestartedLearnerAsync(string name)
    {
        var path = Path.Combine(_directory, name + ".db");
        using var seedStore = new SqliteLearnerStore(path);
        await seedStore.InitializeAsync();
        await SaveAdditionMasteredBandAsync(seedStore);
        await D10MasteryTestSeed.SeedMasteredAsync(path, ArithmeticOperation.Addition);
        return path;
    }

    private static async Task SaveAdditionMasteredBandAsync(SqliteLearnerStore store)
    {
        var progression = LearnerProgression.CreateFresh();
        progression.CurriculumStage = CurriculumStage.Stage1_Addition;
        progression.OperationProgressions[ArithmeticOperation.Addition] =
            new OperationProgression(ArithmeticOperation.Addition, D10MasteryTestSeed.MasteredBandIndex, 0);
        await store.SaveProgressionAsync(progression);
    }

    public enum MasteryRead
    {
        Supported,
        Unsupported,
        Failing
    }

    /// <summary>
    /// Delegating adapter that controls only the durable mastery read and the shape of the runtime snapshot.
    /// Every other member forwards to the real SQLite store so commits remain genuine.
    /// </summary>
    private sealed class MasteryReadAdapter(ILearnerStore inner, MasteryRead read, bool sparseRuntimeSnapshot) : ILearnerStore
    {
        public int MasteryReadCalls { get; private set; }
        public int RemainingTransientFailures { get; set; }
        public string StoragePath => inner.StoragePath;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            inner.InitializeAsync(cancellationToken);

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            inner.LoadSnapshotAsync(cancellationToken);

        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken cancellationToken = default) =>
            sparseRuntimeSnapshot
                ? inner.LoadRuntimeSnapshotAsync(cancellationToken)
                : inner.LoadSnapshotAsync(cancellationToken);

        public Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(
            PracticeSelectionEvidenceRequest request,
            CancellationToken cancellationToken = default) =>
            inner.LoadPracticeSelectionEvidenceAsync(request, cancellationToken);

        public Task<IReadOnlyDictionary<string, ItemLearningState>> LoadItemStatesAsync(
            IReadOnlyCollection<string> factIds,
            CancellationToken cancellationToken = default)
        {
            MasteryReadCalls++;
            if (RemainingTransientFailures > 0)
            {
                RemainingTransientFailures--;
                throw new IOException("Injected transient mastery read failure.");
            }

            return read switch
            {
                MasteryRead.Unsupported => throw new NotSupportedException("Authoritative item-state query requires explicit store support."),
                MasteryRead.Failing => throw new IOException("Injected persistent mastery read failure."),
                _ => inner.LoadItemStatesAsync(factIds, cancellationToken)
            };
        }

        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            inner.LoadLatestFrontierAttemptsAsync(operation, bandStartedPracticePosition, frontierFactIds, cancellationToken);

        public Task<IReadOnlyList<AttemptRecord>> LoadCompleteAttemptTelemetryAsync(
            CancellationToken cancellationToken = default) =>
            inner.LoadCompleteAttemptTelemetryAsync(cancellationToken);

        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default) =>
            inner.CommitSubmissionAsync(changeSet, cancellationToken);

        public Task<CommittedLearnerAttemptEvidence?> GetCommittedAttemptEvidenceAsync(
            string submissionId,
            CancellationToken cancellationToken = default) =>
            inner.GetCommittedAttemptEvidenceAsync(submissionId, cancellationToken);

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) =>
            inner.ResetLearningProgressAsync(cancellationToken);

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            inner.CloseAsync(cancellationToken);

        public void Dispose()
        {
            // The wrapped SQLite store is owned and disposed by the test.
        }
    }
}
