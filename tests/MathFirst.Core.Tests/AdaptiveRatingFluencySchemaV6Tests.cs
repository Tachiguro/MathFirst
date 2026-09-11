namespace MathFirst.Core.Tests;

using System.Reflection;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Progression;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Microsoft.Data.Sqlite;

public sealed class AdaptiveRatingFluencySchemaV6Tests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstSlice2_" + Guid.NewGuid().ToString("N"));

    public AdaptiveRatingFluencySchemaV6Tests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ThresholdPolicy_UsesApprovedRatiosHalfUpRoundingAndClamps()
    {
        Assert.Equal(1700, MathFirst.Application.Practice.AdaptivePacePolicy.CalculateEasyThresholdMs(2000));
        Assert.Equal(2500, MathFirst.Application.Practice.AdaptivePacePolicy.CalculateFluencyThresholdMs(2000));

        Assert.Equal(1709, MathFirst.Application.Practice.AdaptivePacePolicy.CalculateEasyThresholdMs(2010));
        Assert.Equal(2503, MathFirst.Application.Practice.AdaptivePacePolicy.CalculateFluencyThresholdMs(2002));

        Assert.Equal(600, MathFirst.Application.Practice.AdaptivePacePolicy.CalculateEasyThresholdMs(600));
        Assert.Equal(1500, MathFirst.Application.Practice.AdaptivePacePolicy.CalculateFluencyThresholdMs(600));
        Assert.Equal(2000, MathFirst.Application.Practice.AdaptivePacePolicy.CalculateEasyThresholdMs(12000));
        Assert.Equal(4000, MathFirst.Application.Practice.AdaptivePacePolicy.CalculateFluencyThresholdMs(12000));
    }

    [Fact]
    public void PresentationProfile_CarriesThresholdsCalculatedFromItsFactPace()
    {
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var profile = AdaptivePacePolicy.Calculate(fact, [fact.Id], []);

        Assert.Equal(4500, profile.FactPaceMs);
        Assert.Equal(2000, profile.EasyThresholdMs);
        Assert.Equal(4000, profile.FluencyThresholdMs);
        Assert.Equal(9000, profile.DeadlineMs);
    }

    [Theory]
    [InlineData(1700, FsrsRating.Easy, true)]
    [InlineData(1701, FsrsRating.Good, true)]
    [InlineData(2500, FsrsRating.Good, true)]
    [InlineData(2501, FsrsRating.Hard, false)]
    public void PfactTwoSeconds_UsesInclusiveApprovedRatingAndFluencyBoundaries(
        long latencyMs,
        FsrsRating expectedRating,
        bool expectedFluent)
    {
        var classification = AdaptiveAttemptClassifier.Classify(
            AttemptOutcome.Correct,
            latencyMs,
            AdaptivePacePolicy.CalculateEasyThresholdMs(2000),
            AdaptivePacePolicy.CalculateFluencyThresholdMs(2000));

        Assert.Equal(expectedRating, classification.Rating);
        Assert.Equal(expectedFluent, classification.IsFluent);
    }

    [Theory]
    [InlineData(2000, FsrsRating.Easy, true)]
    [InlineData(2001, FsrsRating.Good, true)]
    [InlineData(4000, FsrsRating.Good, true)]
    [InlineData(4001, FsrsRating.Hard, false)]
    public async Task ColdPresentation_UsesFixedAdaptiveRatingAndFluencyBoundaries(
        long elapsedMs,
        FsrsRating expectedRating,
        bool expectedFluent)
    {
        var clock = new FakeClock { ElapsedMs = elapsedMs };
        using var store = new SnapshotStore(FreshSnapshot());
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var evaluation = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        Assert.Equal(expectedRating, evaluation.ChangeSet.UpdatedFsrsState!.LastRating);
        Assert.Equal(expectedFluent, evaluation.ChangeSet.Attempt.IsFluent);
        Assert.Equal(expectedFluent ? 1 : 0, evaluation.ChangeSet.UpdatedItemState.FluentStreak);
    }

    [Theory]
    [InlineData(1000, false)]
    [InlineData(9000, true)]
    [InlineData(9001, true)]
    public async Task IncorrectAndSemanticTimeout_DominateLatencyAndCorrectArithmetic(
        long elapsedMs,
        bool submitCorrectAnswer)
    {
        var clock = new FakeClock { ElapsedMs = elapsedMs };
        using var store = new SnapshotStore(FreshSnapshot());
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var answer = submitCorrectAnswer
            ? session.CurrentFact.CorrectResult
            : checked(session.CurrentFact.CorrectResult + 1);
        var evaluation = session.SubmitAnswer(answer);

        Assert.Equal(FsrsRating.Again, evaluation.ChangeSet.UpdatedFsrsState!.LastRating);
        Assert.False(evaluation.ChangeSet.Attempt.IsFluent);
        Assert.False(evaluation.IsCorrect);
    }

    [Fact]
    public async Task PresentationThresholds_RemainFixedAcrossEvidenceMutationPauseResumeAndPolling()
    {
        var clock = new FakeClock();
        using var store = new SnapshotStore(FreshSnapshot());
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var easy = session.CurrentFactEasyThresholdMs;
        var fluent = session.CurrentFactFluencyThresholdMs;

        var evidenceField = typeof(TrainingSession).GetField("_recentAttempts", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(evidenceField);
        var attempts = Assert.IsType<List<AttemptRecord>>(evidenceField.GetValue(session));
        attempts.Add(new AttemptRecord(
            "post-presentation-evidence",
            session.CurrentFact.Id,
            session.CurrentFact.Operation,
            session.CurrentFact.LeftOperand,
            session.CurrentFact.RightOperand,
            session.CurrentFact.CorrectResult,
            session.CurrentFact.CorrectResult,
            true,
            false,
            12000,
            DateTimeOffset.UnixEpoch,
            AttemptOutcome.Correct,
            1));

        session.PausePractice();
        clock.ElapsedMs = 1234;
        _ = session.GetCurrentActiveElapsedMs();
        session.StartOrResumePractice();
        _ = session.IsCurrentItemTimedOut();
        var partialAnswer = NumericAnswerInputPolicy.Append(string.Empty, "1");
        Assert.Equal("1", partialAnswer);

        Assert.Equal(easy, session.CurrentFactEasyThresholdMs);
        Assert.Equal(fluent, session.CurrentFactFluencyThresholdMs);
    }

    [Fact]
    public async Task FreshDatabase_IsSchemaV6WithConstrainedDurableFluency()
    {
        var path = Path.Combine(_directory, "fresh-v6.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(6, snapshot.SchemaVersion);

        await store.CloseAsync();
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'attempt_history';";
        var sql = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.Contains("is_fluent INTEGER NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_fluent IN (0, 1)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_correct = 1 AND outcome = 'Correct'", sql, StringComparison.OrdinalIgnoreCase);

        command.CommandText = @"
            INSERT INTO attempt_history (
                submission_id, fact_id, operation, left_operand, right_operand,
                submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                response_latency_ms, timestamp, practice_position)
            VALUES ('invalid-fluent', 'add:0+0', 'Addition', 0, 0, 1, 0, 0, 1,
                'Incorrect', 1, '2026-09-11T00:00:00Z', NULL);";
        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public void AttemptRecord_RejectsInconsistentOutcomeCorrectnessAndFluency()
    {
        Assert.Throws<ArgumentException>(() => Attempt(
            "incorrect-marked-correct", 1, "add:0+0", true, false, 1000, AttemptOutcome.Incorrect));
        Assert.Throws<ArgumentException>(() => Attempt(
            "correct-marked-incorrect", 1, "add:0+0", false, false, 1000, AttemptOutcome.Correct));
        Assert.Throws<ArgumentException>(() => Attempt(
            "incorrect-marked-fluent", 1, "add:0+0", false, true, 1000, AttemptOutcome.Incorrect));
        Assert.Throws<ArgumentException>(() => Attempt(
            "timeout-marked-fluent", 1, "add:0+0", false, true, 1000, AttemptOutcome.Timeout));
    }

    [Fact]
    public async Task SameLatencyAttempts_RetainOppositeHistoricalFluencyAcrossSqliteReopen()
    {
        const long latencyMs = 3000;
        const long firstPresentationThresholdMs = 3500;
        const long secondPresentationThresholdMs = 2200;
        var path = Path.Combine(_directory, "opposite-fluency.db");

        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            await store.CloseAsync();
        }

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position)
                VALUES
                    ('presentation-3500', 'add:0+0', 'Addition', 0, 0, 0, 0, 1, @first_fluent,
                        'Correct', @latency, '2026-09-11T00:00:01Z', 1),
                    ('presentation-2200', 'add:0+1', 'Addition', 0, 1, 1, 1, 1, @second_fluent,
                        'Correct', @latency, '2026-09-11T00:00:02Z', 5);";
            command.Parameters.AddWithValue("@first_fluent", latencyMs <= firstPresentationThresholdMs ? 1 : 0);
            command.Parameters.AddWithValue("@second_fluent", latencyMs <= secondPresentationThresholdMs ? 1 : 0);
            command.Parameters.AddWithValue("@latency", latencyMs);
            await command.ExecuteNonQueryAsync();
        }

        using var reopened = new SqliteLearnerStore(path);
        var snapshot = await reopened.LoadSnapshotAsync();
        var attempts = snapshot.RecentAttempts.OrderBy(attempt => attempt.PracticePosition).ToArray();

        Assert.Equal(2, attempts.Length);
        Assert.All(attempts, attempt => Assert.Equal(latencyMs, attempt.ResponseLatencyMs));
        Assert.True(attempts[0].IsFluent);
        Assert.False(attempts[1].IsFluent);
    }

    [Fact]
    public async Task InMemorySnapshotReload_RetainsExactHistoricalFluency()
    {
        var attempts = new[]
        {
            Attempt("presentation-3500", 1, "add:0+0", true, true, 3000, AttemptOutcome.Correct),
            Attempt("presentation-2200", 5, "add:0+1", true, false, 3000, AttemptOutcome.Correct)
        };
        using var store = new SnapshotStore(FreshSnapshot(attempts));

        var reloaded = await store.LoadSnapshotAsync();

        Assert.True(reloaded.RecentAttempts[0].IsFluent);
        Assert.False(reloaded.RecentAttempts[1].IsFluent);
    }

    [Fact]
    public async Task StandardAdvancement_UsesPersistedMixedFluencyAtExactThirtyFourBoundaryAfterReload()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var frontier = new AcquisitionOwnershipResolver(curriculum).GetOwnedFrontier(0);
        var mixed = CreateMixedStandardAttempts(frontier, adaptiveFluentCount: 14);
        using var store = new SnapshotStore(FreshSnapshot(mixed));

        var reloaded = await store.LoadSnapshotAsync();
        var passing = Evaluate(
            new OperationProgression(ArithmeticOperation.Addition, 0, 0),
            curriculum,
            reloaded.RecentAttempts,
            frontier.Select(fact => fact.Id));
        var failing = Evaluate(
            new OperationProgression(ArithmeticOperation.Addition, 0, 0),
            curriculum,
            CreateMixedStandardAttempts(frontier, adaptiveFluentCount: 13),
            frontier.Select(fact => fact.Id));

        Assert.True(passing.Advances);
        Assert.False(failing.Advances);
    }

    [Fact]
    public async Task SqliteMigrationAndRestart_PreserveMixedLegacyAdaptiveThirtyFourFluentBoundary()
    {
        var path = Path.Combine(_directory, "mixed-advancement.db");
        var curriculum = new ArithmeticCurriculum().Addition;
        var frontier = new AcquisitionOwnershipResolver(curriculum).GetOwnedFrontier(0);
        await CreateV5DatabaseAsync(path, installBlockingTrigger: false);
        await ReplaceV5AttemptsWithHistoricalAdvancementEvidenceAsync(path, frontier);

        using (var migratingStore = new SqliteLearnerStore(path))
        {
            await migratingStore.InitializeAsync();
            await migratingStore.CloseAsync();
        }
        await InsertAdaptiveV6AdvancementEvidenceAsync(path, adaptiveFluentCount: 14);

        using (var reopened = new SqliteLearnerStore(path))
        {
            var snapshot = await reopened.LoadSnapshotAsync();
            Assert.Equal(40, snapshot.RecentAttempts.Count);
            Assert.Equal(34, snapshot.RecentAttempts.Count(attempt => attempt.IsFluent));
            Assert.All(
                snapshot.RecentAttempts.Where(attempt => attempt.SubmissionId.StartsWith("historical-v5-", StringComparison.Ordinal)),
                attempt => Assert.True(attempt.IsFluent));
            Assert.Contains(
                snapshot.RecentAttempts,
                attempt => attempt.SubmissionId.StartsWith("adaptive-v6-", StringComparison.Ordinal)
                    && attempt.ResponseLatencyMs == 3000
                    && attempt.IsFluent);
            Assert.True(Evaluate(
                new OperationProgression(ArithmeticOperation.Addition, 0, 0),
                curriculum,
                snapshot.RecentAttempts,
                frontier.Select(fact => fact.Id)).Advances);
        }

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE attempt_history SET is_fluent = 0 WHERE submission_id = 'adaptive-v6-13';";
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        using var secondRestart = new SqliteLearnerStore(path);
        var belowBoundary = await secondRestart.LoadSnapshotAsync();
        Assert.Equal(33, belowBoundary.RecentAttempts.Count(attempt => attempt.IsFluent));
        Assert.False(Evaluate(
            new OperationProgression(ArithmeticOperation.Addition, 0, 0),
            curriculum,
            belowBoundary.RecentAttempts,
            frontier.Select(fact => fact.Id)).Advances);
    }

    [Fact]
    public void InitialMultiplicationAdvancement_UsesPersistedMixedFluencyAtExactElevenBoundary()
    {
        var curriculum = new ArithmeticCurriculum().Multiplication;
        var frontier = new AcquisitionOwnershipResolver(curriculum).GetOwnedFrontier(0);
        var passingAttempts = CreateMixedMultiplicationAttempts(frontier, adaptiveFluentCount: 5);
        var failingAttempts = CreateMixedMultiplicationAttempts(frontier, adaptiveFluentCount: 4);

        var passing = Evaluate(
            new OperationProgression(ArithmeticOperation.Multiplication, 0, 0),
            curriculum,
            passingAttempts,
            frontier.Select(fact => fact.Id));
        var failing = Evaluate(
            new OperationProgression(ArithmeticOperation.Multiplication, 0, 0),
            curriculum,
            failingAttempts,
            frontier.Select(fact => fact.Id));

        Assert.True(passing.Advances);
        Assert.False(failing.Advances);
    }

    [Fact]
    public async Task V5Migration_BackfillsFixedHistoricalFluencyAndPreservesNullPosition()
    {
        var path = Path.Combine(_directory, "migrate-v5.db");
        await CreateV5DatabaseAsync(path, installBlockingTrigger: false);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        await store.CloseAsync();

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
        Assert.Equal("6", Assert.IsType<string>(await command.ExecuteScalarAsync()));

        command.CommandText = "SELECT submission_id, is_fluent, practice_position FROM attempt_history ORDER BY submission_id;";
        using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string Id, long Fluent, bool NullPosition)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1), reader.IsDBNull(2)));
        }

        Assert.Equal(
            [
                ("correct-2500", 1L, true),
                ("correct-2501", 0L, true),
                ("incorrect-fast", 0L, true),
                ("timeout-fast", 0L, true)
            ],
            rows);
    }

    [Fact]
    public async Task V5Migration_LateSchemaVersionFailureRollsBackEverything()
    {
        var path = Path.Combine(_directory, "rollback-v5.db");
        await CreateV5DatabaseAsync(path, installBlockingTrigger: true);

        using var store = new SqliteLearnerStore(path);
        await Assert.ThrowsAsync<SqliteException>(() => store.InitializeAsync());
        await store.CloseAsync();

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
        Assert.Equal("5", Assert.IsType<string>(await command.ExecuteScalarAsync()));
        command.CommandText = "SELECT COUNT(*) FROM attempt_history;";
        Assert.Equal(4L, (long)(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('attempt_history') WHERE name = 'is_fluent';";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT practice_position FROM learner_progression WHERE id = 1;";
        Assert.Equal(37L, (long)(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT band_index, band_started_practice_position FROM operation_progression WHERE operation = 'Addition';";
        using (var progressionReader = await command.ExecuteReaderAsync())
        {
            Assert.True(await progressionReader.ReadAsync());
            Assert.Equal(0L, progressionReader.GetInt64(0));
            Assert.Equal(0L, progressionReader.GetInt64(1));
        }
        command.CommandText = "SELECT fluent_streak FROM item_learning_state WHERE fact_id = 'add:1+1';";
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT last_rating FROM fsrs_card_state WHERE fact_id = 'add:1+1';";
        Assert.Equal(3L, (long)(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT value FROM schema_info WHERE key = 'store_revision';";
        Assert.Equal("9", Assert.IsType<string>(await command.ExecuteScalarAsync()));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static LearnerSnapshot FreshSnapshot(IReadOnlyList<AttemptRecord>? attempts = null) => new(
        LearnerProgression.CreateFresh(),
        new Dictionary<string, ItemLearningState>(StringComparer.Ordinal),
        new Dictionary<string, FsrsCardState>(StringComparer.Ordinal),
        attempts ?? [],
        1,
        LearnerProgression.DefaultSchemaVersion);

    private static AttemptRecord Attempt(
        string id,
        long practicePosition,
        string factId,
        bool isCorrect,
        bool isFluent,
        long latencyMs,
        AttemptOutcome outcome)
    {
        var operation = factId.StartsWith("mul:", StringComparison.Ordinal)
            ? ArithmeticOperation.Multiplication
            : ArithmeticOperation.Addition;
        return new AttemptRecord(
            id,
            factId,
            operation,
            0,
            0,
            outcome == AttemptOutcome.Timeout ? null : 0,
            0,
            isCorrect,
            isFluent,
            latencyMs,
            DateTimeOffset.UnixEpoch.AddSeconds(practicePosition),
            outcome,
            practicePosition);
    }

    private static AttemptRecord[] CreateMixedStandardAttempts(
        IReadOnlyList<ArithmeticFact> frontier,
        int adaptiveFluentCount) => Enumerable.Range(0, 40)
        .Select(index =>
        {
            var isHistorical = index < 20;
            var isFluent = isHistorical || index - 20 < adaptiveFluentCount;
            var factId = isHistorical ? frontier[index % Math.Min(16, frontier.Count)].Id : "add:999+999";
            var latency = isHistorical ? 2500 : isFluent ? 3000 : 1000;
            return Attempt(
                $"{(isHistorical ? "historical-v5" : "adaptive-v6")}-{index}",
                index + 1,
                factId,
                true,
                isFluent,
                latency,
                AttemptOutcome.Correct);
        })
        .ToArray();

    private static AttemptRecord[] CreateMixedMultiplicationAttempts(
        IReadOnlyList<ArithmeticFact> frontier,
        int adaptiveFluentCount) => Enumerable.Range(0, 12)
        .Select(index =>
        {
            var isHistorical = index < 6;
            var isCorrect = index < 11;
            var isFluent = isCorrect && (isHistorical || index - 6 < adaptiveFluentCount);
            var factId = index < 8 ? frontier[index % frontier.Count].Id : "mul:2*2";
            var latency = isHistorical ? 2500 : isFluent ? 3000 : 1000;
            return Attempt(
                $"{(isHistorical ? "historical-v5" : "adaptive-v6")}-mul-{index}",
                index + 1,
                factId,
                isCorrect,
                isFluent,
                latency,
                isCorrect ? AttemptOutcome.Correct : AttemptOutcome.Incorrect);
        })
        .ToArray();

    private static BandAdvancementDecision Evaluate(
        OperationProgression progression,
        OperationCurriculum curriculum,
        IEnumerable<AttemptRecord> attempts,
        IEnumerable<string> lifetimeAttemptedFactIds)
    {
        var evidence = attempts.Select(attempt => new BandAttemptEvidence(
            attempt.PracticePosition!.Value,
            attempt.FactId,
            attempt.IsCorrect,
            attempt.IsFluent,
            attempt.ResponseLatencyMs));
        return new BandAdvancementEvaluator().Evaluate(
            progression,
            curriculum,
            new BandAdvancementEvidence(evidence, lifetimeAttemptedFactIds, []));
    }

    private static async Task CreateV5DatabaseAsync(string path, bool installBlockingTrigger)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO schema_info VALUES ('schema_version', '5');
            INSERT INTO schema_info VALUES ('store_revision', '9');
            CREATE TABLE learner_progression (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                practice_position INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL);
            INSERT INTO learner_progression VALUES (1, 37, '2026-09-11T00:00:00Z');
            CREATE TABLE operation_progression (
                operation TEXT PRIMARY KEY,
                band_index INTEGER NOT NULL CHECK (band_index >= 0),
                band_started_practice_position INTEGER NOT NULL CHECK (band_started_practice_position >= 0));
            INSERT INTO operation_progression VALUES ('Addition', 0, 0);
            INSERT INTO operation_progression VALUES ('Subtraction', 0, 0);
            INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
            INSERT INTO operation_progression VALUES ('Division', 0, 0);
            CREATE TABLE item_learning_state (
                fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL,
                right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL,
                correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL,
                consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL,
                rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL,
                is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL,
                remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL,
                last_practiced_at TEXT);
            INSERT INTO item_learning_state VALUES
                ('add:1+1', 'Addition', 1, 1, 2, 2, 0, 2, 900, 950, 2, 0, 0, 0, 2, '2026-09-11T00:00:00Z');
            CREATE TABLE attempt_history (
                submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL,
                left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL,
                submitted_answer INTEGER, correct_answer INTEGER NOT NULL,
                is_correct INTEGER NOT NULL, outcome TEXT NOT NULL DEFAULT 'Incorrect',
                response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL,
                practice_position INTEGER CHECK (practice_position IS NULL OR practice_position > 0));
            INSERT INTO attempt_history VALUES
                ('correct-2500', 'add:1+1', 'Addition', 1, 1, 2, 2, 1, 'Correct', 2500, '2026-09-11T00:00:01Z', NULL),
                ('correct-2501', 'add:1+1', 'Addition', 1, 1, 2, 2, 1, 'Correct', 2501, '2026-09-11T00:00:02Z', NULL),
                ('incorrect-fast', 'add:1+1', 'Addition', 1, 1, 3, 2, 0, 'Incorrect', 1, '2026-09-11T00:00:03Z', NULL),
                ('timeout-fast', 'add:1+1', 'Addition', 1, 1, NULL, 2, 0, 'Timeout', 1, '2026-09-11T00:00:04Z', NULL);
            CREATE TABLE fsrs_card_state (
                fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL,
                step INTEGER, stability REAL, difficulty REAL,
                due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER,
                last_rating INTEGER);
            INSERT INTO fsrs_card_state VALUES
                ('add:1+1', '00000000-0000-0000-0000-000000000001', 2, NULL, 1.5, 4.5, 40, 37, 3);
            CREATE UNIQUE INDEX ux_attempt_history_practice_position
                ON attempt_history(practice_position) WHERE practice_position IS NOT NULL;
            CREATE INDEX ix_attempt_history_operation_practice_position
                ON attempt_history(operation, practice_position DESC) WHERE practice_position IS NOT NULL;";
        await command.ExecuteNonQueryAsync();

        if (installBlockingTrigger)
        {
            command.CommandText = @"
                CREATE TRIGGER block_schema_v6
                BEFORE UPDATE OF value ON schema_info
                WHEN OLD.key = 'schema_version' AND NEW.value = '6'
                BEGIN
                    SELECT RAISE(ABORT, 'synthetic late V6 migration failure');
                END;";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task ReplaceV5AttemptsWithHistoricalAdvancementEvidenceAsync(
        string path,
        IReadOnlyList<ArithmeticFact> frontier)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM attempt_history;";
        await command.ExecuteNonQueryAsync();

        for (var index = 0; index < 20; index++)
        {
            var fact = frontier[index % Math.Min(16, frontier.Count)];
            command.Parameters.Clear();
            command.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, outcome,
                    response_latency_ms, timestamp, practice_position)
                VALUES (@id, @fact, 'Addition', @left, @right, @answer, @answer, 1,
                    'Correct', 2500, @timestamp, @position);";
            command.Parameters.AddWithValue("@id", $"historical-v5-{index}");
            command.Parameters.AddWithValue("@fact", fact.Id);
            command.Parameters.AddWithValue("@left", fact.LeftOperand);
            command.Parameters.AddWithValue("@right", fact.RightOperand);
            command.Parameters.AddWithValue("@answer", fact.CorrectResult);
            command.Parameters.AddWithValue("@timestamp", DateTimeOffset.UnixEpoch.AddSeconds(index + 1).ToString("O"));
            command.Parameters.AddWithValue("@position", index + 1);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertAdaptiveV6AdvancementEvidenceAsync(string path, int adaptiveFluentCount)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        for (var index = 0; index < 20; index++)
        {
            var isFluent = index < adaptiveFluentCount;
            command.Parameters.Clear();
            command.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position)
                VALUES (@id, 'add:999+999', 'Addition', 999, 999, 1998, 1998, 1,
                    @fluent, 'Correct', @latency, @timestamp, @position);";
            command.Parameters.AddWithValue("@id", $"adaptive-v6-{index}");
            command.Parameters.AddWithValue("@fluent", isFluent ? 1 : 0);
            command.Parameters.AddWithValue("@latency", isFluent ? 3000 : 1000);
            command.Parameters.AddWithValue("@timestamp", DateTimeOffset.UnixEpoch.AddSeconds(index + 21).ToString("O"));
            command.Parameters.AddWithValue("@position", index + 21);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class FakeClock : IClock
    {
        public long ElapsedMs { get; set; }
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(ElapsedMs);
    }

    private sealed class SnapshotStore(LearnerSnapshot snapshot) : ILearnerStore
    {
        public string StoragePath => "inmemory://slice2";
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default) =>
            Task.FromResult(PersistenceResult.Success(changeSet.ExpectedRevision + 1));
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
