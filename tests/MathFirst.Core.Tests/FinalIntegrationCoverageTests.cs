namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using System.Reflection;

public sealed class FinalIntegrationCoverageTests : IDisposable
{
    private const long MigrationPracticePosition = 200;
    private const long MigrationRevision = 37;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstFinalIntegration_" + Guid.NewGuid().ToString("N"));

    public FinalIntegrationCoverageTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task RestartAroundAdvancement_PreservesCommittedStateAndExcludesTheTriggerFromTheNewBandWindow()
    {
        var continuousPath = Path.Combine(_directory, "advancement-continuous.db");
        var beforeRestartPath = Path.Combine(_directory, "advancement-before-restart.db");
        var afterRestartPath = Path.Combine(_directory, "advancement-after-restart.db");
        await PrepareAdvancementTriggerAsync(continuousPath);
        await PrepareAdvancementTriggerAsync(beforeRestartPath);
        await PrepareAdvancementTriggerAsync(afterRestartPath);

        AdvancementRun continuous;
        using (var store = new SqliteLearnerStore(continuousPath))
        {
            var session = new TrainingSession(store, new ScriptedClock());
            await session.InitializeAsync(startTiming: false);
            continuous = await CommitAdvancementTriggerAsync(session);
        }

        SelectionFingerprint beforeRestartTrigger;
        using (var store = new SqliteLearnerStore(beforeRestartPath))
        {
            var session = new TrainingSession(store, new ScriptedClock());
            await session.InitializeAsync(startTiming: false);
            beforeRestartTrigger = CaptureSelection(session);
            Assert.Equal(continuous.Trigger, beforeRestartTrigger);
        }

        AdvancementRun restartedBefore;
        using (var store = new SqliteLearnerStore(beforeRestartPath))
        {
            var session = new TrainingSession(store, new ScriptedClock());
            await session.InitializeAsync(startTiming: false);
            Assert.Equal(beforeRestartTrigger, CaptureSelection(session));
            restartedBefore = await CommitAdvancementTriggerAsync(session);
        }

        long triggerPosition;
        SelectionFingerprint afterRestartTrigger;
        using (var store = new SqliteLearnerStore(afterRestartPath))
        {
            var session = new TrainingSession(store, new ScriptedClock());
            await session.InitializeAsync(startTiming: false);
            afterRestartTrigger = CaptureSelection(session);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
            triggerPosition = session.Progression.PracticePosition;
            Assert.Equal(afterRestartTrigger.PracticePosition, triggerPosition);
        }

        AdvancementRun restartedAfter;
        using (var store = new SqliteLearnerStore(afterRestartPath))
        {
            var session = new TrainingSession(store, new ScriptedClock());
            await session.InitializeAsync(startTiming: false);
            restartedAfter = new AdvancementRun(afterRestartTrigger, CaptureState(session), triggerPosition);
        }

        AssertEquivalentAdvancementRun(continuous, restartedBefore);
        AssertEquivalentAdvancementRun(continuous, restartedAfter);
        Assert.Equal(1, continuous.State.Progressions[ArithmeticOperation.Addition].BandIndex);
        Assert.Equal(continuous.Trigger.PracticePosition, continuous.State.Progressions[ArithmeticOperation.Addition].BandStartedPracticePosition);
        Assert.Equal(0, continuous.State.Progressions[ArithmeticOperation.Multiplication].BandIndex);
        Assert.Equal(1, continuous.State.Progressions[ArithmeticOperation.Subtraction].BandIndex);
        Assert.Equal(1, continuous.State.Progressions[ArithmeticOperation.Division].BandIndex);

        using var reloadedStore = new SqliteLearnerStore(afterRestartPath);
        await reloadedStore.InitializeAsync();
        var runtime = await reloadedStore.LoadRuntimeSnapshotAsync();
        var addition = runtime.OperationProgressions![ArithmeticOperation.Addition];
        var currentBandEvidence = runtime.RecentAttempts
            .Where(attempt => attempt.Operation == ArithmeticOperation.Addition)
            .Where(attempt => attempt.PracticePosition > addition.BandStartedPracticePosition)
            .ToArray();
        Assert.Empty(currentBandEvidence);
        Assert.DoesNotContain(runtime.RecentAttempts, attempt =>
            attempt.PracticePosition == triggerPosition && attempt.PracticePosition > addition.BandStartedPracticePosition);
        Assert.Contains(runtime.RecentAttempts, attempt => attempt.PracticePosition == triggerPosition);
    }

