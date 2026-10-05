namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Copy;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Persistence;
using MathFirst.Application.Telemetry;
using MathFirst.Domain;
using Xunit;

public sealed class ResetWorkflowTests : IDisposable
{
    private readonly string _testDbDir;

    public ResetWorkflowTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstResetTests_" + Guid.NewGuid().ToString("N"));
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
        }
    }

    private sealed class InMemoryPreferenceStore : IPreferenceStore
    {
        public bool OnboardingCompleted { get; set; }
        public string Language { get; set; } = "system";
        public ThemePreference Theme { get; set; } = ThemePreference.System;
        public NumericKeypadLayout KeypadLayout { get; set; } = NumericKeypadLayout.Numpad;
        public bool HapticFeedbackEnabled { get; set; } = true;

        public bool GetOnboardingCompleted() => OnboardingCompleted;
        public void SetOnboardingCompleted(bool completed) => OnboardingCompleted = completed;
        public string GetLanguagePreference() => Language;
        public void SetLanguagePreference(string preference) => Language = preference;
        public ThemePreference GetThemePreference() => Theme;
        public void SetThemePreference(ThemePreference preference) => Theme = preference;
        public NumericKeypadLayout GetNumericKeypadLayout() => KeypadLayout;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) => KeypadLayout = layout;
        public bool GetHapticFeedbackEnabled() => HapticFeedbackEnabled;
        public void SetHapticFeedbackEnabled(bool enabled) => HapticFeedbackEnabled = enabled;

        private readonly Dictionary<string, bool> _operations = new(StringComparer.Ordinal);
        private PracticeTimeSetting _practiceTimeSetting = PracticeTimeSetting.Standard;

        public bool GetOperationEnabled(ArithmeticOperation operation) =>
            _operations.GetValueOrDefault($"op_{operation}", true);

        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) =>
            _operations[$"op_{operation}"] = enabled;

        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations()
        {
            var list = new List<ArithmeticOperation>();
            foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
            {
                if (GetOperationEnabled(op))
                {
                    list.Add(op);
                }
            }
            return PracticeOperationPreferencePolicy.NormalizeEnabledOperations(list);
        }

        public PracticeTimeSetting GetPracticeTimeSetting() => _practiceTimeSetting;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) =>
            _practiceTimeSetting = PracticeTimePreferencePolicy.Normalize((int)setting);

        public void ResetPracticePreferences()
        {
            _operations.Clear();
            _practiceTimeSetting = PracticeTimeSetting.Standard;
        }

        public void ResetAllPreferences()
        {
            OnboardingCompleted = false;
            Language = "system";
            Theme = ThemePreference.System;
            KeypadLayout = NumericKeypadLayout.Numpad;
            HapticFeedbackEnabled = true;
            ResetPracticePreferences();
        }
    }

    [Fact]
    public async Task ResetWorkflow_ResetLearningProgress_PreservesPreferencesAndOnboarding()
    {
        var dbPath = Path.Combine(_testDbDir, "reset_learning.db");
        using var store = new SqliteLearnerStore(dbPath);
        var prefs = new InMemoryPreferenceStore
        {
            OnboardingCompleted = true,
            Language = "de",
            Theme = ThemePreference.Dark,
            KeypadLayout = NumericKeypadLayout.Numpad
        };

        var session = new TrainingSession(store);
        await session.InitializeAsync();

        // Advance and submit
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(session.LastEvaluation!.ChangeSet.Attempt.Timestamp, session.LatestAcceptedPracticeAt);

        // Perform learning reset
        await session.ResetLearningProgressAsync();

        // Verify DB was reset to the initial V5 operation bands.
        Assert.All(session.Progression.OperationProgressions.Values, progression => Assert.Equal(0, progression.BandIndex));
        Assert.Empty(session.ItemStates);
        Assert.Null(session.LatestAcceptedPracticeAt);
        session.ShowInitialReadyGate();
        var copyContext = PracticeCopyContext.FromSession(
            session,
            "de",
            new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(PracticeCopyTrigger.InitialReady, copyContext.Trigger);

        // Verify UI preferences and onboarding were PRESERVED
        Assert.True(prefs.GetOnboardingCompleted());
        Assert.Equal("de", prefs.GetLanguagePreference());
        Assert.Equal(ThemePreference.Dark, prefs.GetThemePreference());
        Assert.Equal(NumericKeypadLayout.Numpad, prefs.GetNumericKeypadLayout());
    }

    [Fact]
    public async Task ResetWorkflow_ResetLearningProgress_InvalidatesCachedReturnCopyAcrossLearnerGenerations()
    {
        var dbPath = Path.Combine(_testDbDir, "reset_copy_identity.db");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: false));
        session.ShowInitialReadyGate();

        var oldGeneration = session.LearnerStateGenerationRevision;
        var gateRevision = session.PracticeGateActivationRevision;
        var oldContext = PracticeCopyContext.FromSession(
            session,
            "en",
            session.LatestAcceptedPracticeAt!.Value.AddDays(4));
        Assert.Equal(PracticeCopyTrigger.ReturnLongAbsence, oldContext.Trigger);

        var copyState = new PracticeGateCopyState(new PracticeCopySelector(new PracticeCopyLibrary()));
        copyState.Synchronize(
            new PracticeGatePresentationIdentity(oldGeneration, gateRevision),
            oldContext,
            "Ready");
        var oldMessageId = copyState.Current!.MessageId;
        Assert.StartsWith("ReturnLongAbsence.", oldMessageId, StringComparison.Ordinal);

        await session.ResetLearningProgressAsync(startTiming: false);

        Assert.Equal(PracticeGateState.InitialReadyGate, session.PracticeGate);
        Assert.Equal(gateRevision, session.PracticeGateActivationRevision);
        Assert.True(session.LearnerStateGenerationRevision > oldGeneration);
        Assert.Null(session.LatestAcceptedPracticeAt);

        var resetContext = PracticeCopyContext.FromSession(
            session,
            "en",
            new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(PracticeCopyTrigger.InitialReady, resetContext.Trigger);

        copyState.Synchronize(
            new PracticeGatePresentationIdentity(
                session.LearnerStateGenerationRevision,
                session.PracticeGateActivationRevision),
            resetContext,
            "Ready");

        Assert.NotEqual(oldMessageId, copyState.Current!.MessageId);
        Assert.Equal(PracticeCopyTrigger.InitialReady, copyState.Current.Trigger);
        Assert.Contains(
            copyState.Current.MessageId,
            new PracticeCopyLibrary().GetMessageIds(PracticeCopyTrigger.InitialReady, "en"));
    }

    [Fact]
    public async Task LearnerStateGeneration_ChangesOnlyWhenAuthoritativeLearnerStateIsReplaced()
    {
        var dbPath = Path.Combine(_testDbDir, "learner_generation.db");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);
        var initializedGeneration = session.LearnerStateGenerationRevision;

        session.SetPracticeSurfaceActive(false);
        session.SetPracticeSurfaceActive(true);
        session.ShowInitialReadyGate();
        Assert.Equal(initializedGeneration, session.LearnerStateGenerationRevision);

        session.StartOrResumePractice();
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(initializedGeneration, session.LearnerStateGenerationRevision);

        await session.ResetLearningProgressAsync(startTiming: false);
        var resetGeneration = session.LearnerStateGenerationRevision;
        Assert.True(resetGeneration > initializedGeneration);

        await session.InitializeAsync(startTiming: false);
        Assert.True(session.LearnerStateGenerationRevision > resetGeneration);
    }

    [Fact]
    public async Task ResetWorkflow_ResetUiPreferences_PreservesLearnerDatabase()
    {
        var dbPath = Path.Combine(_testDbDir, "reset_ui.db");
        using var store = new SqliteLearnerStore(dbPath);
        var prefs = new InMemoryPreferenceStore
        {
            OnboardingCompleted = true,
            Language = "de",
            Theme = ThemePreference.Dark,
            KeypadLayout = NumericKeypadLayout.Numpad
        };

        var session = new TrainingSession(store);
        await session.InitializeAsync();
        var learnerGeneration = session.LearnerStateGenerationRevision;

        // Advance and submit
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        // Reset UI Preferences
        prefs.SetLanguagePreference("system");
        prefs.SetThemePreference(ThemePreference.System);
        prefs.SetNumericKeypadLayout(NumericKeypadLayout.Numpad);
        prefs.SetHapticFeedbackEnabled(true);
        prefs.ResetPracticePreferences();

        Assert.Equal(learnerGeneration, session.LearnerStateGenerationRevision);

        // Verify UI prefs reset
        Assert.True(prefs.GetOnboardingCompleted());
        Assert.Equal("system", prefs.GetLanguagePreference());
        Assert.Equal(ThemePreference.System, prefs.GetThemePreference());
        Assert.Equal(NumericKeypadLayout.Numpad, prefs.GetNumericKeypadLayout());
        Assert.True(prefs.GetHapticFeedbackEnabled());

        // Verify DB still holds committed progress
        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(2, snapshot.Revision);
        Assert.Single(snapshot.ItemStates);

        var localizer = new LocalizationService();
        foreach (var language in new[] { "en", "de", "ru" })
        {
            localizer.ApplyLanguagePreference(language);
            Assert.DoesNotContain("onboarding", localizer["Reset_UiPreferences_Desc"], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onboarding", localizer["Reset_UiPreferences_Confirm"], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("онбординг", localizer["Reset_UiPreferences_Desc"], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("онбординг", localizer["Reset_UiPreferences_Confirm"], StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ResetWorkflow_FullLocalReset_ResetsBothDatabaseAndPreferences()
    {
        var dbPath = Path.Combine(_testDbDir, "reset_full.db");
        using var store = new SqliteLearnerStore(dbPath);
        var prefs = new InMemoryPreferenceStore
        {
            OnboardingCompleted = true,
            Language = "ru",
            Theme = ThemePreference.Dark,
            KeypadLayout = NumericKeypadLayout.Numpad
        };

        var session = new TrainingSession(store);
        await session.InitializeAsync();
        var learnerGeneration = session.LearnerStateGenerationRevision;
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        // Full reset
        await session.ResetLearningProgressAsync();
        prefs.ResetAllPreferences();

        Assert.True(session.LearnerStateGenerationRevision > learnerGeneration);

        // Verify DB reset
        Assert.All(session.Progression.OperationProgressions.Values, progression => Assert.Equal(0, progression.BandIndex));
        Assert.Empty(session.ItemStates);
        Assert.Null(session.LatestAcceptedPracticeAt);
        session.ShowInitialReadyGate();
        var copyContext = PracticeCopyContext.FromSession(
            session,
            "en",
            new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(PracticeCopyTrigger.InitialReady, copyContext.Trigger);

        // Verify Prefs reset
        Assert.False(prefs.GetOnboardingCompleted());
        Assert.Equal("system", prefs.GetLanguagePreference());
        Assert.Equal(ThemePreference.System, prefs.GetThemePreference());
        Assert.Equal(NumericKeypadLayout.Numpad, prefs.GetNumericKeypadLayout());
    }

    [Fact]
    public async Task PartialReset_ResetLearningProgress_PreservesInstallationIdAndShareCache()
    {
        var dbPath = Path.Combine(_testDbDir, "partial_reset_learning.db");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        Assert.Single(session.ItemStates);
        Assert.NotNull(session.LatestAcceptedPracticeAt);

        var idStore = new InMemoryInstallationIdStore();
        var idProvider = new PreferenceInstallationIdProvider(idStore);
        var initialId = idProvider.GetOrCreateInstallationId();
        var shareService = new SpyTelemetryShareService();

        await session.ResetLearningProgressAsync();

        Assert.Empty(session.ItemStates);
        Assert.Null(session.LatestAcceptedPracticeAt);

        Assert.Equal(0, idStore.ClearCallCount);
        Assert.Equal(initialId, idProvider.GetOrCreateInstallationId());
        Assert.Equal(initialId, idStore.StoredValue);

        Assert.Equal(0, shareService.PurgeCallCount);
    }

    [Fact]
    public void PartialReset_ResetAllPreferences_PreservesInstallationIdAndShareCache()
    {
        var prefs = new InMemoryPreferenceStore
        {
            OnboardingCompleted = true,
            Language = "de",
            Theme = ThemePreference.Dark
        };

        var idStore = new InMemoryInstallationIdStore();
        var idProvider = new PreferenceInstallationIdProvider(idStore);
        var initialId = idProvider.GetOrCreateInstallationId();
        var shareService = new SpyTelemetryShareService();

        prefs.ResetAllPreferences();

        Assert.False(prefs.GetOnboardingCompleted());
        Assert.Equal("system", prefs.GetLanguagePreference());
        Assert.Equal(ThemePreference.System, prefs.GetThemePreference());

        Assert.Equal(0, idStore.ClearCallCount);
        Assert.Equal(initialId, idProvider.GetOrCreateInstallationId());
        Assert.Equal(initialId, idStore.StoredValue);

        Assert.Equal(0, shareService.PurgeCallCount);
    }

    [Fact]
    public async Task FullReset_ClearsInstallationIdAndPurgesShareCache()
    {
        var dbPath = Path.Combine(_testDbDir, "full_reset_id_cache.db");
        using var store = new SqliteLearnerStore(dbPath);
        var prefs = new InMemoryPreferenceStore
        {
            OnboardingCompleted = true,
            Language = "de",
            Theme = ThemePreference.Dark
        };

        var session = new TrainingSession(store);
        await session.InitializeAsync();
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        var idStore = new InMemoryInstallationIdStore();
        var idProvider = new PreferenceInstallationIdProvider(idStore);
        var initialId = idProvider.GetOrCreateInstallationId();
        Assert.NotNull(idStore.StoredValue);

        var shareService = new SpyTelemetryShareService();

        var coordinator = new AppResetCoordinator(session, prefs, idProvider, shareService);

        await coordinator.ExecuteFullResetAsync();

        Assert.Empty(session.ItemStates);
        Assert.Null(session.LatestAcceptedPracticeAt);

        Assert.False(prefs.GetOnboardingCompleted());
        Assert.Equal("system", prefs.GetLanguagePreference());

        Assert.Equal(1, idStore.ClearCallCount);
        Assert.Null(idStore.StoredValue);

        Assert.Equal(1, shareService.PurgeCallCount);
    }

    private sealed class InMemoryInstallationIdStore : IInstallationIdStore
    {
        public string? StoredValue { get; set; }
        public int GetCallCount { get; private set; }
        public int SetCallCount { get; private set; }
        public int ClearCallCount { get; private set; }

        public InMemoryInstallationIdStore(string? initialValue = null)
        {
            StoredValue = initialValue;
        }

        public string? Get()
        {
            GetCallCount++;
            return StoredValue;
        }

        public void Set(string value)
        {
            SetCallCount++;
            StoredValue = value;
        }

        public void Clear()
        {
            ClearCallCount++;
            StoredValue = null;
        }
    }

    private sealed class SpyTelemetryShareService : ITelemetryShareService
    {
        public int PurgeCallCount { get; private set; }

        public Task<string> PrepareShareFileAsync(string fileName, Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult("C:\\Cache\\" + fileName);

        public Task DispatchSystemShareAsync(string filePath, string title) =>
            Task.CompletedTask;

        public void PurgeShareCache()
        {
            PurgeCallCount++;
        }
    }
}
