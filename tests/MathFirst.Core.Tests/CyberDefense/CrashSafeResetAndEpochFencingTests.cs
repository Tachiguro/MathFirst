namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Telemetry;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class CrashSafeResetAndEpochFencingTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MathFirst_Tests_ResetFence_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private SqliteGameplayStore CreateGameplayStore(string? dir = null)
    {
        dir ??= CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");
        return new SqliteGameplayStore(dbPath);
    }

    private SqliteLearnerStore CreateLearnerStore(string? dir = null)
    {
        dir ??= CreateTempDirectory();
        var dbPath = Path.Combine(dir, "learner_test.db");
        return new SqliteLearnerStore(dbPath);
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

    [Fact]
    public void GameplayResetIntentRecord_Constructor_ValidatesInvariants()
    {
        var now = DateTimeOffset.UtcNow;
        // Negative current epoch
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameplayResetIntentRecord(false, -1, 0, null, now));
        // Negative target epoch
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameplayResetIntentRecord(false, 0, -1, null, now));
        // Target epoch < current epoch
        Assert.Throws<ArgumentException>(() => new GameplayResetIntentRecord(false, 2, 1, null, now));
        // Pending with target epoch <= current epoch
        Assert.Throws<ArgumentException>(() => new GameplayResetIntentRecord(true, 1, 1, now, now));
        // Not pending with target epoch != current epoch
        Assert.Throws<ArgumentException>(() => new GameplayResetIntentRecord(false, 0, 1, null, now));

        var notPending = new GameplayResetIntentRecord(false, 0, 0, null, now);
        Assert.False(notPending.IsPending);
        Assert.Equal(0, notPending.CurrentEpoch);
        Assert.Equal(0, notPending.TargetEpoch);
        Assert.Null(notPending.CreatedAt);

        var pending = new GameplayResetIntentRecord(true, 0, 1, now, now);
        Assert.True(pending.IsPending);
        Assert.Equal(0, pending.CurrentEpoch);
        Assert.Equal(1, pending.TargetEpoch);
        Assert.Equal(now, pending.CreatedAt);
    }

    [Fact]
    public async Task ResetIntent_FreshStore_InitialStateIsNotPendingEpochZero()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        var intent = await store.GetResetIntentAsync();
        Assert.NotNull(intent);
        Assert.False(intent.IsPending);
        Assert.Equal(0, intent.CurrentEpoch);
        Assert.Equal(0, intent.TargetEpoch);
        Assert.Null(intent.CreatedAt);
    }

    [Fact]
    public async Task ResetIntent_BeginOrGetResetIntent_PersistsPendingIntentAndTargetEpoch()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        var intent = await store.BeginOrGetResetIntentAsync();
        Assert.NotNull(intent);
        Assert.True(intent.IsPending);
        Assert.Equal(0, intent.CurrentEpoch);
        Assert.Equal(1, intent.TargetEpoch);
        Assert.NotNull(intent.CreatedAt);

        var retrieved = await store.GetResetIntentAsync();
        Assert.True(retrieved.IsPending);
        Assert.Equal(0, retrieved.CurrentEpoch);
        Assert.Equal(1, retrieved.TargetEpoch);
    }

    [Fact]
    public async Task ResetIntent_BeginOrGetResetIntent_WhenAlreadyPending_ReturnsSameTargetEpochWithoutIncrementingAgain()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        var first = await store.BeginOrGetResetIntentAsync();
        Assert.Equal(1, first.TargetEpoch);

        var second = await store.BeginOrGetResetIntentAsync();
        Assert.True(second.IsPending);
        Assert.Equal(0, second.CurrentEpoch);
        Assert.Equal(1, second.TargetEpoch);
    }

    [Fact]
    public async Task ResetGameplayState_AdvancesEpochAndClearsLedgersAndResetsRun()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        // 1. Stage an intent and apply a combat action to mutate state
        var intent = new CyberDefensePendingIntentRecord("sub-1", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent);
        await store.ApplyAttemptTransactionAsync(intent);

        // Stage another intent that stays pending
        var stagedOnly = new CyberDefensePendingIntentRecord("sub-2", "add:1+2", true, true, 500, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(stagedOnly);

        Assert.Equal(2, await store.GetStoreRevisionAsync());
        Assert.NotNull(await store.GetReceiptAsync("sub-1"));
        Assert.NotNull(await store.GetPendingIntentAsync("sub-2"));

        // 2. Begin reset intent
        var resetIntent = await store.BeginOrGetResetIntentAsync();
        Assert.Equal(1, resetIntent.TargetEpoch);

        // 3. Reset gameplay state
        await store.ResetGameplayStateAsync(resetIntent.TargetEpoch);

        // Verify progression reset to revision 1 and epoch 1
        Assert.Equal(1, await store.GetStoreRevisionAsync());
        Assert.Equal(1, await store.GetResetEpochAsync());

        // Verify run state reset to InitialRun
        var runState = await store.GetRunStateAsync();
        Assert.Equal(CyberDefenseRunState.InitialRun(), runState);

        // Verify pending intents and receipt ledger cleared
        Assert.Null(await store.GetReceiptAsync("sub-1"));
        Assert.Null(await store.GetPendingIntentAsync("sub-2"));
        var pendingList = await store.GetPendingIntentsAsync(0);
        Assert.Empty(pendingList);
    }

    [Fact]
    public async Task ResetGameplayState_RejectsMismatchedTargetEpochOrNonPendingIntent()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        // Not pending: ResetGameplayStateAsync must fail
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ResetGameplayStateAsync(1));

        // Begin intent for epoch 1
        await store.BeginOrGetResetIntentAsync();

        // Calling with epoch 2 (wrong target) must fail
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ResetGameplayStateAsync(2));
    }

    [Fact]
    public async Task ClearResetIntent_UpdatesMarkerToNotPendingWithTargetEpochAsCurrent()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        var resetIntent = await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(resetIntent.TargetEpoch);
        await store.ClearResetIntentAsync(resetIntent.TargetEpoch);

        var cleared = await store.GetResetIntentAsync();
        Assert.False(cleared.IsPending);
        Assert.Equal(1, cleared.CurrentEpoch);
        Assert.Equal(1, cleared.TargetEpoch);
        Assert.Null(cleared.CreatedAt);
    }

    [Fact]
    public async Task ClearResetIntent_FailsIfGameplayStateHasNotBeenResetToTargetEpoch()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        var resetIntent = await store.BeginOrGetResetIntentAsync();
        // Skip ResetGameplayStateAsync and try to clear
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ClearResetIntentAsync(resetIntent.TargetEpoch));
    }

    [Fact]
    public async Task EpochFencing_StagePendingIntent_FailsClosedWhenResetIsPending()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        await store.BeginOrGetResetIntentAsync();

        var intent = new CyberDefensePendingIntentRecord("sub-fence-1", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(intent));
    }

    [Fact]
    public async Task EpochFencing_ApplyAttemptTransaction_FailsClosedWhenResetIsPending()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord("sub-fence-2", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent);

        await store.BeginOrGetResetIntentAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(intent));
    }

    [Fact]
    public async Task EpochFencing_RejectsStaleEpochIntentsAfterResetCompleted()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();

        // Complete a full reset from epoch 0 to epoch 1
        var resetIntent = await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(resetIntent.TargetEpoch);
        await store.ClearResetIntentAsync(resetIntent.TargetEpoch);

        // Stale intent with epoch 0 must be rejected by both Stage and Apply
        var staleIntent = new CyberDefensePendingIntentRecord("sub-stale-epoch", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(staleIntent));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(staleIntent));

        // Valid intent with epoch 1 must succeed
        var validIntent = new CyberDefensePendingIntentRecord("sub-valid-epoch", "add:1+1", true, true, 500, 1, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(validIntent);
        var receipt = await store.ApplyAttemptTransactionAsync(validIntent);
        Assert.NotNull(receipt);
        Assert.Equal(1, receipt.ResetEpoch);
    }

    [Fact]
    public async Task Consumer_RejectsGameplayConsumptionWhileResetIsPending()
    {
        using var store = CreateGameplayStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        // Commit learner attempt
        var changeSet = CreateTestChangeSet("sub-consumer-fence", "add:1+1", true, 500, 1, 1);
        var cr = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(cr.IsSuccess);

        // Stage intent before reset
        var intent = new CyberDefensePendingIntentRecord("sub-consumer-fence", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await consumer.StageIntentAsync(intent);

        // Mark reset as pending
        await store.BeginOrGetResetIntentAsync();

        // Consumer must reject consumption
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ConsumeAttemptAsync("sub-consumer-fence"));

        // Recovery must also reject while reset is pending
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.RecoverPendingIntentsAsync());
    }

    [Fact]
    public async Task AppResetCoordinator_FullReset_CoordinatesAllStepsInOrder()
    {
        var recordedEvents = new List<string>();
        var dir = CreateTempDirectory();
        var learnerDbPath = Path.Combine(dir, "learner.db");
        var gameplayDbPath = Path.Combine(dir, "gameplay.db");

        using var learnerStore = new SqliteLearnerStore(learnerDbPath);
        await learnerStore.InitializeAsync();
        using var gameplayStore = new SqliteGameplayStore(gameplayDbPath);
        await gameplayStore.InitializeAsync();

        // Seed some learner progress
        var cs = CreateTestChangeSet("sub-seed-1", "add:1+1", true, 500, 1, 1);
        await learnerStore.CommitSubmissionAsync(cs);

        // Seed some gameplay state
        var intent = new CyberDefensePendingIntentRecord("sub-seed-1", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await gameplayStore.StagePendingIntentAsync(intent);
        await gameplayStore.ApplyAttemptTransactionAsync(intent);

        var spyLearner = new SpyLearnerStore(learnerStore, recordedEvents);
        var session = new TrainingSession(spyLearner);
        await session.InitializeAsync(startTiming: true);

        var spyPrefs = new SpyPreferenceStore(recordedEvents);
        var spyInstallId = new SpyInstallationIdProvider(recordedEvents);
        var spyCleaner = new SpyTelemetryShareCacheCleaner(recordedEvents);
        var cyberPrefs = new SpyCyberDefensePreferences();
        var sessionState = new CyberDefenseSessionState(cyberPrefs);

        var spyGameplay = new SpyGameplayStore(gameplayStore, recordedEvents);

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyCleaner, sessionState, spyGameplay);

        await coordinator.ExecuteFullResetAsync();

        // Verify ordering: BeginOrGetResetIntentAsync -> ResetLearningProgressAsync -> ResetGameplayStateAsync -> ResetAllPreferences -> ClearInstallationId -> PurgeShareCache -> ClearResetIntentAsync
        Assert.Equal("BeginOrGetResetIntentAsync", recordedEvents[0]);
        Assert.Equal("ResetLearningProgressAsync", recordedEvents[1]);
        Assert.Equal("ResetGameplayStateAsync", recordedEvents[2]);
        Assert.Equal("ResetAllPreferences", recordedEvents[3]);
        Assert.Equal("ClearInstallationId", recordedEvents[4]);
        Assert.Equal("PurgeShareCache", recordedEvents[5]);
        Assert.Equal("ClearResetIntentAsync", recordedEvents[6]);

        // Verify stores
        var resetIntent = await gameplayStore.GetResetIntentAsync();
        Assert.False(resetIntent.IsPending);
        Assert.Equal(1, resetIntent.CurrentEpoch);
        Assert.Equal(1, resetIntent.TargetEpoch);

        var gameplayEpoch = await gameplayStore.GetResetEpochAsync();
        Assert.Equal(1, gameplayEpoch);
        var gameplayRev = await gameplayStore.GetStoreRevisionAsync();
        Assert.Equal(1, gameplayRev);

        var learnerSnapshot = await learnerStore.LoadSnapshotAsync();
        Assert.Equal(0, learnerSnapshot.Progression.PracticePosition);
    }

    [Fact]
    public async Task AppResetCoordinator_WhenIntentPersistenceFails_AbortsBeforeLearnerReset()
    {
        var recordedEvents = new List<string>();
        var dir = CreateTempDirectory();
        var learnerDbPath = Path.Combine(dir, "learner.db");
        var gameplayDbPath = Path.Combine(dir, "gameplay.db");

        using var learnerStore = new SqliteLearnerStore(learnerDbPath);
        await learnerStore.InitializeAsync();
        using var gameplayStore = new SqliteGameplayStore(gameplayDbPath);
        await gameplayStore.InitializeAsync();

        // Seed learner progress
        var cs = CreateTestChangeSet("sub-seed-1", "add:1+1", true, 500, 1, 1);
        await learnerStore.CommitSubmissionAsync(cs);

        var spyLearner = new SpyLearnerStore(learnerStore, recordedEvents);
        var session = new TrainingSession(spyLearner);
        await session.InitializeAsync(startTiming: true);

        var spyPrefs = new SpyPreferenceStore(recordedEvents);
        var spyInstallId = new SpyInstallationIdProvider(recordedEvents);
        var spyCleaner = new SpyTelemetryShareCacheCleaner(recordedEvents);
        var cyberPrefs = new SpyCyberDefensePreferences();
        var sessionState = new CyberDefenseSessionState(cyberPrefs);

        var spyGameplay = new SpyGameplayStore(gameplayStore, recordedEvents)
        {
            ThrowOnBeginResetIntent = new InvalidOperationException("Simulated failure persisting reset intent")
        };

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyCleaner, sessionState, spyGameplay);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteFullResetAsync());
        Assert.Equal("Simulated failure persisting reset intent", ex.Message);

        // Learner store and preferences MUST NOT have been reset
        Assert.DoesNotContain("ResetLearningProgressAsync", recordedEvents);
        Assert.DoesNotContain("ResetAllPreferences", recordedEvents);

        var learnerSnapshot = await learnerStore.LoadSnapshotAsync();
        Assert.Equal(1, learnerSnapshot.Progression.PracticePosition);
    }

    [Fact]
    public async Task StartupRecovery_WhenInterruptedAfterIntentCommit_ResumesAndCompletesReset()
    {
        var dir = CreateTempDirectory();
        var learnerDbPath = Path.Combine(dir, "learner.db");
        var gameplayDbPath = Path.Combine(dir, "gameplay.db");

        using var learnerStore = new SqliteLearnerStore(learnerDbPath);
        await learnerStore.InitializeAsync();
        using var gameplayStore = new SqliteGameplayStore(gameplayDbPath);
        await gameplayStore.InitializeAsync();

        // Seed state
        var cs = CreateTestChangeSet("sub-seed-1", "add:1+1", true, 500, 1, 1);
        await learnerStore.CommitSubmissionAsync(cs);

        var intent = new CyberDefensePendingIntentRecord("sub-seed-1", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await gameplayStore.StagePendingIntentAsync(intent);
        await gameplayStore.ApplyAttemptTransactionAsync(intent);

        // Crash point: Mark reset intent as pending (target_epoch = 1) without completing reset
        await gameplayStore.BeginOrGetResetIntentAsync();

        // Now simulate app restart: initialize TrainingSession and AppResetCoordinator
        var session = new TrainingSession(learnerStore);
        var prefs = new SpyPreferenceStore();
        var installId = new SpyInstallationIdProvider();
        var cleaner = new SpyTelemetryShareCacheCleaner();
        var cyberPrefs = new SpyCyberDefensePreferences();
        var sessionState = new CyberDefenseSessionState(cyberPrefs);

        var coordinator = new AppResetCoordinator(session, prefs, installId, cleaner, sessionState, gameplayStore);

        var reconciled = await coordinator.ReconcileStartupResetStateAsync();
        Assert.True(reconciled);

        // Verify stores are in clean epoch 1 state
        var resetIntent = await gameplayStore.GetResetIntentAsync();
        Assert.False(resetIntent.IsPending);
        Assert.Equal(1, resetIntent.CurrentEpoch);
        Assert.Equal(1, resetIntent.TargetEpoch);

        var gameplayEpoch = await gameplayStore.GetResetEpochAsync();
        Assert.Equal(1, gameplayEpoch);
        var gameplayRev = await gameplayStore.GetStoreRevisionAsync();
        Assert.Equal(1, gameplayRev);

        var learnerSnapshot = await learnerStore.LoadSnapshotAsync();
        Assert.Equal(0, learnerSnapshot.Progression.PracticePosition);
    }

    [Fact]
    public async Task StartupRecovery_WhenNoResetPending_ReturnsFalseAndMutatesNothing()
    {
        var dir = CreateTempDirectory();
        var learnerDbPath = Path.Combine(dir, "learner.db");
        var gameplayDbPath = Path.Combine(dir, "gameplay.db");

        using var learnerStore = new SqliteLearnerStore(learnerDbPath);
        await learnerStore.InitializeAsync();
        using var gameplayStore = new SqliteGameplayStore(gameplayDbPath);
        await gameplayStore.InitializeAsync();

        var cs = CreateTestChangeSet("sub-seed-1", "add:1+1", true, 500, 1, 1);
        await learnerStore.CommitSubmissionAsync(cs);

        var session = new TrainingSession(learnerStore);
        var prefs = new SpyPreferenceStore();
        var installId = new SpyInstallationIdProvider();
        var cleaner = new SpyTelemetryShareCacheCleaner();
        var cyberPrefs = new SpyCyberDefensePreferences();
        var sessionState = new CyberDefenseSessionState(cyberPrefs);

        var coordinator = new AppResetCoordinator(session, prefs, installId, cleaner, sessionState, gameplayStore);

        var reconciled = await coordinator.ReconcileStartupResetStateAsync();
        Assert.False(reconciled);

        // State remains intact
        var learnerSnapshot = await learnerStore.LoadSnapshotAsync();
        Assert.Equal(1, learnerSnapshot.Progression.PracticePosition);
        Assert.Equal(0, prefs.ResetAllPreferencesCallCount);
    }

    [Fact]
    public async Task StartupRecovery_WhenGameplayStoreFails_ThrowsToPreventMixedState()
    {
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var throwingGameplayStore = new ThrowingGameplayStore();
        var session = new TrainingSession(learnerStore);
        var prefs = new SpyPreferenceStore();
        var installId = new SpyInstallationIdProvider();
        var cleaner = new SpyTelemetryShareCacheCleaner();
        var cyberPrefs = new SpyCyberDefensePreferences();
        var sessionState = new CyberDefenseSessionState(cyberPrefs);

        var throwingCoordinator = new AppResetCoordinator(session, prefs, installId, cleaner, sessionState, throwingGameplayStore);

        await Assert.ThrowsAsync<InvalidOperationException>(() => throwingCoordinator.ReconcileStartupResetStateAsync());
    }


    private sealed class ThrowingGameplayStore : IGameplayStore
    {
        public string StoragePath => "in-memory-throwing";
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Database disk I/O error during startup reset check.");
        public Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class SpyGameplayStore(IGameplayStore inner, List<string> events) : IGameplayStore
    {
        public Exception? ThrowOnBeginResetIntent { get; set; }
        public string StoragePath => inner.StoragePath;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => inner.InitializeAsync(cancellationToken);
        public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default) => inner.GetRunStateAsync(cancellationToken);
        public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default) => inner.GetResetEpochAsync(cancellationToken);
        public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default) => inner.GetStoreRevisionAsync(cancellationToken);
        public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) => inner.GetReceiptAsync(submissionId, cancellationToken);
        public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => inner.StagePendingIntentAsync(intent, cancellationToken);
        public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => inner.GetPendingIntentAsync(submissionId, cancellationToken);
        public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) => inner.GetPendingIntentsAsync(resetEpoch, cancellationToken);
        public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => inner.ClearPendingIntentAsync(submissionId, cancellationToken);
        public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => inner.ApplyAttemptTransactionAsync(intent, cancellationToken);
        public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default)
        {
            events.Add("GetResetIntentAsync");
            return inner.GetResetIntentAsync(cancellationToken);
        }
        public async Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default)
        {
            events.Add("BeginOrGetResetIntentAsync");
            if (ThrowOnBeginResetIntent != null)
            {
                throw ThrowOnBeginResetIntent;
            }
            return await inner.BeginOrGetResetIntentAsync(cancellationToken);
        }
        public async Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default)
        {
            events.Add("ResetGameplayStateAsync");
            await inner.ResetGameplayStateAsync(targetEpoch, cancellationToken);
        }
        public async Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default)
        {
            events.Add("ClearResetIntentAsync");
            await inner.ClearResetIntentAsync(targetEpoch, cancellationToken);
        }
        public Task CloseAsync(CancellationToken cancellationToken = default) => inner.CloseAsync(cancellationToken);
        public void Dispose() => inner.Dispose();
    }

    private sealed class SpyLearnerStore(ILearnerStore inner, List<string> events) : ILearnerStore
    {
        public string StoragePath => inner.StoragePath;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => inner.InitializeAsync(cancellationToken);
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) => inner.LoadSnapshotAsync(cancellationToken);
        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken cancellationToken = default) => inner.LoadRuntimeSnapshotAsync(cancellationToken);
        public Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(PracticeSelectionEvidenceRequest request, CancellationToken cancellationToken = default) => inner.LoadPracticeSelectionEvidenceAsync(request, cancellationToken);
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(ArithmeticOperation operation, long bandStartedPracticePosition, IReadOnlyList<string> frontierFactIds, CancellationToken cancellationToken = default) => inner.LoadLatestFrontierAttemptsAsync(operation, bandStartedPracticePosition, frontierFactIds, cancellationToken);
        public Task<IReadOnlyList<AttemptRecord>> LoadCompleteAttemptTelemetryAsync(CancellationToken cancellationToken = default) => inner.LoadCompleteAttemptTelemetryAsync(cancellationToken);
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default) => inner.CommitSubmissionAsync(changeSet, cancellationToken);
        public Task<CommittedLearnerAttemptEvidence?> GetCommittedAttemptEvidenceAsync(string submissionId, CancellationToken cancellationToken = default) => inner.GetCommittedAttemptEvidenceAsync(submissionId, cancellationToken);
        public async Task ResetLearningProgressAsync(CancellationToken cancellationToken = default)
        {
            events.Add("ResetLearningProgressAsync");
            await inner.ResetLearningProgressAsync(cancellationToken);
        }
        public Task CloseAsync(CancellationToken cancellationToken = default) => inner.CloseAsync(cancellationToken);
        public void Dispose() => inner.Dispose();
    }

    private sealed class SpyPreferenceStore(List<string>? events = null) : IPreferenceStore
    {
        public int ResetAllPreferencesCallCount { get; private set; }
        public int ResetPracticePreferencesCallCount { get; private set; }
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
        public void ResetPracticePreferences() => ResetPracticePreferencesCallCount++;
        public void ResetAllPreferences()
        {
            ResetAllPreferencesCallCount++;
            events?.Add("ResetAllPreferences");
        }
    }

    private sealed class SpyInstallationIdProvider(List<string>? events = null) : IInstallationIdProvider
    {
        public int ClearCallCount { get; private set; }
        public int GetOrCreateCallCount { get; private set; }
        public string InstallationId { get; set; } = Guid.NewGuid().ToString("D");
        public string GetOrCreateInstallationId()
        {
            GetOrCreateCallCount++;
            return InstallationId;
        }
        public void ClearInstallationId()
        {
            ClearCallCount++;
            events?.Add("ClearInstallationId");
        }
    }

    private sealed class SpyTelemetryShareCacheCleaner(List<string>? events = null) : ITelemetryShareCacheCleaner
    {
        public int PurgeCallCount { get; private set; }
        public void PurgeShareCache()
        {
            PurgeCallCount++;
            events?.Add("PurgeShareCache");
        }
    }

    private sealed class SpyCyberDefensePreferences : ICyberDefenseModePreferences
    {
        public bool Enabled { get; set; } = true;
        public bool GetCyberDefenseEnabled() => Enabled;
        public void SetCyberDefenseEnabled(bool enabled) => Enabled = enabled;
    }

    private static SubmissionChangeSet CreateTestChangeSet(
        string submissionId,
        string factId,
        bool isCorrect,
        long responseLatencyMs,
        long practicePosition,
        long expectedRevision)
    {
        var op = ArithmeticOperation.Addition;
        var stripped = factId.StartsWith("add:", StringComparison.Ordinal) ? factId.Substring(4) : factId;
        var parts = stripped.Split('+');
        int left = int.Parse(parts[0]);
        int right = int.Parse(parts[1]);
        int answer = left + right;

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
            responseLatencyMs: responseLatencyMs,
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
            LastLatencyMs = responseLatencyMs,
            RollingLatencyMs = responseLatencyMs,
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
}
