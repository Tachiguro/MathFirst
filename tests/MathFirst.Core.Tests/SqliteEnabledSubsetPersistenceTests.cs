namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using Xunit;

public sealed class SqliteEnabledSubsetPersistenceTests : IDisposable
{
    private readonly string _testDbDirectory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstSqliteEnabledSubset_" + Guid.NewGuid().ToString("N"));

    public SqliteEnabledSubsetPersistenceTests()
    {
        Directory.CreateDirectory(_testDbDirectory);
    }

    [Fact]
    public async Task FreshAdditionOnly_PersistsFortyAcceptedAttempts()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();
        SetOnly(preferences, ArithmeticOperation.Addition);

        using (var store = new SqliteLearnerStore(path))
        {
            var session = new TrainingSession(
                store,
                new FixedClock(),
                new AdaptivePracticeSelector(),
                preferenceStore: preferences);
            await session.InitializeAsync(startTiming: false);

            var operations = await CompleteAcceptedAttemptsAsync(session, preferences, 40);

            Assert.All(operations, operation => Assert.Equal(ArithmeticOperation.Addition, operation));
            Assert.NotEqual(SessionInteractionState.PersistenceFailure, session.InteractionState);
            await store.CloseAsync();
        }

        using var reopened = new SqliteLearnerStore(path);
        var snapshot = await reopened.LoadSnapshotAsync();
        Assert.Equal(40, snapshot.Progression.PracticePosition);
        Assert.Equal(40, snapshot.RecentAttempts.Count);
        Assert.All(snapshot.RecentAttempts, attempt => Assert.Equal(ArithmeticOperation.Addition, attempt.Operation));
    }

    [Fact]
    public async Task ExistingAllOperationHistory_SwitchToAdditionOnly_PersistsThroughPositionEighty()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();

        using (var store = new SqliteLearnerStore(path))
        {
            var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
            await session.InitializeAsync(startTiming: false);

            await CompleteAcceptedAttemptsAsync(session, preferences, 40, advanceAfterFinal: false);
            Assert.Equal(40, (await store.LoadSnapshotAsync()).Progression.PracticePosition);

            SetOnly(preferences, ArithmeticOperation.Addition);
            Assert.Equal([ArithmeticOperation.Addition], preferences.GetEnabledOperations());
            AdvanceAfterAcceptedAttempt(session);

            var postSwitchOperations = await CompleteAcceptedAttemptsAsync(session, preferences, 40);

            Assert.All(postSwitchOperations, operation => Assert.Equal(ArithmeticOperation.Addition, operation));
            Assert.NotEqual(SessionInteractionState.PersistenceFailure, session.InteractionState);
            await store.CloseAsync();
        }

        using var reopened = new SqliteLearnerStore(path);
        var snapshot = await reopened.LoadSnapshotAsync();
        Assert.Equal(80, snapshot.Progression.PracticePosition);
        Assert.Equal(40, snapshot.RecentAttempts.Count(attempt => attempt.Operation == ArithmeticOperation.Addition));
    }

    [Theory]
    [InlineData(ArithmeticOperation.Addition)]
    [InlineData(ArithmeticOperation.Subtraction)]
    [InlineData(ArithmeticOperation.Multiplication)]
    [InlineData(ArithmeticOperation.Division)]
    public async Task EverySingleOperationConfiguration_PersistsTwelveAcceptedAttempts(
        ArithmeticOperation enabledOperation)
    {
        var preferences = new TestPreferenceStore();
        SetOnly(preferences, enabledOperation);
        using var store = new SqliteLearnerStore(GetDatabasePath());
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        var operations = await CompleteAcceptedAttemptsAsync(session, preferences, 12);

        Assert.All(operations, operation => Assert.Equal(enabledOperation, operation));
        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(12, snapshot.Progression.PracticePosition);
        Assert.All(snapshot.RecentAttempts, attempt => Assert.Equal(enabledOperation, attempt.Operation));
    }

    [Fact]
    public async Task AdditionAndMultiplicationSubset_PersistsTwentyAlternatingAttempts()
    {
        var preferences = new TestPreferenceStore();
        SetEnabled(preferences, ArithmeticOperation.Addition, ArithmeticOperation.Multiplication);
        using var store = new SqliteLearnerStore(GetDatabasePath());
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        var operations = await CompleteAcceptedAttemptsAsync(session, preferences, 20);

        Assert.Equal(
            Enumerable.Range(1, 20).Select(position => position % 2 == 1
                ? ArithmeticOperation.Addition
                : ArithmeticOperation.Multiplication),
            operations);
        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(20, snapshot.Progression.PracticePosition);
        Assert.DoesNotContain(snapshot.RecentAttempts, attempt =>
            attempt.Operation is ArithmeticOperation.Subtraction or ArithmeticOperation.Division);
    }

    [Fact]
    public async Task ThreeOperationSubset_PersistsTwentyFourAcceptedAttempts()
    {
        var preferences = new TestPreferenceStore();
        SetEnabled(
            preferences,
            ArithmeticOperation.Addition,
            ArithmeticOperation.Subtraction,
            ArithmeticOperation.Division);
        using var store = new SqliteLearnerStore(GetDatabasePath());
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        var operations = await CompleteAcceptedAttemptsAsync(session, preferences, 24);

        Assert.Equal(24, operations.Count);
        Assert.DoesNotContain(ArithmeticOperation.Multiplication, operations);
        Assert.Equal(24, (await store.LoadSnapshotAsync()).Progression.PracticePosition);
    }

    [Fact]
    public async Task AllToAdditionOnlyToAll_LiveSessionPreservesDisabledStateAndContinues()
    {
        var preferences = new TestPreferenceStore();
        using var store = new SqliteLearnerStore(GetDatabasePath());
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        await CompleteAcceptedAttemptsAsync(session, preferences, 40, advanceAfterFinal: false);
        var beforeDisabledPhase = await store.LoadSnapshotAsync();

        SetOnly(preferences, ArithmeticOperation.Addition);
        AdvanceAfterAcceptedAttempt(session);
        var disabledPhaseOperations = await CompleteAcceptedAttemptsAsync(
            session,
            preferences,
            20,
            advanceAfterFinal: false);
        var afterDisabledPhase = await store.LoadSnapshotAsync();

        Assert.All(disabledPhaseOperations, operation => Assert.Equal(ArithmeticOperation.Addition, operation));
        AssertDisabledOperationStateUnchanged(beforeDisabledPhase, afterDisabledPhase);

        SetEnabled(preferences, PracticeOperationPreferencePolicy.AllOperations.ToArray());
        AdvanceAfterAcceptedAttempt(session);
        var reenabledOperations = await CompleteAcceptedAttemptsAsync(session, preferences, 16);

        Assert.Equal(76, session.Progression.PracticePosition);
        Assert.Equal(PracticeOperationPreferencePolicy.AllOperations, reenabledOperations.Distinct().ToArray());
        Assert.NotEqual(SessionInteractionState.PersistenceFailure, session.InteractionState);
        Assert.Equal(76, (await store.LoadSnapshotAsync()).Progression.PracticePosition);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbDirectory))
            {
                Directory.Delete(_testDbDirectory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of isolated synthetic test data.
        }
    }

    private string GetDatabasePath() =>
        Path.Combine(_testDbDirectory, $"enabled-subset-{Guid.NewGuid():N}.db");

    private static async Task<IReadOnlyList<ArithmeticOperation>> CompleteAcceptedAttemptsAsync(
        TrainingSession session,
        IPreferenceStore preferences,
        int count,
        bool advanceAfterFinal = false)
    {
        var selectedOperations = new List<ArithmeticOperation>(count);
        for (var index = 0; index < count; index++)
        {
            var expectedPosition = checked(session.Progression.PracticePosition + 1);
            var expectedOperation = AdaptivePracticeSelector.GetScheduledOperation(
                expectedPosition,
                preferences.GetEnabledOperations());
            var fact = session.CurrentFact;
            Assert.Equal(expectedOperation, fact.Operation);
            selectedOperations.Add(fact.Operation);

            session.SubmitAnswer(fact.CorrectResult);
            var result = await session.CommitCurrentEvaluationAsync();
            Assert.True(
                result.IsSuccess,
                $"Position {expectedPosition} ({fact.Operation}) failed with {result.Status}: {result.Message}");
            Assert.Equal(expectedPosition, session.Progression.PracticePosition);
            Assert.NotEqual(SessionInteractionState.PersistenceFailure, session.InteractionState);

            if (index < count - 1 || advanceAfterFinal)
            {
                AdvanceAfterAcceptedAttempt(session);
            }
        }

        return selectedOperations;
    }

    private static void AdvanceAfterAcceptedAttempt(TrainingSession session)
    {
        if (session.PendingCheckIn is not null)
        {
            Assert.False(session.AdvanceAfterCorrectAnswer(startTiming: false));
            Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);
            session.ContinuePractice(startTiming: false);
            return;
        }

        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: false));
    }

    private static void SetOnly(TestPreferenceStore preferences, ArithmeticOperation operation) =>
        SetEnabled(preferences, operation);

    private static void SetEnabled(TestPreferenceStore preferences, params ArithmeticOperation[] enabledOperations)
    {
        var enabled = enabledOperations.ToHashSet();
        foreach (var operation in PracticeOperationPreferencePolicy.AllOperations)
        {
            preferences.SetOperationEnabled(operation, enabled.Contains(operation));
        }
    }

    private static void AssertDisabledOperationStateUnchanged(
        LearnerSnapshot before,
        LearnerSnapshot after)
    {
        var disabledOperations = PracticeOperationPreferencePolicy.AllOperations
            .Where(operation => operation != ArithmeticOperation.Addition)
            .ToArray();

        foreach (var operation in disabledOperations)
        {
            Assert.Equal(
                before.Progression.OperationProgressions[operation],
                after.Progression.OperationProgressions[operation]);
        }

        var beforeItems = before.ItemStates.Values
            .Where(state => disabledOperations.Contains(state.Operation))
            .OrderBy(state => state.FactId, StringComparer.Ordinal)
            .ToArray();
        var afterItems = after.ItemStates.Values
            .Where(state => disabledOperations.Contains(state.Operation))
            .OrderBy(state => state.FactId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(beforeItems.Length, afterItems.Length);
        for (var index = 0; index < beforeItems.Length; index++)
        {
            AssertItemStateEqual(beforeItems[index], afterItems[index]);
        }

        var beforeFsrs = before.FsrsStates.Values
            .Where(state => before.ItemStates.TryGetValue(state.FactId, out var item)
                && disabledOperations.Contains(item.Operation))
            .OrderBy(state => state.FactId, StringComparer.Ordinal)
            .ToArray();
        var afterFsrs = after.FsrsStates.Values
            .Where(state => after.ItemStates.TryGetValue(state.FactId, out var item)
                && disabledOperations.Contains(item.Operation))
            .OrderBy(state => state.FactId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(beforeFsrs, afterFsrs);

        var beforeAttempts = before.RecentAttempts
            .Where(attempt => disabledOperations.Contains(attempt.Operation))
            .OrderBy(attempt => attempt.PracticePosition)
            .ToArray();
        var afterAttempts = after.RecentAttempts
            .Where(attempt => disabledOperations.Contains(attempt.Operation))
            .OrderBy(attempt => attempt.PracticePosition)
            .ToArray();
        Assert.Equal(beforeAttempts, afterAttempts);
    }

    private static void AssertItemStateEqual(ItemLearningState expected, ItemLearningState actual)
    {
        Assert.Equal(expected.FactId, actual.FactId);
        Assert.Equal(expected.Operation, actual.Operation);
        Assert.Equal(expected.LeftOperand, actual.LeftOperand);
        Assert.Equal(expected.RightOperand, actual.RightOperand);
        Assert.Equal(expected.TotalAttempts, actual.TotalAttempts);
        Assert.Equal(expected.CorrectAttempts, actual.CorrectAttempts);
        Assert.Equal(expected.IncorrectAttempts, actual.IncorrectAttempts);
        Assert.Equal(expected.ConsecutiveCorrectStreak, actual.ConsecutiveCorrectStreak);
        Assert.Equal(expected.LastLatencyMs, actual.LastLatencyMs);
        Assert.Equal(expected.RollingLatencyMs, actual.RollingLatencyMs);
        Assert.Equal(expected.FluentStreak, actual.FluentStreak);
        Assert.Equal(expected.IsProvisionallyMastered, actual.IsProvisionallyMastered);
        Assert.Equal(expected.NeedsRemediation, actual.NeedsRemediation);
        Assert.Equal(expected.RemediationDueOrder, actual.RemediationDueOrder);
        Assert.Equal(expected.LastPracticedOrder, actual.LastPracticedOrder);
        Assert.Equal(expected.LastPracticedAt, actual.LastPracticedAt);
    }

    private sealed class FixedClock : IClock
    {
        public long GetTimestamp() => 0;

        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(900);
    }

    private sealed class TestPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<ArithmeticOperation, bool> _operationPreferences = [];

        public bool GetOnboardingCompleted() => true;
        public void SetOnboardingCompleted(bool completed) { }
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => "system";
        public void SetLanguagePreference(string languageCode) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) =>
            _operationPreferences.GetValueOrDefault(operation, true);
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) =>
            _operationPreferences[operation] = enabled;
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() =>
            PracticeOperationPreferencePolicy.NormalizeEnabledOperations(
                PracticeOperationPreferencePolicy.AllOperations.Where(GetOperationEnabled));
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public void ResetPracticePreferences() => _operationPreferences.Clear();
        public void ResetAllPreferences() => _operationPreferences.Clear();
    }
}
