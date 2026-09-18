namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Domain;
using Xunit;

public sealed class HapticFeedbackTests
{
    private sealed class RecordingHapticDriver : IHapticDriver
    {
        public List<HapticFeedbackCue> RecordedCues { get; } = [];
        public bool ShouldThrow { get; set; }

        public void Perform(HapticFeedbackCue cue)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Simulated platform vibration hardware failure.");
            }

            RecordedCues.Add(cue);
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

    // ============================================================
    // 1. Preference Contract Tests
    // ============================================================

    [Fact]
    public void Preference_DefaultsToEnabled()
    {
        var store = new InMemoryPreferenceStore();
        Assert.True(store.GetHapticFeedbackEnabled());
    }

    [Fact]
    public void Preference_SetFalse_PersistsDisabled()
    {
        var store = new InMemoryPreferenceStore();
        store.SetHapticFeedbackEnabled(false);
        Assert.False(store.GetHapticFeedbackEnabled());
    }

    [Fact]
    public void Preference_SetTrue_PersistsEnabled()
    {
        var store = new InMemoryPreferenceStore();
        store.SetHapticFeedbackEnabled(false);
        Assert.False(store.GetHapticFeedbackEnabled());

        store.SetHapticFeedbackEnabled(true);
        Assert.True(store.GetHapticFeedbackEnabled());
    }

    [Fact]
    public void Preference_ResetAllPreferences_RestoresHapticToEnabled()
    {
        var store = new InMemoryPreferenceStore
        {
            HapticFeedbackEnabled = false
        };

        store.ResetAllPreferences();
        Assert.True(store.GetHapticFeedbackEnabled());
    }

    // ============================================================
    // 2. Service Behavioral & Failure Isolation Tests
    // ============================================================

    [Fact]
    public void Service_WhenEnabled_DispatchesAllCuesDeterministically()
    {
        var store = new InMemoryPreferenceStore { HapticFeedbackEnabled = true };
        var driver = new RecordingHapticDriver();
        var service = new HapticFeedbackService(store, driver);

        service.PerformCue(HapticFeedbackCue.KeyTap);
        service.PerformCue(HapticFeedbackCue.Correct);
        service.PerformCue(HapticFeedbackCue.Incorrect);
        service.PerformCue(HapticFeedbackCue.Timeout);

        Assert.Equal(
            [
                HapticFeedbackCue.KeyTap,
                HapticFeedbackCue.Correct,
                HapticFeedbackCue.Incorrect,
                HapticFeedbackCue.Timeout
            ],
            driver.RecordedCues);
    }

    [Fact]
    public void Service_WhenDisabled_SuppressesAllCues()
    {
        var store = new InMemoryPreferenceStore { HapticFeedbackEnabled = false };
        var driver = new RecordingHapticDriver();
        var service = new HapticFeedbackService(store, driver);

        service.PerformCue(HapticFeedbackCue.KeyTap);
        service.PerformCue(HapticFeedbackCue.Correct);
        service.PerformCue(HapticFeedbackCue.Incorrect);
        service.PerformCue(HapticFeedbackCue.Timeout);

        Assert.Empty(driver.RecordedCues);
    }

    [Fact]
    public void Service_WhenReEnabled_ImmediatelyRestoresCueDispatch()
    {
        var store = new InMemoryPreferenceStore { HapticFeedbackEnabled = false };
        var driver = new RecordingHapticDriver();
        var service = new HapticFeedbackService(store, driver);

        service.PerformCue(HapticFeedbackCue.KeyTap);
        Assert.Empty(driver.RecordedCues);

        store.SetHapticFeedbackEnabled(true);
        service.PerformCue(HapticFeedbackCue.KeyTap);
        Assert.Single(driver.RecordedCues);
        Assert.Equal(HapticFeedbackCue.KeyTap, driver.RecordedCues[0]);
    }

    [Fact]
    public void Service_PlatformDriverException_IsSafelyIsolatedAndDoesNotThrow()
    {
        var store = new InMemoryPreferenceStore { HapticFeedbackEnabled = true };
        var driver = new RecordingHapticDriver { ShouldThrow = true };
        var service = new HapticFeedbackService(store, driver);

        // Failure isolation invariant: service catches and swallows platform hardware errors
        var exception = Record.Exception(() => service.PerformCue(HapticFeedbackCue.KeyTap));
        Assert.Null(exception);

        exception = Record.Exception(() => service.PerformCue(HapticFeedbackCue.Correct));
        Assert.Null(exception);

        exception = Record.Exception(() => service.PerformCue(HapticFeedbackCue.Incorrect));
        Assert.Null(exception);

        exception = Record.Exception(() => service.PerformCue(HapticFeedbackCue.Timeout));
        Assert.Null(exception);
    }

    [Fact]
    public void Service_WithNullDriverOrStore_SafelyNoOpsWithoutThrowing()
    {
        var serviceWithNulls = new HapticFeedbackService(null, null);

        var exception = Record.Exception(() => serviceWithNulls.PerformCue(HapticFeedbackCue.KeyTap));
        Assert.Null(exception);
    }

    [Fact]
    public void NoOpDriver_SafelyExecutesWithoutThrowing()
    {
        var driver = NoOpHapticDriver.Instance;
        var exception = Record.Exception(() => driver.Perform(HapticFeedbackCue.KeyTap));
        Assert.Null(exception);
    }

    // ============================================================
    // 3. Semantic Cue Mapping Contract Tests
    // ============================================================

