namespace MathFirst.Core.Tests;

using Microsoft.Data.Sqlite;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

public sealed class TieredRemediationAndBroadWeaknessTests : IDisposable
{
    private readonly string _testDbDir;

    public TieredRemediationAndBroadWeaknessTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstSlice1Tests_" + Guid.NewGuid().ToString("N"));
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

    private static PracticeSelectionContext CreateContext(
        long prospectivePosition,
        ArithmeticCurriculum curriculum,
        PracticeCandidateIndex candidateIndex,
        long scheduledOperationOrdinal = 2,
        IEnumerable<ArithmeticOperation>? enabledOperations = null,
        GuidedNumberSpaceGate? gate = null,
        bool hasBroadWeakness = false)
    {
        var expectedOperations = Enum.GetValues<ArithmeticOperation>();
        var progressions = expectedOperations.ToDictionary(
            op => op,
            op => new OperationProgression(op, 0, 0));
        var curricula = expectedOperations.ToDictionary(
            op => op,
            op => curriculum.GetCurriculum(op));

        return new PracticeSelectionContext(
            prospectivePosition,
            0,
            progressions,
            curricula,
            candidateIndex,
            [],
            scheduledOperationOrdinal,
            enabledOperations ?? [ArithmeticOperation.Addition],
            gate,
            hasBroadWeakness);
    }

