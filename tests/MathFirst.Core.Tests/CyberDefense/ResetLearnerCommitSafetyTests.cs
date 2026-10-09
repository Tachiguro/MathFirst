namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Telemetry;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Xunit;

public sealed class ResetLearnerCommitSafetyTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MathFirst_Tests_ResetLearnerSafety_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private SqliteLearnerStore CreateLearnerStore(string? dir = null)
    {
        dir ??= CreateTempDirectory();
        var dbPath = Path.Combine(dir, "learner_test.db");
        return new SqliteLearnerStore(dbPath);
    }

    private SqliteGameplayStore CreateGameplayStore(string? dir = null)
    {
        dir ??= CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");
        return new SqliteGameplayStore(dbPath);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // Ignored in test cleanup
            }
        }
    }

    private static SubmissionChangeSet CreateValidFirstAdditionChangeSet(
        string submissionId = "sub-stale-1",
        string factId = "add:0+1",
        int left = 0,
        int right = 1,
        int answer = 1,
        bool isCorrect = true,
        long expectedRevision = 1,
        long practicePosition = 1)
    {
        var op = ArithmeticOperation.Addition;
        var attempt = new AttemptRecord(
            submissionId: submissionId,
            factId: factId,
            operation: op,
            leftOperand: left,
            rightOperand: right,
            submittedAnswer: isCorrect ? answer : answer + 1,
            correctAnswer: answer,
            isCorrect: isCorrect,
            isFluent: isCorrect,
            responseLatencyMs: 1200,
            timestamp: DateTimeOffset.UtcNow,
            outcome: isCorrect ? AttemptOutcome.Correct : AttemptOutcome.Incorrect,
            practicePosition: practicePosition);

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = practicePosition;
        progression.CurriculumStage = CurriculumStage.Stage1_Addition;

        var itemState = new ItemLearningState
        {
            FactId = factId,
            Operation = op,
            LeftOperand = left,
            RightOperand = right,
            TotalAttempts = 1,
            CorrectAttempts = isCorrect ? 1 : 0,
            IncorrectAttempts = isCorrect ? 0 : 1,
            ConsecutiveCorrectStreak = isCorrect ? 1 : 0,
            LastLatencyMs = 1200,
            RollingLatencyMs = 1200,
            FluentStreak = isCorrect ? 1 : 0,
            IsProvisionallyMastered = false,
            NeedsRemediation = !isCorrect,
            RemediationDueOrder = 0,
            LastPracticedOrder = (int)practicePosition,
            LastPracticedAt = DateTimeOffset.UtcNow
        };

        var opProgressions = progression.OperationProgressions;

        return new SubmissionChangeSet(
            submissionId: submissionId,
            ExpectedRevision: expectedRevision,
            attempt: attempt,
            updatedItemState: itemState,
            updatedProgression: progression,
            updatedFsrsState: null,
            operationProgressions: opProgressions);
    }

    [Fact]
    public async Task StaleSubmission_AfterLearningReset_IsRejectedAndDoesNotRecreateLearnerData()
    {
        using var store = CreateLearnerStore();
        await store.InitializeAsync();

        // 1. Fresh learner store at revision 1, position 0
        var initialSnapshot = await store.LoadSnapshotAsync();
        Assert.Equal(1, initialSnapshot.Revision);
        Assert.Equal(0, initialSnapshot.Progression.PracticePosition);

        // 2. Build valid first submission expecting revision 1, position 1
        var staleChangeSet = CreateValidFirstAdditionChangeSet(
            submissionId: "stale-first-sub",
            expectedRevision: 1,
            practicePosition: 1);

        // 3. Execute learning reset
        await store.ResetLearningProgressAsync();

        // 4. Submit the pre-reset change set after reset
        var result = await store.CommitSubmissionAsync(staleChangeSet);

        // 5. Invariant: stale submission MUST NOT be accepted
        Assert.False(result.IsSuccess, $"Stale submission was accepted with status {result.Status} and new revision {result.NewRevision}");
        Assert.Equal(PersistenceStatus.RevisionConflict, result.Status);

        // 6. Authoritative state must remain clean fresh progression
        var postSnapshot = await store.LoadSnapshotAsync();
        Assert.Empty(postSnapshot.ItemStates);
        Assert.Empty(postSnapshot.RecentAttempts);
        Assert.Null(postSnapshot.LatestAcceptedPracticeAt);
        Assert.Equal(0, postSnapshot.Progression.PracticePosition);

        // 7. Attempt history must not have the stale attempt
        var evidence = await store.GetCommittedAttemptEvidenceAsync("stale-first-sub");
        Assert.Null(evidence);
    }

    [Fact]
    public async Task StaleSubmission_AfterFullLocalReset_IsRejectedAndDoesNotRecreateLearnerData()
    {
        var dir = CreateTempDirectory();
        using var learnerStore = CreateLearnerStore(dir);
        await learnerStore.InitializeAsync();
        using var gameplayStore = CreateGameplayStore(dir);
        await gameplayStore.InitializeAsync();

        var prefs = new InMemoryPreferenceStore();
        var idProvider = new PreferenceInstallationIdProvider(new InMemoryInstallationIdStore());
        var cacheCleaner = new StubShareCacheCleaner();
        var cyberPrefs = new InMemoryCyberDefensePreferences();
        var sessionState = new CyberDefenseSessionState(cyberPrefs);

        var session = new TrainingSession(learnerStore, preferenceStore: prefs);
        await session.InitializeAsync(startTiming: false);

        var coordinator = new AppResetCoordinator(session, prefs, idProvider, cacheCleaner, sessionState, gameplayStore);

        // 1. Evaluate first submission but do NOT commit it yet
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var staleChangeSet = eval.ChangeSet;
        Assert.NotNull(staleChangeSet);
        Assert.Equal(1, staleChangeSet.ExpectedRevision);

        // 2. Execute full reset
        await coordinator.ExecuteFullResetAsync();

        // 3. Submit the stale pre-reset change set
        var result = await learnerStore.CommitSubmissionAsync(staleChangeSet);

        // 4. Invariant: stale submission must be rejected
        Assert.False(result.IsSuccess, "Stale submission was accepted after Full Local Reset");
        Assert.Equal(PersistenceStatus.RevisionConflict, result.Status);

        // 5. Learner store must remain completely clean
        var snapshot = await learnerStore.LoadSnapshotAsync();
        Assert.Empty(snapshot.ItemStates);
        Assert.Empty(snapshot.RecentAttempts);
        Assert.Null(snapshot.LatestAcceptedPracticeAt);
        Assert.Equal(0, snapshot.Progression.PracticePosition);
    }

    [Fact]
    public async Task StaleSubmission_AwaitingPersistenceRetry_AfterReset_CannotCorruptResetState()
    {
        using var store = CreateLearnerStore();
        await store.InitializeAsync();

        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        // Submit first answer
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.NotNull(eval.ChangeSet);

        // Reset learning progress on the session
        await session.ResetLearningProgressAsync(startTiming: false);

        // Verify session interaction state is back to AwaitingAnswer and LastEvaluation is cleared
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.Null(session.LastEvaluation);
        Assert.False(session.IsCurrentSubmissionCommitted);

        // Attempting to recover from persistence failure must return false
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.False(recovered);

        // Verify store remains clean
        var snapshot = await store.LoadSnapshotAsync();
        Assert.Empty(snapshot.ItemStates);
        Assert.Empty(snapshot.RecentAttempts);
        Assert.Equal(0, snapshot.Progression.PracticePosition);
    }

    [Fact]
    public async Task PostReset_ValidNewSubmission_IsAcceptedAndAdvancesMonotonically()
    {
        using var store = CreateLearnerStore();
        await store.InitializeAsync();

        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        // Reset learning progress
        await session.ResetLearningProgressAsync(startTiming: false);

        // Submit the new post-reset fact
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var persistenceResult = await session.CommitCurrentEvaluationAsync();

        Assert.True(persistenceResult.IsSuccess);
        Assert.True(session.IsCurrentSubmissionCommitted);

        // Store snapshot must reflect exactly one new attempt
        var snapshot = await store.LoadSnapshotAsync();
        Assert.Single(snapshot.ItemStates);
        Assert.Single(snapshot.RecentAttempts);
        Assert.Equal(1, snapshot.Progression.PracticePosition);
        Assert.NotNull(snapshot.LatestAcceptedPracticeAt);
    }

    [Fact]
    public async Task ConcurrentResetAndCommit_ResetCommitsFirst_RejectsStaleCommit()
    {
        using var store = CreateLearnerStore();
        await store.InitializeAsync();

        var staleChangeSet = CreateValidFirstAdditionChangeSet(
            submissionId: "concurrent-sub-1",
            expectedRevision: 1,
            practicePosition: 1);

        // Reset executes and commits first
        await store.ResetLearningProgressAsync();

        // In-flight commit arrives after reset committed
        var result = await store.CommitSubmissionAsync(staleChangeSet);

        Assert.False(result.IsSuccess);
        Assert.Equal(PersistenceStatus.RevisionConflict, result.Status);

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Empty(snapshot.ItemStates);
        Assert.Empty(snapshot.RecentAttempts);
    }

    [Fact]
    public async Task ConcurrentResetAndCommit_CommitCommitsFirst_ResetWipesCommittedData()
    {
        using var store = CreateLearnerStore();
        await store.InitializeAsync();

        var changeSet = CreateValidFirstAdditionChangeSet(
            submissionId: "concurrent-sub-2",
            expectedRevision: 1,
            practicePosition: 1);

        // Commit succeeds first
        var commitResult = await store.CommitSubmissionAsync(changeSet);
        Assert.True(commitResult.IsSuccess);

        // Reset executes second
        await store.ResetLearningProgressAsync();

        // Snapshot must be completely wiped
        var snapshot = await store.LoadSnapshotAsync();
        Assert.Empty(snapshot.ItemStates);
        Assert.Empty(snapshot.RecentAttempts);
        Assert.Null(snapshot.LatestAcceptedPracticeAt);
        Assert.Equal(0, snapshot.Progression.PracticePosition);
    }

    [Fact]
    public async Task StaleSubmission_WhenPostResetRevisionsAdvanceMultipleTimes_IsRejected()
    {
        using var store = CreateLearnerStore();
        await store.InitializeAsync();

        // 1. Initial snapshot at revision 1
        var initialSnapshot = await store.LoadSnapshotAsync();
        Assert.Equal(1, initialSnapshot.Revision);

        // 2. Pre-reset stale change set expecting revision 1
        var staleChangeSet = CreateValidFirstAdditionChangeSet(
            submissionId: "stale-aba-sub",
            expectedRevision: 1,
            practicePosition: 1);

        // 3. Reset occurs (revision becomes 2)
        await store.ResetLearningProgressAsync();
        var postResetSnapshot = await store.LoadSnapshotAsync();
        Assert.Equal(2, postResetSnapshot.Revision);

        // 4. Multiple post-reset legitimate submissions commit
        var sub1 = CreateValidFirstAdditionChangeSet("post-reset-sub-1", "add:0+1", 0, 1, 1, true, expectedRevision: 2, practicePosition: 1);
        var r1 = await store.CommitSubmissionAsync(sub1);
        Assert.True(r1.IsSuccess);
        Assert.Equal(3, r1.NewRevision);

        var sub2 = CreateValidFirstAdditionChangeSet("post-reset-sub-2", "add:1+0", 1, 0, 1, true, expectedRevision: 3, practicePosition: 2);
        var r2 = await store.CommitSubmissionAsync(sub2);
        Assert.True(r2.IsSuccess, $"r2 failed: status={r2.Status}, message={r2.Message}");
        Assert.Equal(4, r2.NewRevision);

        // 5. Submit pre-reset stale change set now (store is at revision 4, stale expects revision 1)
        var staleResult = await store.CommitSubmissionAsync(staleChangeSet);
        Assert.False(staleResult.IsSuccess);
        Assert.Equal(PersistenceStatus.RevisionConflict, staleResult.Status);

        // 6. Ensure attempt history does not contain stale-aba-sub
        var evidence = await store.GetCommittedAttemptEvidenceAsync("stale-aba-sub");
        Assert.Null(evidence);

        var snap = await store.LoadSnapshotAsync();
        Assert.Equal(4, snap.Revision);
        Assert.Equal(2, snap.Progression.PracticePosition);
    }

    [Fact]
    public async Task StaleEvaluation_CommittedViaTrainingSession_AfterLearningReset_IsRejectedAndDoesNotCorruptSessionState()
    {
        using var store = CreateLearnerStore();
        await store.InitializeAsync();

        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        // 1. Answer first fact -> generates LastEvaluation
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.NotNull(eval.ChangeSet);
        Assert.Equal(1, eval.ChangeSet.ExpectedRevision);

        // 2. Learning reset occurs
        await session.ResetLearningProgressAsync(startTiming: false);

        // 3. Stale in-flight callback attempts to commit the pre-reset changeSet directly
        var directResult = await store.CommitSubmissionAsync(eval.ChangeSet);
        Assert.False(directResult.IsSuccess);
        Assert.Equal(PersistenceStatus.RevisionConflict, directResult.Status);

        // 4. Session state remains clean fresh state
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Empty(session.ItemStates);
        Assert.Equal(0, session.SessionTotalCount);
        Assert.Equal(0, session.SessionCorrectCount);
        Assert.Null(session.LastEvaluation);
    }

    [Fact]
    public async Task MultipleConsecutiveResets_MonotonicallyAdvanceStoreRevision()
    {
        using var store = CreateLearnerStore();
        await store.InitializeAsync();

        var s0 = await store.LoadSnapshotAsync();
        Assert.Equal(1, s0.Revision);

        await store.ResetLearningProgressAsync();
        var s1 = await store.LoadSnapshotAsync();
        Assert.Equal(2, s1.Revision);

        await store.ResetLearningProgressAsync();
        var s2 = await store.LoadSnapshotAsync();
        Assert.Equal(3, s2.Revision);

        await store.ResetLearningProgressAsync();
        var s3 = await store.LoadSnapshotAsync();
        Assert.Equal(4, s3.Revision);

        // A new valid submission expecting revision 4 succeeds
        var sub = CreateValidFirstAdditionChangeSet("sub-after-multi-reset", "add:0+1", 0, 1, 1, true, expectedRevision: 4, practicePosition: 1);
        var res = await store.CommitSubmissionAsync(sub);
        Assert.True(res.IsSuccess);
        Assert.Equal(5, res.NewRevision);
    }

    private sealed class InMemoryPreferenceStore : IPreferenceStore
    {
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => "system";
        public void SetLanguagePreference(string languageCode) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) => true;
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) { }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => PracticeOperationPreferencePolicy.AllOperations;
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public bool GetHapticFeedbackEnabled() => true;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public void ResetPracticePreferences() { }
        public void ResetAllPreferences() { }
    }

    private sealed class InMemoryInstallationIdStore : IInstallationIdStore
    {
        private string? _id;
        public string? Get() => _id;
        public void Set(string value) => _id = value;
        public void Clear() => _id = null;
    }

    private sealed class StubShareCacheCleaner : ITelemetryShareCacheCleaner
    {
        public void PurgeShareCache() { }
    }

    private sealed class InMemoryCyberDefensePreferences : ICyberDefenseModePreferences
    {
        public bool GetCyberDefenseEnabled() => true;
        public void SetCyberDefenseEnabled(bool enabled) { }
    }
}
