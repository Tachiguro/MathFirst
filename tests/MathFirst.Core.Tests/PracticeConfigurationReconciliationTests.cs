namespace MathFirst.Core.Tests;

using System.Reflection;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class PracticeConfigurationReconciliationTests : IDisposable
{
    private readonly string _testDbDirectory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstPracticeConfigurationReconciliation_" + Guid.NewGuid().ToString("N"));

    public PracticeConfigurationReconciliationTests()
    {
        Directory.CreateDirectory(_testDbDirectory);
    }

    public static IEnumerable<object[]> ValidEnabledSubsets()
    {
        yield return [new[] { ArithmeticOperation.Addition }];
        yield return [new[] { ArithmeticOperation.Subtraction }];
        yield return [new[] { ArithmeticOperation.Multiplication }];
        yield return [new[] { ArithmeticOperation.Division }];
        yield return [new[] { ArithmeticOperation.Addition, ArithmeticOperation.Subtraction }];
        yield return [new[] { ArithmeticOperation.Multiplication, ArithmeticOperation.Division }];
        yield return [new[] { ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication, ArithmeticOperation.Division }];
        yield return [PracticeOperationPreferencePolicy.AllOperations.ToArray()];
    }

    [Fact]
    public async Task DisableCurrentOperation_ReconciliationReplacesFactWithoutDurableMutation()
    {
        var databasePath = Path.Combine(_testDbDirectory, "disable-current-operation.db");
        var preferences = new TestPreferenceStore();
        using var store = new SqliteLearnerStore(databasePath);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        for (var index = 0; index < 4; index++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            var persistence = await session.CommitCurrentEvaluationAsync();
            Assert.True(persistence.IsSuccess);
            Assert.True(await session.AdvanceAfterCorrectAnswerAsync(startTiming: false));
        }

        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
        var discardedFactId = session.CurrentFact.Id;
        var revisionBefore = session.FactInstanceRevision;
        session.SetCurrentAnswerInput("7");
        var durableBefore = await store.LoadSnapshotAsync();
        var sessionTotalBefore = session.SessionTotalCount;
        var sessionCorrectBefore = session.SessionCorrectCount;
        var correctStreakBefore = session.CurrentCorrectStreak;

        preferences.SetEnabledOperations([ArithmeticOperation.Subtraction]);
        await InvokeConfigurationReconciliationAsync(session);

        var durableAfter = await store.LoadSnapshotAsync();
        Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);
        Assert.NotEqual(discardedFactId, session.CurrentFact.Id);
        Assert.True(session.FactInstanceRevision > revisionBefore);
        Assert.Equal(string.Empty, session.CurrentAnswerInput);
        Assert.Equal(sessionTotalBefore, session.SessionTotalCount);
        Assert.Equal(sessionCorrectBefore, session.SessionCorrectCount);
        Assert.Equal(correctStreakBefore, session.CurrentCorrectStreak);
        AssertLearnerStateEquivalent(durableBefore, durableAfter);
    }

    [Fact]
    public async Task RetainEnabledCurrentFact_PreservesIdentityRevisionInputAndDurableState()
    {
        var databasePath = Path.Combine(_testDbDirectory, "retain-current-operation.db");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);
        using var store = new SqliteLearnerStore(databasePath);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        var retainedFact = session.CurrentFact;
        var revisionBefore = session.FactInstanceRevision;
        session.SetCurrentAnswerInput("4");
        var durableBefore = await store.LoadSnapshotAsync();

        preferences.SetEnabledOperations([
            ArithmeticOperation.Addition,
            ArithmeticOperation.Subtraction
        ]);
        await InvokeConfigurationReconciliationAsync(session);

        Assert.Same(retainedFact, session.CurrentFact);
        Assert.Equal(retainedFact.Id, session.CurrentFact.Id);
        Assert.Equal(retainedFact.LeftOperand, session.CurrentFact.LeftOperand);
        Assert.Equal(retainedFact.RightOperand, session.CurrentFact.RightOperand);
        Assert.Equal(retainedFact.CorrectResult, session.CurrentFact.CorrectResult);
        Assert.Equal(revisionBefore, session.FactInstanceRevision);
        Assert.Equal("4", session.CurrentAnswerInput);
        AssertLearnerStateEquivalent(durableBefore, await store.LoadSnapshotAsync());
    }

    [Fact]
    public async Task SequentialConfigurationChanges_FinalCurrentFactMatchesFinalEnabledSet()
    {
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);
        using var store = new SqliteLearnerStore(
            Path.Combine(_testDbDirectory, "sequential-configuration.db"));
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        preferences.SetEnabledOperations([ArithmeticOperation.Subtraction]);
        await InvokeConfigurationReconciliationAsync(session);
        Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);

        preferences.SetEnabledOperations([ArithmeticOperation.Multiplication]);
        await InvokeConfigurationReconciliationAsync(session);

        Assert.Equal([ArithmeticOperation.Multiplication], preferences.GetEnabledOperations());
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Empty((await store.LoadSnapshotAsync()).RecentAttempts);
    }

    [Fact]
    public async Task EvidenceFailure_AfterPreferenceSave_IsObservableAndRetryUsesPersistedPreference()
    {
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);
        using var store = new FaultInjectingLearnerStore(
            new SqliteLearnerStore(Path.Combine(_testDbDirectory, "failure-retry.db")));
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        var originalFact = session.CurrentFact;
        var originalRevision = session.FactInstanceRevision;
        session.SetCurrentAnswerInput("8");
        var durableBefore = await store.LoadSnapshotAsync();
        preferences.SetEnabledOperations([ArithmeticOperation.Subtraction]);
        store.FailNextSelectionEvidenceLoad = true;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeConfigurationReconciliationAsync(session));

        Assert.Equal("Injected selection evidence failure.", failure.Message);
        Assert.Equal([ArithmeticOperation.Subtraction], preferences.GetEnabledOperations());
        Assert.Same(originalFact, session.CurrentFact);
        Assert.Equal(originalRevision, session.FactInstanceRevision);
        Assert.Equal("8", session.CurrentAnswerInput);
        AssertLearnerStateEquivalent(durableBefore, await store.LoadSnapshotAsync());

        await InvokeConfigurationReconciliationAsync(session);

        Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);
        Assert.True(session.FactInstanceRevision > originalRevision);
        Assert.Equal(string.Empty, session.CurrentAnswerInput);
        AssertLearnerStateEquivalent(durableBefore, await store.LoadSnapshotAsync());
    }

    [Fact]
    public async Task PersistedConfiguration_AfterLiveReconciliation_RestartSelectsEnabledOperationWithoutReset()
    {
        var databasePath = Path.Combine(_testDbDirectory, "restart-after-configuration.db");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);

        using (var store = new SqliteLearnerStore(databasePath))
        {
            var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
            await session.InitializeAsync(startTiming: false);
            Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);

            preferences.SetEnabledOperations([ArithmeticOperation.Subtraction]);
            await InvokeConfigurationReconciliationAsync(session);

            Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);
            Assert.Equal(0, session.Progression.PracticePosition);
            Assert.Empty((await store.LoadSnapshotAsync()).RecentAttempts);
            await store.CloseAsync();
        }

        using var reopenedStore = new SqliteLearnerStore(databasePath);
        var restartedSession = new TrainingSession(
            reopenedStore,
            new FixedClock(),
            preferenceStore: preferences);
        await restartedSession.InitializeAsync(startTiming: false);

        Assert.Equal([ArithmeticOperation.Subtraction], preferences.GetEnabledOperations());
        Assert.Equal(ArithmeticOperation.Subtraction, restartedSession.CurrentFact.Operation);
        Assert.Equal(0, restartedSession.Progression.PracticePosition);
        Assert.Empty((await reopenedStore.LoadSnapshotAsync()).RecentAttempts);
    }

    [Theory]
    [MemberData(nameof(ValidEnabledSubsets))]
    public async Task EveryValidSubset_ReconciliationProducesOrRetainsFactWithinSubset(
        ArithmeticOperation[] enabledOperations)
    {
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);
        using var store = new SqliteLearnerStore(Path.Combine(
            _testDbDirectory,
            $"subset-{string.Join('-', enabledOperations)}.db"));
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);
        var originalFact = session.CurrentFact;
        var originalRevision = session.FactInstanceRevision;
        session.SetCurrentAnswerInput("3");

        preferences.SetEnabledOperations(enabledOperations);
        var result = await session.ReconcilePracticeConfigurationAsync(startTiming: false);

        Assert.Contains(session.CurrentFact.Operation, enabledOperations);
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Empty((await store.LoadSnapshotAsync()).RecentAttempts);
        if (enabledOperations.Contains(ArithmeticOperation.Addition))
        {
            Assert.Equal(PracticeConfigurationReconciliationResult.RetainedCurrentFact, result);
            Assert.Same(originalFact, session.CurrentFact);
            Assert.Equal(originalRevision, session.FactInstanceRevision);
            Assert.Equal("3", session.CurrentAnswerInput);
        }
        else
        {
            Assert.Equal(PracticeConfigurationReconciliationResult.ReplacedCurrentFact, result);
            Assert.NotSame(originalFact, session.CurrentFact);
            Assert.True(session.FactInstanceRevision > originalRevision);
            Assert.Equal(string.Empty, session.CurrentAnswerInput);
        }
    }

    [Fact]
    public async Task EnabledButPresentationIneligibleCurrentFact_IsReplacedWithoutLearnerMutation()
    {
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);
        using var store = new BandOneSnapshotStore();
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        var ineligibleFact = session.CurrentFact;
        var ownership = new AcquisitionOwnershipResolver(
            new ArithmeticCurriculum().GetCurriculum(ArithmeticOperation.Addition));
        Assert.False(ownership.IsEligible(ineligibleFact.Id, throughBandIndex: 0));
        var revisionBefore = session.FactInstanceRevision;
        session.Progression.OperationProgressions[ArithmeticOperation.Addition] =
            new OperationProgression(ArithmeticOperation.Addition, 0, 0);

        var result = await session.ReconcilePracticeConfigurationAsync(startTiming: false);

        Assert.Equal(PracticeConfigurationReconciliationResult.ReplacedCurrentFact, result);
        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
        Assert.True(ownership.IsEligible(session.CurrentFact.Id, throughBandIndex: 0));
        Assert.NotEqual(ineligibleFact.Id, session.CurrentFact.Id);
        Assert.True(session.FactInstanceRevision > revisionBefore);
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Empty(store.Commits);
    }

    [Theory]
    [InlineData(AttemptOutcome.Correct, SessionInteractionState.CorrectFeedback)]
    [InlineData(AttemptOutcome.Incorrect, SessionInteractionState.IncorrectFeedback)]
    [InlineData(AttemptOutcome.Timeout, SessionInteractionState.TimeoutFeedback)]
    public async Task AcceptedFeedback_ConfigurationChangeDefersUntilNextPreparation(
        AttemptOutcome outcome,
        SessionInteractionState expectedState)
    {
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);
        using var store = new SqliteLearnerStore(Path.Combine(
            _testDbDirectory,
            $"accepted-{outcome}.db"));
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        var acceptedFact = session.CurrentFact;
        var acceptedRevision = session.FactInstanceRevision;
        if (outcome == AttemptOutcome.Timeout)
        {
            session.RecordTimeout();
        }
        else
        {
            session.SubmitAnswer(
                outcome == AttemptOutcome.Correct
                    ? acceptedFact.CorrectResult
                    : acceptedFact.CorrectResult + 1);
        }

        var persistence = await session.CommitCurrentEvaluationAsync();
        Assert.True(persistence.IsSuccess);
        Assert.Equal(expectedState, session.InteractionState);
        var acceptedEvaluation = session.LastEvaluation;
        var durableAfterAcceptance = await store.LoadSnapshotAsync();
        preferences.SetEnabledOperations([ArithmeticOperation.Subtraction]);

        var result = await session.ReconcilePracticeConfigurationAsync(startTiming: false);

        Assert.Equal(PracticeConfigurationReconciliationResult.DeferredUntilNextPreparation, result);
        Assert.Same(acceptedFact, session.CurrentFact);
        Assert.Equal(acceptedRevision, session.FactInstanceRevision);
        Assert.Same(acceptedEvaluation, session.LastEvaluation);
        Assert.Equal(expectedState, session.InteractionState);
        AssertLearnerStateEquivalent(durableAfterAcceptance, await store.LoadSnapshotAsync());

        var advanced = outcome == AttemptOutcome.Correct
            ? await session.AdvanceAfterCorrectAnswerAsync(startTiming: false)
            : await session.AcknowledgeFeedbackAsync(startTiming: false);

        Assert.True(advanced);
        Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);
        Assert.Equal(1, session.Progression.PracticePosition);
        Assert.Single((await store.LoadSnapshotAsync()).RecentAttempts);
    }

    [Fact]
    public async Task Replacement_LoadsOnlyFinalScheduledOperationEvidenceAtSameProspectivePosition()
    {
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);
        using var store = new FaultInjectingLearnerStore(
            new SqliteLearnerStore(Path.Combine(_testDbDirectory, "evidence-operation.db")));
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);
        store.SelectionEvidenceRequests.Clear();

        preferences.SetEnabledOperations([ArithmeticOperation.Division]);
        var result = await session.ReconcilePracticeConfigurationAsync(startTiming: false);

        Assert.Equal(PracticeConfigurationReconciliationResult.ReplacedCurrentFact, result);
        var request = Assert.Single(store.SelectionEvidenceRequests);
        Assert.Equal(ArithmeticOperation.Division, request.Operation);
        Assert.Equal(1, request.ProspectivePracticePosition);
        Assert.Equal(ArithmeticOperation.Division, session.CurrentFact.Operation);
        Assert.Equal(0, session.Progression.PracticePosition);
    }

    [Fact]
    public async Task PausedRetainedFact_PreservesElapsedTimeRevisionAndInput()
    {
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);
        var clock = new ManualClock();
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDirectory, "timer-retain.db"));
        var session = new TrainingSession(store, clock, preferenceStore: preferences);
        await session.InitializeAsync(startTiming: true);
        clock.AdvanceMilliseconds(1_200);
        session.PauseItemTiming();
        var elapsedBeforeSettings = session.GetCurrentActiveElapsedMs();
        var retainedFact = session.CurrentFact;
        var revisionBefore = session.FactInstanceRevision;
        session.SetCurrentAnswerInput("6");

        preferences.SetEnabledOperations([
            ArithmeticOperation.Addition,
            ArithmeticOperation.Subtraction
        ]);
        var result = await session.ReconcilePracticeConfigurationAsync(startTiming: false);
        clock.AdvanceMilliseconds(10_000);

        Assert.Equal(PracticeConfigurationReconciliationResult.RetainedCurrentFact, result);
        Assert.Same(retainedFact, session.CurrentFact);
        Assert.Equal(revisionBefore, session.FactInstanceRevision);
        Assert.Equal("6", session.CurrentAnswerInput);
        Assert.Equal(elapsedBeforeSettings, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsTimingActive);
    }

    [Fact]
    public async Task PausedReplacement_StartsFreshIntervalOnlyAfterPracticeResumes()
    {
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);
        var clock = new ManualClock();
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDirectory, "timer-replace.db"));
        var session = new TrainingSession(store, clock, preferenceStore: preferences);
        await session.InitializeAsync(startTiming: true);
        clock.AdvanceMilliseconds(1_200);
        session.PauseItemTiming();

        preferences.SetEnabledOperations([ArithmeticOperation.Subtraction]);
        var result = await session.ReconcilePracticeConfigurationAsync(startTiming: false);
        clock.AdvanceMilliseconds(10_000);

        Assert.Equal(PracticeConfigurationReconciliationResult.ReplacedCurrentFact, result);
        Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsTimingActive);

        session.ResumeItemTiming();
        clock.AdvanceMilliseconds(750);
        Assert.True(session.IsTimingActive);
        Assert.Equal(750, session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public void SettingsOperationChange_AwaitsSingleFlightReconciliationAndOffersRecovery()
    {
        var settingsSource = File.ReadAllText(GetRepositoryPath(
            "src",
            "MathFirst.App",
            "Components",
            "Pages",
            "Settings.razor"));

        Assert.Contains("@onclick=\"() => ToggleOperationAsync(operation)\"", settingsSource, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(isLastActive || _operationChangeInProgress)\"", settingsSource, StringComparison.Ordinal);
        Assert.Contains("private async Task ToggleOperationAsync", settingsSource, StringComparison.Ordinal);
        Assert.Contains("await Session.ReconcilePracticeConfigurationAsync(startTiming: false)", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = Session.EnsureScheduledEvidenceAsync()", settingsSource, StringComparison.Ordinal);
        Assert.Contains("RetryPracticeConfigurationReconciliationAsync", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Settings_PracticeConfigurationFailed", settingsSource, StringComparison.Ordinal);
        Assert.Contains("_practiceConfigurationReconciliationFailed", settingsSource, StringComparison.Ordinal);
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

    private static async Task InvokeConfigurationReconciliationAsync(TrainingSession session)
    {
        var reconciliation = typeof(TrainingSession).GetMethod(
            "ReconcilePracticeConfigurationAsync",
            BindingFlags.Instance | BindingFlags.Public);

        if (reconciliation is null)
        {
            // Exercise the pre-slice Settings behavior for the genuine RED baseline.
            await session.EnsureScheduledEvidenceAsync();
            return;
        }

        var invocation = reconciliation.Invoke(session, [false, CancellationToken.None]);
        Assert.NotNull(invocation);
        await (Task)invocation;
    }

    private static void AssertLearnerStateEquivalent(LearnerSnapshot expected, LearnerSnapshot actual)
    {
        Assert.Equal(expected.Progression.PracticePosition, actual.Progression.PracticePosition);
        Assert.Equal(expected.Progression.StoreRevision, actual.Progression.StoreRevision);
        Assert.Equal(expected.Progression.SchemaVersion, actual.Progression.SchemaVersion);
        Assert.Equal(expected.Progression.UpdatedAt, actual.Progression.UpdatedAt);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.LatestAcceptedPracticeAt, actual.LatestAcceptedPracticeAt);
        Assert.Equal(expected.RecentAttempts, actual.RecentAttempts);
        Assert.NotNull(expected.OperationAcceptedAttemptCounts);
        Assert.NotNull(actual.OperationAcceptedAttemptCounts);
        Assert.Equal(expected.OperationAcceptedAttemptCounts, actual.OperationAcceptedAttemptCounts);
        Assert.NotNull(expected.OperationProgressions);
        Assert.NotNull(actual.OperationProgressions);

        foreach (var operation in PracticeOperationPreferencePolicy.AllOperations)
        {
            Assert.Equal(
                expected.OperationProgressions[operation],
                actual.OperationProgressions[operation]);
        }

        Assert.Equal(expected.ItemStates.Keys.Order(StringComparer.Ordinal), actual.ItemStates.Keys.Order(StringComparer.Ordinal));
        foreach (var factId in expected.ItemStates.Keys)
        {
            AssertItemStateEqual(expected.ItemStates[factId], actual.ItemStates[factId]);
        }

        Assert.Equal(
            expected.FsrsStates.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.FsrsStates.OrderBy(pair => pair.Key, StringComparer.Ordinal));
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

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private sealed class FixedClock : IClock
    {
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(900);
    }

    private sealed class ManualClock : IClock
    {
        private long _timestamp;
        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(_timestamp - startTimestamp);
        public void AdvanceMilliseconds(long milliseconds) => _timestamp += milliseconds;
    }

    private sealed class FaultInjectingLearnerStore(ILearnerStore inner) : ILearnerStore
    {
        public bool FailNextSelectionEvidenceLoad { get; set; }
        public List<PracticeSelectionEvidenceRequest> SelectionEvidenceRequests { get; } = [];
        public string StoragePath => inner.StoragePath;
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            inner.InitializeAsync(cancellationToken);
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            inner.LoadSnapshotAsync(cancellationToken);
        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken cancellationToken = default) =>
            inner.LoadRuntimeSnapshotAsync(cancellationToken);
        public Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(
            PracticeSelectionEvidenceRequest request,
            CancellationToken cancellationToken = default)
        {
            SelectionEvidenceRequests.Add(request);
            if (FailNextSelectionEvidenceLoad)
            {
                FailNextSelectionEvidenceLoad = false;
                throw new InvalidOperationException("Injected selection evidence failure.");
            }

            return inner.LoadPracticeSelectionEvidenceAsync(request, cancellationToken);
        }
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            inner.LoadLatestFrontierAttemptsAsync(
                operation,
                bandStartedPracticePosition,
                frontierFactIds,
                cancellationToken);
        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default) =>
            inner.CommitSubmissionAsync(changeSet, cancellationToken);
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) =>
            inner.ResetLearningProgressAsync(cancellationToken);
        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            inner.CloseAsync(cancellationToken);
        public void Dispose() => inner.Dispose();
    }

    private sealed class BandOneSnapshotStore : ILearnerStore
    {
        private readonly LearnerProgression _progression;

        public BandOneSnapshotStore()
        {
            _progression = LearnerProgression.CreateFresh();
            _progression.OperationProgressions[ArithmeticOperation.Addition] =
                new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        }

        public List<SubmissionChangeSet> Commits { get; } = [];
        public string StoragePath => "inmemory://band-one-snapshot";
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LearnerSnapshot(
                _progression,
                new Dictionary<string, ItemLearningState>(StringComparer.Ordinal),
                new Dictionary<string, FsrsCardState>(StringComparer.Ordinal),
                [],
                revision: 1,
                LearnerProgression.DefaultSchemaVersion,
                _progression.OperationProgressions));
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
            Commits.Add(changeSet);
            return Task.FromResult(PersistenceResult.Success(changeSet.ExpectedRevision + 1));
        }
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
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
        public bool GetHapticFeedbackEnabled() => true;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) =>
            _operationPreferences.GetValueOrDefault(operation, true);
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) =>
            _operationPreferences[operation] = enabled;
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() =>
            PracticeOperationPreferencePolicy.NormalizeEnabledOperations(
                PracticeOperationPreferencePolicy.AllOperations.Where(GetOperationEnabled));
        public void SetEnabledOperations(IEnumerable<ArithmeticOperation> operations)
        {
            var enabled = operations.ToHashSet();
            foreach (var operation in PracticeOperationPreferencePolicy.AllOperations)
            {
                SetOperationEnabled(operation, enabled.Contains(operation));
            }
        }
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public void ResetPracticePreferences() => _operationPreferences.Clear();
        public void ResetAllPreferences() => _operationPreferences.Clear();
    }
}
