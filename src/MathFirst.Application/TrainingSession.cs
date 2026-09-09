namespace MathFirst.Application;

using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Progression;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

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
    private readonly ArithmeticCurriculum _curriculum = new();
    private readonly BandAdvancementEvaluator _bandAdvancementEvaluator = new();
    private List<AttemptRecord> _recentAttempts = [];
    private SessionStateBackup? _preSubmissionState;

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
        Progression.OperationProgressions = (snapshot.OperationProgressions ?? Progression.OperationProgressions)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        _recentAttempts = snapshot.RecentAttempts.Where(attempt => attempt.PracticePosition is > 0).ToList();
        SessionOrderCounter = 0;
        SessionCorrectCount = 0;
        SessionTotalCount = 0;
        LastResponseLatencyMs = 0;
        LastPersistenceResult = null;
        _requiresBackgroundResumeAfterAdvance = false;
        IsInitialized = true;

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
        _preSubmissionState = CaptureState();
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

        var practicePosition = checked(Progression.PracticePosition + 1);
        Progression.PracticePosition = practicePosition;

        // 3. Update FSRS card state
        var rating = FsrsRatingMapper.MapRating(outcome, elapsedMs, LearningPolicy.DefaultEasyResponseThresholdMs, _fluentThresholdMs);
        _fsrsStates.TryGetValue(CurrentFact.Id, out var existingFsrsState);
        var updatedFsrsState = _fsrsScheduler.ReviewCard(
            existingFsrsState,
            CurrentFact.Id,
            rating,
            practicePosition,
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

        // Evaluate only the scheduled operation from bounded positioned evidence.
        var operationProgressions = Progression.OperationProgressions.ToDictionary(pair => pair.Key, pair => pair.Value);
        var currentOperationProgression = operationProgressions[CurrentFact.Operation];
        var operationAttempts = _recentAttempts
            .Where(existing => existing.Operation == CurrentFact.Operation)
            .Select(existing => new BandAttemptEvidence(existing.PracticePosition!.Value, existing.FactId, existing.IsCorrect, existing.ResponseLatencyMs))
            .Append(new BandAttemptEvidence(practicePosition, CurrentFact.Id, isCorrect, elapsedMs));
        var operationCurriculum = _curriculum.GetCurriculum(CurrentFact.Operation);
        var ownership = new AcquisitionOwnershipResolver(operationCurriculum);
        var ownedFactIds = ownership.GetOwnedFrontier(currentOperationProgression.BandIndex)
            .Select(fact => fact.Id).ToHashSet(StringComparer.Ordinal);
        var currentBandIntroductions = operationAttempts
            .Where(evidence => evidence.PracticePosition > currentOperationProgression.BandStartedPracticePosition && ownedFactIds.Contains(evidence.FactId))
            .Select(evidence => evidence.FactId);
        var advancement = _bandAdvancementEvaluator.Evaluate(
            currentOperationProgression,
            operationCurriculum,
            new BandAdvancementEvidence(
                operationAttempts,
                ItemStates.Values.Where(state => state.TotalAttempts > 0).Select(state => state.FactId),
                currentBandIntroductions));
        operationProgressions[CurrentFact.Operation] = advancement.ResultingProgression;
        Progression.OperationProgressions = operationProgressions;
        Progression.UpdatedAt = DateTimeOffset.UtcNow;

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
            outcome,
            practicePosition);

        var changeSet = new SubmissionChangeSet(
            submissionId,
            Progression.StoreRevision,
            attempt,
            itemState,
            Progression,
            updatedFsrsState,
            operationProgressions);

        LastEvaluation = new SubmissionEvaluation(
            outcome,
            isCorrect,
            submittedAnswer,
            CurrentFact.CorrectResult,
            elapsedMs,
            changeSet,
            itemState.IsProvisionallyMastered,
            RangeUnlocked: advancement.Advances,
            OperationUnlocked: advancement.Advances)
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
            if (_preSubmissionState is not null)
            {
                Progression = LastEvaluation.ChangeSet.UpdatedProgression;
                ItemStates[LastEvaluation.ChangeSet.UpdatedItemState.FactId] = LastEvaluation.ChangeSet.UpdatedItemState;
                if (LastEvaluation.ChangeSet.UpdatedFsrsState is not null)
                {
                    _fsrsStates[LastEvaluation.ChangeSet.UpdatedFsrsState.FactId] = LastEvaluation.ChangeSet.UpdatedFsrsState;
                }
                SessionTotalCount = _preSubmissionState.SessionTotalCount + 1;
                SessionCorrectCount = _preSubmissionState.SessionCorrectCount + (LastEvaluation.IsCorrect ? 1 : 0);
                LastResponseLatencyMs = LastEvaluation.LatencyMs;
            }
            Progression.StoreRevision = result.NewRevision.Value;
            _recentAttempts.Add(LastEvaluation.ChangeSet.Attempt);
            InteractionState = LastEvaluation.Outcome switch
            {
                AttemptOutcome.Correct => SessionInteractionState.CorrectFeedback,
                AttemptOutcome.Timeout => SessionInteractionState.TimeoutFeedback,
                _ => SessionInteractionState.IncorrectFeedback
            };
            _preSubmissionState = null;
        }
        else
        {
            RestoreState();
            InteractionState = SessionInteractionState.PersistenceFailure;
            ReconcileTimingState();
        }

        return result;
    }

    public async Task<bool> RecoverFromPersistenceFailureAsync(CancellationToken cancellationToken = default)
    {
        if (InteractionState != SessionInteractionState.PersistenceFailure ||
            LastEvaluation is null ||
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
        Progression.OperationProgressions = (snapshot.OperationProgressions ?? Progression.OperationProgressions).ToDictionary(pair => pair.Key, pair => pair.Value);
        _recentAttempts = snapshot.RecentAttempts.Where(attempt => attempt.PracticePosition is > 0).ToList();
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
        var materializedFacts = ItemStates.Values
            .Select(state => new ArithmeticFact(state.Operation, state.LeftOperand, state.RightOperand))
            .ToArray();
        var context = new PracticeSelectionContext(
            checked(Progression.PracticePosition + 1),
            SessionOrderCounter,
            Progression.OperationProgressions,
            Enum.GetValues<ArithmeticOperation>().ToDictionary(operation => operation, operation => _curriculum.GetCurriculum(operation)),
            new PracticeCandidateIndex(materializedFacts, ItemStates, _fsrsStates),
            _recentAttempts.OrderBy(attempt => attempt.PracticePosition).Select(attempt => new ArithmeticFact(attempt.Operation, attempt.LeftOperand, attempt.RightOperand)));
        CurrentFact = _selector.SelectTargetFact(context).Fact;
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
        Progression.OperationProgressions = (snapshot.OperationProgressions ?? Progression.OperationProgressions).ToDictionary(pair => pair.Key, pair => pair.Value);
        _recentAttempts = snapshot.RecentAttempts.Where(attempt => attempt.PracticePosition is > 0).ToList();
        SessionCorrectCount = 0;
        SessionTotalCount = 0;
        SessionOrderCounter = 0;
        LastResponseLatencyMs = 0;
        LastPersistenceResult = null;
        _requiresBackgroundResumeAfterAdvance = false;
        _selector.ResetLastSelected();

        AdvanceToNextFact(shouldStartTiming);
    }

    private SessionStateBackup CaptureState() => new(
        CloneProgression(Progression),
        ItemStates.ToDictionary(pair => pair.Key, pair => CloneItemState(pair.Value), StringComparer.Ordinal),
        _fsrsStates.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        SessionCorrectCount,
        SessionTotalCount,
        LastResponseLatencyMs);

    private void RestoreState()
    {
        if (_preSubmissionState is null)
        {
            return;
        }
        Progression = _preSubmissionState.Progression;
        ItemStates = _preSubmissionState.ItemStates;
        _fsrsStates = _preSubmissionState.FsrsStates;
        SessionCorrectCount = _preSubmissionState.SessionCorrectCount;
        SessionTotalCount = _preSubmissionState.SessionTotalCount;
        LastResponseLatencyMs = _preSubmissionState.LastResponseLatencyMs;
    }

    private static LearnerProgression CloneProgression(LearnerProgression source) => new()
    {
        CurrentOperation = source.CurrentOperation,
        CurrentIntroductionTurn = source.CurrentIntroductionTurn,
        PracticePosition = source.PracticePosition,
        OperationMaxOperands = new Dictionary<ArithmeticOperation, int>(source.OperationMaxOperands),
        OperationProgressions = source.OperationProgressions.ToDictionary(pair => pair.Key, pair => pair.Value),
        CompletedCheckpointLevel = source.CompletedCheckpointLevel,
        ActiveCheckpointLevel = source.ActiveCheckpointLevel,
        CheckpointAttemptCount = source.CheckpointAttemptCount,
        CheckpointCorrectCount = source.CheckpointCorrectCount,
        StoreRevision = source.StoreRevision,
        SchemaVersion = source.SchemaVersion,
        UpdatedAt = source.UpdatedAt
    };

    private static ItemLearningState CloneItemState(ItemLearningState source) => new()
    {
        FactId = source.FactId, Operation = source.Operation, LeftOperand = source.LeftOperand, RightOperand = source.RightOperand,
        TotalAttempts = source.TotalAttempts, CorrectAttempts = source.CorrectAttempts, IncorrectAttempts = source.IncorrectAttempts,
        ConsecutiveCorrectStreak = source.ConsecutiveCorrectStreak, LastLatencyMs = source.LastLatencyMs,
        RollingLatencyMs = source.RollingLatencyMs, FluentStreak = source.FluentStreak,
        IsProvisionallyMastered = source.IsProvisionallyMastered, NeedsRemediation = source.NeedsRemediation,
        RemediationDueOrder = source.RemediationDueOrder, LastPracticedOrder = source.LastPracticedOrder,
        LastPracticedAt = source.LastPracticedAt
    };

    private sealed record SessionStateBackup(
        LearnerProgression Progression,
        Dictionary<string, ItemLearningState> ItemStates,
        Dictionary<string, FsrsCardState> FsrsStates,
        int SessionCorrectCount,
        int SessionTotalCount,
        long LastResponseLatencyMs);
}
