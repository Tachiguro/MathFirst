namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class DirectToPracticeStartupTests
{
    private sealed class FakeClock : IClock
    {
        private long _timestamp = 1_000_000;

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, _timestamp - startTimestamp));

        public void AdvanceMs(long milliseconds) => _timestamp += milliseconds;
    }

    private sealed class InMemoryPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<ArithmeticOperation, bool> _operations = [];

        public static InMemoryPreferenceStore WithEnabled(IEnumerable<ArithmeticOperation> enabled)
        {
            var store = new InMemoryPreferenceStore();
            var enabledSet = enabled.ToHashSet();
            foreach (var operation in PracticeOperationPreferencePolicy.AllOperations)
            {
                store.SetOperationEnabled(operation, enabledSet.Contains(operation));
            }
            return store;
        }

        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => LanguagePreferencePolicy.SystemPreferenceCode;
        public void SetLanguagePreference(string languageCode) { }
        public bool GetHapticFeedbackEnabled() => true;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) => _operations.GetValueOrDefault(operation, true);
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) => _operations[operation] = enabled;
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() =>
            PracticeOperationPreferencePolicy.NormalizeEnabledOperations(
                PracticeOperationPreferencePolicy.AllOperations.Where(GetOperationEnabled));
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public void ResetPracticePreferences() => _operations.Clear();
        public void ResetAllPreferences() => _operations.Clear();
    }

    private sealed class TransientEvidenceLearnerStore : ILearnerStore
    {
        private readonly LearnerSnapshot _snapshot;
        private int _evidenceLoadAttempt;

        public int FailOnEvidenceAttemptsCount { get; set; } = 1;
        public int ResetProgressCallCount { get; private set; }

        public TransientEvidenceLearnerStore(LearnerSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public string StoragePath => "inmemory://startup-recovery-transient";

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);

        public Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(
            PracticeSelectionEvidenceRequest request,
            CancellationToken cancellationToken = default)
        {
            _evidenceLoadAttempt++;

            if (_evidenceLoadAttempt <= FailOnEvidenceAttemptsCount)
            {
                throw new InvalidOperationException("Synthetic transient evidence load failure.");
            }

            return Task.FromResult(PracticeSelectionEvidence.FromSnapshot(_snapshot, request));
        }

        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttemptRecord>>([]);

        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PersistenceResult.Success(changeSet.ExpectedRevision + 1));

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default)
        {
            ResetProgressCallCount++;
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { }
    }

    private static LearnerSnapshot CreateTestSnapshot(
        bool hasCompletedHistory,
        int addBandIndex = 0,
        long practicePosition = 0)
    {
        var operationProgressions = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(
                op => op,
                op => new OperationProgression(
                    op,
                    op == ArithmeticOperation.Addition ? addBandIndex : 0,
                    0));

        var progression = new LearnerProgression
        {
            PracticePosition = practicePosition,
            OperationProgressions = operationProgressions,
            StoreRevision = 1,
            SchemaVersion = 6,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var eligibleFact = new ArithmeticFact(ArithmeticOperation.Addition, 1, 2);
        var eligibleItem = ItemLearningState.CreateNew(eligibleFact);
        var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);
        var recentAttempts = new List<AttemptRecord>();

        if (hasCompletedHistory)
        {
            eligibleItem.CorrectAttempts = 1;
            eligibleItem.TotalAttempts = 1;
            itemStates[eligibleFact.Id] = eligibleItem;
        }

        return new LearnerSnapshot(
            progression,
            itemStates,
            fsrsStates,
            recentAttempts,
            1,
            6,
            operationProgressions,
            latestAcceptedPracticeAt: hasCompletedHistory ? DateTimeOffset.UtcNow.AddMinutes(-5) : null);
    }

    [Fact]
    public void Home_OnInitializedAsync_CallsShowInitialReadyGateUnconditionally()
    {
        var home = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        var onInitIndex = home.IndexOf("protected override async Task OnInitializedAsync()", StringComparison.Ordinal);
        Assert.True(onInitIndex >= 0, "OnInitializedAsync method was not found.");
        var nextMethodIndex = home.IndexOf("private void SyncStateWithSession()", onInitIndex, StringComparison.Ordinal);
        Assert.True(nextMethodIndex > onInitIndex);
        var body = home.Substring(onInitIndex, nextMethodIndex - onInitIndex);

        Assert.Contains("await Session.InitializeAsync(startTiming: false);", body, StringComparison.Ordinal);
        Assert.Contains("Session.ShowInitialReadyGate();", body, StringComparison.Ordinal);
        Assert.Contains("Session.SetPracticeSurfaceActive(true);", body, StringComparison.Ordinal);
        Assert.DoesNotContain("if (Session.HasCompletedPracticeHistory)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_RetryInitializationAsync_CallsShowInitialReadyGateUnconditionally()
    {
        var home = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        var retryIndex = home.IndexOf("private async Task RetryInitializationAsync()", StringComparison.Ordinal);
        Assert.True(retryIndex >= 0, "RetryInitializationAsync method was not found.");
        var nextMethodIndex = home.IndexOf("private async Task HandleKeypadKeyAsync", retryIndex, StringComparison.Ordinal);
        Assert.True(nextMethodIndex > retryIndex);
        var body = home.Substring(retryIndex, nextMethodIndex - retryIndex);

        Assert.Contains("await Session.InitializeAsync(startTiming: false);", body, StringComparison.Ordinal);
        Assert.Contains("Session.ShowInitialReadyGate();", body, StringComparison.Ordinal);
        Assert.Contains("Session.SetPracticeSurfaceActive(true);", body, StringComparison.Ordinal);
        Assert.DoesNotContain("if (Session.HasCompletedPracticeHistory)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_ContainsNoGuardedShowInitialReadyGate()
    {
        var home = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        var normalized = home.Replace("\r\n", "\n");
        var guardedMatches = System.Text.RegularExpressions.Regex.Matches(
            normalized,
            @"if\s*\(Session\.HasCompletedPracticeHistory\)\s*\{\s*Session\.ShowInitialReadyGate\(\);\s*\}");

        Assert.Empty(guardedMatches);

        var allOccurrences = System.Text.RegularExpressions.Regex.Matches(
            normalized,
            @"Session\.ShowInitialReadyGate\(\)");

        Assert.Equal(2, allOccurrences.Count);
    }

    [Fact]
    public void Home_ProgressOverviewRendering_RemainsConditionalOnCompletedPracticeHistory()
    {
        var home = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        Assert.Contains("Session.PracticeGate == PracticeGateState.InitialReadyGate && Session.HasCompletedPracticeHistory", home, StringComparison.Ordinal);
        Assert.Contains("ready-progress-overview", home, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrainingSession_FreshLearner_EntersInitialReadyGateAndPausesTimingUntilExplicitStart()
    {
        var snapshot = CreateTestSnapshot(hasCompletedHistory: false);
        var store = new TransientEvidenceLearnerStore(snapshot)
        {
            FailOnEvidenceAttemptsCount = 0
        };
        var clock = new FakeClock();
        var session = new TrainingSession(
            store,
            clock,
            preferenceStore: InMemoryPreferenceStore.WithEnabled([ArithmeticOperation.Addition]));

        await session.InitializeAsync(startTiming: false);

        Assert.False(session.HasCompletedPracticeHistory);
        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.False(session.IsTimingActive);

        // Fresh learner: Home unconditionally calls ShowInitialReadyGate() before SetPracticeSurfaceActive(true)
        session.ShowInitialReadyGate();
        session.SetPracticeSurfaceActive(true);

        Assert.Equal(PracticeGateState.InitialReadyGate, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);

        clock.AdvanceMs(1000);
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);

        // Explicit Start action from learner
        session.StartOrResumePractice();

        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.True(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        clock.AdvanceMs(300);
        Assert.Equal(300, session.GetCurrentActiveElapsedMs());

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);
        Assert.Equal(300, eval.LatencyMs);
        Assert.NotNull(eval.ChangeSet?.Attempt);
        Assert.False(eval.ChangeSet.Attempt.IsInterrupted);
        Assert.True(eval.ChangeSet.Attempt.IsTimingEligible);
    }

    [Fact]
    public async Task TrainingSession_ReturningLearner_EntersInitialReadyGateAndPausesTimingUntilExplicitStart()
    {
        var snapshot = CreateTestSnapshot(hasCompletedHistory: true, addBandIndex: 1, practicePosition: 15);
        var store = new TransientEvidenceLearnerStore(snapshot)
        {
            FailOnEvidenceAttemptsCount = 0
        };
        var clock = new FakeClock();
        var session = new TrainingSession(
            store,
            clock,
            preferenceStore: InMemoryPreferenceStore.WithEnabled([ArithmeticOperation.Addition]));

        await session.InitializeAsync(startTiming: false);

        Assert.True(session.HasCompletedPracticeHistory);

        // Returning learner: Home unconditionally calls ShowInitialReadyGate() before SetPracticeSurfaceActive(true)
        session.ShowInitialReadyGate();
        session.SetPracticeSurfaceActive(true);

        Assert.Equal(PracticeGateState.InitialReadyGate, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);

        clock.AdvanceMs(1000);
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());

        // Explicit Start/Resume action from learner
        session.StartOrResumePractice();

        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.True(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);

        clock.AdvanceMs(450);
        Assert.Equal(450, session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public async Task StartupRecovery_FreshLearner_AppliesInitialReadyGateOnSuccessfulRetry()
    {
        var snapshot = CreateTestSnapshot(hasCompletedHistory: false);
        var store = new TransientEvidenceLearnerStore(snapshot)
        {
            FailOnEvidenceAttemptsCount = 1
        };
        var clock = new FakeClock();
        var session = new TrainingSession(
            store,
            clock,
            preferenceStore: InMemoryPreferenceStore.WithEnabled([ArithmeticOperation.Addition]));

        // First attempt fails
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InitializeAsync(startTiming: false));
        Assert.False(session.IsInitialized);

        // Retry succeeds
        await session.InitializeAsync(startTiming: false);
        Assert.True(session.IsInitialized);
        Assert.False(session.HasCompletedPracticeHistory);

        // Fresh decision applied after retry: unconditional Ready gate
        session.ShowInitialReadyGate();
        session.SetPracticeSurfaceActive(true);

        Assert.Equal(PracticeGateState.InitialReadyGate, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);

        session.StartOrResumePractice();
        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.True(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);
        Assert.Equal(0, store.ResetProgressCallCount);
    }

    [Fact]
    public async Task StartupRecovery_ReturningLearner_AppliesInitialReadyGateOnSuccessfulRetry()
    {
        var snapshot = CreateTestSnapshot(hasCompletedHistory: true, addBandIndex: 2, practicePosition: 30);
        var store = new TransientEvidenceLearnerStore(snapshot)
        {
            FailOnEvidenceAttemptsCount = 1
        };
        var clock = new FakeClock();
        var session = new TrainingSession(
            store,
            clock,
            preferenceStore: InMemoryPreferenceStore.WithEnabled([ArithmeticOperation.Addition]));

        // First attempt fails
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InitializeAsync(startTiming: false));
        Assert.False(session.IsInitialized);

        // Retry succeeds
        await session.InitializeAsync(startTiming: false);
        Assert.True(session.IsInitialized);
        Assert.True(session.HasCompletedPracticeHistory);

        // Returning decision applied after retry: unconditional Ready gate
        session.ShowInitialReadyGate();
        session.SetPracticeSurfaceActive(true);

        Assert.Equal(PracticeGateState.InitialReadyGate, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);

        session.StartOrResumePractice();
        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.True(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);
        Assert.Equal(0, store.ResetProgressCallCount);
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