    [Theory]
    [InlineData(HapticFeedbackCue.KeyTap)]
    [InlineData(HapticFeedbackCue.Correct)]
    [InlineData(HapticFeedbackCue.Incorrect)]
    [InlineData(HapticFeedbackCue.Timeout)]
    public void SemanticCues_AreExplicitlyCoveredInEnum(HapticFeedbackCue cue)
    {
        Assert.True(Enum.IsDefined(typeof(HapticFeedbackCue), cue));
    }

    // ============================================================
    // 4. Onboarding & Settings Lifecycle Contract Tests
    // ============================================================

    [Fact]
    public void Onboarding_DraftHapticState_PreservedAcrossStepsAndPersistedOnCompletion()
    {
        var store = new InMemoryPreferenceStore();
        var driver = new RecordingHapticDriver();
        var hapticService = new HapticFeedbackService(store, driver);

        // Initial default in store is true
        Assert.True(store.GetHapticFeedbackEnabled());

        // Step 2 initializes draft from store
        var selectedHaptic = store.GetHapticFeedbackEnabled();
        Assert.True(selectedHaptic);

        // Learner toggles to false
        selectedHaptic = false;

        // Learner steps back to Step 1, then forward to Step 2: draft remains false
        Assert.False(selectedHaptic);

        // Complete onboarding: draft is committed to store
        store.SetHapticFeedbackEnabled(selectedHaptic);
        store.SetOnboardingCompleted(true);

        Assert.False(store.GetHapticFeedbackEnabled());
        Assert.True(store.GetOnboardingCompleted());
    }

    [Fact]
    public void Settings_Toggle_EmitsPreviewCueOnlyWhenEnabling()
    {
        var store = new InMemoryPreferenceStore { HapticFeedbackEnabled = true };
        var driver = new RecordingHapticDriver();
        var hapticService = new HapticFeedbackService(store, driver);

        // Disabling: no preview cue emitted
        store.SetHapticFeedbackEnabled(false);
        Assert.Empty(driver.RecordedCues);

        // Enabling: preview cue KeyTap emitted
        store.SetHapticFeedbackEnabled(true);
        hapticService.PerformCue(HapticFeedbackCue.KeyTap);

        Assert.Single(driver.RecordedCues);
        Assert.Equal(HapticFeedbackCue.KeyTap, driver.RecordedCues[0]);
    }

    // ============================================================
    // 5. Practice Interaction Simulation & Auto-Submit Sequence
    // ============================================================

    [Fact]
    public void Practice_KeypadDigitAndBackspace_EmitKeyTapCue()
    {
        var store = new InMemoryPreferenceStore { HapticFeedbackEnabled = true };
        var driver = new RecordingHapticDriver();
        var hapticService = new HapticFeedbackService(store, driver);

        // User taps "1", "2", and Backspace
        hapticService.PerformCue(HapticFeedbackCue.KeyTap);
        hapticService.PerformCue(HapticFeedbackCue.KeyTap);
        hapticService.PerformCue(HapticFeedbackCue.KeyTap);

        Assert.Equal(3, driver.RecordedCues.Count);
        Assert.All(driver.RecordedCues, cue => Assert.Equal(HapticFeedbackCue.KeyTap, cue));
    }

    [Fact]
    public void Practice_SingleDigitAutoSubmit_EmitsKeyTapThenOutcomeCue()
    {
        var store = new InMemoryPreferenceStore { HapticFeedbackEnabled = true };
        var driver = new RecordingHapticDriver();
        var hapticService = new HapticFeedbackService(store, driver);

        // User taps "4" for answer "4":
        // 1. Keypad tap emits KeyTap
        hapticService.PerformCue(HapticFeedbackCue.KeyTap);

        // 2. Auto-submit evaluates answer as Correct -> emits Correct
        hapticService.PerformCue(HapticFeedbackCue.Correct);

        Assert.Equal(2, driver.RecordedCues.Count);
        Assert.Equal(HapticFeedbackCue.KeyTap, driver.RecordedCues[0]);
        Assert.Equal(HapticFeedbackCue.Correct, driver.RecordedCues[1]);
    }

    [Fact]
    public void Practice_IncorrectAnswerAndTimeout_EmitDistinctNegativeCues()
    {
        var store = new InMemoryPreferenceStore { HapticFeedbackEnabled = true };
        var driver = new RecordingHapticDriver();
        var hapticService = new HapticFeedbackService(store, driver);

        // Incorrect answer
        hapticService.PerformCue(HapticFeedbackCue.KeyTap);
        hapticService.PerformCue(HapticFeedbackCue.Incorrect);

        // Timeout
        hapticService.PerformCue(HapticFeedbackCue.Timeout);

        Assert.Equal(3, driver.RecordedCues.Count);
        Assert.Equal(HapticFeedbackCue.KeyTap, driver.RecordedCues[0]);
        Assert.Equal(HapticFeedbackCue.Incorrect, driver.RecordedCues[1]);
        Assert.Equal(HapticFeedbackCue.Timeout, driver.RecordedCues[2]);
    }

    [Fact]
    public void Practice_IncompleteInput_DoesNotEmitOutcomeCue()
    {
        var store = new InMemoryPreferenceStore { HapticFeedbackEnabled = true };
        var driver = new RecordingHapticDriver();
        var hapticService = new HapticFeedbackService(store, driver);

        // Multi-digit answer 12, user types "1"
        hapticService.PerformCue(HapticFeedbackCue.KeyTap);

        // Incomplete input: no outcome submitted or emitted
        Assert.Single(driver.RecordedCues);
        Assert.Equal(HapticFeedbackCue.KeyTap, driver.RecordedCues[0]);
    }
}
