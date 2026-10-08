namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
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

        var cyberPrefs = new SpyCyberDefensePreferences();
        var sessionState = new CyberDefenseSessionState(cyberPrefs);
        var activeEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(activeEncounter);

        var spyPrefs = new SpyPreferenceStore(recordedEvents, () => sessionState.HasActiveEncounter);
        var spyInstallId = new SpyInstallationIdProvider(recordedEvents, () => sessionState.HasActiveEncounter);
        var spyCleaner = new SpyTelemetryShareCacheCleaner(recordedEvents);

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyCleaner, sessionState);

        await coordinator.ExecuteFullResetAsync();

        Assert.Equal(4, recordedEvents.Count);
        Assert.Equal("ResetLearningProgressAsync", recordedEvents[0]);
        Assert.Equal("ResetAllPreferences", recordedEvents[1]);
        Assert.Equal("ClearInstallationId", recordedEvents[2]);
        Assert.Equal("PurgeShareCache", recordedEvents[3]);
        Assert.False(spyStore.WasPracticeSurfaceActiveDuringReset);
        Assert.True(spyPrefs.WasActiveEncounterPresentDuringResetPreferences);
        Assert.False(spyInstallId.WasActiveEncounterPresentDuringClearInstallationId);
        Assert.False(sessionState.HasActiveEncounter);
    }

    [Fact]
    public void Constructor_RequiresNonNullCyberDefenseSessionState()
    {
        var dbPath = Path.Combine(_tempDirectory, "null_cyber.db");
        using var sqliteStore = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(sqliteStore);
        var spyPrefs = new SpyPreferenceStore();
        var spyInstallId = new SpyInstallationIdProvider();
        var spyCleaner = new SpyTelemetryShareCacheCleaner();

        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AppResetCoordinator(session, spyPrefs, spyInstallId, spyCleaner, null!));
        Assert.Equal("cyberDefenseSessionState", ex.ParamName);
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
        var spyCleaner = new SpyTelemetryShareCacheCleaner();
        var sessionState = new CyberDefenseSessionState(new SpyCyberDefensePreferences());

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyCleaner, sessionState);

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
        var spyCleaner = new SpyTelemetryShareCacheCleaner();
        var sessionState = new CyberDefenseSessionState(new SpyCyberDefensePreferences());

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyCleaner, sessionState);

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
        var spyCleaner = new SpyTelemetryShareCacheCleaner
        {
            ThrowOnPurge = new IOException("Simulated disk I/O failure during purge")
        };
        var sessionState = new CyberDefenseSessionState(new SpyCyberDefensePreferences());

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyCleaner, sessionState);

        var exception = await Record.ExceptionAsync(() => coordinator.ExecuteFullResetAsync());

        Assert.Null(exception);
        Assert.Equal(1, spyCleaner.PurgeCallCount);
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
        var spyCleaner = new SpyTelemetryShareCacheCleaner
        {
            ThrowOnPurge = new UnauthorizedAccessException("Simulated access denied during purge")
        };
        var sessionState = new CyberDefenseSessionState(new SpyCyberDefensePreferences());

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyCleaner, sessionState);

        var exception = await Record.ExceptionAsync(() => coordinator.ExecuteFullResetAsync());

        Assert.Null(exception);
        Assert.Equal(1, spyCleaner.PurgeCallCount);
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
        var spyCleaner = new SpyTelemetryShareCacheCleaner
        {
            ThrowOnPurge = new InvalidOperationException("Unexpected failure during purge")
        };
        var sessionState = new CyberDefenseSessionState(new SpyCyberDefensePreferences());

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyCleaner, sessionState);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteFullResetAsync());
        Assert.Equal("Unexpected failure during purge", ex.Message);
        Assert.Equal(1, spyCleaner.PurgeCallCount);
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
        var spyCleaner = new SpyTelemetryShareCacheCleaner();
        var sessionState = new CyberDefenseSessionState(new SpyCyberDefensePreferences());
        var activeEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(activeEncounter);

        var coordinator = new AppResetCoordinator(session, spyPrefs, spyInstallId, spyCleaner, sessionState);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteFullResetAsync());
        Assert.Equal("Store reset simulated failure", ex.Message);

        Assert.False(spyStore.WasPracticeSurfaceActiveDuringReset);
        Assert.Equal(0, spyPrefs.ResetAllPreferencesCallCount);
        Assert.Equal(0, spyInstallId.ClearCallCount);
        Assert.Equal(0, spyCleaner.PurgeCallCount);
        Assert.True(sessionState.HasActiveEncounter);
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

    private sealed class SpyCyberDefensePreferences : ICyberDefenseModePreferences
    {
        public bool Enabled { get; set; } = true;
        public bool GetCyberDefenseEnabled() => Enabled;
        public void SetCyberDefenseEnabled(bool enabled) => Enabled = enabled;
    }

    private sealed class SpyPreferenceStore(List<string>? events = null, Func<bool>? hasActiveEncounterQuery = null) : IPreferenceStore
    {
        public int ResetAllPreferencesCallCount { get; private set; }
        public int ResetPracticePreferencesCallCount { get; private set; }
        public bool? WasActiveEncounterPresentDuringResetPreferences { get; private set; }

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
            if (hasActiveEncounterQuery is not null)
            {
                WasActiveEncounterPresentDuringResetPreferences = hasActiveEncounterQuery();
            }

            ResetAllPreferencesCallCount++;
            events?.Add("ResetAllPreferences");
        }
    }

    private sealed class SpyInstallationIdProvider(List<string>? events = null, Func<bool>? hasActiveEncounterQuery = null) : IInstallationIdProvider
    {
        public int ClearCallCount { get; private set; }
        public int GetOrCreateCallCount { get; private set; }
        public string InstallationId { get; set; } = Guid.NewGuid().ToString("D");
        public bool? WasActiveEncounterPresentDuringClearInstallationId { get; private set; }

        public string GetOrCreateInstallationId()
        {
            GetOrCreateCallCount++;
            return InstallationId;
        }

        public void ClearInstallationId()
        {
            if (hasActiveEncounterQuery is not null)
            {
                WasActiveEncounterPresentDuringClearInstallationId = hasActiveEncounterQuery();
            }

            ClearCallCount++;
            events?.Add("ClearInstallationId");
        }
    }

    private sealed class SpyTelemetryShareCacheCleaner(List<string>? events = null) : ITelemetryShareCacheCleaner
    {
        public int PurgeCallCount { get; private set; }
        public Exception? ThrowOnPurge { get; set; }

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