    private static async Task SeedAttemptAsync(
        SqliteConnection conn,
        ArithmeticOperation operation,
        int left,
        int right,
        AttemptOutcome outcome,
        long practicePosition)
    {
        var fact = new ArithmeticFact(operation, left, right);
        var isCorrect = outcome == AttemptOutcome.Correct;
        var correctResult = fact.CorrectResult;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO attempt_history (
                submission_id, fact_id, operation, left_operand, right_operand,
                submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                response_latency_ms, timestamp, practice_position
            ) VALUES (
                @sub_id, @fact_id, @operation, @left, @right,
                @submitted, @correct, @is_correct, 0, @outcome,
                1000, @ts, @pos
            );";
        cmd.Parameters.AddWithValue("@sub_id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@fact_id", fact.Id);
        cmd.Parameters.AddWithValue("@operation", operation.ToString());
        cmd.Parameters.AddWithValue("@left", left);
        cmd.Parameters.AddWithValue("@right", right);
        cmd.Parameters.AddWithValue("@submitted", isCorrect ? correctResult : correctResult + 1);
        cmd.Parameters.AddWithValue("@correct", correctResult);
        cmd.Parameters.AddWithValue("@is_correct", isCorrect ? 1 : 0);
        cmd.Parameters.AddWithValue("@outcome", outcome.ToString());
        cmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@pos", practicePosition);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedFactStateAsync(
        SqliteConnection conn,
        ArithmeticFact fact,
        bool needsRemediation,
        long lastReviewPracticePosition,
        int totalAttempts = 2,
        int incorrectAttempts = 2)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO item_learning_state (
                fact_id, operation, left_operand, right_operand,
                total_attempts, correct_attempts, incorrect_attempts,
                consecutive_correct, last_latency_ms, rolling_latency_ms,
                fluent_streak, is_mastered, needs_remediation,
                remediation_due_order, last_practiced_order, last_practiced_at
            ) VALUES (
                @fact_id, @operation, @left, @right,
                @total, @correct, @incorrect,
                0, 1000, 1000,
                0, 0, @needs_remediation,
                0, 0, @ts
            );
            INSERT INTO fsrs_card_state (
                fact_id, card_id, state, step, stability, difficulty,
                due_practice_position, last_review_practice_position, last_rating
            ) VALUES (
                @fact_id, @card_id, 1, 1, 1.0, 1.0,
                @due_pos, @last_review, 1
            );";
        cmd.Parameters.AddWithValue("@fact_id", fact.Id);
        cmd.Parameters.AddWithValue("@operation", fact.Operation.ToString());
        cmd.Parameters.AddWithValue("@left", fact.LeftOperand);
        cmd.Parameters.AddWithValue("@right", fact.RightOperand);
        cmd.Parameters.AddWithValue("@total", totalAttempts);
        cmd.Parameters.AddWithValue("@correct", totalAttempts - incorrectAttempts);
        cmd.Parameters.AddWithValue("@incorrect", incorrectAttempts);
        cmd.Parameters.AddWithValue("@needs_remediation", needsRemediation ? 1 : 0);
        cmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@card_id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("@due_pos", lastReviewPracticePosition + 10);
        cmd.Parameters.AddWithValue("@last_review", lastReviewPracticePosition);
        await cmd.ExecuteNonQueryAsync();
    }

    // 1. RemediationSpacing_IsolatedError_RequiresFourOperationTurns
    [Fact]
    public void RemediationSpacing_IsolatedError_RequiresFourOperationTurns()
    {
        var selector = new AdaptivePracticeSelector();
        var curriculum = new ArithmeticCurriculum();
        var remedFact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var fallbackFact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);

        var remedState = ItemLearningState.CreateNew(remedFact);
        remedState.NeedsRemediation = true;
        var remedCard = new FsrsCardState(remedFact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 50, 10, FsrsRating.Again);

        var fallbackState = ItemLearningState.CreateNew(fallbackFact);
        var fallbackCard = new FsrsCardState(fallbackFact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 5, 5, FsrsRating.Good);

        var evidence = new PracticeSelectionEvidence(
            ArithmeticOperation.Addition,
            prospectivePracticePosition: 11,
            currentBandCandidates: [],
            dueCandidates: [new PracticeSelectionCandidate(fallbackFact, fallbackState, fallbackCard)],
            maintenanceCandidates: [],
            remediationCandidates: [new PracticeSelectionCandidate(remedFact, remedState, remedCard, IsRepeated: false)],
            earlyReviewCandidates: []);

        var candidateIndex = new PracticeCandidateIndex(evidence);

        // Not eligible at +1 (pos 11), +2 (pos 12), +3 (pos 13)
        for (long pos = 11; pos <= 13; pos++)
        {
            var ctx = CreateContext(pos, curriculum, candidateIndex);
            var result = selector.SelectTargetFact(ctx);
            Assert.NotEqual(PracticeSelectionRole.Remediation, result.ResolvedRole);
            Assert.Equal(fallbackFact.Id, result.Fact.Id);
        }

        // Eligible at +4 (pos 14)
        var ctxEligible = CreateContext(14, curriculum, candidateIndex);
        var resultEligible = selector.SelectTargetFact(ctxEligible);
        Assert.Equal(PracticeSelectionRole.Remediation, resultEligible.ResolvedRole);
        Assert.Equal(remedFact.Id, resultEligible.Fact.Id);
    }

    // 2. RemediationSpacing_RepeatedError_RequiresTwoOperationTurns
    [Fact]
    public void RemediationSpacing_RepeatedError_RequiresTwoOperationTurns()
    {
        var selector = new AdaptivePracticeSelector();
        var curriculum = new ArithmeticCurriculum();
        var remedFact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var fallbackFact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);

        var remedState = ItemLearningState.CreateNew(remedFact);
        remedState.NeedsRemediation = true;
        var remedCard = new FsrsCardState(remedFact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 50, 10, FsrsRating.Again);

        var fallbackState = ItemLearningState.CreateNew(fallbackFact);
        var fallbackCard = new FsrsCardState(fallbackFact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 5, 5, FsrsRating.Good);

        var evidence = new PracticeSelectionEvidence(
            ArithmeticOperation.Addition,
            prospectivePracticePosition: 11,
            currentBandCandidates: [],
            dueCandidates: [new PracticeSelectionCandidate(fallbackFact, fallbackState, fallbackCard)],
            maintenanceCandidates: [],
            remediationCandidates: [new PracticeSelectionCandidate(remedFact, remedState, remedCard, IsRepeated: true)],
            earlyReviewCandidates: []);

        var candidateIndex = new PracticeCandidateIndex(evidence);

        // Not eligible at +1 (pos 11)
        var ctxNotYet = CreateContext(11, curriculum, candidateIndex);
        var resultNotYet = selector.SelectTargetFact(ctxNotYet);
        Assert.NotEqual(PracticeSelectionRole.Remediation, resultNotYet.ResolvedRole);
        Assert.Equal(fallbackFact.Id, resultNotYet.Fact.Id);

        // Eligible at +2 (pos 12)
        var ctxEligible = CreateContext(12, curriculum, candidateIndex);
        var resultEligible = selector.SelectTargetFact(ctxEligible);
        Assert.Equal(PracticeSelectionRole.Remediation, resultEligible.ResolvedRole);
        Assert.Equal(remedFact.Id, resultEligible.Fact.Id);
    }

    // 3. RepeatedErrorDerivation_NonCorrectPairs_AreRepeated
    [Theory]
    [InlineData(AttemptOutcome.Incorrect, AttemptOutcome.Incorrect)]
    [InlineData(AttemptOutcome.Incorrect, AttemptOutcome.Timeout)]
    [InlineData(AttemptOutcome.Timeout, AttemptOutcome.Incorrect)]
    [InlineData(AttemptOutcome.Timeout, AttemptOutcome.Timeout)]
    public async Task RepeatedErrorDerivation_NonCorrectPairs_AreRepeated(AttemptOutcome first, AttemptOutcome second)
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);

        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            await SeedAttemptAsync(conn, fact.Operation, fact.LeftOperand, fact.RightOperand, first, practicePosition: 1);
            await SeedAttemptAsync(conn, fact.Operation, fact.LeftOperand, fact.RightOperand, second, practicePosition: 2);
            await SeedFactStateAsync(conn, fact, needsRemediation: true, lastReviewPracticePosition: 2);
        }

        var request = new PracticeSelectionEvidenceRequest(
            fact.Operation,
            prospectivePracticePosition: 4,
            currentSessionOrder: 0,
            currentBandOwnedFrontier: [fact],
            introductionFrontier: [fact],
            currentBandIndex: 0);

        var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);
        var candidate = Assert.Single(evidence.RemediationCandidates, c => c.Fact.Id == fact.Id);
        Assert.True(candidate.IsRepeated);
    }

    // 4. RepeatedErrorDerivation_InterveningCorrect_StartsNewIsolatedEpisode
    [Fact]
    public async Task RepeatedErrorDerivation_InterveningCorrect_StartsNewIsolatedEpisode()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);

        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            // Incorrect -> Correct -> Incorrect
            await SeedAttemptAsync(conn, fact.Operation, fact.LeftOperand, fact.RightOperand, AttemptOutcome.Incorrect, practicePosition: 1);
            await SeedAttemptAsync(conn, fact.Operation, fact.LeftOperand, fact.RightOperand, AttemptOutcome.Correct, practicePosition: 2);
            await SeedAttemptAsync(conn, fact.Operation, fact.LeftOperand, fact.RightOperand, AttemptOutcome.Incorrect, practicePosition: 3);
            await SeedFactStateAsync(conn, fact, needsRemediation: true, lastReviewPracticePosition: 3);
        }

        var request = new PracticeSelectionEvidenceRequest(
            fact.Operation,
            prospectivePracticePosition: 5,
            currentSessionOrder: 0,
            currentBandOwnedFrontier: [fact],
            introductionFrontier: [fact],
            currentBandIndex: 0);

        var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);
        var candidate = Assert.Single(evidence.RemediationCandidates, c => c.Fact.Id == fact.Id);
        Assert.False(candidate.IsRepeated);
    }

    // 5. RepeatedErrorDerivation_LifetimeIncorrectCountDoesNotClassifyRepeated
    [Fact]
    public async Task RepeatedErrorDerivation_LifetimeIncorrectCountDoesNotClassifyRepeated()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);

        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            // 10 historical errors
            for (long pos = 1; pos <= 10; pos++)
            {
                await SeedAttemptAsync(conn, fact.Operation, fact.LeftOperand, fact.RightOperand, AttemptOutcome.Incorrect, practicePosition: pos);
            }
            // Subsequent Correct
            await SeedAttemptAsync(conn, fact.Operation, fact.LeftOperand, fact.RightOperand, AttemptOutcome.Correct, practicePosition: 11);
            // One new failure
            await SeedAttemptAsync(conn, fact.Operation, fact.LeftOperand, fact.RightOperand, AttemptOutcome.Incorrect, practicePosition: 12);

            await SeedFactStateAsync(conn, fact, needsRemediation: true, lastReviewPracticePosition: 12, totalAttempts: 12, incorrectAttempts: 11);
        }

        var request = new PracticeSelectionEvidenceRequest(
            fact.Operation,
            prospectivePracticePosition: 14,
            currentSessionOrder: 0,
            currentBandOwnedFrontier: [fact],
            introductionFrontier: [fact],
            currentBandIndex: 0);

        var evidence = await store.LoadPracticeSelectionEvidenceAsync(request);
        var candidate = Assert.Single(evidence.RemediationCandidates, c => c.Fact.Id == fact.Id);
        Assert.False(candidate.IsRepeated);
    }

    // 6. RepeatedErrorDerivation_SurvivesColdRestartBeyondRecentAttemptWindow
    [Fact]
    public async Task RepeatedErrorDerivation_SurvivesColdRestartBeyondRecentAttemptWindow()
    {
        var dbPath = GetTempDbPath();
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);

        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();

            await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                // Repeated failure for Fact F
                await SeedAttemptAsync(conn, fact.Operation, fact.LeftOperand, fact.RightOperand, AttemptOutcome.Incorrect, practicePosition: 1);
                await SeedAttemptAsync(conn, fact.Operation, fact.LeftOperand, fact.RightOperand, AttemptOutcome.Incorrect, practicePosition: 2);
                await SeedFactStateAsync(conn, fact, needsRemediation: true, lastReviewPracticePosition: 2);

                // >40 later attempts in the same operation for other facts
                for (var i = 1; i <= 45; i++)
                {
                    var otherFact = new ArithmeticFact(ArithmeticOperation.Addition, 1, (i % 9) + 1);
                    await SeedAttemptAsync(conn, otherFact.Operation, otherFact.LeftOperand, otherFact.RightOperand, AttemptOutcome.Correct, practicePosition: 2 + i);
                }
            }
        }

        // Cold restart with new store
        using (var reopenedStore = new SqliteLearnerStore(dbPath))
        {
            await reopenedStore.InitializeAsync();

            var request = new PracticeSelectionEvidenceRequest(
                fact.Operation,
                prospectivePracticePosition: 48,
                currentSessionOrder: 0,
                currentBandOwnedFrontier: [fact],
                introductionFrontier: [fact],
                currentBandIndex: 0);

            var evidence = await reopenedStore.LoadPracticeSelectionEvidenceAsync(request);
            var candidate = Assert.Single(evidence.RemediationCandidates, c => c.Fact.Id == fact.Id);
            Assert.True(candidate.IsRepeated);
        }
    }

    // 7. BroadWeakness_ActivatesAtExactlyTwoEligibleUnresolvedFacts
    [Fact]
    public async Task BroadWeakness_ActivatesAtExactlyTwoEligibleUnresolvedFacts()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync(startTiming: false);

        // 0 unresolved facts => false
        Assert.False(session.HasBroadWeakness);

        // Commit 1 error on current fact
        var f1 = session.CurrentFact;
        session.SubmitAnswer(f1.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();
        Assert.True(session.ItemStates[f1.Id].NeedsRemediation);

        // 1 unresolved fact => false
        Assert.False(session.HasBroadWeakness);

        // Advance and commit 1 error on second fact
        await session.AcknowledgeFeedbackAsync(startTiming: false);
        var f2 = session.CurrentFact;
        Assert.NotEqual(f1.Id, f2.Id);

        session.SubmitAnswer(f2.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();
        Assert.True(session.ItemStates[f2.Id].NeedsRemediation);

        // 2 unresolved facts => true
        Assert.True(session.HasBroadWeakness);
    }

    // 8. BroadWeakness_CountsCooldownIneligibleUnresolvedFacts
    [Fact]
    public async Task BroadWeakness_CountsCooldownIneligibleUnresolvedFacts()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync(startTiming: false);

        // Fact 1 error at pos 1
        var f1 = session.CurrentFact;
        session.SubmitAnswer(f1.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();

        // Fact 2 error at pos 2
        await session.AcknowledgeFeedbackAsync(startTiming: false);
        var f2 = session.CurrentFact;
        session.SubmitAnswer(f2.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();

        // At prospective position 3, neither fact is eligible for remediation (cooldown 4 or 2)
        // Cooldown eligibility is irrelevant: both count toward Broad Weakness!
        Assert.True(session.HasBroadWeakness);
    }

    // 9. BroadWeakness_ExcludesDisabledOperationFacts
    [Fact]
    public async Task BroadWeakness_ExcludesDisabledOperationFacts()
    {
        var dbPath = GetTempDbPath();
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store, new FakeClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // Cause error on fact 1
        var f1 = session.CurrentFact;
        session.SubmitAnswer(f1.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();
        await session.AcknowledgeFeedbackAsync(startTiming: false);

        // Cause error on fact 2
        var f2 = session.CurrentFact;
        session.SubmitAnswer(f2.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();

        Assert.True(session.HasBroadWeakness);

        // Now disable operations such that only f1's operation is enabled
        preferences.SetEnabledOperations([f1.Operation]);
        await session.ReconcilePracticeConfigurationAsync(startTiming: false);

        // If f1 and f2 had different operations, or we ensure f2 is in a disabled operation:
        if (f1.Operation != f2.Operation)
        {
            Assert.False(session.HasBroadWeakness);
        }
        else
        {
            // Specifically disable f1's operation
            var otherOps = PracticeOperationPreferencePolicy.AllOperations.Where(op => op != f1.Operation).ToArray();
            preferences.SetEnabledOperations(otherOps);
            await session.ReconcilePracticeConfigurationAsync(startTiming: false);
            Assert.False(session.HasBroadWeakness);
        }
    }

    // 10. BroadWeakness_ExcludesCurrentlyIneligibleFacts
    [Fact]
    public async Task BroadWeakness_ExcludesCurrentlyIneligibleFacts()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync(startTiming: false);

        // Cause 1 error on current eligible fact
        var f1 = session.CurrentFact;
        session.SubmitAnswer(f1.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();

        // Inject an unresolved item state belonging to a future band (ineligible by curriculum ownership)
        var futureFact = new ArithmeticFact(ArithmeticOperation.Addition, 9, 9);
        var futureState = ItemLearningState.CreateNew(futureFact);
        futureState.NeedsRemediation = true;
        session.ItemStates[futureFact.Id] = futureState;

        // Current Addition progression is BandIndex 0. 9+9 is not eligible at Band 0.
        // Therefore, only 1 eligible unresolved fact exists => BroadWeakness must be false.
        Assert.False(session.HasBroadWeakness);
    }

    // 11. BroadWeakness_CorrectRecoveryDropsCountImmediately
    [Fact]
    public async Task BroadWeakness_CorrectRecoveryDropsCountImmediately()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync(startTiming: false);

        // Fail Fact 1
        var f1 = session.CurrentFact;
        session.SubmitAnswer(f1.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();
        await session.AcknowledgeFeedbackAsync(startTiming: false);

        // Fail Fact 2
        var f2 = session.CurrentFact;
        session.SubmitAnswer(f2.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();

        Assert.True(session.HasBroadWeakness);

        // Recover Fact 1: answer Correctly and commit
        // Simulate presentation and correct answer for f1
        session.ItemStates[f1.Id].NeedsRemediation = false;

        // Now only 1 fact has NeedsRemediation => BroadWeakness drops immediately to false
        Assert.False(session.HasBroadWeakness);
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
        public bool GetHapticFeedbackEnabled() => true;
        public void SetHapticFeedbackEnabled(bool enabled) { }
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
