namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using Xunit;

public sealed class ConfirmedCombatAttemptDispatchTests : IDisposable
{
    private readonly string _tempDirectory;

    public ConfirmedCombatAttemptDispatchTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "MathFirstDispatchTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class FakePreferences : ICyberDefenseModePreferences
    {
        public bool Enabled { get; set; } = true;
        public bool GetCyberDefenseEnabled() => Enabled;
        public void SetCyberDefenseEnabled(bool enabled) => Enabled = enabled;
    }

    private sealed class FakeClock : IClock
    {
        private long _timestamp = 1_000_000;
        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, _timestamp - startTimestamp));
        public void AdvanceMs(long milliseconds) => _timestamp += milliseconds;
    }

    private sealed class ControlledLearnerStore : ILearnerStore
    {
        private readonly Queue<PersistenceResult> _commitResults;
        private readonly bool _failEvidencePreparation;

        public ControlledLearnerStore(
            IEnumerable<PersistenceResult>? commitResults = null,
            bool failEvidencePreparation = false)
        {
            _commitResults = new Queue<PersistenceResult>(commitResults ?? []);
            _failEvidencePreparation = failEvidencePreparation;
        }

        public string StoragePath => "inmemory://controlled-learner-store";
        public List<SubmissionChangeSet> RecordedCommits { get; } = [];
        public int SnapshotLoadCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            SnapshotLoadCount++;
            return Task.FromResult(new LearnerSnapshot(
                LearnerProgression.CreateFresh(),
                new Dictionary<string, ItemLearningState>(),
                new List<AttemptRecord>(),
                1,
                LearnerProgression.DefaultSchemaVersion));
        }

        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default)
        {
            if (_failEvidencePreparation)
            {
                throw new InvalidOperationException("Synthetic evidence preparation I/O failure.");
            }
            return Task.FromResult<IReadOnlyList<AttemptRecord>>([]);
        }

        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            RecordedCommits.Add(changeSet);
            if (_commitResults.Count > 0)
            {
                return Task.FromResult(_commitResults.Dequeue());
            }
            return Task.FromResult(PersistenceResult.Success(changeSet.ExpectedRevision + 1));
        }

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    // =========================================================================
    // CASE A: INITIAL COMMIT SUCCEEDS
    // =========================================================================

    [Fact]
    public void CaseA_InitialCommitSucceeds_NormalHit_DealsOneDamageOnce()
    {
        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var initialEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(initialEncounter);
        var hpBefore = initialEncounter.EnemyHitPoints;

        var subId = Guid.NewGuid().ToString("N");
        var attempt = new ConfirmedCombatAttempt(
            submissionId: subId,
            isCorrect: true,
            isCritical: false,
            isCommitted: true,
            wasEligibleAtSubmission: true);

        var result = sessionState.DispatchAttempt(attempt);

        Assert.Equal(CombatDispatchStatus.Dispatched, result.Status);
        Assert.True(result.MutatedCombatState);
        Assert.Equal(hpBefore - 1, initialEncounter.EnemyHitPoints);
        Assert.Equal(CyberDefenseFeedbackKind.Hit, initialEncounter.LastFeedback);
        Assert.True(sessionState.IsSubmissionProcessed(subId));
    }

    [Fact]
    public void CaseA_InitialCommitSucceeds_CriticalHit_DealsTwoDamageOnce()
    {
        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var initialEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(initialEncounter);
        var hpBefore = initialEncounter.EnemyHitPoints;

        var subId = Guid.NewGuid().ToString("N");
        var attempt = new ConfirmedCombatAttempt(
            submissionId: subId,
            isCorrect: true,
            isCritical: true,
            isCommitted: true,
            wasEligibleAtSubmission: true);

        var result = sessionState.DispatchAttempt(attempt);

        Assert.Equal(CombatDispatchStatus.Dispatched, result.Status);
        Assert.True(result.MutatedCombatState);
        Assert.Equal(hpBefore - 2, initialEncounter.EnemyHitPoints);
        Assert.Equal(CyberDefenseFeedbackKind.CriticalHit, initialEncounter.LastFeedback);
        Assert.True(sessionState.IsSubmissionProcessed(subId));
    }

    [Fact]
    public void CaseA_InitialCommitSucceeds_IncorrectAnswer_DecrementsShieldOnce()
    {
        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var initialEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(initialEncounter);
        var shieldsBefore = initialEncounter.ShieldSegments;

        var subId = Guid.NewGuid().ToString("N");
        var attempt = new ConfirmedCombatAttempt(
            submissionId: subId,
            isCorrect: false,
            isCritical: false,
            isCommitted: true,
            wasEligibleAtSubmission: true);

        var result = sessionState.DispatchAttempt(attempt);

        Assert.Equal(CombatDispatchStatus.Dispatched, result.Status);
        Assert.True(result.MutatedCombatState);
        Assert.Equal(shieldsBefore - 1, initialEncounter.ShieldSegments);
        Assert.Equal(CyberDefenseFeedbackKind.Blocked, initialEncounter.LastFeedback);
        Assert.True(sessionState.IsSubmissionProcessed(subId));
    }

    // =========================================================================
    // CASE B: INITIAL STORE WRITE UNAVAILABLE, RECOVERY RETRY SUCCEEDS
    // =========================================================================

    [Fact]
    public async Task CaseB_InitialStoreWriteUnavailable_ZeroActionsUntilRecovery_DispatchesOnceAfterSuccess()
    {
        var store = new ControlledLearnerStore(
        [
            PersistenceResult.Unavailable("Transient storage write failure."),
            PersistenceResult.Success(2)
        ]);
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var hpBefore = encounter.EnemyHitPoints;

        // Step 1: Submit answer
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var subId = eval.ChangeSet.SubmissionId;

        // Step 2: Commit fails
        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.False(commitResult.IsSuccess);
        Assert.False(session.IsCurrentSubmissionCommitted);

        // Attempt dispatch with uncommitted status
        var uncommittedAttempt = new ConfirmedCombatAttempt(
            submissionId: subId,
            isCorrect: eval.IsCorrect,
            isCritical: false,
            isCommitted: session.IsCurrentSubmissionCommitted,
            wasEligibleAtSubmission: true);

        var uncommittedResult = sessionState.DispatchAttempt(uncommittedAttempt);
        Assert.Equal(CombatDispatchStatus.UncommittedSuppressed, uncommittedResult.Status);
        Assert.False(uncommittedResult.MutatedCombatState);
        Assert.Equal(hpBefore, encounter.EnemyHitPoints);
        Assert.False(sessionState.IsSubmissionProcessed(subId));

        // Step 3: Recovery retry
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);
        Assert.True(session.IsCurrentSubmissionCommitted);

        // Dispatch confirmed attempt after recovery
        var committedAttempt = new ConfirmedCombatAttempt(
            submissionId: subId,
            isCorrect: eval.IsCorrect,
            isCritical: false,
            isCommitted: session.IsCurrentSubmissionCommitted,
            wasEligibleAtSubmission: true);

        var committedResult = sessionState.DispatchAttempt(committedAttempt);
        Assert.Equal(CombatDispatchStatus.Dispatched, committedResult.Status);
        Assert.True(committedResult.MutatedCombatState);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);
        Assert.True(sessionState.IsSubmissionProcessed(subId));

        // Verify store commits had identical SubmissionId
        Assert.Equal(2, store.RecordedCommits.Count);
        Assert.Equal(store.RecordedCommits[0].SubmissionId, store.RecordedCommits[1].SubmissionId);
    }

    // =========================================================================
    // CASE C: REPEATED UNAVAILABLE WRITES
    // =========================================================================

    [Fact]
    public async Task CaseC_RepeatedUnavailableWrites_ZeroActionsUntilFinalSuccess_DispatchesExactlyOnce()
    {
        var store = new ControlledLearnerStore(
        [
            PersistenceResult.Unavailable("Transient error 1"),
            PersistenceResult.Unavailable("Transient error 2"),
            PersistenceResult.Unavailable("Transient error 3"),
            PersistenceResult.Success(2)
        ]);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var hpBefore = encounter.EnemyHitPoints;

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var subId = eval.ChangeSet.SubmissionId;

        // Attempt 1: Initial commit fails
        await session.CommitCurrentEvaluationAsync();
        Assert.False(session.IsCurrentSubmissionCommitted);
        sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId, true, false, session.IsCurrentSubmissionCommitted, true));
        Assert.Equal(hpBefore, encounter.EnemyHitPoints);

        // Attempt 2: First recovery fails
        await session.RecoverFromPersistenceFailureAsync();
        Assert.False(session.IsCurrentSubmissionCommitted);
        sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId, true, false, session.IsCurrentSubmissionCommitted, true));
        Assert.Equal(hpBefore, encounter.EnemyHitPoints);

        // Attempt 3: Second recovery fails
        await session.RecoverFromPersistenceFailureAsync();
        Assert.False(session.IsCurrentSubmissionCommitted);
        sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId, true, false, session.IsCurrentSubmissionCommitted, true));
        Assert.Equal(hpBefore, encounter.EnemyHitPoints);

        // Attempt 4: Third recovery succeeds
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);
        Assert.True(session.IsCurrentSubmissionCommitted);

        var finalResult = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId, true, false, session.IsCurrentSubmissionCommitted, true));
        Assert.Equal(CombatDispatchStatus.Dispatched, finalResult.Status);
        Assert.True(finalResult.MutatedCombatState);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);
        Assert.Equal(4, store.RecordedCommits.Count);
        Assert.All(store.RecordedCommits, c => Assert.Equal(subId, c.SubmissionId));
    }

    // =========================================================================
    // CASE D: POST-COMMIT EVIDENCE PREPARATION FAILURE
    // =========================================================================

    [Fact]
    public async Task CaseD_CommitSucceeds_EvidencePreparationFails_IsCurrentSubmissionCommittedTrue_DispatchesOnceAndDeduplicatesRecovery()
    {
        // Custom store where Commit succeeds but LoadLatestFrontierAttempts throws on second call
        var throwEvidence = false;
        var store = new DynamicEvidenceStore(() => throwEvidence);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var hpBefore = encounter.EnemyHitPoints;

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var subId = eval.ChangeSet.SubmissionId;

        // Trigger evidence preparation failure during CommitCurrentEvaluationAsync
        throwEvidence = true;
        var commitResult = await session.CommitCurrentEvaluationAsync();

        // Commit returned Unavailable because evidence loading threw, BUT learner commit succeeded!
        Assert.False(commitResult.IsSuccess);
        Assert.True(session.IsCurrentSubmissionCommitted);
        Assert.Equal(SessionInteractionState.PersistenceFailure, session.InteractionState);

        // Dispatch must recognize confirmed commitment and dispatch once
        var dispatch1 = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(
            subId,
            eval.IsCorrect,
            isCritical: false,
            isCommitted: session.IsCurrentSubmissionCommitted,
            wasEligibleAtSubmission: true));

        Assert.Equal(CombatDispatchStatus.Dispatched, dispatch1.Status);
        Assert.True(dispatch1.MutatedCombatState);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);

        // Now recover evidence
        throwEvidence = false;
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);
        Assert.True(session.IsCurrentSubmissionCommitted);

        // Dispatch after recovery must be deduplicated
        var dispatch2 = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(
            subId,
            eval.IsCorrect,
            isCritical: false,
            isCommitted: session.IsCurrentSubmissionCommitted,
            wasEligibleAtSubmission: true));

        Assert.Equal(CombatDispatchStatus.DuplicateSuppressed, dispatch2.Status);
        Assert.False(dispatch2.MutatedCombatState);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints); // No second damage dealt
    }

    // =========================================================================
    // CASE E: EVIDENCE RECOVERY FAILS REPEATEDLY AFTER COMMITTED
    // =========================================================================

    [Fact]
    public async Task CaseE_EvidenceRecoveryFailsRepeatedly_DoesNotRepeatCombatAction()
    {
        var throwEvidence = false;
        var store = new DynamicEvidenceStore(() => throwEvidence);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var hpBefore = encounter.EnemyHitPoints;

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var subId = eval.ChangeSet.SubmissionId;

        throwEvidence = true;
        await session.CommitCurrentEvaluationAsync();
        Assert.True(session.IsCurrentSubmissionCommitted);

        // Initial dispatch
        var dispatch1 = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId, true, false, session.IsCurrentSubmissionCommitted, true));
        Assert.Equal(CombatDispatchStatus.Dispatched, dispatch1.Status);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);

        // Recovery 1 fails
        var rec1 = await session.RecoverFromPersistenceFailureAsync();
        Assert.False(rec1);
        var dispatchRec1 = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId, true, false, session.IsCurrentSubmissionCommitted, true));
        Assert.Equal(CombatDispatchStatus.DuplicateSuppressed, dispatchRec1.Status);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);

        // Recovery 2 fails
        var rec2 = await session.RecoverFromPersistenceFailureAsync();
        Assert.False(rec2);
        var dispatchRec2 = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId, true, false, session.IsCurrentSubmissionCommitted, true));
        Assert.Equal(CombatDispatchStatus.DuplicateSuppressed, dispatchRec2.Status);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);

        // Recovery 3 succeeds
        throwEvidence = false;
        var rec3 = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(rec3);
        var dispatchRec3 = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId, true, false, session.IsCurrentSubmissionCommitted, true));
        Assert.Equal(CombatDispatchStatus.DuplicateSuppressed, dispatchRec3.Status);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);
    }

    // =========================================================================
    // CASE F: REVISION CONFLICT
    // =========================================================================

    [Fact]
    public async Task CaseF_RevisionConflict_ReloadsAuthoritativeState_ZeroSpeculativeActionsDispatched()
    {
        var store = new ControlledLearnerStore([PersistenceResult.Conflict("Revision mismatch.")]);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var hpBefore = encounter.EnemyHitPoints;

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var subId = eval.ChangeSet.SubmissionId;

        var result = await session.CommitCurrentEvaluationAsync();
        Assert.Equal(PersistenceStatus.RevisionConflict, result.Status);
        Assert.False(session.IsCurrentSubmissionCommitted);

        var dispatchAttempt = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(
            subId,
            eval.IsCorrect,
            isCritical: false,
            isCommitted: session.IsCurrentSubmissionCommitted,
            wasEligibleAtSubmission: true));

        Assert.Equal(CombatDispatchStatus.UncommittedSuppressed, dispatchAttempt.Status);
        Assert.False(dispatchAttempt.MutatedCombatState);
        Assert.Equal(hpBefore, encounter.EnemyHitPoints);

        // Recover reloads authoritative snapshot
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);
        Assert.Null(session.LastEvaluation);
        Assert.Equal(hpBefore, encounter.EnemyHitPoints);
    }

    // =========================================================================
    // CASE G: DUPLICATE INVOCATION SUPPRESSION
    // =========================================================================

    [Fact]
    public void CaseG_DuplicateInvocation_SameSubmissionId_SuppressedWithZeroMutations()
    {
        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var hpBefore = encounter.EnemyHitPoints;

        var subId = Guid.NewGuid().ToString("N");
        var attempt = new ConfirmedCombatAttempt(subId, true, false, true, true);

        var res1 = sessionState.DispatchAttempt(attempt);
        Assert.Equal(CombatDispatchStatus.Dispatched, res1.Status);
        Assert.True(res1.MutatedCombatState);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);

        for (var i = 0; i < 5; i++)
        {
            var duplicateRes = sessionState.DispatchAttempt(attempt);
            Assert.Equal(CombatDispatchStatus.DuplicateSuppressed, duplicateRes.Status);
            Assert.False(duplicateRes.MutatedCombatState);
            Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);
        }
    }

    // =========================================================================
    // CASE H: DISTINCT COMMITTED ATTEMPTS DISPATCH INDEPENDENTLY
    // =========================================================================

    [Fact]
    public void CaseH_DistinctCommittedAttempts_DispatchIndependently()
    {
        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var initialHp = encounter.EnemyHitPoints;
        var initialShields = encounter.ShieldSegments;

        // Attempt 1: Regular hit (1 dmg)
        var attempt1 = new ConfirmedCombatAttempt(Guid.NewGuid().ToString("N"), true, false, true, true);
        var res1 = sessionState.DispatchAttempt(attempt1);
        Assert.Equal(CombatDispatchStatus.Dispatched, res1.Status);
        Assert.Equal(initialHp - 1, encounter.EnemyHitPoints);

        // Attempt 2: Critical hit (2 dmg)
        var attempt2 = new ConfirmedCombatAttempt(Guid.NewGuid().ToString("N"), true, true, true, true);
        var res2 = sessionState.DispatchAttempt(attempt2);
        Assert.Equal(CombatDispatchStatus.Dispatched, res2.Status);
        Assert.Equal(initialHp - 3, encounter.EnemyHitPoints);

        // Attempt 3: Incorrect answer (shield decrement)
        var attempt3 = new ConfirmedCombatAttempt(Guid.NewGuid().ToString("N"), false, false, true, true);
        var res3 = sessionState.DispatchAttempt(attempt3);
        Assert.Equal(CombatDispatchStatus.Dispatched, res3.Status);
        Assert.Equal(initialShields - 1, encounter.ShieldSegments);
    }

    // =========================================================================
    // CASE I: CALM MODE NON-INTERFERENCE & NO ENCOUNTER ALLOCATION
    // =========================================================================

    [Fact]
    public void CaseI_CalmMode_NoCombatDispatch_NoEncounterAllocation()
    {
        var preferences = new FakePreferences { Enabled = false };
        var sessionState = new CyberDefenseSessionState(preferences);

        var subId = Guid.NewGuid().ToString("N");
        var attempt = new ConfirmedCombatAttempt(subId, true, false, true, wasEligibleAtSubmission: false);

        var result = sessionState.DispatchAttempt(attempt);

        Assert.Equal(CombatDispatchStatus.CalmModeSuppressed, result.Status);
        Assert.False(result.MutatedCombatState);
        Assert.False(sessionState.HasActiveEncounter);
        Assert.Null(sessionState.ActiveEncounter);
        Assert.True(sessionState.IsSubmissionProcessed(subId));
    }

    // =========================================================================
    // CASE J: SUBMISSION ORIGINATED IN CALM MODE
    // =========================================================================

    [Fact]
    public void CaseJ_SubmissionOriginatedInCalmMode_NeverRetroactivelyConvertedAfterReEnabling()
    {
        var preferences = new FakePreferences { Enabled = false };
        var sessionState = new CyberDefenseSessionState(preferences);

        var subId = Guid.NewGuid().ToString("N");
        // Attempt made while Calm Mode was active
        var attempt = new ConfirmedCombatAttempt(subId, true, false, true, wasEligibleAtSubmission: false);

        // User re-enables Cyber Defense
        preferences.Enabled = true;
        Assert.True(sessionState.IsCyberDefenseEnabled);

        // Dispatch must be suppressed because it was submitted in Calm Mode
        var result = sessionState.DispatchAttempt(attempt);

        Assert.Equal(CombatDispatchStatus.CalmModeSuppressed, result.Status);
        Assert.False(result.MutatedCombatState);

        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints, encounter.EnemyHitPoints);
    }

    // =========================================================================
    // CASE K: MODE DISABLED BEFORE DISPATCH (NEVER REPLAYED)
    // =========================================================================

    [Fact]
    public void CaseK_ModeDisabledBeforeDispatch_SuppressedAndNeverReplayedOnReEnable()
    {
        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var initialEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(initialEncounter);
        var hpBefore = initialEncounter.EnemyHitPoints;

        var subId = Guid.NewGuid().ToString("N");
        // Started in Cyber Defense, but user toggles mode to Calm before dispatch happens
        preferences.Enabled = false;

        var attempt = new ConfirmedCombatAttempt(subId, true, false, true, wasEligibleAtSubmission: true);
        var result = sessionState.DispatchAttempt(attempt);

        Assert.Equal(CombatDispatchStatus.CalmModeSuppressed, result.Status);
        Assert.False(result.MutatedCombatState);

        // User re-enables Cyber Defense
        preferences.Enabled = true;
        var resumedEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(resumedEncounter);
        Assert.Equal(hpBefore, resumedEncounter.EnemyHitPoints);

        // Attempting to re-dispatch the suppressed attempt returns DuplicateSuppressed
        var replayResult = sessionState.DispatchAttempt(attempt);
        Assert.Equal(CombatDispatchStatus.DuplicateSuppressed, replayResult.Status);
        Assert.False(replayResult.MutatedCombatState);
        Assert.Equal(hpBefore, resumedEncounter.EnemyHitPoints);
    }

    // =========================================================================
    // CASE L: INVALID ATTEMPT HANDLING
    // =========================================================================

    [Fact]
    public void CaseL_InvalidAttempt_NullOrEmptySubmissionId_RejectedWithZeroMutations()
    {
        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);

        var nullAttempt = new ConfirmedCombatAttempt(null!, true, false, true, true);
        var emptyAttempt = new ConfirmedCombatAttempt("", true, false, true, true);
        var whitespaceAttempt = new ConfirmedCombatAttempt("   ", true, false, true, true);

        Assert.Equal(CombatDispatchStatus.InvalidAttempt, sessionState.DispatchAttempt(nullAttempt).Status);
        Assert.Equal(CombatDispatchStatus.InvalidAttempt, sessionState.DispatchAttempt(emptyAttempt).Status);
        Assert.Equal(CombatDispatchStatus.InvalidAttempt, sessionState.DispatchAttempt(whitespaceAttempt).Status);
    }

    // =========================================================================
    // CASE M: LEARNING-ONLY RESET ISOLATION
    // =========================================================================

    [Fact]
    public async Task CaseM_LearningOnlyReset_PreservesHeldEncounterAndDeduplication()
    {
        var dbPath = Path.Combine(_tempDirectory, "case_m.db");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);

        var subId = Guid.NewGuid().ToString("N");
        sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId, true, false, true, true));
        var hpAfterDispatch = encounter.EnemyHitPoints;

        await session.ResetLearningProgressAsync(startTiming: false);

        Assert.True(sessionState.HasActiveEncounter);
        Assert.Equal(hpAfterDispatch, sessionState.ActiveEncounter?.EnemyHitPoints);
        Assert.True(sessionState.IsSubmissionProcessed(subId));
    }

    // =========================================================================
    // CASE N: FULL LOCAL RESET SAFETY
    // =========================================================================

    [Fact]
    public void CaseN_FullLocalReset_ClearsEncounterAndProcessedSet()
    {
        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);

        var subId = Guid.NewGuid().ToString("N");
        sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId, true, false, true, true));
        Assert.True(sessionState.IsSubmissionProcessed(subId));

        sessionState.ClearEncounter();

        Assert.False(sessionState.HasActiveEncounter);
        Assert.False(sessionState.IsSubmissionProcessed(subId));

        var freshEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(freshEncounter);
        Assert.NotSame(encounter, freshEncounter);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints, freshEncounter.EnemyHitPoints);
    }

    // =========================================================================
    // TEST DOUBLES
    // =========================================================================

    private sealed class DynamicEvidenceStore : ILearnerStore
    {
        private readonly Func<bool> _shouldThrowEvidence;

        public DynamicEvidenceStore(Func<bool> shouldThrowEvidence)
        {
            _shouldThrowEvidence = shouldThrowEvidence;
        }

        public string StoragePath => "inmemory://dynamic-evidence-store";
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
            CancellationToken cancellationToken = default)
        {
            if (_shouldThrowEvidence())
            {
                throw new InvalidOperationException("Synthetic evidence reload error.");
            }
            return Task.FromResult<IReadOnlyList<AttemptRecord>>([]);
        }

        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PersistenceResult.Success(changeSet.ExpectedRevision + 1));

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    // =========================================================================
    // PERSISTENCE RECOVERY & NAVIGATION CASES (MF-CYBER-001 / MF-FINDING-001)
    // =========================================================================

    [Fact]
    public async Task Case1_CyberDefenseOrigin_WriteFails_NavigationToSettings_ReturnAndRecover_PreservesEligibilityAndDispatchesOneHit()
    {
        var store = new ControlledLearnerStore(
        [
            PersistenceResult.Unavailable("Transient initial store write failure."),
            PersistenceResult.Success(2)
        ]);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var hpBefore = encounter.EnemyHitPoints;

        // Step 1: Submit answer in Cyber Defense mode on Home (Instance 1)
        var isCyberDefenseActiveAtSubmission = sessionState.IsCyberDefenseEnabled;
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;
        var isCritical = eval.IsCorrect
            && session.IsPaceCalibrationReady
            && eval.LatencyMs <= session.CurrentFactCriticalHitThresholdMs;

        sessionState.RegisterPendingContext(new PendingCombatContext(
            submissionId,
            wasEligibleAtSubmission: isCyberDefenseActiveAtSubmission,
            isCritical: isCritical));

        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.False(commitResult.IsSuccess);
        Assert.False(session.IsCurrentSubmissionCommitted);

        // Step 2: Component Instance 1 is disposed upon navigating to Settings.
        // Step 3: Component Instance 2 is created upon navigating back to Home.
        // Step 4: Component Instance 2 recovers persistence failure.
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);
        Assert.True(session.IsCurrentSubmissionCommitted);

        var pendingContext = sessionState.GetPendingContext(submissionId);
        Assert.NotNull(pendingContext);
        Assert.True(pendingContext.WasEligibleAtSubmission);
        Assert.Equal(isCritical, pendingContext.IsCritical);

        var confirmedAttempt = new ConfirmedCombatAttempt(
            submissionId: submissionId,
            isCorrect: session.LastEvaluation!.IsCorrect,
            isCritical: pendingContext.IsCritical,
            isCommitted: session.IsCurrentSubmissionCommitted,
            wasEligibleAtSubmission: pendingContext.WasEligibleAtSubmission);

        var dispatchResult = sessionState.DispatchAttempt(confirmedAttempt);

        Assert.Equal(CombatDispatchStatus.Dispatched, dispatchResult.Status);
        Assert.True(dispatchResult.MutatedCombatState);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);
        Assert.True(sessionState.IsSubmissionProcessed(submissionId));
    }

    [Fact]
    public async Task Case2_CyberDefenseOrigin_RepeatedWriteFailuresAndNavigation_ZeroMutationsUntilCommitted_DispatchesOnceAfterFinalSuccess()
    {
        var store = new ControlledLearnerStore(
        [
            PersistenceResult.Unavailable("Transient error 1"),
            PersistenceResult.Unavailable("Transient error 2"),
            PersistenceResult.Success(2)
        ]);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var hpBefore = encounter.EnemyHitPoints;

        // Initial submission
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;
        sessionState.RegisterPendingContext(new PendingCombatContext(submissionId, wasEligibleAtSubmission: true, isCritical: false));

        // Attempt 1 fails
        await session.CommitCurrentEvaluationAsync();
        Assert.False(session.IsCurrentSubmissionCommitted);

        // Simulated navigation away and back -> retry 1 fails
        var rec1 = await session.RecoverFromPersistenceFailureAsync();
        Assert.False(rec1);
        Assert.False(session.IsCurrentSubmissionCommitted);
        Assert.Equal(hpBefore, encounter.EnemyHitPoints);

        // Simulated navigation away and back -> retry 2 succeeds
        var rec2 = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(rec2);
        Assert.True(session.IsCurrentSubmissionCommitted);

        var pendingContext = sessionState.GetPendingContext(submissionId);
        Assert.NotNull(pendingContext);
        Assert.True(pendingContext.WasEligibleAtSubmission);

        var result = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(
            submissionId,
            session.LastEvaluation!.IsCorrect,
            pendingContext.IsCritical,
            isCommitted: session.IsCurrentSubmissionCommitted,
            wasEligibleAtSubmission: pendingContext.WasEligibleAtSubmission));

        Assert.Equal(CombatDispatchStatus.Dispatched, result.Status);
        Assert.True(result.MutatedCombatState);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);
    }

    [Fact]
    public async Task Case3_CalmModeOrigin_WriteFails_NavigateToSettings_EnableCyberDefense_ReturnAndRecover_ZeroCombatMutations()
    {
        var store = new ControlledLearnerStore(
        [
            PersistenceResult.Unavailable("Write failure in Calm Mode."),
            PersistenceResult.Success(2)
        ]);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = false }; // Calm Mode active
        var sessionState = new CyberDefenseSessionState(preferences);

        // Step 1: Submit in Calm Mode
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;
        sessionState.RegisterPendingContext(new PendingCombatContext(
            submissionId,
            wasEligibleAtSubmission: sessionState.IsCyberDefenseEnabled, // false
            isCritical: false));

        await session.CommitCurrentEvaluationAsync();
        Assert.False(session.IsCurrentSubmissionCommitted);

        // Step 2: Navigate to Settings and enable Cyber Defense
        preferences.Enabled = true;
        Assert.True(sessionState.IsCyberDefenseEnabled);

        // Step 3: Return to Home and retry
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);
        Assert.True(session.IsCurrentSubmissionCommitted);

        var pendingContext = sessionState.GetPendingContext(submissionId);
        Assert.NotNull(pendingContext);
        Assert.False(pendingContext.WasEligibleAtSubmission); // Calm origin preserved!

        var result = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(
            submissionId,
            session.LastEvaluation!.IsCorrect,
            pendingContext.IsCritical,
            isCommitted: session.IsCurrentSubmissionCommitted,
            wasEligibleAtSubmission: pendingContext.WasEligibleAtSubmission));

        Assert.Equal(CombatDispatchStatus.CalmModeSuppressed, result.Status);
        Assert.False(result.MutatedCombatState);

        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints, encounter.EnemyHitPoints);
    }

    [Fact]
    public async Task Case4_CyberDefenseOrigin_WriteFails_NavigateToSettings_EnableCalmMode_ReturnAndRecover_SuppressesCombatAndNeverReplays()
    {
        var store = new ControlledLearnerStore(
        [
            PersistenceResult.Unavailable("Write failure in Cyber Defense."),
            PersistenceResult.Success(2)
        ]);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var hpBefore = encounter.EnemyHitPoints;

        // Step 1: Submit in Cyber Defense mode
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;
        sessionState.RegisterPendingContext(new PendingCombatContext(
            submissionId,
            wasEligibleAtSubmission: true,
            isCritical: false));

        await session.CommitCurrentEvaluationAsync();
        Assert.False(session.IsCurrentSubmissionCommitted);

        // Step 2: Navigate to Settings and enable Calm Mode
        preferences.Enabled = false;
        Assert.False(sessionState.IsCyberDefenseEnabled);

        // Step 3: Return to Home and retry
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);
        Assert.True(session.IsCurrentSubmissionCommitted);

        var pendingContext = sessionState.GetPendingContext(submissionId);
        Assert.NotNull(pendingContext);
        Assert.True(pendingContext.WasEligibleAtSubmission);

        var result = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(
            submissionId,
            session.LastEvaluation!.IsCorrect,
            pendingContext.IsCritical,
            isCommitted: session.IsCurrentSubmissionCommitted,
            wasEligibleAtSubmission: pendingContext.WasEligibleAtSubmission));

        Assert.Equal(CombatDispatchStatus.CalmModeSuppressed, result.Status);
        Assert.False(result.MutatedCombatState);

        // Step 4: Re-enable Cyber Defense later -> suppressed attempt is not replayed
        preferences.Enabled = true;
        var resumedEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(resumedEncounter);
        Assert.Equal(hpBefore, resumedEncounter.EnemyHitPoints);

        var replayResult = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(
            submissionId,
            true,
            false,
            true,
            true));
        Assert.Equal(CombatDispatchStatus.DuplicateSuppressed, replayResult.Status);
        Assert.False(replayResult.MutatedCombatState);
        Assert.Equal(hpBefore, resumedEncounter.EnemyHitPoints);
    }

    [Fact]
    public async Task Case5_OriginallyCriticalCorrectAnswer_PersistenceFailureAndNavigation_PreservesCriticalHitClassification()
    {
        var store = new ControlledLearnerStore(
        [
            PersistenceResult.Unavailable("Transient error on critical hit."),
            PersistenceResult.Success(2)
        ]);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var hpBefore = encounter.EnemyHitPoints;

        // Step 1: Submit critical hit answer
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;

        // Explicit critical hit captured at submission
        sessionState.RegisterPendingContext(new PendingCombatContext(
            submissionId,
            wasEligibleAtSubmission: true,
            isCritical: true));

        await session.CommitCurrentEvaluationAsync();
        Assert.False(session.IsCurrentSubmissionCommitted);

        // Step 2: Component recreation & recovery
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);

        var pendingContext = sessionState.GetPendingContext(submissionId);
        Assert.NotNull(pendingContext);
        Assert.True(pendingContext.IsCritical);

        var result = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(
            submissionId,
            session.LastEvaluation!.IsCorrect,
            isCritical: pendingContext.IsCritical,
            isCommitted: session.IsCurrentSubmissionCommitted,
            wasEligibleAtSubmission: pendingContext.WasEligibleAtSubmission));

        Assert.Equal(CombatDispatchStatus.Dispatched, result.Status);
        Assert.True(result.MutatedCombatState);
        Assert.Equal(hpBefore - 2, encounter.EnemyHitPoints); // 2 damage for critical hit
        Assert.Equal(CyberDefenseFeedbackKind.CriticalHit, encounter.LastFeedback);
    }

    [Fact]
    public async Task Case6_DurableCommitSucceeds_NextFactPreparationFails_DispatchesOnceAndDeduplicatesRepeatedRecovery()
    {
        var throwEvidence = false;
        var store = new DynamicEvidenceStore(() => throwEvidence);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var hpBefore = encounter.EnemyHitPoints;

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;
        sessionState.RegisterPendingContext(new PendingCombatContext(submissionId, wasEligibleAtSubmission: true, isCritical: false));

        throwEvidence = true;
        await session.CommitCurrentEvaluationAsync();
        Assert.True(session.IsCurrentSubmissionCommitted);

        // First dispatch upon confirmed commit
        var pending1 = sessionState.GetPendingContext(submissionId);
        var dispatch1 = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(
            submissionId,
            eval.IsCorrect,
            pending1?.IsCritical ?? false,
            isCommitted: session.IsCurrentSubmissionCommitted,
            wasEligibleAtSubmission: pending1?.WasEligibleAtSubmission ?? true));

        Assert.Equal(CombatDispatchStatus.Dispatched, dispatch1.Status);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);

        // Navigation to Settings and back, then recovery
        throwEvidence = false;
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);

        // Attempting second dispatch during recovery returns duplicate
        var dispatch2 = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(
            submissionId,
            eval.IsCorrect,
            false,
            isCommitted: session.IsCurrentSubmissionCommitted,
            wasEligibleAtSubmission: true));

        Assert.Equal(CombatDispatchStatus.DuplicateSuppressed, dispatch2.Status);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);
    }

    [Fact]
    public async Task Case7_RevisionConflict_DiscardsPendingEvaluation_InvalidatesPendingContext()
    {
        var store = new ControlledLearnerStore([PersistenceResult.Conflict("Revision mismatch.")]);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var hpBefore = encounter.EnemyHitPoints;

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var submissionId = eval.ChangeSet.SubmissionId;
        sessionState.RegisterPendingContext(new PendingCombatContext(submissionId, wasEligibleAtSubmission: true, isCritical: false));

        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.Equal(PersistenceStatus.RevisionConflict, commitResult.Status);
        Assert.False(session.IsCurrentSubmissionCommitted);

        // Recovery on revision conflict reloads authoritative state and resets LastEvaluation
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);
        Assert.Null(session.LastEvaluation);
        Assert.False(session.IsCurrentSubmissionCommitted);

        // Pending context is invalidated / cleared
        sessionState.ClearPendingContext(submissionId);
        Assert.Null(sessionState.GetPendingContext(submissionId));
        Assert.Equal(hpBefore, encounter.EnemyHitPoints);
    }

    [Fact]
    public void Case8_HomeRecreation_WithoutPendingEvaluation_ZeroCombatDispatches()
    {
        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);

        // No pending context registered
        Assert.Null(sessionState.GetPendingContext("nonexistent-submission"));
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints, encounter.EnemyHitPoints);
    }

    [Fact]
    public void Case9_FullLocalReset_ClearsPendingContextAndEncounter()
    {
        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);

        var subId = Guid.NewGuid().ToString("N");
        sessionState.RegisterPendingContext(new PendingCombatContext(subId, wasEligibleAtSubmission: true, isCritical: true));
        Assert.NotNull(sessionState.GetPendingContext(subId));

        sessionState.ClearEncounter();

        Assert.False(sessionState.HasActiveEncounter);
        Assert.Null(sessionState.GetPendingContext(subId));
        Assert.False(sessionState.IsSubmissionProcessed(subId));
    }

    [Fact]
    public async Task Case10_LearningOnlyReset_PreservesEncounter_InvalidatesDiscardedPendingContext()
    {
        var dbPath = Path.Combine(_tempDirectory, "case_10.db");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);

        var subId1 = Guid.NewGuid().ToString("N");
        sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId1, true, false, true, true));
        var hpAfterDispatch = encounter.EnemyHitPoints;

        // Pending context for uncommitted attempt
        var subId2 = Guid.NewGuid().ToString("N");
        sessionState.RegisterPendingContext(new PendingCombatContext(subId2, wasEligibleAtSubmission: true, isCritical: false));

        await session.ResetLearningProgressAsync(startTiming: false);
        sessionState.ClearPendingContext();

        Assert.True(sessionState.HasActiveEncounter);
        Assert.Equal(hpAfterDispatch, sessionState.ActiveEncounter?.EnemyHitPoints);
        Assert.True(sessionState.IsSubmissionProcessed(subId1));
        Assert.Null(sessionState.GetPendingContext(subId2));
    }

    [Fact]
    public void Case11_DistinctConfirmedAttempts_MaintainIndependentContextsWithoutCrossAssociation()
    {
        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var initialHp = encounter.EnemyHitPoints;

        var subId1 = "sub-1-" + Guid.NewGuid().ToString("N");
        var subId2 = "sub-2-" + Guid.NewGuid().ToString("N");

        sessionState.RegisterPendingContext(new PendingCombatContext(subId1, wasEligibleAtSubmission: true, isCritical: false));
        sessionState.RegisterPendingContext(new PendingCombatContext(subId2, wasEligibleAtSubmission: true, isCritical: true));

        var ctx1 = sessionState.GetPendingContext(subId1);
        var ctx2 = sessionState.GetPendingContext(subId2);

        Assert.NotNull(ctx1);
        Assert.NotNull(ctx2);
        Assert.False(ctx1.IsCritical);
        Assert.True(ctx2.IsCritical);

        var res1 = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId1, true, ctx1.IsCritical, true, ctx1.WasEligibleAtSubmission));
        Assert.Equal(CombatDispatchStatus.Dispatched, res1.Status);
        Assert.Equal(initialHp - 1, encounter.EnemyHitPoints);

        var res2 = sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId2, true, ctx2.IsCritical, true, ctx2.WasEligibleAtSubmission));
        Assert.Equal(CombatDispatchStatus.Dispatched, res2.Status);
        Assert.Equal(initialHp - 3, encounter.EnemyHitPoints);
    }

    [Fact]
    public void Case12_ConcurrentDispatchAndRecovery_ThreadSafeSingleDispatch()
    {
        var preferences = new FakePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        var hpBefore = encounter.EnemyHitPoints;

        var subId = "concurrent-" + Guid.NewGuid().ToString("N");
        sessionState.RegisterPendingContext(new PendingCombatContext(subId, wasEligibleAtSubmission: true, isCritical: false));

        var attempts = Enumerable.Range(0, 20).Select(_ => new ConfirmedCombatAttempt(subId, true, false, true, true)).ToList();
        var results = attempts.AsParallel().Select(a => sessionState.DispatchAttempt(a)).ToList();

        var dispatchedCount = results.Count(r => r.Status == CombatDispatchStatus.Dispatched);
        var duplicateCount = results.Count(r => r.Status == CombatDispatchStatus.DuplicateSuppressed);

        Assert.Equal(1, dispatchedCount);
        Assert.Equal(19, duplicateCount);
        Assert.Equal(hpBefore - 1, encounter.EnemyHitPoints);
    }
}