    [Fact]
    public async Task PopulatedV4Migration_ContinuesThroughV5TrainingAndRestartWithoutRewritingLegacyHistory()
    {
        var path = Path.Combine(_directory, "populated-v4.db");
        var fixture = await CreatePopulatedV4DatabaseAsync(path);

        using (var migrationStore = new SqliteLearnerStore(path))
        {
            await migrationStore.InitializeAsync();
            var snapshot = await migrationStore.LoadSnapshotAsync();
            Assert.Equal(6, snapshot.SchemaVersion);
            Assert.Equal(MigrationPracticePosition, snapshot.Progression.PracticePosition);
            Assert.Equal(MigrationRevision, snapshot.Revision);
            Assert.Equal(fixture.OperationMaximums.Select(pair => new OperationProgression(pair.Key, pair.Value - 1, MigrationPracticePosition)).OrderBy(value => value.Operation),
                snapshot.OperationProgressions!.Values.OrderBy(value => value.Operation));
            Assert.Equal(fixture.ItemFactIds.OrderBy(id => id, StringComparer.Ordinal), snapshot.ItemStates.Keys.OrderBy(id => id, StringComparer.Ordinal));
            Assert.Contains(snapshot.ItemStates.Values, state => state.NeedsRemediation && state.RemediationDueOrder == 999);

            var additionProgression = snapshot.OperationProgressions[ArithmeticOperation.Addition];
            var additionCurriculum = new ArithmeticCurriculum().GetCurriculum(ArithmeticOperation.Addition);
            var additionOwned = new AcquisitionOwnershipResolver(additionCurriculum).GetOwnedFrontier(additionProgression.BandIndex);
            var selectionEvidence = await migrationStore.LoadPracticeSelectionEvidenceAsync(new PracticeSelectionEvidenceRequest(
                ArithmeticOperation.Addition,
                MigrationPracticePosition + 1,
                1,
                additionOwned,
                additionOwned));
            Assert.Equal(additionOwned.Select(fact => fact.Id).OrderBy(id => id, StringComparer.Ordinal),
                selectionEvidence.CurrentBandCandidates.Select(candidate => candidate.Fact.Id).OrderBy(id => id, StringComparer.Ordinal));
            Assert.All(selectionEvidence.CurrentBandCandidates, candidate => Assert.True(candidate.ItemState.TotalAttempts > 0));
        }

        await AssertLegacyHistoryAsync(path, fixture, expectedPositionedCount: 0);
        Assert.Equal(
            ["id", "practice_position", "updated_at"],
            await ReadColumnNamesAsync(path, "learner_progression"));

        const int acceptedV5Count = 160;
        DurableState stateAtRestart;
        using (var store = new SqliteLearnerStore(path))
        {
            var session = new TrainingSession(store, new ScriptedClock());
            await session.InitializeAsync(startTiming: false);
            for (var offset = 1; offset <= acceptedV5Count / 2; offset++)
            {
                await SubmitFluentAndAdvanceAsync(session, MigrationPracticePosition + offset);
            }

            stateAtRestart = CaptureState(session);
        }

        using (var restartedStore = new SqliteLearnerStore(path))
        {
            var restartedSession = new TrainingSession(restartedStore, new ScriptedClock());
            await restartedSession.InitializeAsync(startTiming: false);
            AssertEquivalentState(stateAtRestart, CaptureState(restartedSession));

            for (var offset = (acceptedV5Count / 2) + 1; offset <= acceptedV5Count; offset++)
            {
                await SubmitFluentAndAdvanceAsync(restartedSession, MigrationPracticePosition + offset);
            }

            Assert.Equal(MigrationPracticePosition + acceptedV5Count, restartedSession.Progression.PracticePosition);
            Assert.All(Enum.GetValues<ArithmeticOperation>(), operation =>
                Assert.True(restartedSession.Progression.OperationProgressions[operation].BandIndex >= fixture.OperationMaximums[operation] - 1));
            Assert.Contains(restartedSession.Progression.OperationProgressions, pair => pair.Value.BandIndex > fixture.OperationMaximums[pair.Key] - 1);
        }

        using (var finalStore = new SqliteLearnerStore(path))
        {
            var finalSession = new TrainingSession(finalStore, new ScriptedClock());
            await finalSession.InitializeAsync(startTiming: false);
            var finalState = CaptureState(finalSession);
            Assert.Equal(MigrationPracticePosition + acceptedV5Count, finalState.PracticePosition);
            Assert.Equal(MigrationRevision + acceptedV5Count, finalState.Revision);
            Assert.Equal(AdaptivePracticeSelector.GetScheduledOperation(finalState.PracticePosition + 1), finalState.NextSelection.Operation);

            var runtime = await finalStore.LoadRuntimeSnapshotAsync();
            Assert.All(runtime.RecentAttempts, attempt => Assert.True(attempt.PracticePosition > MigrationPracticePosition));
            Assert.DoesNotContain(runtime.RecentAttempts, attempt => attempt.PracticePosition is null);
        }

        await AssertLegacyHistoryAsync(path, fixture, expectedPositionedCount: acceptedV5Count);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private static async Task PrepareAdvancementTriggerAsync(string path)
    {
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, new ScriptedClock());
        await session.InitializeAsync(startTiming: false);
        for (var position = 1L; position <= 28; position++)
        {
            await SubmitFluentAndAdvanceAsync(session, position);
        }

        Assert.Equal(28, session.Progression.PracticePosition);
        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
        Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
    }

