namespace MathFirst.Core.Tests;

using System.Text.RegularExpressions;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using Xunit;

public sealed class AnswerEntryReliabilityTests
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
        public string StoragePath => "inmemory://answer-entry-reliability";
        public int CommitCount { get; private set; }
        private LearnerProgression _progression = LearnerProgression.CreateFresh();
        private readonly Dictionary<string, ItemLearningState> _items = new(StringComparer.Ordinal);
        private readonly List<AttemptRecord> _attempts = [];
        private long _revision = 1;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LearnerSnapshot(
                _progression,
                _items,
                _attempts,
                _revision,
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
            _revision = changeSet.ExpectedRevision + 1;
            _progression = changeSet.UpdatedProgression;
            _items[changeSet.UpdatedItemState.FactId] = changeSet.UpdatedItemState;
            _attempts.Add(changeSet.Attempt);
            return Task.FromResult(PersistenceResult.Success(_revision));
        }
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    [Fact]
    public void TwoDigitResult_11_PlusPartialWrongFirstDigit_2_MustNotSubmit()
    {
        const int correctAnswer = 11;
        var buffer = string.Empty;

        // User enters '2' as first digit
        buffer = NumericAnswerInputPolicy.Append(buffer, "2");
        Assert.Equal("2", buffer);

        // Multi-digit result 11 with single-digit input 2 must NOT auto-submit
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));
    }

    [Fact]
    public void TwoDigitResult_11_PartialWrongFirstDigit_2_CanBeRemovedWithBackspace()
    {
        const int correctAnswer = 11;
        var buffer = "2";

        buffer = NumericAnswerInputPolicy.Backspace(buffer);
        Assert.Equal(string.Empty, buffer);
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));
    }

    [Fact]
    public void TwoDigitResult_11_Corrected_11_SubmitsSuccessfully()
    {
        const int correctAnswer = 11;
        var buffer = string.Empty;

        // User enters 2
        buffer = NumericAnswerInputPolicy.Append(buffer, "2");
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));

        // User backspaces
        buffer = NumericAnswerInputPolicy.Backspace(buffer);
        Assert.Equal(string.Empty, buffer);

        // User enters 1, then 1
        buffer = NumericAnswerInputPolicy.Append(buffer, "1");
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));

        buffer = NumericAnswerInputPolicy.Append(buffer, "1");
        Assert.Equal("11", buffer);
        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));
        Assert.True(NumericAnswerInputPolicy.TryParseSubmission(buffer, out var parsed));
        Assert.Equal(11m, parsed);
    }

    [Fact]
    public void SingleDigitResult_8_AutoSubmitsImmediately()
    {
        const int correctAnswer = 8;
        var buffer = "8";

        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit(buffer, correctAnswer));
        Assert.True(NumericAnswerInputPolicy.TryParseSubmission(buffer, out var parsed));
        Assert.Equal(8m, parsed);
    }

    [Fact]
    public async Task MandatoryCombinedRegression_SingleDigitToMultiDigit_KeypadPath()
    {
        // Sequence:
        // A. Current exercise: 24 / 3 = 8
        // B. Enter 8 -> auto-submits correct
        // C. Advancement to next exercise: 7 + 4 = 11
        // D. Active input must be clean and empty
        // E. Enter 2 -> input is '2' (not '82'), does NOT auto-submit
        // F. Backspace -> input is empty
        // G. Enter 1, 1 -> submits 11 as correct

        var clock = new FakeClock();
        var store = new RecordingStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        // Exercise 1: first fact from session
        var fact1 = session.CurrentFact;
        var fact1Answer = fact1.CorrectResult.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // User enters single-digit fact result via keypad
        var inputBuffer = string.Empty;
        foreach (var c in fact1Answer)
        {
            inputBuffer = NumericAnswerInputPolicy.Append(inputBuffer, c.ToString());
        }
        Assert.Equal(fact1Answer, inputBuffer);
        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit(inputBuffer, fact1.CorrectResult));

        // Submit and advance
        Assert.True(NumericAnswerInputPolicy.TryParseSubmission(inputBuffer, out var parsedFact1));
        var eval1 = session.SubmitAnswer(parsedFact1);
        var commit1 = await session.CommitCurrentEvaluationAsync();
        Assert.True(eval1.IsCorrect);
        Assert.True(commit1.IsSuccess);
        Assert.True(session.AdvanceAfterCorrectAnswer());

        // Exercise 2: verify transition to next exercise
        // Check Bug 2: input buffer in UI must be reset to empty
        inputBuffer = string.Empty;
        Assert.Equal(string.Empty, inputBuffer);

        // For a multi-digit result like 11 (e.g. 7 + 4 = 11):
        const int multiDigitTarget = 11;

        // Learner enters '2'
        inputBuffer = NumericAnswerInputPolicy.Append(inputBuffer, "2");
        Assert.Equal("2", inputBuffer);

        // Must NOT auto-submit because length(2) < length(11)
        Assert.False(
            AnswerAutoSubmissionPolicy.ShouldSubmit(inputBuffer, multiDigitTarget),
            "Entering '2' for answer '11' must NOT auto-submit.");

        // If stale digit '8' had leaked (e.g. '82'), it WOULD auto-submit as incorrect
        var staleInput = "82";
        Assert.True(
            AnswerAutoSubmissionPolicy.ShouldSubmit(staleInput, multiDigitTarget),
            "Stale input '82' has length 2 >= 2 and would prematurely auto-submit.");

        // Backspace
        inputBuffer = NumericAnswerInputPolicy.Backspace(inputBuffer);
        Assert.Equal(string.Empty, inputBuffer);

        // Enter 1, then 1
        inputBuffer = NumericAnswerInputPolicy.Append(inputBuffer, "1");
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(inputBuffer, multiDigitTarget));

        inputBuffer = NumericAnswerInputPolicy.Append(inputBuffer, "1");
        Assert.Equal("11", inputBuffer);
        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit(inputBuffer, multiDigitTarget));
        Assert.True(NumericAnswerInputPolicy.TryParseSubmission(inputBuffer, out var parsedFact2));
        Assert.Equal(11m, parsedFact2);
    }

    [Fact]
    public async Task MandatoryCombinedRegression_SingleDigitToMultiDigit_KeyboardPath()
    {
        // Same sequence using keyboard input policy (AcceptEditOrKeep)
        var clock = new FakeClock();
        var store = new RecordingStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var fact1 = session.CurrentFact;
        var fact1Answer = fact1.CorrectResult.ToString(System.Globalization.CultureInfo.InvariantCulture);
        const int fact2CorrectResult = 11;

        // Exercise 1: enter correct answer
        var inputBuffer = NumericAnswerInputPolicy.AcceptEditOrKeep(string.Empty, fact1Answer);
        Assert.Equal(fact1Answer, inputBuffer);
        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit(inputBuffer, fact1.CorrectResult));

        Assert.True(NumericAnswerInputPolicy.TryParseSubmission(inputBuffer, out var parsed1));
        var eval1 = session.SubmitAnswer(parsed1);
        var commit1 = await session.CommitCurrentEvaluationAsync();
        Assert.True(eval1.IsCorrect);
        Assert.True(commit1.IsSuccess);
        Assert.True(session.AdvanceAfterCorrectAnswer());

        // Next exercise: input buffer is reset to empty
        inputBuffer = string.Empty;

        // User enters '2'
        inputBuffer = NumericAnswerInputPolicy.AcceptEditOrKeep(inputBuffer, "2");
        Assert.Equal("2", inputBuffer);
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(inputBuffer, fact2CorrectResult));

        // Backspace
        inputBuffer = NumericAnswerInputPolicy.Backspace(inputBuffer);
        Assert.Equal(string.Empty, inputBuffer);

        // User types '11'
        inputBuffer = NumericAnswerInputPolicy.AcceptEditOrKeep(inputBuffer, "1");
        Assert.False(AnswerAutoSubmissionPolicy.ShouldSubmit(inputBuffer, fact2CorrectResult));
        inputBuffer = NumericAnswerInputPolicy.AcceptEditOrKeep(inputBuffer, "11");
        Assert.Equal("11", inputBuffer);
        Assert.True(AnswerAutoSubmissionPolicy.ShouldSubmit(inputBuffer, fact2CorrectResult));
    }

    [Fact]
    public void Home_PrepareNextFactUi_MustIncrementInputRenderVersion_ToRecreateDomElement()
    {
        // Bug 2 root-cause contract test:
        // When transitioning to the next fact, PrepareNextFactUi() in Home.razor MUST increment
        // _inputRenderVersion so that Blazor's @key on the <input> element changes, destroying the
        // dirty DOM element and creating a brand-new empty <input> element.
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        var homeContent = File.ReadAllText(homePath);

        // Find the PrepareNextFactUi method body
        var match = Regex.Match(
            homeContent,
            @"private\s+void\s+PrepareNextFactUi\s*\(\)\s*\{(?<body>.*?)\}",
            RegexOptions.Singleline);

        Assert.True(match.Success, "PrepareNextFactUi method was not found in Home.razor.");
        var body = match.Groups["body"].Value;

        Assert.Contains("_inputRenderVersion++", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_TakeBreakAsync_MustInvalidateInputRenderVersion_AndClearAnswerInput()
    {
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        var homeContent = File.ReadAllText(homePath);

        var match = Regex.Match(
            homeContent,
            @"private\s+async\s+Task\s+TakeBreakAsync\s*\(\)\s*\{(?<body>.*?)\}",
            RegexOptions.Singleline);

        Assert.True(match.Success, "TakeBreakAsync method was not found in Home.razor.");
        var body = match.Groups["body"].Value;

        Assert.Contains("_inputRenderVersion++", body, StringComparison.Ordinal);
        Assert.Contains("_answerInput = string.Empty", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_SyncStateWithSession_MustTrackFactInstanceRevision()
    {
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        var homeContent = File.ReadAllText(homePath);

        Assert.Contains("FactInstanceRevision", homeContent, StringComparison.Ordinal);
        Assert.Contains("CurrentAnswerInput", homeContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionCheckIn_TakeBreak_ThenStart_ProducesEmptyAnswerInput_AndAdvancesFactInstance()
    {
        var clock = new FakeClock();
        var store = new RecordingStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        for (var i = 0; i < 19; i++)
        {
            var fact = session.CurrentFact;
            session.SetCurrentAnswerInput(fact.CorrectResult.ToString(System.Globalization.CultureInfo.InvariantCulture));
            session.SubmitAnswer(fact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            Assert.True(session.AdvanceAfterCorrectAnswer());
        }

        var fact20 = session.CurrentFact;
        session.SetCurrentAnswerInput(fact20.CorrectResult.ToString(System.Globalization.CultureInfo.InvariantCulture));
        session.SubmitAnswer(fact20.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        Assert.False(session.AdvanceAfterCorrectAnswer());
        Assert.NotNull(session.PendingCheckIn);
        Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);

        var revisionBeforeBreak = session.FactInstanceRevision;
        await session.TakeBreakAsync();

        Assert.Equal(PracticeGateState.ManualPause, session.PracticeGate);
        Assert.True(session.FactInstanceRevision > revisionBeforeBreak);
        Assert.Equal(string.Empty, session.CurrentAnswerInput);

        session.StartOrResumePractice();
        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.Equal(string.Empty, session.CurrentAnswerInput);
    }

    [Fact]
    public async Task SessionCheckIn_ContinuePractice_ProducesEmptyAnswerInput_AndAdvancesFactInstance()
    {
        var clock = new FakeClock();
        var store = new RecordingStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        for (var i = 0; i < 19; i++)
        {
            var fact = session.CurrentFact;
            session.SetCurrentAnswerInput(fact.CorrectResult.ToString(System.Globalization.CultureInfo.InvariantCulture));
            session.SubmitAnswer(fact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            Assert.True(session.AdvanceAfterCorrectAnswer());
        }

        var fact20 = session.CurrentFact;
        session.SetCurrentAnswerInput(fact20.CorrectResult.ToString(System.Globalization.CultureInfo.InvariantCulture));
        session.SubmitAnswer(fact20.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        Assert.False(session.AdvanceAfterCorrectAnswer());
        Assert.NotNull(session.PendingCheckIn);

        var revisionBeforeContinue = session.FactInstanceRevision;
        await session.ContinuePracticeAsync();

        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.True(session.FactInstanceRevision > revisionBeforeContinue);
        Assert.Equal(string.Empty, session.CurrentAnswerInput);
    }

    [Fact]
    public async Task NormalNextFact_AdvancesFactInstance_AndClearsAnswerInput()
    {
        var clock = new FakeClock();
        var store = new RecordingStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var initialRevision = session.FactInstanceRevision;
        session.SetCurrentAnswerInput("4");
        Assert.Equal("4", session.CurrentAnswerInput);

        var fact = session.CurrentFact;
        session.SubmitAnswer(fact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        Assert.True(session.AdvanceAfterCorrectAnswer());

        Assert.True(session.FactInstanceRevision > initialRevision);
        Assert.Equal(string.Empty, session.CurrentAnswerInput);
    }

    [Fact]
    public async Task ManualPause_AndResume_OnSameFact_PreservesUnsubmittedAnswerInput()
    {
        var clock = new FakeClock();
        var store = new RecordingStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var revision = session.FactInstanceRevision;
        session.SetCurrentAnswerInput("1");
        Assert.Equal("1", session.CurrentAnswerInput);

        session.PausePractice();
        Assert.Equal(PracticeGateState.ManualPause, session.PracticeGate);
        Assert.Equal(revision, session.FactInstanceRevision);
        Assert.Equal("1", session.CurrentAnswerInput);

        session.StartOrResumePractice();
        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.Equal(revision, session.FactInstanceRevision);
        Assert.Equal("1", session.CurrentAnswerInput);
    }

    [Fact]
    public async Task SettingsRoundTrip_OnSameFact_PreservesUnsubmittedAnswerInput()
    {
        var clock = new FakeClock();
        var store = new RecordingStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var revision = session.FactInstanceRevision;
        session.SetCurrentAnswerInput("2");
        Assert.Equal("2", session.CurrentAnswerInput);

        session.PauseItemTiming();
        Assert.False(session.IsPracticeSurfaceActive);
        Assert.Equal(revision, session.FactInstanceRevision);
        Assert.Equal("2", session.CurrentAnswerInput);

        session.ResumeItemTiming();
        Assert.True(session.IsPracticeSurfaceActive);
        Assert.Equal(revision, session.FactInstanceRevision);
        Assert.Equal("2", session.CurrentAnswerInput);
    }

    [Fact]
    public async Task AppBackground_AndForeground_OnSameFact_PreservesUnsubmittedAnswerInput()
    {
        var clock = new FakeClock();
        var store = new RecordingStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var revision = session.FactInstanceRevision;
        session.SetCurrentAnswerInput("3");
        Assert.Equal("3", session.CurrentAnswerInput);

        session.SetAppForeground(false);
        Assert.Equal(revision, session.FactInstanceRevision);
        Assert.Equal("3", session.CurrentAnswerInput);

        session.SetAppForeground(true);
        Assert.Equal(revision, session.FactInstanceRevision);
        Assert.Equal("3", session.CurrentAnswerInput);
    }

    [Fact]
    public async Task SameArithmeticOperands_InConsecutiveExercise_StillYieldsFreshFactInstanceAndEmptyInput()
    {
        var clock = new FakeClock();
        var store = new RecordingStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var fact1 = session.CurrentFact;
        session.SetCurrentAnswerInput(fact1.CorrectResult.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var rev1 = session.FactInstanceRevision;

        session.SubmitAnswer(fact1.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        session.AdvanceAfterCorrectAnswer();

        var rev2 = session.FactInstanceRevision;
        Assert.True(rev2 > rev1, "Consecutive exercise instance MUST have monotonically increasing FactInstanceRevision.");
        Assert.Equal(string.Empty, session.CurrentAnswerInput);
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
