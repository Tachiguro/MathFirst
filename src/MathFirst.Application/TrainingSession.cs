namespace MathFirst.Application;

using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;

public sealed class TrainingSession
{
    private readonly ILearnerStore _store;
    private readonly IClock _clock;
    private readonly AdaptivePracticeSelector _selector;
    private readonly IFsrsScheduler _fsrsScheduler;
    private readonly long _fluentThresholdMs;
    private Dictionary<string, FsrsCardState> _fsrsStates = new(StringComparer.Ordinal);
    private long _accumulatedActiveElapsedMs;
    private long _activeSegmentStartTimestamp;
    private bool _isTimingActive;
    private bool _isAppForeground = true;
    private bool _isPracticeSurfaceActive = true;
    private PracticeGateState _practiceGateState = PracticeGateState.Running;
    private bool _requiresBackgroundResumeAfterAdvance;
    private int _sessionCorrectCountBeforePendingEvaluation;
    private int _sessionTotalCountBeforePendingEvaluation;
    private long _lastResponseLatencyBeforePendingEvaluation;

    public event EventHandler? AppForegroundStateChanged;

    public LearnerProgression Progression { get; private set; } = LearnerProgression.CreateFresh();
    public Dictionary<string, ItemLearningState> ItemStates { get; private set; } = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, FsrsCardState> FsrsStates => _fsrsStates;
    public int SessionOrderCounter { get; private set; }
    public int SessionCorrectCount { get; private set; }
    public int SessionTotalCount { get; private set; }
    public ArithmeticFact CurrentFact { get; private set; } = null!;
    public long ItemReadyTimestamp { get; private set; }
    public long CurrentFactDeadlineMs { get; private set; } = LearningPolicy.DeadlineStreak0Ms;
    public double CurrentFactDeadlineSeconds => CurrentFactDeadlineMs / 1000.0;
    public long LastResponseLatencyMs { get; private set; }
    public SubmissionEvaluation? LastEvaluation { get; private set; }
    public PersistenceResult? LastPersistenceResult { get; private set; }
    public SessionInteractionState InteractionState { get; private set; } = SessionInteractionState.AwaitingAnswer;
    public bool IsTimingActive => _isTimingActive;
    public bool IsAppForeground => _isAppForeground;
    public bool IsPracticeSurfaceActive => _isPracticeSurfaceActive;
    public PracticeGateState PracticeGate => _practiceGateState;
    public bool IsInitialized { get; private set; }

