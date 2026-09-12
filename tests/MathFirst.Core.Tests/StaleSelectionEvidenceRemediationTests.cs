namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class StaleSelectionEvidenceRemediationTests : IDisposable
{
    private readonly string _testDbDirectory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstStaleEvidence_" + Guid.NewGuid().ToString("N"));

    public StaleSelectionEvidenceRemediationTests()
    {
        Directory.CreateDirectory(_testDbDirectory);
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
        }
    }

    private string GetDatabasePath() => Path.Combine(_testDbDirectory, $"test_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task IncorrectFeedback_ConfigurationChange_SelectsNewOperationWithFreshEvidence()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // Attempt 1: Addition (pos 1) -> correct
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Attempt 2: Subtraction (pos 2) -> correct
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Attempt 3: Multiplication (pos 3) -> incorrect
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        session.SubmitAnswer(session.CurrentFact.CorrectResult + 10);
        var persistResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(persistResult.IsSuccess);
        Assert.Equal(SessionInteractionState.IncorrectFeedback, session.InteractionState);

        // At this moment, prospective position 4 prefetched Division evidence!
        // While in IncorrectFeedback, change preference to Addition-only (Structured Band 0).
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);

        // Acknowledge feedback to advance.
        var acknowledged = session.AcknowledgeFeedback(startTiming: false);
        Assert.True(acknowledged);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        // CurrentFact MUST be Addition (the only enabled operation), NOT the prefetched Division fact.
        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
        Assert.NotEqual(SessionInteractionState.PersistenceFailure, session.InteractionState);
    }

    [Fact]
    public async Task TimeoutFeedback_ConfigurationChange_SelectsNewOperationWithFreshEvidence()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // Attempt 1: Addition (pos 1) -> correct
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Attempt 2: Subtraction (pos 2) -> correct
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Attempt 3: Multiplication (pos 3) -> timeout
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        session.RecordTimeout();
        var persistResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(persistResult.IsSuccess);
        Assert.Equal(SessionInteractionState.TimeoutFeedback, session.InteractionState);

        // At this moment, prospective position 4 prefetched Division evidence!
        // While in TimeoutFeedback, change preference to Addition-only (Structured Band 0).
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);

        // Acknowledge feedback to advance.
        var acknowledged = session.AcknowledgeFeedback(startTiming: false);
        Assert.True(acknowledged);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        // CurrentFact MUST be Addition.
        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
        Assert.NotEqual(SessionInteractionState.PersistenceFailure, session.InteractionState);
    }

    [Fact]
    public async Task TeachingIntervention_ConfigurationChange_SelectsNewOperationWithFreshEvidence()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        var factId = session.CurrentFact.Id;
        // Submit incorrect answer 1
        session.SubmitAnswer(session.CurrentFact.CorrectResult + 10);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(SessionInteractionState.IncorrectFeedback, session.InteractionState);
        session.AcknowledgeFeedback(startTiming: false);

        while (session.CurrentFact.Id != factId && session.SessionOrderCounter < 30)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceAfterCorrectAnswer(startTiming: false);
        }

        if (session.CurrentFact.Id == factId)
        {
            // Second error on same fact
            session.SubmitAnswer(session.CurrentFact.CorrectResult + 10);
            await session.CommitCurrentEvaluationAsync();
            Assert.Equal(SessionInteractionState.TeachingIntervention, session.InteractionState);

            // Change preferences to Subtraction-only
            preferences.SetEnabledOperations([ArithmeticOperation.Subtraction]);

            var acknowledged = session.AcknowledgeTeachingIntervention(startTiming: false);
            Assert.True(acknowledged);
            Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
            Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);
        }
    }

    [Fact]
    public async Task SessionCheckIn_ConfigurationChange_ContinuePracticeAndTakeBreak()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // Complete 19 correct attempts
        for (var i = 1; i <= 19; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            var result = await session.CommitCurrentEvaluationAsync();
            Assert.True(result.IsSuccess);
            var advanced = session.AdvanceAfterCorrectAnswer(startTiming: false);
            Assert.True(advanced);
        }

        // 20th attempt triggers CheckIn
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var checkInResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(checkInResult.IsSuccess);
        var advanced20 = session.AdvanceAfterCorrectAnswer(startTiming: false);
        Assert.False(advanced20);
        Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);
        Assert.NotNull(session.PendingCheckIn);

        // Change preferences to Addition-only while in CheckIn
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);

        // Continue practice
        session.ContinuePractice(startTiming: false);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
    }

    [Fact]
    public async Task ConfigurationChangeWithoutFeedback_ActiveQuestionUnchanged_NextQuestionUsesNewConfig()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        var firstFact = session.CurrentFact;
        var firstDeadline = session.CurrentFactDeadlineMs;

        // Change settings while awaiting answer on first fact
        preferences.SetEnabledOperations([ArithmeticOperation.Subtraction]);

        // Active fact and deadline MUST NOT change
        Assert.Equal(firstFact.Id, session.CurrentFact.Id);
        Assert.Equal(firstDeadline, session.CurrentFactDeadlineMs);

        // Submit first fact
        session.SubmitAnswer(firstFact.CorrectResult);
        var persist = await session.CommitCurrentEvaluationAsync();
        Assert.True(persist.IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Next generated fact MUST use Subtraction
        Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);
    }

    [Fact]
    public async Task ReverseTransitions_AllFourToAdditionOnly_AndAdditionOnlyToAllFour_SameLiveSession()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // Part 1: All 4 operations (4 attempts completed, 5th committed)
        for (var i = 0; i < 4; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceAfterCorrectAnswer(startTiming: false);
        }

        // 5th attempt: answer and commit with 4 operations active, then switch preferences BEFORE advancing
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        // Switch to Addition only while on feedback / before advancing to next question
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Next 4 attempts (after the 1st addition above = total 5 addition attempts) must all be Addition
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceAfterCorrectAnswer(startTiming: false);
        }

        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        // Switch back to all four before advancing
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Next 8 attempts should rotate across enabled operations
        var observedOps = new HashSet<ArithmeticOperation>();
        for (var i = 0; i < 8; i++)
        {
            observedOps.Add(session.CurrentFact.Operation);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceAfterCorrectAnswer(startTiming: false);
        }

        Assert.True(observedOps.Count > 1, "Switching back to all 4 operations must practice multiple operations.");
    }

    [Fact]
    public void SelectorPureDefense_MixedOperationBoundedPools_OnlyScheduledOperationSelected()
    {
        var selector = new AdaptivePracticeSelector();
        var curriculum = new ArithmeticCurriculum();
        var progressions = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(op => op, op => new OperationProgression(op, 0, 0));
        var curricula = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(op => op, op => curriculum.GetCurriculum(op));

        // Create candidates for all addition band 0 facts so newPool is empty, plus a subtraction fact
        var f00 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var f01 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var f10 = new ArithmeticFact(ArithmeticOperation.Addition, 1, 0);
        var additionFact = new ArithmeticFact(ArithmeticOperation.Addition, 1, 1);
        var subtractionFact = new ArithmeticFact(ArithmeticOperation.Subtraction, 5, 2);

        var s00 = ItemLearningState.CreateNew(f00);
        var s01 = ItemLearningState.CreateNew(f01);
        var s10 = ItemLearningState.CreateNew(f10);
        var additionItemState = ItemLearningState.CreateNew(additionFact);
        var subtractionItemState = ItemLearningState.CreateNew(subtractionFact);

        var additionFsrs = new FsrsCardState(additionFact.Id, Guid.NewGuid(), 2, null, 5.0, 2.0, 1, 1, FsrsRating.Good);
        var subtractionFsrs = new FsrsCardState(subtractionFact.Id, Guid.NewGuid(), 2, null, 5.0, 2.0, 1, 1, FsrsRating.Good);

        var currentBandCandidates = new[]
        {
            new PracticeSelectionCandidate(f00, s00, null),
            new PracticeSelectionCandidate(f01, s01, null),
            new PracticeSelectionCandidate(f10, s10, null),
            new PracticeSelectionCandidate(additionFact, additionItemState, additionFsrs),
            new PracticeSelectionCandidate(subtractionFact, subtractionItemState, subtractionFsrs)
        };

        var evidence = new PracticeSelectionEvidence(
            currentBandCandidates: currentBandCandidates,
            dueCandidates: [new PracticeSelectionCandidate(subtractionFact, subtractionItemState, subtractionFsrs), new PracticeSelectionCandidate(additionFact, additionItemState, additionFsrs)],
            maintenanceCandidates: [],
            remediationCandidates: [],
            earlyReviewCandidates: []);

        var index = new PracticeCandidateIndex(evidence);
        // Position 2 with Addition only -> attempt ordinal 2 -> requested role Due
        var context = new PracticeSelectionContext(
            prospectivePracticePosition: 2,
            currentSessionOrder: 2,
            operationProgressions: progressions,
            curricula: curricula,
            candidateIndex: index,
            recentAcceptedFactsOldestToNewest: [],
            enabledOperations: [ArithmeticOperation.Addition]);

        var result = selector.SelectTargetFact(context);

        Assert.Equal(additionFact.Id, result.Fact.Id);
        Assert.Equal(ArithmeticOperation.Addition, result.Fact.Operation);
    }

    [Fact]
    public void SelectorPureDefense_OnlyForeignOperationInDuePool_FallsBackWithinScheduledOperation()
    {
        var selector = new AdaptivePracticeSelector();
        var curriculum = new ArithmeticCurriculum();
        var progressions = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(op => op, op => new OperationProgression(op, 0, 0));
        var curricula = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(op => op, op => curriculum.GetCurriculum(op));

        // Subtraction in due pool, addition in current band
        var additionFact = new ArithmeticFact(ArithmeticOperation.Addition, 2, 3);
        var subtractionFact = new ArithmeticFact(ArithmeticOperation.Subtraction, 6, 2);

        var additionItemState = ItemLearningState.CreateNew(additionFact);
        var subtractionItemState = ItemLearningState.CreateNew(subtractionFact);

        var subtractionFsrs = new FsrsCardState(subtractionFact.Id, Guid.NewGuid(), 2, null, 5.0, 2.0, 1, 1, FsrsRating.Good);

        var evidence = new PracticeSelectionEvidence(
            currentBandCandidates: [new PracticeSelectionCandidate(additionFact, additionItemState, null)],
            dueCandidates: [new PracticeSelectionCandidate(subtractionFact, subtractionItemState, subtractionFsrs)],
            maintenanceCandidates: [],
            remediationCandidates: [],
            earlyReviewCandidates: []);

        var index = new PracticeCandidateIndex(evidence);
        // Position 2 with 1 enabled op -> attempt ordinal 2 -> requested role Due
        var context = new PracticeSelectionContext(
            prospectivePracticePosition: 2,
            currentSessionOrder: 2,
            operationProgressions: progressions,
            curricula: curricula,
            candidateIndex: index,
            recentAcceptedFactsOldestToNewest: [],
            enabledOperations: [ArithmeticOperation.Addition]);

        var result = selector.SelectTargetFact(context);

        Assert.Equal(ArithmeticOperation.Addition, result.Fact.Operation);
        Assert.NotEqual(subtractionFact.Id, result.Fact.Id);
    }

    [Fact]
    public void SelectorPureDefense_AllMaterializedPoolsContainOnlyForeignOperations_FallsBackToNewFact()
    {
        var selector = new AdaptivePracticeSelector();
        var curriculum = new ArithmeticCurriculum();
        var progressions = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(op => op, op => new OperationProgression(op, 0, 0));
        var curricula = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(op => op, op => curriculum.GetCurriculum(op));

        var subtractionFact = new ArithmeticFact(ArithmeticOperation.Subtraction, 6, 2);
        var subtractionItemState = ItemLearningState.CreateNew(subtractionFact);
        var subtractionFsrs = new FsrsCardState(subtractionFact.Id, Guid.NewGuid(), 2, null, 5.0, 2.0, 1, 1, FsrsRating.Good);

        // ONLY subtraction facts in evidence
        var evidence = new PracticeSelectionEvidence(
            currentBandCandidates: [new PracticeSelectionCandidate(subtractionFact, subtractionItemState, subtractionFsrs)],
            dueCandidates: [new PracticeSelectionCandidate(subtractionFact, subtractionItemState, subtractionFsrs)],
            maintenanceCandidates: [new PracticeSelectionCandidate(subtractionFact, subtractionItemState, subtractionFsrs)],
            remediationCandidates: [],
            earlyReviewCandidates: []);

        var index = new PracticeCandidateIndex(evidence);
        // Position 2 with Addition only -> requested role Due
        var context = new PracticeSelectionContext(
            prospectivePracticePosition: 2,
            currentSessionOrder: 2,
            operationProgressions: progressions,
            curricula: curricula,
            candidateIndex: index,
            recentAcceptedFactsOldestToNewest: [],
            enabledOperations: [ArithmeticOperation.Addition]);

        var result = selector.SelectTargetFact(context);

        Assert.Equal(ArithmeticOperation.Addition, result.Fact.Operation);
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
        public void SetEnabledOperations(IEnumerable<ArithmeticOperation> operations)
        {
            _operationPreferences.Clear();
            foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
            {
                _operationPreferences[op] = false;
            }
            foreach (var op in operations)
            {
                _operationPreferences[op] = true;
            }
        }
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public void ResetPracticePreferences() => _operationPreferences.Clear();
        public void ResetAllPreferences() => _operationPreferences.Clear();
    }
}