    private static async Task SubmitFluentAndAdvanceAsync(TrainingSession session, long expectedPosition)
    {
        Assert.Equal(expectedPosition, session.Progression.PracticePosition + 1);
        Assert.Equal(AdaptivePracticeSelector.GetScheduledOperation(expectedPosition), session.CurrentFact.Operation);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        Assert.Equal(expectedPosition, session.Progression.PracticePosition);
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: false));
    }

    private static async Task<AdvancementRun> CommitAdvancementTriggerAsync(TrainingSession session)
    {
        var trigger = CaptureSelection(session);
        var before = session.Progression.OperationProgressions[ArithmeticOperation.Addition];
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: false));
        var run = new AdvancementRun(trigger, CaptureState(session), trigger.PracticePosition);
        Assert.Equal(before.BandIndex + 1, run.State.Progressions[ArithmeticOperation.Addition].BandIndex);
        return run;
    }

    private static DurableState CaptureState(TrainingSession session) => new(
        session.Progression.PracticePosition,
        session.Progression.StoreRevision,
        session.Progression.OperationProgressions.ToDictionary(pair => pair.Key, pair => pair.Value),
        CaptureSelectionAtCurrentPosition(session));

    private static SelectionFingerprint CaptureSelection(TrainingSession session)
    {
        var evidence = (PracticeSelectionEvidence)typeof(TrainingSession)
            .GetField("_selectionEvidence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(session)!;
        var recentAttempts = (IReadOnlyCollection<AttemptRecord>)typeof(TrainingSession)
            .GetField("_recentAttempts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(session)!;
        var curricula = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(operation => operation, operation => new ArithmeticCurriculum().GetCurriculum(operation));
        var result = new AdaptivePracticeSelector().SelectTargetFact(new PracticeSelectionContext(
            session.Progression.PracticePosition + 1,
            session.SessionOrderCounter,
            session.Progression.OperationProgressions,
            curricula,
            new PracticeCandidateIndex(evidence),
            recentAttempts.OrderBy(attempt => attempt.PracticePosition)
                .Select(attempt => new ArithmeticFact(attempt.Operation, attempt.LeftOperand, attempt.RightOperand))));
        Assert.Equal(session.CurrentFact.Id, result.Fact.Id);
        return new SelectionFingerprint(
            session.Progression.PracticePosition + 1,
            result.ScheduledOperation,
            result.RequestedRole,
            result.ResolvedRole,
            result.Fact.Id);
    }

    private static SelectionFingerprint CaptureSelectionAtCurrentPosition(TrainingSession session) => CaptureSelection(session);

    private static void AssertEquivalentAdvancementRun(AdvancementRun expected, AdvancementRun actual)
    {
        Assert.Equal(expected.Trigger, actual.Trigger);
        Assert.Equal(expected.TriggerPosition, actual.TriggerPosition);
        AssertEquivalentState(expected.State, actual.State);
    }

    private static void AssertEquivalentState(DurableState expected, DurableState actual)
    {
        Assert.Equal(expected.PracticePosition, actual.PracticePosition);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.NextSelection, actual.NextSelection);
        Assert.Equal(
            expected.Progressions.OrderBy(pair => pair.Key).Select(pair => pair.Value),
            actual.Progressions.OrderBy(pair => pair.Key).Select(pair => pair.Value));
    }

    private static async Task<PopulatedV4Fixture> CreatePopulatedV4DatabaseAsync(string path)
    {
        var maximums = new Dictionary<ArithmeticOperation, int>
        {
            [ArithmeticOperation.Addition] = 2,
            [ArithmeticOperation.Subtraction] = 1,
            [ArithmeticOperation.Multiplication] = 1,
            [ArithmeticOperation.Division] = 1
        };
        var curriculum = new ArithmeticCurriculum();
        var facts = maximums.SelectMany(pair => new AcquisitionOwnershipResolver(curriculum.GetCurriculum(pair.Key))
            .GetOwnedFrontier(pair.Value - 1)).ToArray();

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, transaction, @"
            CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO schema_info VALUES ('schema_version', '4');
            INSERT INTO schema_info VALUES ('store_revision', '37');
            CREATE TABLE learner_progression (
                id INTEGER PRIMARY KEY CHECK (id = 1), current_operation TEXT NOT NULL,
                current_max_operand INTEGER NOT NULL, operation_max_operands_json TEXT NOT NULL,
                practice_position INTEGER NOT NULL, completed_checkpoint_level INTEGER NOT NULL,
                active_checkpoint_level INTEGER, checkpoint_attempt_count INTEGER NOT NULL,
                checkpoint_correct_count INTEGER NOT NULL, updated_at TEXT NOT NULL);
            INSERT INTO learner_progression VALUES (1, 'Addition', 2,
                '{""Addition"":2,""Subtraction"":1,""Multiplication"":1,""Division"":1}', 200, 0, NULL, 0, 0, '2026-09-09T00:00:00Z');
            CREATE TABLE item_learning_state (fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL, correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL, consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL, rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL, is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL, remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL, last_practiced_at TEXT);
            CREATE TABLE attempt_history (submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, submitted_answer INTEGER, correct_answer INTEGER NOT NULL, is_correct INTEGER NOT NULL, outcome TEXT NOT NULL, response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL);
            CREATE TABLE fsrs_card_state (fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL, step INTEGER, stability REAL, difficulty REAL, due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER, last_rating INTEGER);",
            CancellationToken.None);

        for (var index = 0; index < facts.Length; index++)
        {
            var fact = facts[index];
            await InsertPopulatedV4FactAsync(connection, transaction, fact, index);
        }

        await transaction.CommitAsync();
        return new PopulatedV4Fixture(maximums, facts.Select(fact => fact.Id).ToArray(), facts.Length);
    }

    private static async Task InsertPopulatedV4FactAsync(SqliteConnection connection, SqliteTransaction transaction, ArithmeticFact fact, int index)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO item_learning_state VALUES (@fact_id, @operation, @left, @right, 3, 3, 0, 3, 900, 900, 3, 1, @remediation, @remediation_order, @last_order, '2026-09-09T00:00:00Z');
            INSERT INTO attempt_history VALUES (@submission_id, @fact_id, @operation, @left, @right, @answer, @answer, 1, 'Correct', 900, '2026-09-09T00:00:00Z');
            INSERT INTO fsrs_card_state VALUES (@fact_id, @card_id, 2, NULL, 3.0, 2.5, 10000, 100, 3);";
        command.Parameters.AddWithValue("@fact_id", fact.Id);
        command.Parameters.AddWithValue("@operation", fact.Operation.ToString());
        command.Parameters.AddWithValue("@left", fact.LeftOperand);
        command.Parameters.AddWithValue("@right", fact.RightOperand);
        command.Parameters.AddWithValue("@answer", fact.CorrectResult);
        command.Parameters.AddWithValue("@submission_id", $"legacy-{index:D3}");
        command.Parameters.AddWithValue("@card_id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("@remediation", index == 0 ? 1 : 0);
        command.Parameters.AddWithValue("@remediation_order", index == 0 ? 999 : 0);
        command.Parameters.AddWithValue("@last_order", 100 + index);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertLegacyHistoryAsync(string path, PopulatedV4Fixture fixture, int expectedPositionedCount)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                SUM(CASE WHEN submission_id LIKE 'legacy-%' AND practice_position IS NULL THEN 1 ELSE 0 END),
                SUM(CASE WHEN submission_id LIKE 'legacy-%' AND practice_position IS NOT NULL THEN 1 ELSE 0 END),
                COUNT(CASE WHEN practice_position IS NOT NULL THEN 1 END),
                COUNT(DISTINCT practice_position)
            FROM attempt_history;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(fixture.LegacyAttemptCount, reader.GetInt64(0));
        Assert.Equal(0, reader.GetInt64(1));
        Assert.Equal(expectedPositionedCount, reader.GetInt64(2));
        Assert.Equal(expectedPositionedCount, reader.GetInt64(3));
    }

    private static async Task<IReadOnlyList<string>> ReadColumnNamesAsync(string path, string table)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class ScriptedClock : IClock
    {
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(900);
    }

    private sealed record SelectionFingerprint(
        long PracticePosition,
        ArithmeticOperation Operation,
        PracticeSelectionRole RequestedRole,
        PracticeSelectionRole ResolvedRole,
        string FactId);

    private sealed record DurableState(
        long PracticePosition,
        long Revision,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression> Progressions,
        SelectionFingerprint NextSelection);

    private sealed record AdvancementRun(SelectionFingerprint Trigger, DurableState State, long TriggerPosition);

    private sealed record PopulatedV4Fixture(
        IReadOnlyDictionary<ArithmeticOperation, int> OperationMaximums,
        IReadOnlyList<string> ItemFactIds,
        int LegacyAttemptCount);
}
