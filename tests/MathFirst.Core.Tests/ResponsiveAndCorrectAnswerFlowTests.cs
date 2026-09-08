namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using Xunit;

public sealed class ResponsiveAndCorrectAnswerFlowTests
{
    private sealed class FakeClock : IClock
    {
        private long _timestamp = 1_000_000;
        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, _timestamp - startTimestamp));
        public void AdvanceMs(long milliseconds) => _timestamp += milliseconds;
    }

    private sealed class RecordingStore : ILearnerStore
    {
        public string StoragePath => "inmemory://practice-interaction";
        public int CommitCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LearnerSnapshot(
                LearnerProgression.CreateFresh(),
                new Dictionary<string, ItemLearningState>(),
                new List<AttemptRecord>(),
                1,
                LearnerProgression.DefaultSchemaVersion));
        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.FromResult(PersistenceResult.Success(changeSet.ExpectedRevision + 1));
        }
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    [Theory]
    [InlineData("2", 2, true)]
    [InlineData("1", 12, false)]
    [InlineData("12", 12, true)]
    [InlineData("13", 12, true)]
    [InlineData("8", 12, true)]
    [InlineData("14.0", 14, true)]
    [InlineData("14,0", 14, true)]
    [InlineData("1.", 12, false)]
    [InlineData("", 12, false)]
    public void AutoSubmissionPolicy_IsDeterministicAndMultiDigitSafe(
        string input,
        int correctAnswer,
        bool expected)
    {
        Assert.Equal(expected, AnswerAutoSubmissionPolicy.ShouldSubmit(input, correctAnswer));
    }

    [Fact]
    public async Task SingleDigitCorrectAnswer_OneDigitPersistsOnceAndAdvances()
    {
        var (session, clock, store) = await CreateSession();
        var fact = session.CurrentFact;
        Assert.InRange(fact.CorrectResult, 0, 9);
        var digit = fact.CorrectResult.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit(digit, fact.CorrectResult));

        clock.AdvanceMs(750);
        var evaluation = session.SubmitAnswer(fact.CorrectResult);
        var persistence = await session.CommitCurrentEvaluationAsync();
        var advanced = persistence.IsSuccess && session.AdvanceAfterCorrectAnswer();

        Assert.True(evaluation.IsCorrect);
        Assert.True(advanced);
        Assert.Equal(1, store.CommitCount);
        Assert.Equal(1, session.SessionCorrectCount);
        Assert.Equal(1, session.SessionTotalCount);
        Assert.Equal(1, session.Progression.PracticePosition);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.Null(session.LastEvaluation);
        Assert.True(session.IsTimingActive);
    }

    [Fact]
    public void MultiDigitPrefix_CreatesNoSemanticAttempt()
    {
        var session = CreateInitializedSessionSynchronously();
        var position = session.Progression.PracticePosition;

        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit("1", 12));
        Assert.Equal(position, session.Progression.PracticePosition);
        Assert.Equal(0, session.SessionTotalCount);
        Assert.Null(session.LastEvaluation);
    }

    [Fact]
    public async Task IncorrectAnswer_PersistsOnceAndRequiresAcknowledgement()
    {
        var (session, clock, store) = await CreateSession();
        var fact = session.CurrentFact;
        clock.AdvanceMs(900);

        var evaluation = session.SubmitAnswer(fact.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();

        Assert.Equal(AttemptOutcome.Incorrect, evaluation.Outcome);
        Assert.Equal(SessionInteractionState.IncorrectFeedback, session.InteractionState);
        Assert.Equal(fact, session.CurrentFact);
        Assert.Equal(fact.CorrectResult + 1, session.LastEvaluation!.SubmittedNumericAnswer);
        Assert.Equal(fact.CorrectResult, session.LastEvaluation!.CorrectAnswer);
        Assert.False(session.IsTimingActive);
        Assert.Equal(1, store.CommitCount);
        Assert.Equal(1, session.Progression.PracticePosition);

        session.AdvanceToNextFact();
        Assert.Equal(1, store.CommitCount);
        Assert.Equal(1, session.Progression.PracticePosition);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
    }

    [Fact]
    public async Task Timeout_PersistsOnceAndRequiresAcknowledgement()
    {
        var (session, clock, store) = await CreateSession();
        var fact = session.CurrentFact;
        clock.AdvanceMs(session.CurrentFactDeadlineMs);

        var evaluation = session.RecordTimeout();
        await session.CommitCurrentEvaluationAsync();

        Assert.Equal(AttemptOutcome.Timeout, evaluation.Outcome);
        Assert.Equal(SessionInteractionState.TimeoutFeedback, session.InteractionState);
        Assert.Equal(fact, session.CurrentFact);
        Assert.Null(session.LastEvaluation!.SubmittedNumericAnswer);
        Assert.Equal(fact.CorrectResult, session.LastEvaluation!.CorrectAnswer);
        Assert.False(session.IsTimingActive);
        Assert.Equal(1, store.CommitCount);

        session.AdvanceToNextFact();
        Assert.Equal(1, store.CommitCount);
        Assert.Equal(1, session.Progression.PracticePosition);
    }

    [Fact]
    public async Task ColdPracticeReadyGate_BlocksTimingUntilExplicitStart()
    {
        var clock = new FakeClock();
        var store = new RecordingStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync(startTiming: false);
        session.ShowInitialReadyGate();
        session.SetPracticeSurfaceActive(true);
        var fact = session.CurrentFact.Id;

        clock.AdvanceMs(120_000);

        Assert.Equal(PracticeGateState.InitialReadyGate, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Equal(0, store.CommitCount);
        Assert.Equal(fact, session.CurrentFact.Id);

        session.StartOrResumePractice();
        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.True(session.IsTimingActive);
    }

    [Fact]
    public async Task ManualPause_FreezesSameFactAndIsIdempotent()
    {
        var (session, clock, store) = await CreateSession();
        var fact = session.CurrentFact.Id;
        clock.AdvanceMs(4_200);
        var frozenElapsed = session.GetCurrentActiveElapsedMs();

        session.PausePractice();
        session.PausePractice();
        clock.AdvanceMs(120_000);

        Assert.Equal(PracticeGateState.ManualPause, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.Equal(frozenElapsed, session.GetCurrentActiveElapsedMs());
        Assert.Equal(fact, session.CurrentFact.Id);
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Equal(0, store.CommitCount);

        session.StartOrResumePractice();
        session.StartOrResumePractice();
        Assert.True(session.IsTimingActive);
        clock.AdvanceMs(800);
        Assert.Equal(frozenElapsed + 800, session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public async Task BackgroundReturn_RequiresExplicitResumeWithSameRemainingTime()
    {
        var (session, clock, _) = await CreateSession();
        clock.AdvanceMs(5_000);
        var frozenElapsed = session.GetCurrentActiveElapsedMs();

        session.SetAppForeground(false);
        clock.AdvanceMs(120_000);
        session.SetAppForeground(true);

        Assert.Equal(PracticeGateState.BackgroundResumeGate, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.Equal(frozenElapsed, session.GetCurrentActiveElapsedMs());

        session.StartOrResumePractice();
        Assert.True(session.IsTimingActive);
        Assert.Equal(frozenElapsed, session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public async Task BackgroundWhileSettings_DefersResumeGateUntilPracticeReturns()
    {
        var (session, clock, _) = await CreateSession();
        clock.AdvanceMs(2_000);
        session.SetPracticeSurfaceActive(false);
        session.SetAppForeground(false);
        clock.AdvanceMs(60_000);
        session.SetAppForeground(true);

        Assert.False(session.IsPracticeSurfaceActive);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, session.PracticeGate);
        Assert.False(session.IsTimingActive);

        session.SetPracticeSurfaceActive(true);
        Assert.False(session.IsTimingActive);
        session.StartOrResumePractice();
        Assert.True(session.IsTimingActive);
        Assert.Equal(2_000, session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public void UiContracts_RemovePermanentSubmitAndHidePracticeBehindDialogs()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));
        var styles = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css"));
        var onboarding = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Onboarding", "OnboardingHost.razor"));
        var settings = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Settings.razor"));

        Assert.Contains("class=\"practice-header-actions\"", home, StringComparison.Ordinal);
        Assert.Contains("Training_Pause", home, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", home, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", home, StringComparison.Ordinal);
        Assert.Contains("Session.PracticeGate != PracticeGateState.Running", home, StringComparison.Ordinal);
        Assert.Contains("else if (IsAwaitingAcknowledgement", home, StringComparison.Ordinal);
        Assert.Contains("class=\"feedback-expression\"", home, StringComparison.Ordinal);
        Assert.Contains("@Session.CurrentFact.LeftOperand", home, StringComparison.Ordinal);
        Assert.Contains("@Session.CurrentFact.DisplaySymbol", home, StringComparison.Ordinal);
        Assert.Contains("@Session.CurrentFact.RightOperand", home, StringComparison.Ordinal);
        Assert.Contains("Training_YourAnswer", home, StringComparison.Ordinal);
        Assert.Contains("Training_TimeExpired", home, StringComparison.Ordinal);
        Assert.Contains("Training_CorrectAnswer", home, StringComparison.Ordinal);
        Assert.Contains("Common_Continue", home, StringComparison.Ordinal);
        Assert.Contains("inputmode=\"@AnswerInputMode\"", home, StringComparison.Ordinal);
        Assert.Contains("private static string AnswerInputMode => \"none\";", home, StringComparison.Ordinal);
        Assert.DoesNotContain("@onclick=\"Submit\"", home, StringComparison.Ordinal);
        Assert.Contains("(orientation: landscape) and (max-height:", styles, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 42fr) minmax(16rem, 58fr);", styles, StringComparison.Ordinal);
        Assert.Contains(".practice-overlay", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("settings-correct-answer-title", settings, StringComparison.Ordinal);
        Assert.Contains("InitializeAsync(startTiming: true)", onboarding, StringComparison.Ordinal);
    }

    private static async Task<(TrainingSession Session, FakeClock Clock, RecordingStore Store)> CreateSession()
    {
        var clock = new FakeClock();
        var store = new RecordingStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();
        return (session, clock, store);
    }

    private static TrainingSession CreateInitializedSessionSynchronously()
    {
        var session = new TrainingSession(new RecordingStore(), new FakeClock());
        session.InitializeAsync().GetAwaiter().GetResult();
        return session;
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = GetRepositoryRoot();
        return Path.Combine([root, .. segments]);
    }

    private static string GetRepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
