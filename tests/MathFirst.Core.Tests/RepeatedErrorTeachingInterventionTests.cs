namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

public sealed class RepeatedErrorTeachingInterventionTests : IDisposable
{
    private readonly string _testDbDir;

    public RepeatedErrorTeachingInterventionTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstSlice5Tests_" + Guid.NewGuid().ToString("N"));
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
        public long CurrentTimestamp { get; set; } = 1000;
        public TimeSpan Elapsed { get; set; } = TimeSpan.FromMilliseconds(500);

        public long GetTimestamp() => CurrentTimestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) => Elapsed;
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
    public async Task A_FirstError_DoesNotTriggerTeaching_IncrementsSessionCounter()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var fact = session.CurrentFact;
        var wrongAnswer = fact.CorrectResult + 1;

        session.SubmitAnswer(wrongAnswer);
        var persistResult = await session.CommitCurrentEvaluationAsync();

        Assert.True(persistResult.IsSuccess);
        Assert.Equal(SessionInteractionState.IncorrectFeedback, session.InteractionState);
        Assert.Equal(1, session.GetConsecutiveErrorCount(fact.Id));
    }

    [Fact]
    public async Task B_SecondConsecutiveError_SameFact_TriggersTeachingIntervention_AndResetsCounter()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        // Let's run until a fact is seen a second time with errors
        var errorCounts = new Dictionary<string, int>();
        var observedTeaching = false;

        for (var step = 0; step < 40; step++)
        {
            var fact = session.CurrentFact;
            var prevCount = session.GetConsecutiveErrorCount(fact.Id);

            if (step % 2 == 0)
            {
                session.SubmitAnswer(fact.CorrectResult + 1); // Incorrect
            }
            else
            {
                session.RecordTimeout(); // Timeout
            }

            var persist = await session.CommitCurrentEvaluationAsync();
            Assert.True(persist.IsSuccess);

            if (prevCount == 1)
            {
                Assert.Equal(SessionInteractionState.TeachingIntervention, session.InteractionState);
                Assert.Equal(0, session.GetConsecutiveErrorCount(fact.Id));
                observedTeaching = true;
                break;
            }
            else
            {
                Assert.Equal(1, session.GetConsecutiveErrorCount(fact.Id));
                session.AdvanceToNextFact();
            }
        }

        Assert.True(observedTeaching, "Expected teaching intervention on second consecutive error.");
    }

    [Fact]
    public async Task C_CorrectAttempt_ResetsSessionErrorCounter_Immediately()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var fact = session.CurrentFact;

        // 1st error on fact
        session.SubmitAnswer(fact.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(1, session.GetConsecutiveErrorCount(fact.Id));
        session.AdvanceToNextFact();

        // Find fact again or advance until fact appears, answer correctly
        var targetFactId = fact.Id;
        for (var i = 0; i < 40; i++)
        {
            if (session.CurrentFact.Id == targetFactId)
            {
                // Correct answer on the same fact
                session.SubmitAnswer(session.CurrentFact.CorrectResult);
                await session.CommitCurrentEvaluationAsync();
                Assert.Equal(0, session.GetConsecutiveErrorCount(targetFactId));
                Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);
                session.AdvanceAfterCorrectAnswer();

                // Next time this fact appears, make an error - must be count 1 (no teaching)
                for (var j = 0; j < 40; j++)
                {
                    if (session.CurrentFact.Id == targetFactId)
                    {
                        session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
                        await session.CommitCurrentEvaluationAsync();
                        Assert.Equal(1, session.GetConsecutiveErrorCount(targetFactId));
                        Assert.Equal(SessionInteractionState.IncorrectFeedback, session.InteractionState);
                        return;
                    }
                    session.SubmitAnswer(session.CurrentFact.CorrectResult);
                    await session.CommitCurrentEvaluationAsync();
                    session.AdvanceAfterCorrectAnswer();
                }
                return;
            }

            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceAfterCorrectAnswer();
        }
    }

    [Fact]
    public async Task D_AttemptsForOtherFacts_DoNotResetFactCounter()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var factA = session.CurrentFact;

        // Fact A: Error 1
        session.SubmitAnswer(factA.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(1, session.GetConsecutiveErrorCount(factA.Id));

        session.AdvanceToNextFact();
        var factB = session.CurrentFact;

        if (factB.Id != factA.Id)
        {
            // Fact B: Correct
            session.SubmitAnswer(factB.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            Assert.Equal(0, session.GetConsecutiveErrorCount(factB.Id));
            // Fact A counter must remain 1
            Assert.Equal(1, session.GetConsecutiveErrorCount(factA.Id));
        }
    }

    [Fact]
    public async Task E_PairwiseContinuedFailures_TriggersTeachingRepeatedly()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var teachingCount = 0;
        string? monitoredFactId = null;

        for (var step = 0; step < 80; step++)
        {
            var f = session.CurrentFact;
            monitoredFactId ??= f.Id;

            if (f.Id == monitoredFactId)
            {
                var prev = session.GetConsecutiveErrorCount(f.Id);
                session.SubmitAnswer(f.CorrectResult + 1);
                await session.CommitCurrentEvaluationAsync();

                if (prev == 1)
                {
                    Assert.Equal(SessionInteractionState.TeachingIntervention, session.InteractionState);
                    Assert.Equal(0, session.GetConsecutiveErrorCount(f.Id));
                    teachingCount++;

                    var acknowledged = session.AcknowledgeTeachingIntervention();
                    Assert.True(acknowledged);
                    Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

                    if (teachingCount >= 2)
                    {
                        break;
                    }
                }
                else
                {
                    Assert.Equal(1, session.GetConsecutiveErrorCount(f.Id));
                    Assert.Equal(SessionInteractionState.IncorrectFeedback, session.InteractionState);
                    session.AdvanceToNextFact();
                }
            }
            else
            {
                // Answer correctly for other facts
                session.SubmitAnswer(f.CorrectResult);
                await session.CommitCurrentEvaluationAsync();
                session.AdvanceAfterCorrectAnswer();
            }
        }

        Assert.Equal(2, teachingCount);
    }

    [Fact]
    public async Task F_PersistenceFailure_DoesNotIncrementCounter_AndDoesNotTriggerTeachingUntilRecovery()
    {
        var dbPath = GetTempDbPath();
        using var store = new GatedFailingStore(dbPath);
        await store.InitializeAsync();
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var fact = session.CurrentFact;

        // First error succeeds
        session.SubmitAnswer(fact.CorrectResult + 1);
        var r1 = await session.CommitCurrentEvaluationAsync();
        Assert.True(r1.IsSuccess);
        Assert.Equal(1, session.GetConsecutiveErrorCount(fact.Id));

        session.AdvanceToNextFact();

        // Simulate second attempt fails persistence
        store.FailNextCommit = true;
        session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
        var r2 = await session.CommitCurrentEvaluationAsync();
        Assert.False(r2.IsSuccess);
        Assert.Equal(SessionInteractionState.PersistenceFailure, session.InteractionState);

        // Counter must NOT have incremented on failure
        // Now recover
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);
    }

    [Fact]
    public async Task G_SemanticTimeout_SubmittedAtOrAfterDeadline_CountsAsErrorTowardsTeaching()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var fact = session.CurrentFact;
        // Set clock elapsed to exactly deadline
        clock.Elapsed = TimeSpan.FromMilliseconds(session.CurrentFactDeadlineMs);

        // Submit mathematically correct answer but at deadline => semantic timeout
        var eval = session.SubmitAnswer(fact.CorrectResult);
        Assert.Equal(AttemptOutcome.Timeout, eval.Outcome);

        var persist = await session.CommitCurrentEvaluationAsync();
        Assert.True(persist.IsSuccess);
        Assert.Equal(1, session.GetConsecutiveErrorCount(fact.Id));
    }

    [Fact]
    public void H_TeachingEquation_RendersCanonicalSymbolsAndCorrectResult_ForAllOperations()
    {
        var add = new ArithmeticFact(ArithmeticOperation.Addition, 7, 8);
        Assert.Equal("7 + 8 = 15", add.EquationText);
        Assert.Equal("+", add.DisplaySymbol);
        Assert.Equal(15, add.CorrectResult);

        var sub = new ArithmeticFact(ArithmeticOperation.Subtraction, 15, 7);
        Assert.Equal("15 \u2212 7 = 8", sub.EquationText);
        Assert.Equal("\u2212", sub.DisplaySymbol);
        Assert.Equal(8, sub.CorrectResult);

        var mul = new ArithmeticFact(ArithmeticOperation.Multiplication, 7, 8);
        Assert.Equal("7 \u00D7 8 = 56", mul.EquationText);
        Assert.Equal("\u00D7", mul.DisplaySymbol);
        Assert.Equal(56, mul.CorrectResult);

        var div = new ArithmeticFact(ArithmeticOperation.Division, 56, 7);
        Assert.Equal("56 \u00F7 7 = 8", div.EquationText);
        Assert.Equal("\u00F7", div.DisplaySymbol);
        Assert.Equal(8, div.CorrectResult);
    }

    [Fact]
    public async Task I_Acknowledgement_DoesNotCreateAttemptRecord()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        // Trigger teaching
        while (session.InteractionState != SessionInteractionState.TeachingIntervention)
        {
            var f = session.CurrentFact;
            session.SubmitAnswer(f.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            if (session.InteractionState != SessionInteractionState.TeachingIntervention)
            {
                session.AdvanceToNextFact();
            }
        }

        var snapshotBefore = await store.LoadSnapshotAsync();
        var attemptsBefore = snapshotBefore.RecentAttempts.Count;

        session.AcknowledgeTeachingIntervention();

        var snapshotAfter = await store.LoadSnapshotAsync();
        var attemptsAfter = snapshotAfter.RecentAttempts.Count;

        Assert.Equal(attemptsBefore, attemptsAfter);
    }

    [Fact]
    public async Task J_Acknowledgement_DoesNotIncrementPracticePosition()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        while (session.InteractionState != SessionInteractionState.TeachingIntervention)
        {
            var f = session.CurrentFact;
            session.SubmitAnswer(f.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            if (session.InteractionState != SessionInteractionState.TeachingIntervention)
            {
                session.AdvanceToNextFact();
            }
        }

        var posBefore = session.Progression.PracticePosition;
        session.AcknowledgeTeachingIntervention();
        var posAfter = session.Progression.PracticePosition;

        Assert.Equal(posBefore, posAfter);
    }

    [Fact]
    public async Task K_Acknowledgement_DoesNotMutateItemLearningState_OrFsrs_OrProgression()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        while (session.InteractionState != SessionInteractionState.TeachingIntervention)
        {
            var f = session.CurrentFact;
            session.SubmitAnswer(f.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            if (session.InteractionState != SessionInteractionState.TeachingIntervention)
            {
                session.AdvanceToNextFact();
            }
        }

        var snapshotBefore = await store.LoadSnapshotAsync();
        var revisionBefore = session.Progression.StoreRevision;

        session.AcknowledgeTeachingIntervention();

        var snapshotAfter = await store.LoadSnapshotAsync();
        var revisionAfter = session.Progression.StoreRevision;

        Assert.Equal(revisionBefore, revisionAfter);
        Assert.Equal(snapshotBefore.Progression.StoreRevision, snapshotAfter.Progression.StoreRevision);
        Assert.Equal(snapshotBefore.Progression.PracticePosition, snapshotAfter.Progression.PracticePosition);
        Assert.Equal(snapshotBefore.ItemStates.Count, snapshotAfter.ItemStates.Count);
        Assert.Equal(snapshotBefore.FsrsStates.Count, snapshotAfter.FsrsStates.Count);
    }

    [Fact]
    public async Task L_Acknowledgement_DoesNotMutateScoreOrHud()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        while (session.InteractionState != SessionInteractionState.TeachingIntervention)
        {
            var f = session.CurrentFact;
            session.SubmitAnswer(f.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            if (session.InteractionState != SessionInteractionState.TeachingIntervention)
            {
                session.AdvanceToNextFact();
            }
        }

        var correctBefore = session.SessionCorrectCount;
        var totalBefore = session.SessionTotalCount;

        session.AcknowledgeTeachingIntervention();

        Assert.Equal(correctBefore, session.SessionCorrectCount);
        Assert.Equal(totalBefore, session.SessionTotalCount);
    }

    [Fact]
    public async Task M_NoTimerDuringTeaching_AndCannotRecordTimeout()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        while (session.InteractionState != SessionInteractionState.TeachingIntervention)
        {
            var f = session.CurrentFact;
            session.SubmitAnswer(f.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            if (session.InteractionState != SessionInteractionState.TeachingIntervention)
            {
                session.AdvanceToNextFact();
            }
        }

        Assert.Equal(SessionInteractionState.TeachingIntervention, session.InteractionState);
        Assert.False(session.IsTimingActive);

        var eval = session.RecordTimeout();
        Assert.NotNull(eval);
        Assert.Equal(AttemptOutcome.Incorrect, eval.Outcome);
    }

    [Fact]
    public async Task N_CannotSubmitAnswerDuringTeaching()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        while (session.InteractionState != SessionInteractionState.TeachingIntervention)
        {
            var f = session.CurrentFact;
            session.SubmitAnswer(f.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            if (session.InteractionState != SessionInteractionState.TeachingIntervention)
            {
                session.AdvanceToNextFact();
            }
        }

        Assert.Equal(SessionInteractionState.TeachingIntervention, session.InteractionState);

        var eval = session.SubmitAnswer(42);
        Assert.NotNull(eval);
        Assert.Equal(session.LastEvaluation, eval);
    }

    [Fact]
    public async Task O_NextSelectionAfterAcknowledgement_UsesRealSelectorWithProspectivePosition()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        while (session.InteractionState != SessionInteractionState.TeachingIntervention)
        {
            var f = session.CurrentFact;
            session.SubmitAnswer(f.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            if (session.InteractionState != SessionInteractionState.TeachingIntervention)
            {
                session.AdvanceToNextFact();
            }
        }

        var currentPos = session.Progression.PracticePosition;
        session.AcknowledgeTeachingIntervention();

        Assert.NotNull(session.CurrentFact);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.Equal(currentPos, session.Progression.PracticePosition);
    }

    [Fact]
    public async Task Q_Restart_ClearsSessionErrorCounter()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store, new FakeClock());
            await session.InitializeAsync();
            var fact = session.CurrentFact;

            session.SubmitAnswer(fact.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            Assert.Equal(1, session.GetConsecutiveErrorCount(fact.Id));
        }

        using (var store2 = new SqliteLearnerStore(dbPath))
        {
            var session2 = new TrainingSession(store2, new FakeClock());
            await session2.InitializeAsync();

            var fact = session2.CurrentFact;
            Assert.Equal(0, session2.GetConsecutiveErrorCount(fact.Id));
        }
    }

    [Fact]
    public async Task R_RestartAfterTeachingTrigger_ResetsInterventionStateAndOverlay()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store, new FakeClock());
            await session.InitializeAsync();

            while (session.InteractionState != SessionInteractionState.TeachingIntervention)
            {
                var f = session.CurrentFact;
                session.SubmitAnswer(f.CorrectResult + 1);
                await session.CommitCurrentEvaluationAsync();
                if (session.InteractionState != SessionInteractionState.TeachingIntervention)
                {
                    session.AdvanceToNextFact();
                }
            }

            Assert.Equal(SessionInteractionState.TeachingIntervention, session.InteractionState);
        }

        using (var store2 = new SqliteLearnerStore(dbPath))
        {
            var session2 = new TrainingSession(store2, new FakeClock());
            await session2.InitializeAsync();

            Assert.Equal(SessionInteractionState.AwaitingAnswer, session2.InteractionState);
            Assert.Equal(0, session2.GetConsecutiveErrorCount(session2.CurrentFact.Id));
        }
    }

    [Fact]
    public async Task S_DifferentFacts_HaveIndependentCounters()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var factA = session.CurrentFact;
        session.SubmitAnswer(factA.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(1, session.GetConsecutiveErrorCount(factA.Id));

        session.AdvanceToNextFact();
        var factB = session.CurrentFact;

        if (factB.Id != factA.Id)
        {
            session.SubmitAnswer(factB.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            Assert.Equal(1, session.GetConsecutiveErrorCount(factB.Id));
            Assert.Equal(1, session.GetConsecutiveErrorCount(factA.Id));
        }
    }

    [Fact]
    public async Task T_DuplicateAcknowledgement_IsIdempotentAndSafe()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        while (session.InteractionState != SessionInteractionState.TeachingIntervention)
        {
            var f = session.CurrentFact;
            session.SubmitAnswer(f.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            if (session.InteractionState != SessionInteractionState.TeachingIntervention)
            {
                session.AdvanceToNextFact();
            }
        }

        var pos1 = session.Progression.PracticePosition;
        var r1 = session.AcknowledgeTeachingIntervention();
        Assert.True(r1);

        var r2 = session.AcknowledgeTeachingIntervention();
        Assert.False(r2);
        Assert.Equal(pos1, session.Progression.PracticePosition);
    }

    [Theory]
    [InlineData("en", "Remember this fact")]
    [InlineData("de", "Merke dir diese Aufgabe")]
    [InlineData("ru", "Запомни этот пример")]
    public void U_Localization_TeachingInterventionKeys_HaveLanguageParity(string lang, string expectedTitle)
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference(lang);

        Assert.Equal(expectedTitle, service["Training_TeachingTitle"]);
        Assert.False(string.IsNullOrWhiteSpace(service["Training_TeachingExplanation"]));
        Assert.False(string.IsNullOrWhiteSpace(service["Common_Continue"]));
    }

    [Fact]
    public async Task V_ExistingDurableRemediation_RemainsUnaffectedByTeachingIntervention()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        string? taughtFactId = null;
        while (session.InteractionState != SessionInteractionState.TeachingIntervention)
        {
            var f = session.CurrentFact;
            session.SubmitAnswer(f.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            if (session.InteractionState == SessionInteractionState.TeachingIntervention)
            {
                taughtFactId = f.Id;
            }
            else
            {
                session.AdvanceToNextFact();
            }
        }

        Assert.NotNull(taughtFactId);
        Assert.True(session.ItemStates[taughtFactId].NeedsRemediation);

        session.AcknowledgeTeachingIntervention();
        // Item state in session still has NeedsRemediation == true
        Assert.True(session.ItemStates[taughtFactId].NeedsRemediation);

        // And in durable store:
        var snapshot = await store.LoadSnapshotAsync();
        Assert.True(snapshot.ItemStates[taughtFactId].NeedsRemediation);
    }
}
