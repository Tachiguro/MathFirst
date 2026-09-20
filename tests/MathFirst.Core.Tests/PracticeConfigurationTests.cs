namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class PracticeConfigurationTests
{
    private sealed class InMemoryPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<string, bool> _boolPrefs = new(StringComparer.Ordinal);
        private int _practiceTimeSetting = 0;
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

        public bool GetOperationEnabled(ArithmeticOperation operation) =>
            _boolPrefs.GetValueOrDefault($"op_{operation}", true);

        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) =>
            _boolPrefs[$"op_{operation}"] = enabled;

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

        public PracticeTimeSetting GetPracticeTimeSetting() =>
            PracticeTimePreferencePolicy.Normalize(_practiceTimeSetting);

        public void SetPracticeTimeSetting(PracticeTimeSetting setting) =>
            _practiceTimeSetting = (int)PracticeTimePreferencePolicy.Normalize((int)setting);

        public void ResetPracticePreferences()
        {
            _boolPrefs.Clear();
            _practiceTimeSetting = 0;
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

    private sealed class RecordingStore : ILearnerStore
    {
        public string StoragePath => "inmemory://practice-config";
        public int CommitCount { get; private set; }
        public LearnerProgression Progression { get; set; } = LearnerProgression.CreateFresh();
        public Dictionary<string, ItemLearningState> Items { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, FsrsCardState> FsrsCards { get; } = new(StringComparer.Ordinal);
        public List<AttemptRecord> Attempts { get; } = [];
        public long Revision { get; set; } = 1;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LearnerSnapshot(
                Progression,
                Items,
                FsrsCards,
                Attempts,
                Revision,
                LearnerProgression.DefaultSchemaVersion,
                Progression.OperationProgressions));
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttemptRecord>>([]);
        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            CommitCount++;
            Revision = changeSet.ExpectedRevision + 1;
            Progression = changeSet.UpdatedProgression;
            Items[changeSet.UpdatedItemState.FactId] = changeSet.UpdatedItemState;
            if (changeSet.UpdatedFsrsState is not null)
            {
                FsrsCards[changeSet.UpdatedFsrsState.FactId] = changeSet.UpdatedFsrsState;
            }
            Attempts.Add(changeSet.Attempt);
            return Task.FromResult(PersistenceResult.Success(Revision));
        }
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default)
        {
            Progression = LearnerProgression.CreateFresh();
            Items.Clear();
            FsrsCards.Clear();
            Attempts.Clear();
            Revision++;
            return Task.CompletedTask;
        }
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    // ============================================================
    // 1. Operation Selection & Persistence Tests
    // ============================================================

    [Fact]
    public void OperationPolicy_DefaultsToAllFourOperationsEnabled()
    {
        var defaults = PracticeOperationPreferencePolicy.NormalizeEnabledOperations(null);
        Assert.Equal(
            [ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication, ArithmeticOperation.Division],
            defaults);
    }

    [Fact]
    public void OperationPolicy_EmptyOrCorruptedList_RecoversToAllFourOperations()
    {
        var empty = PracticeOperationPreferencePolicy.NormalizeEnabledOperations([]);
        Assert.Equal(4, empty.Count);
        Assert.Equal(
            [ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication, ArithmeticOperation.Division],
            empty);
    }

    [Fact]
    public void OperationPolicy_PreservesCanonicalEnumOrder()
    {
        // Even if passed out-of-order e.g. Division then Addition
        var enabled = PracticeOperationPreferencePolicy.NormalizeEnabledOperations(
            [ArithmeticOperation.Division, ArithmeticOperation.Addition]);
        Assert.Equal(
            [ArithmeticOperation.Addition, ArithmeticOperation.Division],
            enabled);
    }

    [Fact]
    public void OperationPreferenceStore_RoundTripsIndependently()
    {
        var store = new InMemoryPreferenceStore();
        Assert.True(store.GetOperationEnabled(ArithmeticOperation.Addition));
        Assert.True(store.GetOperationEnabled(ArithmeticOperation.Subtraction));
        Assert.True(store.GetOperationEnabled(ArithmeticOperation.Multiplication));
        Assert.True(store.GetOperationEnabled(ArithmeticOperation.Division));

        store.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
        store.SetOperationEnabled(ArithmeticOperation.Division, false);

        Assert.True(store.GetOperationEnabled(ArithmeticOperation.Addition));
        Assert.False(store.GetOperationEnabled(ArithmeticOperation.Subtraction));
        Assert.True(store.GetOperationEnabled(ArithmeticOperation.Multiplication));
        Assert.False(store.GetOperationEnabled(ArithmeticOperation.Division));

        Assert.Equal(
            [ArithmeticOperation.Addition, ArithmeticOperation.Multiplication],
            store.GetEnabledOperations());
    }

    [Fact]
    public void OperationPersistence_DisableThreeOperations_PersistsAdditionOnlySuccessfully()
    {
        var store = new InMemoryPreferenceStore();

        // 1. Begin with all four enabled
        Assert.True(store.GetOperationEnabled(ArithmeticOperation.Addition));
        Assert.True(store.GetOperationEnabled(ArithmeticOperation.Subtraction));
        Assert.True(store.GetOperationEnabled(ArithmeticOperation.Multiplication));
        Assert.True(store.GetOperationEnabled(ArithmeticOperation.Division));
        Assert.Equal(PracticeOperationPreferencePolicy.AllOperations, store.GetEnabledOperations());

        // 2. Disable Subtraction
        store.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
        // 3. Disable Multiplication
        store.SetOperationEnabled(ArithmeticOperation.Multiplication, false);
        // 4. Disable Division
        store.SetOperationEnabled(ArithmeticOperation.Division, false);

        // 5. Verify final persisted state is exactly: Addition=true, others=false
        Assert.True(store.GetOperationEnabled(ArithmeticOperation.Addition));
        Assert.False(store.GetOperationEnabled(ArithmeticOperation.Subtraction));
        Assert.False(store.GetOperationEnabled(ArithmeticOperation.Multiplication));
        Assert.False(store.GetOperationEnabled(ArithmeticOperation.Division));

        // 6. Re-read configuration from the persistence authority
        var enabled = store.GetEnabledOperations();

        // 7. Verify normalized enabled set is exactly Addition
        Assert.Single(enabled);
        Assert.Equal(ArithmeticOperation.Addition, enabled[0]);

        // 8. Verify no fallback to all operations occurs
        Assert.NotEqual(4, enabled.Count);
    }

    [Theory]
    [InlineData(ArithmeticOperation.Addition)]
    [InlineData(ArithmeticOperation.Subtraction)]
    [InlineData(ArithmeticOperation.Multiplication)]
    [InlineData(ArithmeticOperation.Division)]
    public void OperationPersistence_AllFourSingleOperationConfigurations_PersistAndNormalizeCorrectly(
        ArithmeticOperation singleEnabledOp)
    {
        var store = new InMemoryPreferenceStore();

        foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
        {
            store.SetOperationEnabled(op, op == singleEnabledOp);
        }

        foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
        {
            Assert.Equal(op == singleEnabledOp, store.GetOperationEnabled(op));
        }

        var enabled = store.GetEnabledOperations();
        Assert.Single(enabled);
        Assert.Equal(singleEnabledOp, enabled[0]);
    }

    [Fact]
    public void OperationPersistence_CorruptAllFalsePersistedState_RecoversToAllFourOperations()
    {
        var store = new InMemoryPreferenceStore();
        foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
        {
            store.SetOperationEnabled(op, false);
        }

        // When all 4 are persisted as false, reading normalized config safely recovers to all 4
        var enabled = store.GetEnabledOperations();
        Assert.Equal(4, enabled.Count);
        Assert.Equal(PracticeOperationPreferencePolicy.AllOperations, enabled);
    }

    [Fact]
    public void SettingsOrchestration_ToggleFlow_SyncsPreferenceStoreAndUiState()
    {
        var store = new InMemoryPreferenceStore();
        var uiEnabled = new HashSet<ArithmeticOperation>(store.GetEnabledOperations());
        Assert.Equal(4, uiEnabled.Count);

        // Disable Subtraction via orchestration helper
        var toggled = PracticeOperationPreferenceCoordinator.TryToggleOperation(
            store, uiEnabled, ArithmeticOperation.Subtraction, out var resulting);
        Assert.True(toggled);
        uiEnabled = new HashSet<ArithmeticOperation>(resulting);
        Assert.Equal([ArithmeticOperation.Addition, ArithmeticOperation.Multiplication, ArithmeticOperation.Division], resulting);
        Assert.False(store.GetOperationEnabled(ArithmeticOperation.Subtraction));

        // Disable Multiplication
        toggled = PracticeOperationPreferenceCoordinator.TryToggleOperation(
            store, uiEnabled, ArithmeticOperation.Multiplication, out resulting);
        Assert.True(toggled);
        uiEnabled = new HashSet<ArithmeticOperation>(resulting);
        Assert.Equal([ArithmeticOperation.Addition, ArithmeticOperation.Division], resulting);
        Assert.False(store.GetOperationEnabled(ArithmeticOperation.Multiplication));

        // Disable Division -> Addition only remains
        toggled = PracticeOperationPreferenceCoordinator.TryToggleOperation(
            store, uiEnabled, ArithmeticOperation.Division, out resulting);
        Assert.True(toggled);
        uiEnabled = new HashSet<ArithmeticOperation>(resulting);
        Assert.Equal([ArithmeticOperation.Addition], resulting);
        Assert.False(store.GetOperationEnabled(ArithmeticOperation.Division));
        Assert.True(store.GetOperationEnabled(ArithmeticOperation.Addition));

        // Attempting to disable Addition (the last remaining operation) is blocked
        toggled = PracticeOperationPreferenceCoordinator.TryToggleOperation(
            store, uiEnabled, ArithmeticOperation.Addition, out resulting);
        Assert.False(toggled);
        Assert.Equal([ArithmeticOperation.Addition], resulting);
        Assert.True(store.GetOperationEnabled(ArithmeticOperation.Addition));
    }

    private sealed class ThrowingPreferenceStore : IPreferenceStore
    {
        public bool ThrowOnWrite { get; set; } = true;
        public bool GetOnboardingCompleted() => true;
        public void SetOnboardingCompleted(bool completed) { }
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => "system";
        public void SetLanguagePreference(string languageCode) { }
        public bool GetHapticFeedbackEnabled() => true;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) => true;
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled)
        {
            if (ThrowOnWrite) throw new InvalidOperationException("Storage unavailable.");
        }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => PracticeOperationPreferencePolicy.AllOperations;
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public void ResetPracticePreferences() { }
        public void ResetAllPreferences() { }
    }

    [Fact]
    public void SettingsOrchestration_PersistenceFailure_ThrowsAndPreservesStoreState()
    {
        var throwingStore = new ThrowingPreferenceStore();
        var uiEnabled = new HashSet<ArithmeticOperation>(throwingStore.GetEnabledOperations());

        Assert.Throws<InvalidOperationException>(() =>
        {
            PracticeOperationPreferenceCoordinator.TryToggleOperation(
                throwingStore, uiEnabled, ArithmeticOperation.Subtraction, out _);
        });

        // Authoritative store state remains intact
        Assert.Equal(PracticeOperationPreferencePolicy.AllOperations, throwingStore.GetEnabledOperations());
    }

    // ============================================================
    // 2. Deterministic Enabled-Subset Scheduling Tests
    // ============================================================

    [Fact]
    public void Scheduling_AllFourEnabled_MatchesExactCurrentSchedule()
    {
        var allFour = PracticeOperationPreferencePolicy.AllOperations;

        var expectedOps = new[]
        {
            ArithmeticOperation.Addition,
            ArithmeticOperation.Subtraction,
            ArithmeticOperation.Multiplication,
            ArithmeticOperation.Division,
            ArithmeticOperation.Addition
        };

        for (var p = 1L; p <= 40; p++)
        {
            var op = AdaptivePracticeSelector.GetScheduledOperation(p, allFour);
            Assert.Equal(AdaptivePracticeSelector.GetScheduledOperation(p), op);
        }
    }

    [Fact]
    public void Scheduling_OneOperationEnabled_AlwaysSelectsThatOperation()
    {
        var singleOp = new[] { ArithmeticOperation.Multiplication };

        for (var p = 1L; p <= 30; p++)
        {
            var op = AdaptivePracticeSelector.GetScheduledOperation(p, singleOp);
            Assert.Equal(ArithmeticOperation.Multiplication, op);
        }
    }

    [Fact]
    public void Scheduling_TwoOperationsEnabled_BoundedBagSchedule()
    {
        var twoOps = new[] { ArithmeticOperation.Addition, ArithmeticOperation.Multiplication };

        // Position 1 & 2 (Bag 0): both operations present
        var bag0 = new[] { AdaptivePracticeSelector.GetScheduledOperation(1, twoOps), AdaptivePracticeSelector.GetScheduledOperation(2, twoOps) };
        Assert.Contains(ArithmeticOperation.Addition, bag0);
        Assert.Contains(ArithmeticOperation.Multiplication, bag0);

        // Position 3 & 4 (Bag 1): both operations present
        var bag1 = new[] { AdaptivePracticeSelector.GetScheduledOperation(3, twoOps), AdaptivePracticeSelector.GetScheduledOperation(4, twoOps) };
        Assert.Contains(ArithmeticOperation.Addition, bag1);
        Assert.Contains(ArithmeticOperation.Multiplication, bag1);
    }

    [Fact]
    public void Scheduling_ThreeOperationsEnabled_BoundedBagSchedule()
    {
        var threeOps = new[] { ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Division };

        // Bag 0 (P=1,2,3): all three operations present
        var bag0 = new[]
        {
            AdaptivePracticeSelector.GetScheduledOperation(1, threeOps),
            AdaptivePracticeSelector.GetScheduledOperation(2, threeOps),
            AdaptivePracticeSelector.GetScheduledOperation(3, threeOps)
        };
        Assert.Contains(ArithmeticOperation.Addition, bag0);
        Assert.Contains(ArithmeticOperation.Subtraction, bag0);
        Assert.Contains(ArithmeticOperation.Division, bag0);

        // Bag 1 (P=4,5,6): all three operations present
        var bag1 = new[]
        {
            AdaptivePracticeSelector.GetScheduledOperation(4, threeOps),
            AdaptivePracticeSelector.GetScheduledOperation(5, threeOps),
            AdaptivePracticeSelector.GetScheduledOperation(6, threeOps)
        };
        Assert.Contains(ArithmeticOperation.Addition, bag1);
        Assert.Contains(ArithmeticOperation.Subtraction, bag1);
        Assert.Contains(ArithmeticOperation.Division, bag1);
    }

    [Fact]
    public async Task DisabledOperation_IsNeverSelected_AndPreservesLearnerState()
    {
        var store = new RecordingStore();
        var prefStore = new InMemoryPreferenceStore();
        // Disable Subtraction and Division, leaving Addition and Multiplication
        prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
        prefStore.SetOperationEnabled(ArithmeticOperation.Division, false);

        var session = new TrainingSession(store, preferenceStore: prefStore);
        await session.InitializeAsync();

        // Run 20 submissions
        for (var i = 0; i < 20; i++)
        {
            var fact = session.CurrentFact;
            Assert.True(
                fact.Operation is ArithmeticOperation.Addition or ArithmeticOperation.Multiplication,
                $"Scheduled fact {fact.Id} had disabled operation {fact.Operation}.");

            session.SubmitAnswer(fact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceAfterCorrectAnswer();
        }

        // Verify Subtraction and Division progressions remained intact at band 0
        Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Subtraction].BandIndex);
        Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Division].BandIndex);

        // Re-enable Subtraction
        prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, true);

        // Next advance uses Subtraction when its turn arrives
        var nextOp = AdaptivePracticeSelector.GetScheduledOperation(
            session.Progression.PracticePosition + 1,
            prefStore.GetEnabledOperations());
        Assert.Contains(nextOp, prefStore.GetEnabledOperations());
    }

    [Theory]
    [InlineData(ArithmeticOperation.Addition)]
    [InlineData(ArithmeticOperation.Subtraction)]
    [InlineData(ArithmeticOperation.Multiplication)]
    [InlineData(ArithmeticOperation.Division)]
    public async Task SingleOperation_EachOfFourOperations_OnlySelectsThatOperationAndPreservesOtherProgressions(
        ArithmeticOperation singleOp)
    {
        var store = new RecordingStore();
        var prefStore = new InMemoryPreferenceStore();

        foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
        {
            prefStore.SetOperationEnabled(op, op == singleOp);
        }

        var session = new TrainingSession(store, preferenceStore: prefStore);
        await session.InitializeAsync();

        // Run 15 submissions with only the single operation enabled
        for (var i = 0; i < 15; i++)
        {
            var fact = session.CurrentFact;
            Assert.Equal(singleOp, fact.Operation);

            session.SubmitAnswer(fact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceAfterCorrectAnswer();
        }

        // Verify disabled operations' progressions remain untouched at band 0
        foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
        {
            if (op != singleOp)
            {
                Assert.Equal(0, session.Progression.OperationProgressions[op].BandIndex);
                Assert.Equal(0, session.Progression.OperationProgressions[op].BandStartedPracticePosition);
            }
        }
    }

    // ============================================================
    // 3. Practice-Time Setting & Effective Deadline Tests
    // ============================================================

    [Theory]
    [InlineData(0, PracticeTimeSetting.Standard)]
    [InlineData(-1, PracticeTimeSetting.NoTimePressure)]
    [InlineData(30, PracticeTimeSetting.Seconds30)]
    [InlineData(45, PracticeTimeSetting.Seconds45)]
    [InlineData(60, PracticeTimeSetting.Seconds60)]
    [InlineData(-2, PracticeTimeSetting.Standard)]
    [InlineData(99, PracticeTimeSetting.Standard)]
    public void PracticeTimePreferencePolicy_NormalizesCorrectly(int raw, PracticeTimeSetting expected)
    {
        Assert.Equal(expected, PracticeTimePreferencePolicy.Normalize(raw));
    }

    [Fact]
    public void PracticeTimePreferencePolicy_DeadlineFloors()
    {
        Assert.Equal(0L, PracticeTimePreferencePolicy.GetDeadlineFloorMs(PracticeTimeSetting.Standard));
        Assert.Equal(0L, PracticeTimePreferencePolicy.GetDeadlineFloorMs(PracticeTimeSetting.NoTimePressure));
        Assert.Equal(30_000L, PracticeTimePreferencePolicy.GetDeadlineFloorMs(PracticeTimeSetting.Seconds30));
        Assert.Equal(45_000L, PracticeTimePreferencePolicy.GetDeadlineFloorMs(PracticeTimeSetting.Seconds45));
        Assert.Equal(60_000L, PracticeTimePreferencePolicy.GetDeadlineFloorMs(PracticeTimeSetting.Seconds60));
    }

    [Theory]
    [InlineData(PracticeTimeSetting.Standard, true, false)]
    [InlineData(PracticeTimeSetting.NoTimePressure, false, true)]
    [InlineData(PracticeTimeSetting.Seconds30, true, false)]
    [InlineData(PracticeTimeSetting.Seconds45, true, false)]
    [InlineData(PracticeTimeSetting.Seconds60, true, false)]
    public void PracticeTimePreferencePolicy_SemanticFlags(
        PracticeTimeSetting setting,
        bool expectedHasEnforcedDeadline,
        bool expectedIsNoTimePressure)
    {
        Assert.Equal(expectedHasEnforcedDeadline, PracticeTimePreferencePolicy.HasEnforcedDeadline(setting));
        Assert.Equal(expectedIsNoTimePressure, PracticeTimePreferencePolicy.IsNoTimePressure(setting));
    }

    [Theory]
    [InlineData(PracticeTimeSetting.Standard, 30_000L, 30_000L)]
    [InlineData(PracticeTimeSetting.Seconds30, 15_000L, 30_000L)]
    [InlineData(PracticeTimeSetting.Seconds30, 30_000L, 30_000L)]
    [InlineData(PracticeTimeSetting.Seconds45, 15_000L, 45_000L)]
    [InlineData(PracticeTimeSetting.Seconds45, 30_000L, 45_000L)]
    [InlineData(PracticeTimeSetting.Seconds60, 20_000L, 60_000L)]
    [InlineData(PracticeTimeSetting.Seconds60, 30_000L, 60_000L)]
    public void EffectiveDeadline_AppliesFloorWithoutShorteningLargerAdaptiveDeadlines(
        PracticeTimeSetting setting,
        long adaptiveDeadlineMs,
        long expectedEffectiveDeadlineMs)
    {
        var floorMs = PracticeTimePreferencePolicy.GetDeadlineFloorMs(setting);
        var effectiveMs = Math.Max(adaptiveDeadlineMs, floorMs);
        Assert.Equal(expectedEffectiveDeadlineMs, effectiveMs);
    }

    [Fact]
    public async Task TrainingSession_AppliesConfiguredPracticeTimeSettingAsDeadlineFloor()
    {
        var store = new RecordingStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.Seconds60);

        var session = new TrainingSession(store, preferenceStore: prefStore);
        await session.InitializeAsync();

        // 60-second setting must guarantee CurrentFactDeadlineMs >= 60,000
        Assert.True(
            session.CurrentFactDeadlineMs >= 60_000,
            $"Expected deadline >= 60000 ms, got {session.CurrentFactDeadlineMs} ms.");
        Assert.True(
            session.CurrentFactDeadlineSeconds >= 60.0,
            $"Expected deadline seconds >= 60.0, got {session.CurrentFactDeadlineSeconds}.");

        // Fluency threshold is NOT altered by practice time floor
        Assert.True(session.CurrentFactFluencyThresholdMs <= AdaptivePacePolicy.MaximumFluencyThresholdMs);
        Assert.True(session.CurrentFactEasyThresholdMs <= AdaptivePacePolicy.MaximumEasyThresholdMs);
    }

    // ============================================================
    // 4. Reset Semantics Tests
    // ============================================================

    [Fact]
    public async Task ResetLearningProgress_PreservesOperationAndPracticeTimeSettings()
    {
        var store = new RecordingStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetOperationEnabled(ArithmeticOperation.Addition, true);
        prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.Seconds45);

        var session = new TrainingSession(store, preferenceStore: prefStore);
        await session.InitializeAsync();

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        // Reset learning progress
        await session.ResetLearningProgressAsync();

        // Preferences must remain preserved
        Assert.True(prefStore.GetOperationEnabled(ArithmeticOperation.Addition));
        Assert.False(prefStore.GetOperationEnabled(ArithmeticOperation.Subtraction));
        Assert.Equal(PracticeTimeSetting.Seconds45, prefStore.GetPracticeTimeSetting());
    }

    [Fact]
    public void RestoreDefaultSettings_RestoresAllFourOperationsAndStandardPracticeTime()
    {
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.Seconds60);

        // Reset practice preferences (part of restore defaults)
        prefStore.ResetPracticePreferences();

        Assert.Equal(
            [ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication, ArithmeticOperation.Division],
            prefStore.GetEnabledOperations());
        Assert.Equal(PracticeTimeSetting.Standard, prefStore.GetPracticeTimeSetting());
    }
}