    public TrainingSession(
        ILearnerStore store,
        IClock? clock = null,
        AdaptivePracticeSelector? selector = null,
        IFsrsScheduler? fsrsScheduler = null,
        long fluentThresholdMs = LearningPolicy.DefaultFluentResponseThresholdMs)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? MonotonicClock.Instance;
        _selector = selector ?? new AdaptivePracticeSelector();
        _fsrsScheduler = fsrsScheduler ?? new FsrsSchedulerAdapter();
        _fluentThresholdMs = fluentThresholdMs;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default,
        bool startTiming = true)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await _store.LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);

        Progression = snapshot.Progression;
        ItemStates = snapshot.ItemStates.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
        _fsrsStates = snapshot.FsrsStates.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
        SessionOrderCounter = 0;
        SessionCorrectCount = 0;
        SessionTotalCount = 0;
        LastResponseLatencyMs = 0;
        LastPersistenceResult = null;
        _requiresBackgroundResumeAfterAdvance = false;
        IsInitialized = true;

        LearningPolicy.SynchronizeProgression(Progression, ItemStates);
        AdvanceToNextFact(startTiming);
    }

    public void ResetItemReadyTiming()
    {
        ItemReadyTimestamp = _clock.GetTimestamp();
        _activeSegmentStartTimestamp = ItemReadyTimestamp;
        _accumulatedActiveElapsedMs = 0;
        _isTimingActive = false;
        InteractionState = SessionInteractionState.AwaitingAnswer;

        if (CurrentFact is not null)
        {
            ItemStates.TryGetValue(CurrentFact.Id, out var state);
            CurrentFactDeadlineMs = LearningPolicy.GetAnswerDeadlineMs(state?.ConsecutiveCorrectStreak ?? 0);
        }

        ReconcileTimingState();
    }

    public void SetAppForeground(bool isForeground)
    {
        if (_isAppForeground == isForeground)
        {
            return;
        }

        if (!isForeground && IsInitialized && _practiceGateState == PracticeGateState.Running)
        {
            if (InteractionState == SessionInteractionState.AwaitingAnswer)
            {
                _practiceGateState = PracticeGateState.BackgroundResumeGate;
            }
            else if (LastEvaluation?.Outcome == AttemptOutcome.Correct &&
                     InteractionState is SessionInteractionState.CorrectFeedback or SessionInteractionState.PersistenceFailure)
            {
                _requiresBackgroundResumeAfterAdvance = true;
            }
        }

        _isAppForeground = isForeground;
        ReconcileTimingState();

        AppForegroundStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPracticeSurfaceActive(bool isActive)
    {
        if (_isPracticeSurfaceActive == isActive)
        {
            return;
        }

        _isPracticeSurfaceActive = isActive;
        ReconcileTimingState();
    }

    public void PauseItemTiming()
    {
        SetPracticeSurfaceActive(false);
    }

    public void ResumeItemTiming()
    {
        SetPracticeSurfaceActive(true);
    }

    public void ShowInitialReadyGate()
    {
        if (InteractionState != SessionInteractionState.AwaitingAnswer)
        {
            return;
        }

        _practiceGateState = PracticeGateState.InitialReadyGate;
        ReconcileTimingState();
    }

    public void PausePractice()
    {
        if (InteractionState != SessionInteractionState.AwaitingAnswer ||
            _practiceGateState != PracticeGateState.Running)
        {
            return;
        }

        _practiceGateState = PracticeGateState.ManualPause;
        ReconcileTimingState();
    }

    public void StartOrResumePractice()
    {
        if (_practiceGateState == PracticeGateState.Running)
        {
            return;
        }

        _practiceGateState = PracticeGateState.Running;
        ReconcileTimingState();
    }

    private void ReconcileTimingState()
    {
        var shouldBeActive =
            _isAppForeground &&
            _isPracticeSurfaceActive &&
            _practiceGateState == PracticeGateState.Running &&
            InteractionState == SessionInteractionState.AwaitingAnswer;

        if (_isTimingActive == shouldBeActive)
        {
            return;
        }

        if (_isTimingActive)
        {
            var segmentElapsedMs = (long)Math.Max(
                0,
                _clock.GetElapsedTime(_activeSegmentStartTimestamp).TotalMilliseconds);
            _accumulatedActiveElapsedMs += segmentElapsedMs;
        }
        else
        {
            _activeSegmentStartTimestamp = _clock.GetTimestamp();
        }

        _isTimingActive = shouldBeActive;
    }

    public long GetCurrentActiveElapsedMs()
    {
        if (!_isTimingActive)
        {
            return _accumulatedActiveElapsedMs;
        }
        var segmentElapsedMs = (long)Math.Max(0, _clock.GetElapsedTime(_activeSegmentStartTimestamp).TotalMilliseconds);
        return _accumulatedActiveElapsedMs + segmentElapsedMs;
    }

    public const double DefaultAnswerDeadlineSeconds = 30.0;

    public TimeSpan GetCurrentItemElapsed() => TimeSpan.FromMilliseconds(GetCurrentActiveElapsedMs());

    public bool IsCurrentItemTimedOut() =>
        GetCurrentActiveElapsedMs() >= CurrentFactDeadlineMs;

    public bool IsCurrentItemTimedOut(double deadlineSeconds) =>
        GetCurrentItemElapsed().TotalSeconds >= deadlineSeconds;

    public SubmissionEvaluation SubmitAnswer(int submittedAnswer)
        => SubmitAnswerCore(submittedAnswer, submittedAnswer);

    public SubmissionEvaluation SubmitAnswer(decimal submittedAnswer)
    {
        int? persistedAnswer = null;
        if (submittedAnswer == decimal.Truncate(submittedAnswer) &&
            submittedAnswer >= int.MinValue &&
            submittedAnswer <= int.MaxValue)
        {
            persistedAnswer = decimal.ToInt32(submittedAnswer);
        }

        return SubmitAnswerCore(submittedAnswer, persistedAnswer);
    }

    private SubmissionEvaluation SubmitAnswerCore(decimal submittedAnswer, int? persistedAnswer)
    {
        if (!IsInitialized || CurrentFact is null)
        {
            throw new InvalidOperationException("Training session is not initialized.");
        }

        if (InteractionState != SessionInteractionState.AwaitingAnswer)
        {
            if (LastEvaluation is not null)
            {
                return LastEvaluation;
            }
            throw new InvalidOperationException($"Cannot submit answer in interaction state {InteractionState}.");
        }

        // 1. Capture response latency BEFORE any persistence I/O
        var elapsedMs = (long)Math.Max(1, GetCurrentActiveElapsedMs());
        _accumulatedActiveElapsedMs = elapsedMs;
        _isTimingActive = false;

        var isCorrect = submittedAnswer == CurrentFact.CorrectResult;
        var outcome = isCorrect ? AttemptOutcome.Correct : AttemptOutcome.Incorrect;
        InteractionState = isCorrect ? SessionInteractionState.CorrectFeedback : SessionInteractionState.IncorrectFeedback;

        return EvaluateAndRecord(outcome, persistedAnswer, elapsedMs, submittedAnswer);
    }

    public SubmissionEvaluation RecordTimeout()
    {
        if (!IsInitialized || CurrentFact is null)
        {
            throw new InvalidOperationException("Training session is not initialized.");
        }

        if (InteractionState != SessionInteractionState.AwaitingAnswer)
        {
            if (LastEvaluation is not null)
            {
                return LastEvaluation;
            }
            throw new InvalidOperationException($"Cannot record timeout in interaction state {InteractionState}.");
        }

        var elapsedMs = (long)Math.Max(CurrentFactDeadlineMs, GetCurrentActiveElapsedMs());
        _accumulatedActiveElapsedMs = elapsedMs;
        _isTimingActive = false;
        InteractionState = SessionInteractionState.TimeoutFeedback;

        return EvaluateAndRecord(AttemptOutcome.Timeout, null, elapsedMs, null);
    }

    public SubmissionEvaluation SubmitTimeout() => RecordTimeout();

    private SubmissionEvaluation EvaluateAndRecord(
        AttemptOutcome outcome,
        int? submittedAnswer,
        long elapsedMs,
        decimal? submittedNumericAnswer)
    {
        _sessionCorrectCountBeforePendingEvaluation = SessionCorrectCount;
        _sessionTotalCountBeforePendingEvaluation = SessionTotalCount;
        _lastResponseLatencyBeforePendingEvaluation = LastResponseLatencyMs;
        LastPersistenceResult = null;
        LastResponseLatencyMs = elapsedMs;
        var isCorrect = (outcome == AttemptOutcome.Correct);

        SessionTotalCount++;
        if (isCorrect)
        {
            SessionCorrectCount++;
        }

        // 2. Increment Practice Position (Task-based monotonic counter for accepted arithmetic attempts)
        Progression.PracticePosition++;

        // 3. Update FSRS card state
        var rating = FsrsRatingMapper.MapRating(outcome, elapsedMs, LearningPolicy.DefaultEasyResponseThresholdMs, _fluentThresholdMs);
        _fsrsStates.TryGetValue(CurrentFact.Id, out var existingFsrsState);
        var updatedFsrsState = _fsrsScheduler.ReviewCard(
            existingFsrsState,
            CurrentFact.Id,
            rating,
            Progression.PracticePosition,
            elapsedMs);
        _fsrsStates[CurrentFact.Id] = updatedFsrsState;

        // 4. Update ItemLearningState
        if (!ItemStates.TryGetValue(CurrentFact.Id, out var itemState))
        {
            itemState = ItemLearningState.CreateNew(CurrentFact);
            ItemStates[CurrentFact.Id] = itemState;
        }

        itemState.TotalAttempts++;
        itemState.LastLatencyMs = elapsedMs;
        itemState.RollingLatencyMs = (itemState.RollingLatencyMs == 0) ? elapsedMs : (itemState.RollingLatencyMs + elapsedMs) / 2;
        itemState.LastPracticedOrder = SessionOrderCounter;
        itemState.LastPracticedAt = DateTimeOffset.UtcNow;

        if (isCorrect)
        {
            itemState.CorrectAttempts++;
            itemState.ConsecutiveCorrectStreak++;
            if (elapsedMs <= _fluentThresholdMs)
            {
                itemState.FluentStreak++;
            }
            else
            {
                itemState.FluentStreak = 0;
            }

            if (itemState.NeedsRemediation)
            {
                itemState.NeedsRemediation = false;
            }
        }
        else
        {
            itemState.IncorrectAttempts++;
            itemState.ConsecutiveCorrectStreak = 0;
            itemState.FluentStreak = 0;
            itemState.NeedsRemediation = true;
            itemState.RemediationDueOrder = SessionOrderCounter + LearningPolicy.RemediationInterveningCount;
        }

        itemState.IsProvisionallyMastered = LearningPolicy.EvaluateItemMastery(itemState, _fluentThresholdMs);

        // 5. Update Checkpoint State (if in Checkpoint)
        if (Progression.IsInCheckpoint)
        {
            Progression.CheckpointAttemptCount++;
            if (isCorrect)
            {
                Progression.CheckpointCorrectCount++;
            }

            if (Progression.CheckpointAttemptCount >= LearningPolicy.DefaultCheckpointAttemptCount)
            {
                Progression.CompletedCheckpointLevel = Progression.ActiveCheckpointLevel ?? Progression.CurrentMaxOperand;
                Progression.ActiveCheckpointLevel = null;
                Progression.CheckpointAttemptCount = 0;
                Progression.CheckpointCorrectCount = 0;
            }
        }

        // 6. Evaluate Multi-Operation Range Progression & Checkpoint Entry
        var rangeUnlocked = LearningPolicy.SynchronizeProgression(Progression, ItemStates);

        Progression.CurrentOperation = CurrentFact.Operation;
        Progression.UpdatedAt = DateTimeOffset.UtcNow;

        // 7. Construct Attempt Record and ChangeSet
        var submissionId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(
            submissionId,
            CurrentFact.Id,
            CurrentFact.Operation,
            CurrentFact.LeftOperand,
            CurrentFact.RightOperand,
            submittedAnswer,
            CurrentFact.CorrectResult,
            isCorrect,
            elapsedMs,
            DateTimeOffset.UtcNow,
            outcome);

        var changeSet = new SubmissionChangeSet(
            submissionId,
            Progression.StoreRevision,
            attempt,
            itemState,
            Progression,
            updatedFsrsState);

        LastEvaluation = new SubmissionEvaluation(
            outcome,
            isCorrect,
            submittedAnswer,
            CurrentFact.CorrectResult,
            elapsedMs,
            changeSet,
            itemState.IsProvisionallyMastered,
            rangeUnlocked,
            OperationUnlocked: false)
        {
            SubmittedNumericAnswer = submittedNumericAnswer
        };

        return LastEvaluation;
    }

    public async Task<PersistenceResult> CommitCurrentEvaluationAsync(CancellationToken cancellationToken = default)
    {
        if (LastEvaluation?.ChangeSet is null)
        {
            return PersistenceResult.Success(Progression.StoreRevision);
        }

        var result = await _store.CommitSubmissionAsync(LastEvaluation.ChangeSet, cancellationToken).ConfigureAwait(false);
        LastPersistenceResult = result;
        if (result.IsSuccess && result.NewRevision.HasValue)
        {
            Progression.StoreRevision = result.NewRevision.Value;
            if (InteractionState == SessionInteractionState.PersistenceFailure &&
                LastEvaluation.Outcome == AttemptOutcome.Correct)
            {
                InteractionState = SessionInteractionState.CorrectFeedback;
            }
        }
        else if (LastEvaluation.Outcome == AttemptOutcome.Correct)
        {
            InteractionState = SessionInteractionState.PersistenceFailure;
            ReconcileTimingState();
        }

        return result;
    }

    public async Task<bool> RecoverFromPersistenceFailureAsync(CancellationToken cancellationToken = default)
    {
        if (InteractionState != SessionInteractionState.PersistenceFailure ||
            LastEvaluation?.Outcome != AttemptOutcome.Correct ||
            LastPersistenceResult is null)
        {
            return false;
        }

        if (LastPersistenceResult.Status == PersistenceStatus.RevisionConflict)
        {
            await ReloadAuthoritativeStateAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        var result = await CommitCurrentEvaluationAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    private async Task ReloadAuthoritativeStateAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _store.LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);

        Progression = snapshot.Progression;
        ItemStates = snapshot.ItemStates.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
        _fsrsStates = snapshot.FsrsStates.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
        SessionCorrectCount = _sessionCorrectCountBeforePendingEvaluation;
        SessionTotalCount = _sessionTotalCountBeforePendingEvaluation;
        LastResponseLatencyMs = _lastResponseLatencyBeforePendingEvaluation;
        LastEvaluation = null;
        LastPersistenceResult = null;
        _selector.ResetLastSelected();

        AdvanceToNextFact(startTiming: true);
    }

    public void AdvanceToNextFact(bool startTiming = true)
    {
        SessionOrderCounter++;
        CurrentFact = _selector.SelectNextFact(
            Progression,
            ItemStates,
            _fsrsStates,
            SessionOrderCounter,
            Progression.PracticePosition,
            _fluentThresholdMs);
        Progression.CurrentOperation = CurrentFact.Operation;
        ItemReadyTimestamp = _clock.GetTimestamp();
        _activeSegmentStartTimestamp = ItemReadyTimestamp;
        _accumulatedActiveElapsedMs = 0;
        _isPracticeSurfaceActive = startTiming;
        _isTimingActive = false;
        InteractionState = SessionInteractionState.AwaitingAnswer;
        LastEvaluation = null;
        LastPersistenceResult = null;

        if (_requiresBackgroundResumeAfterAdvance)
        {
            _practiceGateState = PracticeGateState.BackgroundResumeGate;
            _requiresBackgroundResumeAfterAdvance = false;
        }

        ItemStates.TryGetValue(CurrentFact.Id, out var state);
        CurrentFactDeadlineMs = LearningPolicy.GetAnswerDeadlineMs(state?.ConsecutiveCorrectStreak ?? 0);
        ReconcileTimingState();
    }

    public bool AdvanceAfterCorrectAnswer(bool startTiming = true)
    {
        if (InteractionState != SessionInteractionState.CorrectFeedback ||
            LastEvaluation?.Outcome != AttemptOutcome.Correct)
        {
            return false;
        }

        AdvanceToNextFact(startTiming);
        return true;
    }

    public async Task ResetLearningProgressAsync(
        CancellationToken cancellationToken = default,
        bool? startTiming = null)
    {
        var shouldStartTiming = startTiming ?? _isTimingActive;
        await _store.ResetLearningProgressAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await _store.LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);

        Progression = snapshot.Progression;
        ItemStates = snapshot.ItemStates.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
        _fsrsStates = snapshot.FsrsStates.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
        SessionCorrectCount = 0;
        SessionTotalCount = 0;
        SessionOrderCounter = 0;
        LastResponseLatencyMs = 0;
        LastPersistenceResult = null;
        _requiresBackgroundResumeAfterAdvance = false;
        _selector.ResetLastSelected();

        AdvanceToNextFact(shouldStartTiming);
    }
}
