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
    [InlineData("8", 12, false)]
    [InlineData("1", 36, false)]
    [InlineData("3", 36, false)]
    [InlineData("36", 36, true)]
    [InlineData("12", 36, true)]
    [InlineData("1", 144, false)]
    [InlineData("14", 144, false)]
    [InlineData("144", 144, true)]
    [InlineData("98", 980, false)]
    [InlineData("980", 980, true)]
    [InlineData("14.0", 14, true)]
    [InlineData("14,0", 14, true)]
    [InlineData("1.", 12, false)]
    [InlineData("", 12, false)]
    [InlineData("1", 6, true)]
    [InlineData("6", 6, true)]
    [InlineData("0", 0, true)]
    [InlineData("1", 0, true)]
    public void AutoSubmissionPolicy_IsDeterministicAndMultiDigitSafe(
        string input,
        int correctAnswer,
        bool expected)
    {
        Assert.Equal(expected, AnswerAutoSubmissionPolicy.ShouldSubmit(input, correctAnswer));
    }

    [Fact]
    public void TwoDigitAnswer_WrongFirstDigit_RemainsPending_CanBeDeleted_AndCorrected()
    {
        var buffer = string.Empty;
        const int correctAnswer = 36;

        // A. Press '1'
        buffer = NumericAnswerInputPolicy.Append(buffer, "1");
        Assert.Equal("1", buffer);
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));

        // B. Backspace
        buffer = NumericAnswerInputPolicy.Backspace(buffer);
        Assert.Equal(string.Empty, buffer);
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));

        // C. Enter 3, then 6
        buffer = NumericAnswerInputPolicy.Append(buffer, "3");
        Assert.Equal("3", buffer);
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));

        buffer = NumericAnswerInputPolicy.Append(buffer, "6");
        Assert.Equal("36", buffer);
        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));
        Assert.True(NumericAnswerInputPolicy.TryParseSubmission(buffer, out var parsed));
        Assert.Equal(36m, parsed);
    }

    [Fact]
    public void TwoDigitAnswer_TwoWrongDigits_SubmitsIncorrect()
    {
        var buffer = string.Empty;
        const int correctAnswer = 36;

        // Press '1'
        buffer = NumericAnswerInputPolicy.Append(buffer, "1");
        Assert.Equal("1", buffer);
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));

        // Press '2'
        buffer = NumericAnswerInputPolicy.Append(buffer, "2");
        Assert.Equal("12", buffer);
        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));
        Assert.True(NumericAnswerInputPolicy.TryParseSubmission(buffer, out var parsed));
        Assert.Equal(12m, parsed);
    }

    [Fact]
    public void ThreeDigitAnswer_RemainsEditableThroughFirstTwoDigits()
    {
        var buffer = string.Empty;
        const int correctAnswer = 144;

        buffer = NumericAnswerInputPolicy.Append(buffer, "1");
        Assert.Equal("1", buffer);
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));

        buffer = NumericAnswerInputPolicy.Append(buffer, "4");
        Assert.Equal("14", buffer);
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));

        buffer = NumericAnswerInputPolicy.Backspace(buffer);
        Assert.Equal("1", buffer);
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));

        buffer = NumericAnswerInputPolicy.Append(buffer, "4");
        Assert.Equal("14", buffer);
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));

        buffer = NumericAnswerInputPolicy.Append(buffer, "4");
        Assert.Equal("144", buffer);
        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));
    }

    [Fact]
    public void SingleDigitAnswer_PreservesImmediateSubmit_ForCorrectAndIncorrect()
    {
        const int correctAnswer = 6;

        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit("1", correctAnswer));
        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit("6", correctAnswer));
    }

    [Fact]
    public void EmptyBackspace_IsSafeNoOp()
    {
        var buffer = string.Empty;
        var backspaced = NumericAnswerInputPolicy.Backspace(buffer);

        Assert.Equal(string.Empty, backspaced);
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(backspaced, 36));
    }

    [Fact]
    public async Task PartialMultiDigitInput_ThenTimeout_RecordsTimeoutWithoutIncorrectAttempt()
    {
        var (session, clock, store) = await CreateSession();
        var fact = session.CurrentFact;
        var buffer = "3";

        // Partial input for multi-digit answer does not auto-submit
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, 36));
        Assert.Equal(0, store.CommitCount);
        Assert.Equal(0, session.SessionTotalCount);

        clock.AdvanceMs(session.CurrentFactDeadlineMs);

        var evaluation = session.RecordTimeout();
        await session.CommitCurrentEvaluationAsync();

        Assert.Equal(AttemptOutcome.Timeout, evaluation.Outcome);
        Assert.Equal(SessionInteractionState.TimeoutFeedback, session.InteractionState);
        Assert.Null(session.LastEvaluation!.SubmittedNumericAnswer);
        Assert.Equal(1, store.CommitCount);
        Assert.Equal(1, session.SessionTotalCount);
    }

    [Fact]
    public async Task PartialEditing_DoesNotResetResponseTimer()
    {
        var (session, clock, _) = await CreateSession();
        var fact = session.CurrentFact;

        // User enters '1' at t=1000ms
        clock.AdvanceMs(1000);
        var buffer = NumericAnswerInputPolicy.Append(string.Empty, "1");
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, 36));

        // User backspaces at t=2000ms
        clock.AdvanceMs(1000);
        buffer = NumericAnswerInputPolicy.Backspace(buffer);
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, 36));

        // User enters '3' at t=2500ms
        clock.AdvanceMs(500);
        buffer = NumericAnswerInputPolicy.Append(buffer, "3");
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, 36));

        // User enters '6' at t=3500ms
        clock.AdvanceMs(1000);
        buffer = NumericAnswerInputPolicy.Append(buffer, "6");
        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, 36));

        Assert.True(NumericAnswerInputPolicy.TryParseSubmission(buffer, out var parsedAnswer));
        var eval = session.SubmitAnswer(parsedAnswer);

        // Latency must reflect full elapsed active time (3500ms), not reset time
        Assert.Equal(3500, eval.LatencyMs);
        Assert.Equal(3500, session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public void PhysicalKeyboardAndOnScreenKeypad_UseIdenticalBufferingAndSubmissionPolicy()
    {
        const int correctAnswer = 36;

        // Keypad path: Append -> ShouldSubmit
        var keypadBuffer = NumericAnswerInputPolicy.Append(string.Empty, "1");
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(keypadBuffer, correctAnswer));

        // Keyboard path: IsValidEdit -> ShouldSubmit
        var keyboardBuffer = NumericAnswerInputPolicy.AcceptEditOrKeep(string.Empty, "1");
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(keyboardBuffer, correctAnswer));
        Assert.Equal(keypadBuffer, keyboardBuffer);

        // Keypad second digit
        keypadBuffer = NumericAnswerInputPolicy.Append(keypadBuffer, "2");
        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit(keypadBuffer, correctAnswer));

        // Keyboard second digit
        keyboardBuffer = NumericAnswerInputPolicy.AcceptEditOrKeep(keyboardBuffer, "12");
        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit(keyboardBuffer, correctAnswer));
        Assert.Equal(keypadBuffer, keyboardBuffer);
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
    public async Task PracticeGateActivationRevision_ChangesOnlyForNewGateActivations()
    {
        var (session, _, _) = await CreateSession();
        var initialRevision = session.PracticeGateActivationRevision;

        session.PausePractice();
        var pausedRevision = session.PracticeGateActivationRevision;
        session.PausePractice();

        Assert.True(pausedRevision > initialRevision);
        Assert.Equal(pausedRevision, session.PracticeGateActivationRevision);

        session.StartOrResumePractice();
        session.PausePractice();

        Assert.True(session.PracticeGateActivationRevision > pausedRevision);
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

    [Fact]
    public void PracticeHeader_UsesDangerPauseAndRemovesPermanentScoreFromHud()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));
        var styles = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css"));
        var header = home[..home.IndexOf("</header>", StringComparison.Ordinal)];

        Assert.Contains("class=\"button button-danger pause-practice-btn\"", header, StringComparison.Ordinal);
        Assert.DoesNotContain("button button-secondary pause-practice-btn", home, StringComparison.Ordinal);
        Assert.Contains("class=\"button button-primary practice-overlay-action\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Training_Score", header, StringComparison.Ordinal);
        Assert.DoesNotContain("header-session-score", header, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSessionScoreVisible", home, StringComparison.Ordinal);
        Assert.Contains("class=\"operation-progress-hud\"", home, StringComparison.Ordinal);
        Assert.Contains("GetOperationSymbol(progress.Operation)", home, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(4, minmax(0, 1fr));", styles, StringComparison.Ordinal);
        Assert.Contains(".operation-progress-hud", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 480px)", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationProgressHud_UsesComponentLifetimeCurriculumAndProgressionCache()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        Assert.Contains("private readonly ArithmeticCurriculum _curriculum = new();", home, StringComparison.Ordinal);
        Assert.Contains("private IReadOnlyList<OperationProgressDiagnostics> _operationProgress", home, StringComparison.Ordinal);
        Assert.Contains("_operationProgressLearnerStateGeneration", home, StringComparison.Ordinal);
        Assert.Contains("_operationProgressPracticePosition", home, StringComparison.Ordinal);
        Assert.Contains("Session.LearnerStateGenerationRevision", home, StringComparison.Ordinal);
        Assert.Contains("LearningProgressDiagnostics.Create(Session.Progression, _curriculum)", home, StringComparison.Ordinal);
        Assert.DoesNotContain("LearningProgressDiagnostics.Create(Session.Progression, new ArithmeticCurriculum())", home, StringComparison.Ordinal);
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
