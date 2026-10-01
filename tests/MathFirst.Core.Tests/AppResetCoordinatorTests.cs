namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Persistence;
using MathFirst.Application.Telemetry;
using MathFirst.Domain;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

public sealed class AppResetCoordinatorTests : IDisposable
{
    private readonly string _tempDirectory;

    public AppResetCoordinatorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "MathFirstAppResetCoord_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task ExecuteFullResetAsync_SequencesAllFiveStepsInOrder()
    {
        var recordedEvents = new List<string>();
        var dbPath = Path.Combine(_tempDirectory, "seq_order.db");
        using var sqliteStore = new SqliteLearnerStore(dbPath);
        var spyStore = new SpyLearnerStore(sqliteStore, recordedEvents);
        var session = new TrainingSession(spyStore);
        await session.InitializeAsync(startTiming: true);
        spyStore.IsPracticeSurfaceActiveQuery = () => session.IsPracticeSurfaceActive;

        var spyPrefs = new SpyPreferenceStore(recordedEvents);
        var spyInstallId = new SpyInstallationIdProvider(recordedEvents);
        var spyShare = new SpyTelemetryShareService(recordedEvents);

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyShare);

        await coordinator.ExecuteFullResetAsync();

        Assert.Equal(4, recordedEvents.Count);
        Assert.Equal("ResetLearningProgressAsync", recordedEvents[0]);
        Assert.Equal("ResetAllPreferences", recordedEvents[1]);
        Assert.Equal("ClearInstallationId", recordedEvents[2]);
        Assert.Equal("PurgeShareCache", recordedEvents[3]);
        Assert.False(spyStore.WasPracticeSurfaceActiveDuringReset);
    }

    [Fact]
    public async Task ExecuteFullResetAsync_PausesItemTimingFirst()
    {
        var dbPath = Path.Combine(_tempDirectory, "pause_first.db");
        using var sqliteStore = new SqliteLearnerStore(dbPath);
        var spyStore = new SpyLearnerStore(sqliteStore);
        var session = new TrainingSession(spyStore);
        await session.InitializeAsync(startTiming: true);

        Assert.True(session.IsPracticeSurfaceActive);
        Assert.True(session.IsTimingActive);

        spyStore.IsPracticeSurfaceActiveQuery = () => session.IsPracticeSurfaceActive;

        var spyPrefs = new SpyPreferenceStore();
        var spyInstallId = new SpyInstallationIdProvider();
        var spyShare = new SpyTelemetryShareService();

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyShare);

        await coordinator.ExecuteFullResetAsync();

        Assert.False(spyStore.WasPracticeSurfaceActiveDuringReset);
        Assert.False(session.IsPracticeSurfaceActive);
        Assert.False(session.IsTimingActive);
    }

    [Fact]
    public async Task ExecuteFullResetAsync_PropagatesCancellationTokenToLearnerReset()
    {
        var dbPath = Path.Combine(_tempDirectory, "propagate_ct.db");
        using var sqliteStore = new SqliteLearnerStore(dbPath);
        var spyStore = new SpyLearnerStore(sqliteStore);
        var session = new TrainingSession(spyStore);
        await session.InitializeAsync();

        var spyPrefs = new SpyPreferenceStore();
        var spyInstallId = new SpyInstallationIdProvider();
        var spyShare = new SpyTelemetryShareService();

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyShare);

        using var cts = new CancellationTokenSource();
        await coordinator.ExecuteFullResetAsync(cts.Token);

        Assert.Equal(cts.Token, spyStore.LastResetCancellationToken);
        Assert.NotEqual(CancellationToken.None, spyStore.LastResetCancellationToken);
    }

    [Fact]
    public async Task ExecuteFullResetAsync_WhenCachePurgeThrowsIOException_CompletesSuccessfully()
    {
        var dbPath = Path.Combine(_tempDirectory, "purge_io_ex.db");
        using var sqliteStore = new SqliteLearnerStore(dbPath);
        var spyStore = new SpyLearnerStore(sqliteStore);
        var session = new TrainingSession(spyStore);
        await session.InitializeAsync();

        var spyPrefs = new SpyPreferenceStore();
        var spyInstallId = new SpyInstallationIdProvider();
        var spyShare = new SpyTelemetryShareService
        {
            ThrowOnPurge = new IOException("Simulated disk I/O failure during purge")
        };

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyShare);

        var exception = await Record.ExceptionAsync(() => coordinator.ExecuteFullResetAsync());

        Assert.Null(exception);
        Assert.Equal(1, spyShare.PurgeCallCount);
        Assert.Equal(1, spyPrefs.ResetAllPreferencesCallCount);
        Assert.Equal(1, spyInstallId.ClearCallCount);
    }

    [Fact]
    public async Task ExecuteFullResetAsync_WhenCachePurgeThrowsUnauthorizedAccessException_CompletesSuccessfully()
    {
        var dbPath = Path.Combine(_tempDirectory, "purge_auth_ex.db");
        using var sqliteStore = new SqliteLearnerStore(dbPath);
        var spyStore = new SpyLearnerStore(sqliteStore);
        var session = new TrainingSession(spyStore);
        await session.InitializeAsync();

        var spyPrefs = new SpyPreferenceStore();
        var spyInstallId = new SpyInstallationIdProvider();
        var spyShare = new SpyTelemetryShareService
        {
            ThrowOnPurge = new UnauthorizedAccessException("Simulated access denied during purge")
        };

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyShare);

        var exception = await Record.ExceptionAsync(() => coordinator.ExecuteFullResetAsync());

        Assert.Null(exception);
        Assert.Equal(1, spyShare.PurgeCallCount);
        Assert.Equal(1, spyPrefs.ResetAllPreferencesCallCount);
        Assert.Equal(1, spyInstallId.ClearCallCount);
    }

    [Fact]
    public async Task ExecuteFullResetAsync_WhenCachePurgeThrowsUnexpectedException_PropagatesException()
    {
        var dbPath = Path.Combine(_tempDirectory, "purge_unexp_ex.db");
        using var sqliteStore = new SqliteLearnerStore(dbPath);
        var spyStore = new SpyLearnerStore(sqliteStore);
        var session = new TrainingSession(spyStore);
        await session.InitializeAsync();

        var spyPrefs = new SpyPreferenceStore();
        var spyInstallId = new SpyInstallationIdProvider();
        var spyShare = new SpyTelemetryShareService
        {
            ThrowOnPurge = new InvalidOperationException("Unexpected failure during purge")
        };

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyShare);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteFullResetAsync());
        Assert.Equal("Unexpected failure during purge", ex.Message);
        Assert.Equal(1, spyShare.PurgeCallCount);
        Assert.Equal(1, spyPrefs.ResetAllPreferencesCallCount);
        Assert.Equal(1, spyInstallId.ClearCallCount);
    }

    [Fact]
    public async Task ExecuteFullResetAsync_WhenStoreResetThrows_AbortsSubsequentSteps()
    {
        var dbPath = Path.Combine(_tempDirectory, "store_throws.db");
        using var sqliteStore = new SqliteLearnerStore(dbPath);
        var spyStore = new SpyLearnerStore(sqliteStore)
        {
            ThrowOnReset = new InvalidOperationException("Store reset simulated failure")
        };
        var session = new TrainingSession(spyStore);
        await session.InitializeAsync(startTiming: true);
        spyStore.IsPracticeSurfaceActiveQuery = () => session.IsPracticeSurfaceActive;

        var spyPrefs = new SpyPreferenceStore();
        var spyInstallId = new SpyInstallationIdProvider();
        var spyShare = new SpyTelemetryShareService();

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyShare);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteFullResetAsync());
        Assert.Equal("Store reset simulated failure", ex.Message);

        Assert.False(spyStore.WasPracticeSurfaceActiveDuringReset);
        Assert.Equal(0, spyPrefs.ResetAllPreferencesCallCount);
        Assert.Equal(0, spyInstallId.ClearCallCount);
        Assert.Equal(0, spyShare.PurgeCallCount);
    }

    private sealed class SpyLearnerStore(ILearnerStore inner, List<string>? events = null) : ILearnerStore
    {
        public CancellationToken LastResetCancellationToken { get; private set; }
        public Exception? ThrowOnReset { get; set; }
        public Func<bool>? IsPracticeSurfaceActiveQuery { get; set; }
        public bool? WasPracticeSurfaceActiveDuringReset { get; private set; }

        public string StoragePath => inner.StoragePath;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            inner.InitializeAsync(cancellationToken);

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            inner.LoadSnapshotAsync(cancellationToken);

        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken cancellationToken = default) =>
            inner.LoadRuntimeSnapshotAsync(cancellationToken);

        public Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(
            PracticeSelectionEvidenceRequest request,
            CancellationToken cancellationToken = default) =>
            inner.LoadPracticeSelectionEvidenceAsync(request, cancellationToken);

        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            inner.LoadLatestFrontierAttemptsAsync(operation, bandStartedPracticePosition, frontierFactIds, cancellationToken);

        public Task<IReadOnlyList<AttemptRecord>> LoadCompleteAttemptTelemetryAsync(CancellationToken cancellationToken = default) =>
            inner.LoadCompleteAttemptTelemetryAsync(cancellationToken);

        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default) =>
            inner.CommitSubmissionAsync(changeSet, cancellationToken);

        public async Task ResetLearningProgressAsync(CancellationToken cancellationToken = default)
        {
            LastResetCancellationToken = cancellationToken;
            if (IsPracticeSurfaceActiveQuery is not null)
            {
                WasPracticeSurfaceActiveDuringReset = IsPracticeSurfaceActiveQuery();
            }

            events?.Add("ResetLearningProgressAsync");

            if (ThrowOnReset is not null)
            {
                throw ThrowOnReset;
            }

            await inner.ResetLearningProgressAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            inner.CloseAsync(cancellationToken);

        public void Dispose() => inner.Dispose();
    }

    private sealed class SpyPreferenceStore(List<string>? events = null) : IPreferenceStore
    {
        public int ResetAllPreferencesCallCount { get; private set; }
        public int ResetPracticePreferencesCallCount { get; private set; }

        public bool GetOnboardingCompleted() => true;
        public void SetOnboardingCompleted(bool completed) { }
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

    private sealed class SpyTelemetryShareService(List<string>? events = null) : ITelemetryShareService
    {
        public int PurgeCallCount { get; private set; }
        public Exception? ThrowOnPurge { get; set; }

        public Task<string> PrepareShareFileAsync(string fileName, Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult("C:\\Cache\\" + fileName);

        public Task DispatchSystemShareAsync(string filePath, string title) =>
            Task.CompletedTask;

        public void PurgeShareCache()
        {
            PurgeCallCount++;
            events?.Add("PurgeShareCache");
            if (ThrowOnPurge is not null)
            {
                throw ThrowOnPurge;
            }
        }
    }
}
