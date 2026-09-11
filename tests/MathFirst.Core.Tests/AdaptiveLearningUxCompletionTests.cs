namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

public sealed class AdaptiveLearningUxCompletionTests : IDisposable
{
    private readonly string _testDbDir;

    public AdaptiveLearningUxCompletionTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstSlice6Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbDir))
            {
                Directory.Delete(_testDbDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    private string GetTempDbPath() => Path.Combine(_testDbDir, $"test_{Guid.NewGuid():N}.db");

    private sealed class FakeClock : IClock
    {
        public long CurrentTimestamp { get; set; } = 1_000_000;
        public long AutoAdvanceMs { get; set; } = 0;
        public TimeSpan? CustomElapsed { get; set; }
        public int ElapsedTimeCallCount { get; private set; }

        public long GetTimestamp()
        {
            var ts = CurrentTimestamp;
            CurrentTimestamp += AutoAdvanceMs;
            return ts;
        }

        public TimeSpan GetElapsedTime(long startTimestamp)
        {
            ElapsedTimeCallCount++;
            if (CustomElapsed is not null) return CustomElapsed.Value;
            var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, CurrentTimestamp - startTimestamp));
            CurrentTimestamp += AutoAdvanceMs;
            return elapsed;
        }

        public void ResetCallCounts()
        {
            ElapsedTimeCallCount = 0;
        }
    }

    private sealed class GatedFailingStore : ILearnerStore
    {
        private readonly SqliteLearnerStore _inner;
        public bool FailNextCommit { get; set; }

        public GatedFailingStore(string path)
        {
            _inner = new SqliteLearnerStore(path);
        }

        public string StoragePath => _inner.StoragePath;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            _inner.InitializeAsync(cancellationToken);

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            _inner.LoadSnapshotAsync(cancellationToken);

        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken cancellationToken = default) =>
            _inner.LoadRuntimeSnapshotAsync(cancellationToken);

        public Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(
            PracticeSelectionEvidenceRequest request,
            CancellationToken cancellationToken = default) =>
            _inner.LoadPracticeSelectionEvidenceAsync(request, cancellationToken);

        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            if (FailNextCommit)
            {
                FailNextCommit = false;
                return Task.FromResult(PersistenceResult.Unavailable("Simulated storage failure."));
            }
            return _inner.CommitSubmissionAsync(changeSet, cancellationToken);
        }

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) =>
            _inner.ResetLearningProgressAsync(cancellationToken);

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            _inner.CloseAsync(cancellationToken);

        public void Dispose() => _inner.Dispose();
    }

    [Fact]
    public async Task A_Cadence_19AttemptsNoCheckIn_20thAttemptTriggersCheckIn_40thTriggersSecondCheckIn()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        // 1 to 19 attempts
        for (var i = 1; i <= 19; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            var res = await session.CommitCurrentEvaluationAsync();
            Assert.True(res.IsSuccess);
            Assert.Null(session.PendingCheckIn);
            var advanced = session.AdvanceAfterCorrectAnswer();
            Assert.True(advanced);
        }

        // 20th attempt
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var res20 = await session.CommitCurrentEvaluationAsync();
        Assert.True(res20.IsSuccess);
        Assert.NotNull(session.PendingCheckIn);
        Assert.Equal(20, session.PendingCheckIn.TotalCount);
        Assert.Equal(20, session.PendingCheckIn.CorrectCount);

        // Advance after correct answer enters SessionCheckIn state
        var advanced20 = session.AdvanceAfterCorrectAnswer();
        Assert.False(advanced20);
        Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);

        // Learner continues practice
        session.ContinuePractice();
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.Null(session.PendingCheckIn);

        // 21 to 39 attempts
        for (var i = 21; i <= 39; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            var res = await session.CommitCurrentEvaluationAsync();
            Assert.True(res.IsSuccess);
            Assert.Null(session.PendingCheckIn);
            var advanced = session.AdvanceAfterCorrectAnswer();
            Assert.True(advanced);
        }

        // 40th attempt
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var res40 = await session.CommitCurrentEvaluationAsync();
        Assert.True(res40.IsSuccess);
        Assert.NotNull(session.PendingCheckIn);
        Assert.Equal(20, session.PendingCheckIn.TotalCount);
        Assert.Equal(20, session.PendingCheckIn.CorrectCount);

        var advanced40 = session.AdvanceAfterCorrectAnswer();
        Assert.False(advanced40);
        Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);
    }

    [Fact]
    public async Task B_PersistenceFailure_OnAttempt20_ProducesNoCheckInUntilSuccessfulRecovery()
    {
        var dbPath = GetTempDbPath();
        using var store = new GatedFailingStore(dbPath);
        await store.InitializeAsync();
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        // 1 to 19 attempts
        for (var i = 1; i <= 19; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            var res = await session.CommitCurrentEvaluationAsync();
            Assert.True(res.IsSuccess);
            session.AdvanceAfterCorrectAnswer();
        }

        // 20th attempt fails persistence
        store.FailNextCommit = true;
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var failRes = await session.CommitCurrentEvaluationAsync();
        Assert.False(failRes.IsSuccess);
        Assert.Equal(SessionInteractionState.PersistenceFailure, session.InteractionState);
        Assert.Null(session.PendingCheckIn);

        // Recover from persistence failure
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);
        Assert.NotNull(session.PendingCheckIn);
        Assert.Equal(20, session.PendingCheckIn.TotalCount);
        Assert.Equal(20, session.PendingCheckIn.CorrectCount);
    }

    [Fact]
    public async Task C_NoDuplicateCheckIn_UnderRepeatedStateTransitions()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        for (var i = 1; i <= 20; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            if (i < 20)
            {
                session.AdvanceAfterCorrectAnswer();
            }
        }

        Assert.NotNull(session.PendingCheckIn);
        session.AdvanceAfterCorrectAnswer();
        Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);

        // Calling AdvanceAfterCorrectAnswer again is no-op
        session.AdvanceAfterCorrectAnswer();
        Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);

        // Dismiss check-in
        session.ContinuePractice();
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.Null(session.PendingCheckIn);

        // Calling ContinuePractice again is no-op
        session.ContinuePractice();
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
    }

    [Fact]
    public async Task D_RestartFreshCadence_DoesNotReconstructFromLifetimePracticePosition()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store, new FakeClock());
            await session.InitializeAsync();

            for (var i = 1; i <= 19; i++)
            {
                session.SubmitAnswer(session.CurrentFact.CorrectResult);
                await session.CommitCurrentEvaluationAsync();
                session.AdvanceAfterCorrectAnswer();
            }
        }

        // Fresh session on restart
        using (var store2 = new SqliteLearnerStore(dbPath))
        {
            var session2 = new TrainingSession(store2, new FakeClock());
            await session2.InitializeAsync();

            Assert.Equal(19, session2.Progression.PracticePosition);
            Assert.Null(session2.PendingCheckIn);

            // 1 attempt in new session (PracticePosition = 20, but session attempt = 1)
            session2.SubmitAnswer(session2.CurrentFact.CorrectResult);
            var res = await session2.CommitCurrentEvaluationAsync();
            Assert.True(res.IsSuccess);
            Assert.Equal(20, session2.Progression.PracticePosition);
            Assert.Null(session2.PendingCheckIn); // No check-in because session attempt is 1, not 20
        }
    }

    [Fact]
    public async Task E_CorrectnessSummary_AccuratelyReflectsCorrectCountOutOf20()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        // 14 correct, 4 incorrect, 2 timeouts
        for (var i = 1; i <= 20; i++)
        {
            if (i <= 14)
            {
                session.SubmitAnswer(session.CurrentFact.CorrectResult);
                await session.CommitCurrentEvaluationAsync();
                session.AdvanceAfterCorrectAnswer();
            }
            else if (i <= 18)
            {
                session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
                await session.CommitCurrentEvaluationAsync();
                if (session.InteractionState == SessionInteractionState.TeachingIntervention)
                {
                    session.AcknowledgeTeachingIntervention();
                }
                else
                {
                    session.AdvanceToNextFact();
                }
            }
            else
            {
                session.RecordTimeout();
                await session.CommitCurrentEvaluationAsync();
                if (i < 20)
                {
                    if (session.InteractionState == SessionInteractionState.TeachingIntervention)
                    {
                        session.AcknowledgeTeachingIntervention();
                    }
                    else
                    {
                        session.AdvanceToNextFact();
                    }
                }
            }
        }

        Assert.NotNull(session.PendingCheckIn);
        Assert.Equal(20, session.PendingCheckIn.TotalCount);
        Assert.Equal(14, session.PendingCheckIn.CorrectCount);
    }

    [Fact]
    public async Task F_MedianCorrectLatency_CalculatesDeterministicMedian_ExcludingErrorsAndOddEven()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        // Let's create 20 attempts with explicit latencies:
        // 5 correct attempts with latencies: 1000, 1200, 1400, 1600, 2000 -> median = 1400 (odd count = 5)
        // 15 incorrect/timeout attempts with large latencies: 8000
        var latencies = new List<long> { 1000, 1200, 1400, 1600, 2000 };

        for (var i = 1; i <= 20; i++)
        {
            if (i <= 5)
            {
                clock.CustomElapsed = TimeSpan.FromMilliseconds(latencies[i - 1]);
                session.SubmitAnswer(session.CurrentFact.CorrectResult);
                await session.CommitCurrentEvaluationAsync();
                session.AdvanceAfterCorrectAnswer();
            }
            else
            {
                clock.CustomElapsed = TimeSpan.FromMilliseconds(8000);
                session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
                await session.CommitCurrentEvaluationAsync();
                if (i < 20)
                {
                    if (session.InteractionState == SessionInteractionState.TeachingIntervention)
                    {
                        session.AcknowledgeTeachingIntervention();
                    }
                    else
                    {
                        session.AdvanceToNextFact();
                    }
                }
            }
        }

        Assert.NotNull(session.PendingCheckIn);
        Assert.Equal(5, session.PendingCheckIn.CorrectCount);
        Assert.Equal(1400, session.PendingCheckIn.MedianCorrectLatencyMs);

        // Test even count median calculation
        // Samples: 1000, 1200, 1400, 1600 -> upperIndex = 2 -> (1200 + 1400 + 1) / 2 = 1300
        var evenMedian = AdaptivePacePolicy.Median([1000, 1200, 1400, 1600]);
        Assert.Equal(1300, evenMedian);

        // Round half up on odd sum: 1000, 1001 -> (1000 + 1001 + 1)/2 = 1001
        var halfUpMedian = AdaptivePacePolicy.Median([1000, 1001]);
        Assert.Equal(1001, halfUpMedian);
    }

    [Fact]
    public async Task G_ZeroCorrectWindow_ReportsZeroOutOf20_AndNullMedianLatency()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        for (var i = 1; i <= 20; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            if (i < 20)
            {
                if (session.InteractionState == SessionInteractionState.TeachingIntervention)
                {
                    session.AcknowledgeTeachingIntervention();
                }
                else
                {
                    session.AdvanceToNextFact();
                }
            }
        }

        Assert.NotNull(session.PendingCheckIn);
        Assert.Equal(0, session.PendingCheckIn.CorrectCount);
        Assert.Equal(20, session.PendingCheckIn.TotalCount);
        Assert.Null(session.PendingCheckIn.MedianCorrectLatencyMs);
    }

    [Fact]
    public async Task H_CheckInNonMutation_ShowsZeroLearningMutations()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        for (var i = 1; i <= 20; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            if (i < 20) session.AdvanceAfterCorrectAnswer();
        }

        var snapshotBefore = await store.LoadSnapshotAsync();
        var positionBefore = session.Progression.PracticePosition;
        var revBefore = session.Progression.StoreRevision;
        var attemptsBefore = snapshotBefore.RecentAttempts.Count;

        session.AdvanceAfterCorrectAnswer();
        Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);

        // During check-in presentation
        Assert.Equal(positionBefore, session.Progression.PracticePosition);
        Assert.Equal(revBefore, session.Progression.StoreRevision);

        // Dismiss check-in via Keep Going
        session.ContinuePractice();
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        var snapshotAfter = await store.LoadSnapshotAsync();
        Assert.Equal(positionBefore, session.Progression.PracticePosition);
        Assert.Equal(revBefore, session.Progression.StoreRevision);
        Assert.Equal(attemptsBefore, snapshotAfter.RecentAttempts.Count);
        Assert.Equal(snapshotBefore.ItemStates.Count, snapshotAfter.ItemStates.Count);
        Assert.Equal(snapshotBefore.FsrsStates.Count, snapshotAfter.FsrsStates.Count);
    }

    [Fact]
    public async Task I_KeepGoing_PreparesRealSelectorFact_AwaitingAnswer_WithFullDeadline()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        for (var i = 1; i <= 20; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            if (i < 20) session.AdvanceAfterCorrectAnswer();
        }

        session.AdvanceAfterCorrectAnswer();
        Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);

        session.ContinuePractice();

        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.NotNull(session.CurrentFact);
        Assert.True(session.IsTimingActive);
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());
        Assert.InRange(session.CurrentFactDeadlineMs, AdaptivePacePolicy.MinimumDeadlineMs, AdaptivePacePolicy.MaximumDeadlineMs);
    }

    [Fact]
    public async Task J_TakeABreak_PreparesRealFact_EntersManualPause_NoTimingUntilResume()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        for (var i = 1; i <= 20; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            if (i < 20) session.AdvanceAfterCorrectAnswer();
        }

        session.AdvanceAfterCorrectAnswer();
        Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);

        var positionBefore = session.Progression.PracticePosition;
        var revisionBefore = session.Progression.StoreRevision;
        var snapshotBefore = await store.LoadSnapshotAsync();
        Assert.NotNull(snapshotBefore);
        var sessionTotalCountBefore = session.SessionTotalCount;
        var sessionCorrectCountBefore = session.SessionCorrectCount;

        // Configure adversarial clock that advances time on every read during TakeBreak
        clock.AutoAdvanceMs = 10;
        clock.ResetCallCounts();

        session.TakeBreak();

        // A. Immediately after TakeBreak:
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.Equal(PracticeGateState.ManualPause, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());
        Assert.Equal(0, clock.ElapsedTimeCallCount);

        // B. Time passes substantially while paused
        clock.CurrentTimestamp += 60_000;
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());

        // C & D. Real next fact is prepared with full adaptive pace profile
        Assert.NotNull(session.CurrentFact);
        Assert.InRange(session.CurrentFactExpectedPaceMs, AdaptivePacePolicy.MinimumSampleMs, AdaptivePacePolicy.MaximumSampleMs);
        Assert.InRange(session.CurrentFactEasyThresholdMs, AdaptivePacePolicy.MinimumEasyThresholdMs, AdaptivePacePolicy.MaximumEasyThresholdMs);
        Assert.InRange(session.CurrentFactFluencyThresholdMs, AdaptivePacePolicy.MinimumFluencyThresholdMs, AdaptivePacePolicy.MaximumFluencyThresholdMs);
        Assert.InRange(session.CurrentFactDeadlineMs, AdaptivePacePolicy.MinimumDeadlineMs, AdaptivePacePolicy.MaximumDeadlineMs);

        // G. Non-mutation proof
        var snapshotAfter = await store.LoadSnapshotAsync();
        Assert.NotNull(snapshotBefore);
        Assert.NotNull(snapshotAfter);
        Assert.NotNull(snapshotBefore.OperationProgressions);
        Assert.Equal(positionBefore, session.Progression.PracticePosition);
        Assert.Equal(revisionBefore, session.Progression.StoreRevision);
        Assert.Equal(snapshotBefore.RecentAttempts.Count, snapshotAfter.RecentAttempts.Count);
        Assert.Equal(snapshotBefore.ItemStates.Count, snapshotAfter.ItemStates.Count);
        Assert.Equal(snapshotBefore.FsrsStates.Count, snapshotAfter.FsrsStates.Count);
        Assert.Equal(
            snapshotBefore.OperationProgressions[session.CurrentFact!.Operation].BandIndex,
            session.Progression.OperationProgressions[session.CurrentFact!.Operation].BandIndex);
        Assert.Equal(sessionTotalCountBefore, session.SessionTotalCount);
        Assert.Equal(sessionCorrectCountBefore, session.SessionCorrectCount);

        // E & F. Resume practice and verify full deadline is available from zero
        clock.AutoAdvanceMs = 0;
        session.StartOrResumePractice();
        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.True(session.IsTimingActive);
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());

        // Full deadline available after resume
        clock.CurrentTimestamp += session.CurrentFactDeadlineMs - 1;
        Assert.False(session.IsCurrentItemTimedOut());
        clock.CurrentTimestamp += 1;
        Assert.True(session.IsCurrentItemTimedOut());
    }

    [Fact]
    public async Task K_TeachingPrecedence_20thAttemptTriggersTeachingFirst_ThenCheckInAfterAck()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        // 1st attempt: Error on Fact A
        var factA = session.CurrentFact;
        session.SubmitAnswer(factA.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(1, session.GetConsecutiveErrorCount(factA.Id));
        session.AdvanceToNextFact();

        // 2nd through 19th attempts: answer correctly, but if factA appears, answer correctly on others
        // We want factA to remain with consecutive errors = 1 until attempt 20
        for (var i = 2; i <= 19; i++)
        {
            if (session.CurrentFact.Id == factA.Id)
            {
                // Advance without answering if possible or answer correctly on other facts
                // Wait: answering correctly on factA would reset its error count.
                // So if factA appears before attempt 20, let's keep error count = 1 by doing error on factA? No, that would trigger teaching early.
                // Instead, answer correctly on other facts:
                session.SubmitAnswer(session.CurrentFact.CorrectResult);
                await session.CommitCurrentEvaluationAsync();
                session.AdvanceAfterCorrectAnswer();
            }
            else
            {
                session.SubmitAnswer(session.CurrentFact.CorrectResult);
                await session.CommitCurrentEvaluationAsync();
                session.AdvanceAfterCorrectAnswer();
            }
        }

        // On attempt 20: error on current fact
        var fact20 = session.CurrentFact;
        var prevErrorCount = session.GetConsecutiveErrorCount(fact20.Id);
        session.SubmitAnswer(fact20.CorrectResult + 1);
        var eval = await session.CommitCurrentEvaluationAsync();
        Assert.True(eval.IsSuccess);

        Assert.NotNull(session.PendingCheckIn);
        Assert.Equal(20, session.PendingCheckIn.TotalCount);

        if (prevErrorCount >= 1)
        {
            // Triggered teaching intervention on attempt 20
            Assert.Equal(SessionInteractionState.TeachingIntervention, session.InteractionState);

            // Acknowledge teaching -> transitions to pending CheckIn, NOT directly to next fact
            var ack = session.AcknowledgeTeachingIntervention();
            Assert.True(ack);
            Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);

            // Dismiss check-in
            session.ContinuePractice();
            Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        }
        else
        {
            // Incorrect feedback on attempt 20
            Assert.Equal(SessionInteractionState.IncorrectFeedback, session.InteractionState);

            // Acknowledge feedback -> transitions to pending CheckIn
            session.AcknowledgeFeedback();
            Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);

            session.ContinuePractice();
            Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        }
    }

    [Fact]
    public async Task L_Reset_ClearsSessionCheckInWindowAndPendingCheckIn()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        for (var i = 1; i <= 20; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            if (i < 20) session.AdvanceAfterCorrectAnswer();
        }

        Assert.NotNull(session.PendingCheckIn);

        await session.ResetLearningProgressAsync();

        Assert.Null(session.PendingCheckIn);
        Assert.Equal(0, session.SessionTotalCount);
        Assert.Equal(0, session.SessionCorrectCount);
    }

    [Fact]
    public void O_PermanentScore_IsRemovedFromNormalPracticeHud()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));
        var headerEnd = home.IndexOf("</header>", StringComparison.Ordinal);
        var header = home[..headerEnd];

        Assert.DoesNotContain("Training_Score", header, StringComparison.Ordinal);
        Assert.DoesNotContain("header-session-score", header, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSessionScoreVisible", home, StringComparison.Ordinal);
    }

    [Fact]
    public void P_OnboardingSemantics_CommunicatesAllFiveLearnerConceptsWithoutBannedJargon()
    {
        var service = new LocalizationService();

        foreach (var lang in new[] { "en", "de", "ru" })
        {
            service.ApplyLanguagePreference(lang);
            var tutorialText = service["Onboarding_TutorialStep1Text"] + " " +
                               service["Onboarding_TutorialStep2Text"] + " " +
                               service["Onboarding_TutorialStep3Text"];

            // No banned internal jargon
            Assert.DoesNotContain("FSRS", tutorialText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("retention", tutorialText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PracticePosition", tutorialText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("band", tutorialText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("frontier", tutorialText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("remediation", tutorialText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("scheduler", tutorialText, StringComparison.OrdinalIgnoreCase);
        }

        // EN explicit concept checks
        service.ApplyLanguagePreference("en");
        var enAll = service["Onboarding_TutorialStep1Text"] + " " +
                    service["Onboarding_TutorialStep2Text"] + " " +
                    service["Onboarding_TutorialStep3Text"];

        // 1. correctness matters most
        Assert.Contains("Correctness matters most", enAll, StringComparison.OrdinalIgnoreCase);
        // 2. answering correctly and quickly helps progress faster
        Assert.Contains("progress faster", enAll, StringComparison.OrdinalIgnoreCase);
        // 3. secure facts appear less often
        Assert.Contains("Secure facts appear less often", enAll, StringComparison.OrdinalIgnoreCase);
        // 4. mistakes and weaker facts return for more practice
        Assert.Contains("return for more practice", enAll, StringComparison.OrdinalIgnoreCase);
        // 5. available answer time adapts as learner practices
        Assert.Contains("answer time adapts", enAll, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Q_Localization_CheckInAndOnboardingKeys_HaveParityAcrossAllLanguages()
    {
        var service = new LocalizationService();
        var keys = new[]
        {
            "Training_CheckInTitle",
            "Training_CheckInCorrect",
            "Training_CheckInSpeed",
            "Training_KeepGoing",
            "Training_TakeBreak",
            "Onboarding_TutorialStep1Title",
            "Onboarding_TutorialStep1Text",
            "Onboarding_TutorialStep2Title",
            "Onboarding_TutorialStep2Text",
            "Onboarding_TutorialStep3Title",
            "Onboarding_TutorialStep3Text"
        };

        foreach (var lang in new[] { "en", "de", "ru" })
        {
            service.ApplyLanguagePreference(lang);
            foreach (var key in keys)
            {
                var value = service[key];
                Assert.False(string.IsNullOrWhiteSpace(value), $"Missing or empty translation for key '{key}' in language '{lang}'.");
                Assert.NotEqual(key, value);
            }
        }
    }

    [Fact]
    public void R_PauseIcon_UsesCssDrawnBarsAndCurrentColor_NoEmoji()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));
        var styles = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css"));

        // No emoji pause icon in Home.razor
        Assert.DoesNotContain("⏸", home, StringComparison.Ordinal);

        // Uses CSS-drawn pause icon bars
        Assert.Contains("class=\"pause-icon\"", home, StringComparison.Ordinal);
        Assert.Contains("class=\"pause-icon-bar\"", home, StringComparison.Ordinal);
        Assert.Contains("background-color: currentColor;", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void S_PauseDangerStyle_HasExplicitPrecedenceInCss()
    {
        var styles = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css"));

        Assert.Contains(".pause-practice-btn.button-danger", styles, StringComparison.Ordinal);
        Assert.Contains("background: var(--color-danger);", styles, StringComparison.Ordinal);
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = GetRepositoryRoot();
        return Path.Combine([root, .. segments]);
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
