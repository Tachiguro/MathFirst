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
    private readonly IPreferenceStore? _preferenceStore;
    private Dictionary<string, FsrsCardState> _fsrsStates = new(StringComparer.Ordinal);
    private long _accumulatedActiveElapsedMs;
    private long _activeSegmentStartTimestamp;
    private bool _isTimingActive;
    private bool _isAppForeground = true;
    private bool _isPracticeSurfaceActive = true;
    private PracticeGateState _practiceGateState = PracticeGateState.Running;
    private long _practiceGateActivationRevision;
    private long _learnerStateGenerationRevision;
    private bool _requiresBackgroundResumeAfterAdvance;
    private int _sessionCorrectCountBeforePendingEvaluation;
    private int _sessionTotalCountBeforePendingEvaluation;
    private long _lastResponseLatencyBeforePendingEvaluation;
    private readonly ArithmeticCurriculum _curriculum = new();
    private readonly BandAdvancementEvaluator _bandAdvancementEvaluator = new();
    private readonly Dictionary<string, int> _sessionConsecutiveErrors = new(StringComparer.Ordinal);
    private readonly List<AttemptRecord> _sessionCheckInAttempts = [];
    private List<AttemptRecord> _recentAttempts = [];
    private IReadOnlyList<AttemptRecord> _currentDenseFrontierAttempts = [];
    private PracticeSelectionEvidence? _selectionEvidence;
    private readonly Dictionary<ArithmeticOperation, CachedOperationEvidence> _selectionEvidenceCache = new();

    private sealed record CachedOperationEvidence(
        ArithmeticOperation Operation,
        long ProspectivePracticePosition,
        PracticeSelectionEvidence Evidence,
        IReadOnlyList<AttemptRecord> DenseFrontierAttempts);

    public event EventHandler? AppForegroundStateChanged;

    public LearnerProgression Progression { get; private set; } = LearnerProgression.CreateFresh();
    public Dictionary<string, ItemLearningState> ItemStates { get; private set; } = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, FsrsCardState> FsrsStates => _fsrsStates;
    public PracticeCheckInSummary? PendingCheckIn { get; private set; }
    public int SessionOrderCounter { get; private set; }
    public int SessionCorrectCount { get; private set; }
    public int SessionTotalCount { get; private set; }
    public ArithmeticFact CurrentFact { get; private set; } = null!;
    public long ItemReadyTimestamp { get; private set; }
    public long CurrentFactExpectedPaceMs { get; private set; } = AdaptivePacePolicy.StaticPriorMs;
    public long CurrentFactEasyThresholdMs { get; private set; } = AdaptivePacePolicy.MaximumEasyThresholdMs;
    public long CurrentFactFluencyThresholdMs { get; private set; } = AdaptivePacePolicy.MaximumFluencyThresholdMs;
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
    /// <summary>
    /// Monotonic identity for the current non-running practice-gate activation.
    /// This transient presentation lifecycle metadata is deliberately not persisted.
    /// </summary>
    public long PracticeGateActivationRevision => _practiceGateActivationRevision;
    /// <summary>
    /// Monotonic identity for the authoritative learner-state generation held by
    /// this session object. This transient lifecycle metadata is not persisted.
    /// </summary>
    public long LearnerStateGenerationRevision => _learnerStateGenerationRevision;
    public bool IsInitialized { get; private set; }
    public DateTimeOffset? LatestAcceptedPracticeAt { get; private set; }

    public int GetConsecutiveErrorCount(string factId)
    {
        ArgumentNullException.ThrowIfNull(factId);
        return _sessionConsecutiveErrors.GetValueOrDefault(factId, 0);
    }

    public bool AcknowledgeTeachingIntervention(bool startTiming = true)
    {
        if (InteractionState != SessionInteractionState.TeachingIntervention)
        {
            return false;
        }

        if (PendingCheckIn is not null)
        {
            InteractionState = SessionInteractionState.SessionCheckIn;
            _isTimingActive = false;
            _isPracticeSurfaceActive = false;
            ReconcileTimingState();
            return true;
        }

        AdvanceToNextFact(startTiming);
        return true;
    }

    public bool AcknowledgeFeedback(bool startTiming = true)
    {
        if (InteractionState is not SessionInteractionState.IncorrectFeedback and not SessionInteractionState.TimeoutFeedback)
        {
            return false;
        }

        if (PendingCheckIn is not null)
        {
            InteractionState = SessionInteractionState.SessionCheckIn;
            _isTimingActive = false;
            _isPracticeSurfaceActive = false;
            ReconcileTimingState();
            return true;
        }

        AdvanceToNextFact(startTiming);
        return true;
    }

    public void ContinuePractice(bool startTiming = true)
    {
        if (InteractionState != SessionInteractionState.SessionCheckIn)
        {
            return;
        }

        PendingCheckIn = null;
        AdvanceToNextFact(startTiming);
    }

    public void TakeBreak()
    {
        if (InteractionState != SessionInteractionState.SessionCheckIn)
        {
            return;
        }

        PendingCheckIn = null;
        TransitionPracticeGate(PracticeGateState.ManualPause);
        AdvanceToNextFact(startTiming: true);
    }

    public TrainingSession(
        ILearnerStore store,
        IClock? clock = null,
        AdaptivePracticeSelector? selector = null,
        IFsrsScheduler? fsrsScheduler = null,
        IPreferenceStore? preferenceStore = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? MonotonicClock.Instance;
        _selector = selector ?? new AdaptivePracticeSelector();
        _fsrsScheduler = fsrsScheduler ?? new FsrsSchedulerAdapter();
        _preferenceStore = preferenceStore;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default,
        bool startTiming = true)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await _store.LoadRuntimeSnapshotAsync(cancellationToken).ConfigureAwait(false);
        ApplyRuntimeSnapshot(snapshot);
        _sessionConsecutiveErrors.Clear();
        _sessionCheckInAttempts.Clear();
        PendingCheckIn = null;
        SessionOrderCounter = 0;
        SessionCorrectCount = 0;
        SessionTotalCount = 0;
        LastResponseLatencyMs = 0;
        LastPersistenceResult = null;
        _requiresBackgroundResumeAfterAdvance = false;
        IsInitialized = true;

        await LoadNextSelectionEvidenceAsync(cancellationToken).ConfigureAwait(false);
        AdvanceToNextFact(startTiming);
    }

    public void ResetItemReadyTiming()
    {
        ItemReadyTimestamp = _clock.GetTimestamp();
        _activeSegmentStartTimestamp = ItemReadyTimestamp;
        _accumulatedActiveElapsedMs = 0;
        _isTimingActive = false;
        InteractionState = SessionInteractionState.AwaitingAnswer;

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
                TransitionPracticeGate(PracticeGateState.BackgroundResumeGate);
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

        TransitionPracticeGate(PracticeGateState.InitialReadyGate);
        ReconcileTimingState();
    }

    public void PausePractice()
    {
        if (InteractionState != SessionInteractionState.AwaitingAnswer ||
            _practiceGateState != PracticeGateState.Running)
        {
            return;
        }

        TransitionPracticeGate(PracticeGateState.ManualPause);
        ReconcileTimingState();
    }

    public void StartOrResumePractice()
    {
        if (_practiceGateState == PracticeGateState.Running)
        {
            return;
        }

        TransitionPracticeGate(PracticeGateState.Running);
        ReconcileTimingState();
    }

    private void TransitionPracticeGate(PracticeGateState nextState)
    {
        if (_practiceGateState == nextState)
        {
            return;
        }

        _practiceGateState = nextState;
        if (nextState != PracticeGateState.Running)
        {
            _practiceGateActivationRevision++;
        }
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

        if (elapsedMs >= CurrentFactDeadlineMs)
        {
            InteractionState = SessionInteractionState.TimeoutFeedback;
            return EvaluateAndRecord(AttemptOutcome.Timeout, null, elapsedMs, null);
        }

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
        var classification = AdaptiveAttemptClassifier.Classify(
            outcome,
            elapsedMs,
            CurrentFactEasyThresholdMs,
            CurrentFactFluencyThresholdMs);
        var isFluent = classification.IsFluent;

        var candidateProgression = CloneProgression(Progression);
        var practicePosition = checked(candidateProgression.PracticePosition + 1);
        candidateProgression.PracticePosition = practicePosition;

        // 3. Update FSRS card state
        _fsrsStates.TryGetValue(CurrentFact.Id, out var existingFsrsState);
        var updatedFsrsState = _fsrsScheduler.ReviewCard(
            existingFsrsState,
            CurrentFact.Id,
            classification.Rating,
            practicePosition,
            elapsedMs);

        // 4. Update ItemLearningState
        if (!ItemStates.TryGetValue(CurrentFact.Id, out var currentItemState))
        {
            currentItemState = ItemLearningState.CreateNew(CurrentFact);
        }
        var itemState = CloneItemState(currentItemState);

        itemState.TotalAttempts++;
        itemState.LastLatencyMs = elapsedMs;
        itemState.RollingLatencyMs = (itemState.RollingLatencyMs == 0) ? elapsedMs : (itemState.RollingLatencyMs + elapsedMs) / 2;
        itemState.LastPracticedOrder = SessionOrderCounter;
        itemState.LastPracticedAt = DateTimeOffset.UtcNow;

        if (isCorrect)
        {
            itemState.CorrectAttempts++;
            itemState.ConsecutiveCorrectStreak++;
            if (isFluent)
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

        itemState.IsProvisionallyMastered = LearningPolicy.EvaluateItemMastery(itemState, isFluent);

        // Evaluate only the scheduled operation from bounded positioned evidence.
        var operationProgressions = candidateProgression.OperationProgressions.ToDictionary(pair => pair.Key, pair => pair.Value);
        var currentOperationProgression = operationProgressions[CurrentFact.Operation];
        var operationCurriculum = _curriculum.GetCurriculum(CurrentFact.Operation);
        if (!operationCurriculum.TryGetBand(currentOperationProgression.BandIndex, out var currentBand) || currentBand is null)
        {
            throw new InvalidOperationException("The current target curriculum band is unavailable.");
        }

        var candidateAttempt = new BandAttemptEvidence(practicePosition, CurrentFact.Id, isCorrect, isFluent, elapsedMs);

        BandAdvancementEvidence advancementEvidence;
        if (currentBand.Kind == CurriculumBandKind.Dense)
        {
            var combinedDenseAttempts = new List<BandAttemptEvidence>();
            var candidateOverlaid = false;
            foreach (var existing in _currentDenseFrontierAttempts)
            {
                if (string.Equals(existing.FactId, CurrentFact.Id, StringComparison.Ordinal))
                {
                    combinedDenseAttempts.Add(candidateAttempt);
                    candidateOverlaid = true;
                }
                else
                {
                    combinedDenseAttempts.Add(new BandAttemptEvidence(
                        existing.PracticePosition!.Value,
                        existing.FactId,
                        existing.IsCorrect,
                        existing.IsFluent,
                        existing.ResponseLatencyMs));
                }
            }

            if (!candidateOverlaid)
            {
                var ownership = new AcquisitionOwnershipResolver(operationCurriculum);
                var ownedFactIds = ownership.GetOwnedFrontier(currentOperationProgression.BandIndex)
                    .Select(fact => fact.Id)
                    .ToHashSet(StringComparer.Ordinal);
                if (ownedFactIds.Contains(CurrentFact.Id))
                {
                    combinedDenseAttempts.Add(candidateAttempt);
                }
            }

            advancementEvidence = new BandAdvancementEvidence(
                combinedDenseAttempts,
                [],
                [],
                combinedDenseAttempts);
        }
        else
        {
            var operationAttempts = _recentAttempts
                .Where(existing => existing.Operation == CurrentFact.Operation)
                .Select(existing => new BandAttemptEvidence(
                    existing.PracticePosition!.Value,
                    existing.FactId,
                    existing.IsCorrect,
                    existing.IsFluent,
                    existing.ResponseLatencyMs))
                .Append(candidateAttempt);

            var ownership = new AcquisitionOwnershipResolver(operationCurriculum);
            var ownedFactIds = ownership.GetOwnedFrontier(currentOperationProgression.BandIndex)
                .Select(fact => fact.Id)
                .ToHashSet(StringComparer.Ordinal);

            var currentBandIntroductions = operationAttempts
                .Where(evidence => evidence.PracticePosition > currentOperationProgression.BandStartedPracticePosition && ownedFactIds.Contains(evidence.FactId))
                .Select(evidence => evidence.FactId);

            var lifetimeAttemptedFactIds = _selectionEvidence?.CurrentBandCandidates
                .Where(candidate => candidate.ItemState.TotalAttempts > 0)
                .Select(candidate => candidate.Fact.Id)
                .Append(itemState.TotalAttempts > 0 ? itemState.FactId : string.Empty)
                .Where(factId => !string.IsNullOrEmpty(factId))
                ?? [];

            advancementEvidence = new BandAdvancementEvidence(
                operationAttempts,
                lifetimeAttemptedFactIds,
                currentBandIntroductions);
        }

        var advancement = _bandAdvancementEvaluator.Evaluate(
            currentOperationProgression,
            operationCurriculum,
            advancementEvidence);
        operationProgressions[CurrentFact.Operation] = advancement.ResultingProgression;
        candidateProgression.OperationProgressions = operationProgressions;
        candidateProgression.UpdatedAt = DateTimeOffset.UtcNow;

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
            isFluent,
            elapsedMs,
            DateTimeOffset.UtcNow,
            outcome,
            practicePosition);

        var changeSet = new SubmissionChangeSet(
            submissionId,
            candidateProgression.StoreRevision,
            attempt,
            itemState,
            candidateProgression,
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
            OperationAdvanced: advancement.Advances)
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
            Progression = CloneProgression(LastEvaluation.ChangeSet.UpdatedProgression);
            ItemStates[LastEvaluation.ChangeSet.UpdatedItemState.FactId] = CloneItemState(LastEvaluation.ChangeSet.UpdatedItemState);
            if (LastEvaluation.ChangeSet.UpdatedFsrsState is not null)
            {
                _fsrsStates[LastEvaluation.ChangeSet.UpdatedFsrsState.FactId] = LastEvaluation.ChangeSet.UpdatedFsrsState;
            }
            SessionTotalCount++;
            SessionCorrectCount += LastEvaluation.IsCorrect ? 1 : 0;
            LastResponseLatencyMs = LastEvaluation.LatencyMs;
            Progression.StoreRevision = result.NewRevision.Value;
            LatestAcceptedPracticeAt = LastEvaluation.ChangeSet.Attempt.Timestamp;
            _recentAttempts = BoundRecentAttempts(_recentAttempts.Append(LastEvaluation.ChangeSet.Attempt));
            _sessionCheckInAttempts.Add(LastEvaluation.ChangeSet.Attempt);
            if (_sessionCheckInAttempts.Count == 20)
            {
                var correctAttempts = _sessionCheckInAttempts.Where(a => a.Outcome == AttemptOutcome.Correct).ToList();
                long? medianLatency = correctAttempts.Count > 0
                    ? AdaptivePacePolicy.Median(correctAttempts.Select(a => a.ResponseLatencyMs))
                    : null;
                PendingCheckIn = new PracticeCheckInSummary(
                    correctAttempts.Count,
                    _sessionCheckInAttempts.Count,
                    medianLatency);
                _sessionCheckInAttempts.Clear();
            }

            try
            {
                await LoadNextSelectionEvidenceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InteractionState = SessionInteractionState.PersistenceFailure;
                ReconcileTimingState();
                return PersistenceResult.Unavailable($"Evidence preparation failed: {ex.Message}");
            }

            if (LastEvaluation.Outcome == AttemptOutcome.Correct)
            {
                _sessionConsecutiveErrors[CurrentFact.Id] = 0;
                InteractionState = SessionInteractionState.CorrectFeedback;
            }
            else
            {
                var errorCount = _sessionConsecutiveErrors.GetValueOrDefault(CurrentFact.Id, 0) + 1;
                if (errorCount >= 2)
                {
                    _sessionConsecutiveErrors[CurrentFact.Id] = 0;
                    InteractionState = SessionInteractionState.TeachingIntervention;
                }
                else
                {
                    _sessionConsecutiveErrors[CurrentFact.Id] = errorCount;
                    InteractionState = LastEvaluation.Outcome switch
                    {
                        AttemptOutcome.Timeout => SessionInteractionState.TimeoutFeedback,
                        _ => SessionInteractionState.IncorrectFeedback
                    };
                }
            }
        }
        else
        {
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

        if (LastPersistenceResult.IsSuccess)
        {
            try
            {
                await LoadNextSelectionEvidenceAsync(cancellationToken).ConfigureAwait(false);
                if (LastEvaluation.Outcome == AttemptOutcome.Correct)
                {
                    _sessionConsecutiveErrors[CurrentFact.Id] = 0;
                    InteractionState = SessionInteractionState.CorrectFeedback;
                }
                else
                {
                    var errorCount = _sessionConsecutiveErrors.GetValueOrDefault(CurrentFact.Id, 0) + 1;
                    if (errorCount >= 2)
                    {
                        _sessionConsecutiveErrors[CurrentFact.Id] = 0;
                        InteractionState = SessionInteractionState.TeachingIntervention;
                    }
                    else
                    {
                        _sessionConsecutiveErrors[CurrentFact.Id] = errorCount;
                        InteractionState = LastEvaluation.Outcome switch
                        {
                            AttemptOutcome.Timeout => SessionInteractionState.TimeoutFeedback,
                            _ => SessionInteractionState.IncorrectFeedback
                        };
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        var result = await CommitCurrentEvaluationAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    private async Task ReloadAuthoritativeStateAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _store.LoadRuntimeSnapshotAsync(cancellationToken).ConfigureAwait(false);
        ApplyRuntimeSnapshot(snapshot);
        SessionCorrectCount = _sessionCorrectCountBeforePendingEvaluation;
        SessionTotalCount = _sessionTotalCountBeforePendingEvaluation;
        LastResponseLatencyMs = _lastResponseLatencyBeforePendingEvaluation;
        LastEvaluation = null;
        LastPersistenceResult = null;
        await LoadNextSelectionEvidenceAsync(cancellationToken).ConfigureAwait(false);
        AdvanceToNextFact(startTiming: true);
    }

    private IReadOnlyList<ArithmeticOperation> GetCurrentEnabledOperations() =>
        _preferenceStore?.GetEnabledOperations() ?? PracticeOperationPreferencePolicy.AllOperations;

    private PracticeTimeSetting GetCurrentPracticeTimeSetting() =>
        _preferenceStore?.GetPracticeTimeSetting() ?? PracticeTimeSetting.Standard;

    public void AdvanceToNextFact(bool startTiming = true)
    {
        var prospectivePosition = checked(Progression.PracticePosition + 1);
        var enabledOperations = GetCurrentEnabledOperations();
        var scheduledOperation = AdaptivePracticeSelector.GetScheduledOperation(prospectivePosition, enabledOperations);

        if (!_selectionEvidenceCache.TryGetValue(scheduledOperation, out var cached)
            || cached.Operation != scheduledOperation
            || cached.ProspectivePracticePosition != prospectivePosition)
        {
            var fallback = _selectionEvidenceCache.Values
                .FirstOrDefault(c => enabledOperations.Contains(c.Operation) && c.ProspectivePracticePosition == prospectivePosition);
            if (fallback is not null)
            {
                cached = fallback;
                scheduledOperation = fallback.Operation;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Bounded selection evidence for operation {scheduledOperation} at prospective position {prospectivePosition} is not loaded.");
            }
        }

        _selectionEvidence = cached.Evidence;
        _currentDenseFrontierAttempts = cached.DenseFrontierAttempts;

        SessionOrderCounter++;
        var practiceTimeSetting = GetCurrentPracticeTimeSetting();
        var context = new PracticeSelectionContext(
            prospectivePosition,
            SessionOrderCounter,
            Progression.OperationProgressions,
            Enum.GetValues<ArithmeticOperation>().ToDictionary(operation => operation, operation => _curriculum.GetCurriculum(operation)),
            new PracticeCandidateIndex(_selectionEvidence),
            _recentAttempts.OrderBy(attempt => attempt.PracticePosition).Select(attempt => new ArithmeticFact(attempt.Operation, attempt.LeftOperand, attempt.RightOperand)),
            enabledOperations);
        CurrentFact = _selector.SelectTargetFact(context).Fact;
        var currentProgression = Progression.OperationProgressions[CurrentFact.Operation];
        var ownedFrontierFactIds = new AcquisitionOwnershipResolver(_curriculum.GetCurriculum(CurrentFact.Operation))
            .GetOwnedFrontier(currentProgression.BandIndex)
            .Select(fact => fact.Id);
        var isProven = ItemStates.TryGetValue(CurrentFact.Id, out var itemState) && itemState.CorrectAttempts > 0;
        var adaptivePace = AdaptivePacePolicy.Calculate(CurrentFact, ownedFrontierFactIds, _recentAttempts, isProven);
        CurrentFactExpectedPaceMs = adaptivePace.FactPaceMs;
        CurrentFactEasyThresholdMs = adaptivePace.EasyThresholdMs;
        CurrentFactFluencyThresholdMs = adaptivePace.FluencyThresholdMs;
        var deadlineFloorMs = PracticeTimePreferencePolicy.GetDeadlineFloorMs(practiceTimeSetting);
        CurrentFactDeadlineMs = Math.Max(adaptivePace.DeadlineMs, deadlineFloorMs);

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
            TransitionPracticeGate(PracticeGateState.BackgroundResumeGate);
            _requiresBackgroundResumeAfterAdvance = false;
        }

        ReconcileTimingState();
    }

    public bool AdvanceAfterCorrectAnswer(bool startTiming = true)
    {
        if (InteractionState != SessionInteractionState.CorrectFeedback ||
            LastEvaluation?.Outcome != AttemptOutcome.Correct)
        {
            return false;
        }

        if (PendingCheckIn is not null)
        {
            InteractionState = SessionInteractionState.SessionCheckIn;
            _isTimingActive = false;
            _isPracticeSurfaceActive = false;
            ReconcileTimingState();
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
        var snapshot = await _store.LoadRuntimeSnapshotAsync(cancellationToken).ConfigureAwait(false);
        ApplyRuntimeSnapshot(snapshot);
        _sessionConsecutiveErrors.Clear();
        _sessionCheckInAttempts.Clear();
        PendingCheckIn = null;
        SessionCorrectCount = 0;
        SessionTotalCount = 0;
        SessionOrderCounter = 0;
        LastResponseLatencyMs = 0;
        LastPersistenceResult = null;
        _requiresBackgroundResumeAfterAdvance = false;
        await LoadNextSelectionEvidenceAsync(cancellationToken).ConfigureAwait(false);
        AdvanceToNextFact(shouldStartTiming);
    }

    private void ApplyRuntimeSnapshot(LearnerSnapshot snapshot)
    {
        _learnerStateGenerationRevision = checked(_learnerStateGenerationRevision + 1);
        Progression = snapshot.Progression;
        ItemStates = snapshot.ItemStates.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
        _fsrsStates = snapshot.FsrsStates.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
        Progression.OperationProgressions = (snapshot.OperationProgressions ?? Progression.OperationProgressions)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        _recentAttempts = snapshot.RecentAttempts.Where(attempt => attempt.PracticePosition is > 0).ToList();
        _currentDenseFrontierAttempts = [];
        _selectionEvidence = null;
        _selectionEvidenceCache.Clear();
        LatestAcceptedPracticeAt = snapshot.LatestAcceptedPracticeAt;
    }

    private async Task LoadNextSelectionEvidenceAsync(CancellationToken cancellationToken)
    {
        var prospectivePosition = checked(Progression.PracticePosition + 1);
        _selectionEvidenceCache.Clear();

        var enabledOperations = GetCurrentEnabledOperations();
        var scheduledOperation = AdaptivePracticeSelector.GetScheduledOperation(prospectivePosition, enabledOperations);

        // 1. Authoritatively load evidence for enabled operations.
        foreach (var operation in enabledOperations)
        {
            await LoadSingleOperationEvidenceAsync(operation, prospectivePosition, cancellationToken).ConfigureAwait(false);
        }

        // 2. Best-effort defensive prefetch for disabled operations.
        // Failures in loading evidence for disabled operations must never fail practice for enabled operations.
        var disabledOperations = PracticeOperationPreferencePolicy.AllOperations
            .Where(op => !enabledOperations.Contains(op));

        foreach (var operation in disabledOperations)
        {
            try
            {
                await LoadSingleOperationEvidenceAsync(operation, prospectivePosition, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Defensive isolation: errors prefetching evidence for disabled operations are ignored.
            }
        }

        if (_selectionEvidenceCache.TryGetValue(scheduledOperation, out var scheduledEvidence))
        {
            _selectionEvidence = scheduledEvidence.Evidence;
            _currentDenseFrontierAttempts = scheduledEvidence.DenseFrontierAttempts;
        }
    }

    private async Task LoadSingleOperationEvidenceAsync(
        ArithmeticOperation operation,
        long prospectivePosition,
        CancellationToken cancellationToken)
    {
        var progression = Progression.OperationProgressions[operation];
        var curriculum = _curriculum.GetCurriculum(operation);
        if (!curriculum.TryGetBand(progression.BandIndex, out var band) || band is null)
        {
            throw new InvalidOperationException($"The current target curriculum band for operation {operation} is unavailable.");
        }

        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(progression.BandIndex);
        var introductionFrontier = band.Kind == CurriculumBandKind.Structured
            ? DeterministicFactRanker.SelectStructuredSample(ownedFrontier, operation, band.Id)
            : ownedFrontier;
        var request = new PracticeSelectionEvidenceRequest(
            operation,
            prospectivePosition,
            checked(SessionOrderCounter + 1),
            ownedFrontier,
            introductionFrontier);
        var evidence = await _store.LoadPracticeSelectionEvidenceAsync(request, cancellationToken).ConfigureAwait(false);
        foreach (var (factId, state) in evidence.ItemStates)
        {
            ItemStates[factId] = CloneItemState(state);
        }

        foreach (var (factId, state) in evidence.FsrsStates)
        {
            _fsrsStates[factId] = state;
        }

        IReadOnlyList<AttemptRecord> denseAttempts = [];
        if (band.Kind == CurriculumBandKind.Dense)
        {
            var frontierFactIds = ownedFrontier.Select(fact => fact.Id).ToArray();
            denseAttempts = await _store.LoadLatestFrontierAttemptsAsync(
                operation,
                progression.BandStartedPracticePosition,
                frontierFactIds,
                cancellationToken).ConfigureAwait(false);
        }

        _selectionEvidenceCache[operation] = new CachedOperationEvidence(
            operation,
            prospectivePosition,
            evidence,
            denseAttempts);
    }

    private static LearnerProgression CloneProgression(LearnerProgression source) => new()
    {
        PracticePosition = source.PracticePosition,
        OperationProgressions = source.OperationProgressions.ToDictionary(pair => pair.Key, pair => pair.Value),
        StoreRevision = source.StoreRevision,
        SchemaVersion = source.SchemaVersion,
        UpdatedAt = source.UpdatedAt
    };

    private static List<AttemptRecord> BoundRecentAttempts(IEnumerable<AttemptRecord> attempts)
    {
        var positioned = attempts
            .Where(attempt => attempt.PracticePosition is > 0)
            .OrderBy(attempt => attempt.PracticePosition)
            .ToArray();
        var requiredForAdvancement = positioned
            .GroupBy(attempt => attempt.Operation)
            .SelectMany(group => group.OrderByDescending(attempt => attempt.PracticePosition).Take(40));
        var requiredForCooldown = positioned.TakeLast(LearningPolicy.ExactFactCooldownDistance);

        return requiredForAdvancement
            .Concat(requiredForCooldown)
            .GroupBy(attempt => attempt.SubmissionId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(attempt => attempt.PracticePosition)
            .ToList();
    }

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
}
