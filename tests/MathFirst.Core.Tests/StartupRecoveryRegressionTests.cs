namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Copy;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class StartupRecoveryRegressionTests
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

        public bool GetOnboardingCompleted() => false;
        public void SetOnboardingCompleted(bool completed) { }
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
        public List<PracticeSelectionEvidenceRequest> RecordedRequests { get; } = [];
        public List<SubmissionChangeSet> RecordedCommits { get; } = [];

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
            RecordedRequests.Add(request);
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
            CancellationToken cancellationToken = default)
        {
            RecordedCommits.Add(changeSet);
            return Task.FromResult(PersistenceResult.Success(changeSet.ExpectedRevision + 1));
        }

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default)
        {
            ResetProgressCallCount++;
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private static LearnerSnapshot CreateTestSnapshot(
        int mulBandIndex = 1,
        long practicePosition = 10)
    {
        var operationProgressions = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(
                op => op,
                op => new OperationProgression(
                    op,
                    op == ArithmeticOperation.Multiplication ? mulBandIndex : 0,
                    0));

        var progression = new LearnerProgression
        {
            PracticePosition = practicePosition,
            OperationProgressions = operationProgressions,
            StoreRevision = 1,
            SchemaVersion = 6,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var eligibleFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2);
        var eligibleItem = ItemLearningState.CreateNew(eligibleFact);
        eligibleItem.CorrectAttempts = 3;
        eligibleItem.TotalAttempts = 3;
        var eligibleFsrs = new FsrsCardState(
            eligibleFact.Id,
            Guid.NewGuid(),
            2,
            null,
            10.0,
            5.0,
            practicePosition,
            practicePosition - 1,
            FsrsRating.Good);

        var futureFact = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 8);
        var futureItem = ItemLearningState.CreateNew(futureFact);
        var futureFsrs = new FsrsCardState(
            futureFact.Id,
            Guid.NewGuid(),
            2,
            null,
            30.0,
            5.0,
            1,
            1,
            FsrsRating.Good);

        var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [eligibleFact.Id] = eligibleItem,
            [futureFact.Id] = futureItem
        };

        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [eligibleFact.Id] = eligibleFsrs,
            [futureFact.Id] = futureFsrs
        };

        return new LearnerSnapshot(
            progression,
            itemStates,
            fsrsStates,
            [],
            1,
            6,
            latestAcceptedPracticeAt: DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task InitializationEvidenceFailure_DoesNotRequireCurrentFactToRenderRecovery()
    {
        var snapshot = CreateTestSnapshot();
        var store = new TransientEvidenceLearnerStore(snapshot)
        {
            FailOnEvidenceAttemptsCount = 10
        };
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => session.InitializeAsync());
        Assert.Contains("Synthetic transient evidence load failure", exception.Message, StringComparison.Ordinal);

        // TrainingSession Invariants upon failed initialization:
        Assert.False(session.IsInitialized);
        Assert.Null(session.LastEvaluation);
        Assert.Null(session.LastPersistenceResult);

        // Home.razor source/UI contract verification:
        // Must have a dedicated startup failure overlay that does NOT access Session.CurrentFact,
        // Session.LastEvaluation, or Session.LastPersistenceResult, and renders the retry action.
        var home = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        Assert.Contains("startup-error-dialog", home, StringComparison.Ordinal);
        Assert.Contains("Training_EvidencePreparationFailureTitle", home, StringComparison.Ordinal);
        Assert.Contains("Training_Retry", home, StringComparison.Ordinal);
        Assert.Contains("RetryInitializationAsync", home, StringComparison.Ordinal);
        Assert.Contains("_startupFailed", home, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializationEvidenceFailure_RetryReinitializesSessionWithoutLearnerDataReset()
    {
        var snapshot = CreateTestSnapshot(mulBandIndex: 2, practicePosition: 42);
        var store = new TransientEvidenceLearnerStore(snapshot)
        {
            FailOnEvidenceAttemptsCount = 1
        };
        var clock = new FakeClock();
        var session = new TrainingSession(
            store,
            clock,
            preferenceStore: InMemoryPreferenceStore.WithEnabled([ArithmeticOperation.Multiplication]));

        // First initialization attempt fails due to transient evidence loading error.
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InitializeAsync(startTiming: false));

        Assert.False(session.IsInitialized);
        Assert.Equal(0, store.ResetProgressCallCount);

        // Retry initialization.
        await session.InitializeAsync(startTiming: false);

        Assert.True(session.IsInitialized);
        Assert.NotNull(session.CurrentFact);
        Assert.Equal(42, session.Progression.PracticePosition);
        Assert.Equal(2, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
        Assert.Equal(0, store.ResetProgressCallCount);
        Assert.True(session.HasCompletedPracticeHistory);
    }

    [Fact]
    public async Task InitializationFailure_DoesNotUseSubmissionRecoveryPreconditionsUnlessContractExplicitlySupportsIt()
    {
        var snapshot = CreateTestSnapshot();
        var store = new TransientEvidenceLearnerStore(snapshot)
        {
            FailOnEvidenceAttemptsCount = 1
        };
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InitializeAsync());

        // Startup failure must not synthesize submission evaluation, persistence result, or set state to PersistenceFailure.
        Assert.Null(session.LastEvaluation);
        Assert.Null(session.LastPersistenceResult);
        Assert.NotEqual(SessionInteractionState.PersistenceFailure, session.InteractionState);

        // Calling submission recovery directly must fail gracefully (return false) without throwing.
        var recoveryResult = await session.RecoverFromPersistenceFailureAsync();
        Assert.False(recoveryResult);
    }

    [Fact]
    public async Task StartupRecovery_AfterTransientEvidenceFailure_ProducesValidCurrentFact()
    {
        var snapshot = CreateTestSnapshot(mulBandIndex: 1, practicePosition: 15);
        var store = new TransientEvidenceLearnerStore(snapshot)
        {
            FailOnEvidenceAttemptsCount = 1
        };
        var clock = new FakeClock();
        var session = new TrainingSession(
            store,
            clock,
            preferenceStore: InMemoryPreferenceStore.WithEnabled([ArithmeticOperation.Multiplication]));

        // Initial attempt throws.
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InitializeAsync(startTiming: false));
        Assert.False(session.IsInitialized);

        // Retry succeeds.
        await session.InitializeAsync(startTiming: false);

        Assert.True(session.IsInitialized);
        Assert.NotNull(session.CurrentFact);
        Assert.False(string.IsNullOrWhiteSpace(session.CurrentFact.Id));
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        // Selected fact must be eligible under the current band.
        var curriculum = new ArithmeticCurriculum().GetCurriculum(session.CurrentFact.Operation);
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var currentBandIndex = session.Progression.OperationProgressions[session.CurrentFact.Operation].BandIndex;
        Assert.True(
            ownership.IsEligible(session.CurrentFact.Id, currentBandIndex),
            $"CurrentFact {session.CurrentFact.Id} is not eligible at band {currentBandIndex}.");
    }

    [Fact]
    public async Task Restart_PreservesLearnerStateAndMaintainsEligibility()
    {
        var snapshot = CreateTestSnapshot(mulBandIndex: 1, practicePosition: 25);
        var store = new TransientEvidenceLearnerStore(snapshot)
        {
            FailOnEvidenceAttemptsCount = 0
        };
        var clock = new FakeClock();

        // Simulate fresh session startup after app restart.
        var session = new TrainingSession(
            store,
            clock,
            preferenceStore: InMemoryPreferenceStore.WithEnabled([ArithmeticOperation.Multiplication]));
        await session.InitializeAsync(startTiming: false);

        Assert.True(session.IsInitialized);
        Assert.Equal(25, session.Progression.PracticePosition);
        Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
        Assert.True(session.HasCompletedPracticeHistory);

        // Future fact mul:2*8 (owner=7) must NOT be selected as CurrentFact at BandIndex 1.
        Assert.NotEqual("mul:2*8", session.CurrentFact.Id);
        var curriculum = new ArithmeticCurriculum().GetCurriculum(session.CurrentFact.Operation);
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var currentBandIndex = session.Progression.OperationProgressions[session.CurrentFact.Operation].BandIndex;
        Assert.True(ownership.IsEligible(session.CurrentFact.Id, currentBandIndex));
    }

    [Fact]
    public async Task TrainingSession_EvidenceRequest_UsesCurrentOperationBandIndex()
    {
        var snapshot = CreateTestSnapshot(mulBandIndex: 3, practicePosition: 30);
        var store = new TransientEvidenceLearnerStore(snapshot)
        {
            FailOnEvidenceAttemptsCount = 0
        };
        var clock = new FakeClock();
        var session = new TrainingSession(
            store,
            clock,
            preferenceStore: InMemoryPreferenceStore.WithEnabled([ArithmeticOperation.Multiplication]));

        await session.InitializeAsync(startTiming: false);

        Assert.NotEmpty(store.RecordedRequests);

        // Verify that evidence requests pass the real BandIndex instead of int.MaxValue.
        var mulRequest = store.RecordedRequests.FirstOrDefault(r => r.Operation == ArithmeticOperation.Multiplication);
        Assert.NotNull(mulRequest);
        Assert.Equal(3, mulRequest.CurrentBandIndex);
        Assert.NotEqual(int.MaxValue, mulRequest.CurrentBandIndex);

        foreach (var req in store.RecordedRequests)
        {
            Assert.NotEqual(int.MaxValue, req.CurrentBandIndex);
            var expectedBand = session.Progression.OperationProgressions[req.Operation].BandIndex;
            Assert.Equal(expectedBand, req.CurrentBandIndex);
        }
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "MathFirst.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine([current!.FullName, .. segments]);
    }
}
