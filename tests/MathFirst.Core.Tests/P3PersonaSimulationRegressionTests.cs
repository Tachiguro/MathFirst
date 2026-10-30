namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class P3PersonaSimulationRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstP3Persona_" + Guid.NewGuid().ToString("N"));

    public P3PersonaSimulationRegressionTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch { }
    }

    private string GetDatabasePath(string name = "persona") =>
        Path.Combine(_directory, $"{name}_{Guid.NewGuid():N}.db");

    private sealed class ScriptedLatencyClock : IClock
    {
        public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(800);
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp) => Latency;
    }

    // =========================================================================
    // PERSONA A — BEGINNER / YOUNG CHILD TRAJECTORY
    // =========================================================================

    [Fact]
    public async Task PersonaA_BeginnerLearner_ProgressesDeterministicallyThroughStagesWithBroadWeaknessProtection()
    {
        var path = GetDatabasePath("persona_a");
        var clock = new ScriptedLatencyClock { Latency = TimeSpan.FromMilliseconds(6500) }; // slow timing

        // A1. Fresh state: Stage1
        using (var store = new SqliteLearnerStore(path))
        {
            var session = new TrainingSession(store, clock, practiceMode: PracticeMode.CurriculumManaged);
            await session.InitializeAsync(startTiming: false);

            Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
            Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);

            // A2. Introduce all ADD-D01 facts (0+0, 0+1, 1+0, 1+1).
            // We want 0+0 and 1+1 to be weak, 0+1 and 1+0 to be correct.
            var addPrereqs = CurriculumUnlockPolicy.GetPrerequisiteFactIds(CurriculumStage.Stage1_Addition);
            var maxTurns = 30;
            while (maxTurns-- > 0 && !addPrereqs.All(id => session.ItemStates.ContainsKey(id) && session.ItemStates[id].TotalAttempts > 0))
            {
                var fact = session.CurrentFact;
                var shouldFail = fact.Id is "add:0+0" or "add:1+1";
                if (shouldFail)
                {
                    session.SubmitAnswer(fact.CorrectResult + 1);
                    await session.CommitCurrentEvaluationAsync();
                    await session.AcknowledgeFeedbackAsync(startTiming: false);
                }
                else
                {
                    session.SubmitAnswer(fact.CorrectResult);
                    await session.CommitCurrentEvaluationAsync();
                    if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
                    {
                        session.ContinuePractice(startTiming: false);
                    }
                }
            }

            // Ensure all 4 ADD-D01 facts are introduced
            Assert.All(addPrereqs, id => Assert.True(session.ItemStates.ContainsKey(id) && session.ItemStates[id].TotalAttempts > 0));

            // Ensure 0+0 and 1+1 are weak
            session.ItemStates["add:0+0"].NeedsRemediation = true;
            session.ItemStates["add:1+1"].NeedsRemediation = true;
            session.ItemStates["add:0+1"].NeedsRemediation = false;
            session.ItemStates["add:1+0"].NeedsRemediation = false;

            // Two weak facts exist (0+0, 1+1) -> HasBroadWeakness = true, Stage remains Stage 1
            var weakAddFacts = addPrereqs.Where(id => session.ItemStates[id].NeedsRemediation).ToList();
            Assert.Equal(2, weakAddFacts.Count);
            Assert.True(session.HasBroadWeakness);
            Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);

            // A3. Slow latency alone does not change the stage decision
            clock.Latency = TimeSpan.FromMilliseconds(15000); // 15 seconds
            Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);

            // A4. ADR-0013: resolving down to one weak fact no longer unlocks Stage 2 from introductory D01 exposure.
            session.ItemStates["add:0+0"].NeedsRemediation = false;
            session.ItemStates["add:0+0"].CorrectAttempts++;

            Assert.False(session.HasBroadWeakness);
            Assert.Equal(1, CurriculumUnlockPolicy.CountPrerequisiteWeakFacts(CurriculumStage.Stage1_Addition, session.ItemStates));
            Assert.False(CurriculumUnlockPolicy.IsPrerequisiteMastered(
                CurriculumStage.Stage1_Addition,
                session.ItemStates,
                session.Progression.OperationProgressions));

            var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.NotNull(eval.ChangeSet);
            Assert.Equal(CurriculumStage.Stage1_Addition, eval.ChangeSet.UpdatedProgression.CurriculumStage);

            await session.CommitCurrentEvaluationAsync();
            Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);

            // A5. Only Addition remains scheduled for the beginner.
            var unlockedOps = CurriculumUnlockPolicy.GetUnlockedOperations(session.Progression.CurriculumStage);
            Assert.Equal([ArithmeticOperation.Addition], unlockedOps);
        }

        // Verify Stage 1 durability on cold reload
        using (var reopened = new SqliteLearnerStore(path))
        {
            var reopenedSession = new TrainingSession(reopened, clock, practiceMode: PracticeMode.CurriculumManaged);
            await reopenedSession.InitializeAsync(startTiming: false);
            Assert.Equal(CurriculumStage.Stage1_Addition, reopenedSession.Progression.CurriculumStage);
        }
    }

    // =========================================================================
    // PERSONA B — PERSISTENT ISOLATED WEAK FACT (ADR-0013: BLOCKS UNTIL RESOLVED, NO RELOCK)
    // =========================================================================

    [Fact]
    public async Task PersonaB_PersistentIsolatedWeakFact_BlocksUnlockUntilResolvedWithoutBandMutation()
    {
        var path = GetDatabasePath("persona_b");
        var clock = new ScriptedLatencyClock { Latency = TimeSpan.FromMilliseconds(1200) };

        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();

            // Addition has completed D01..D10 except one persistently weak foundation fact (add:1+1).
            var progression = LearnerProgression.CreateFresh();
            progression.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, D10MasteryTestSeed.MasteredBandIndex, 0);
            await store.SaveProgressionAsync(progression);
            await D10MasteryTestSeed.SeedMasteredAsync(path, ArithmeticOperation.Addition, exceptFactId: "add:1+1");
            await SeedItemStateAsync(path, new ItemLearningState
            {
                FactId = "add:1+1", Operation = ArithmeticOperation.Addition, LeftOperand = 1, RightOperand = 1,
                TotalAttempts = 5, CorrectAttempts = 0, IncorrectAttempts = 5, NeedsRemediation = true
            });

            var session = new TrainingSession(store, clock, practiceMode: PracticeMode.CurriculumManaged);
            await session.InitializeAsync(startTiming: false);

            // B1. One isolated weak fact is not broad weakness, but it is unresolved D10 evidence.
            Assert.False(session.HasBroadWeakness);
            Assert.NotEqual("add:1+1", session.CurrentFact.Id);

            // B2. Correct answers on other facts keep the learner in Stage 1 (no unearned mastery).
            for (var turn = 0; turn < 3; turn++)
            {
                await SubmitPersonaBTurnAsync(session);
                Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
            }

            // B3. ADD BandIndex is not mutated by the stage decision.
            Assert.Equal(D10MasteryTestSeed.MasteredBandIndex, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);

            // B4. A later correct remediation retrieval resolves add:1+1; the next accepted submission unlocks Stage 2.
            var recovered = session.ItemStates.TryGetValue("add:1+1", out var cached)
                ? cached
                : ItemLearningState.CreateNew(new ArithmeticFact(ArithmeticOperation.Addition, 1, 1));
            recovered.TotalAttempts = 6;
            recovered.CorrectAttempts = 1;
            recovered.IncorrectAttempts = 5;
            recovered.ConsecutiveCorrectStreak = 1;
            recovered.NeedsRemediation = false;
            session.ItemStates["add:1+1"] = recovered;

            await SubmitPersonaBTurnAsync(session);
            Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
        }

        // B5. Reopen: Stage 2 is durable and the band is unchanged.
        using (var reopened = new SqliteLearnerStore(path))
        {
            var reopenedSession = new TrainingSession(reopened, clock, practiceMode: PracticeMode.CurriculumManaged);
            await reopenedSession.InitializeAsync(startTiming: false);

            Assert.Equal(CurriculumStage.Stage2_Subtraction, reopenedSession.Progression.CurriculumStage);
            Assert.True(reopenedSession.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex >= D10MasteryTestSeed.MasteredBandIndex);
        }
    }

    private static async Task SubmitPersonaBTurnAsync(TrainingSession session)
    {
        var fact = session.CurrentFact;
        if (fact.Id == "add:1+1")
        {
            session.SubmitAnswer(fact.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            await session.AcknowledgeFeedbackAsync(startTiming: false);
        }
        else
        {
            session.SubmitAnswer(fact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
            {
                session.ContinuePractice(startTiming: false);
            }
        }
    }

    private static async Task SeedItemStateAsync(string dbPath, ItemLearningState item)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = dbPath };
        using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO item_learning_state (
                fact_id, operation, left_operand, right_operand,
                total_attempts, correct_attempts, incorrect_attempts,
                consecutive_correct, last_latency_ms, rolling_latency_ms,
                fluent_streak, is_mastered, needs_remediation,
                remediation_due_order, last_practiced_order, last_practiced_at
            ) VALUES (
                @fact_id, @operation, @left_operand, @right_operand,
                @total_attempts, @correct_attempts, @incorrect_attempts,
                @consecutive_correct, @last_latency_ms, @rolling_latency_ms,
                @fluent_streak, @is_mastered, @needs_remediation,
                @remediation_due_order, @last_practiced_order, @last_practiced_at
            )
            ON CONFLICT(fact_id) DO UPDATE SET
                total_attempts = excluded.total_attempts,
                correct_attempts = excluded.correct_attempts,
                incorrect_attempts = excluded.incorrect_attempts,
                needs_remediation = excluded.needs_remediation;
        ";
        cmd.Parameters.AddWithValue("@fact_id", item.FactId);
        cmd.Parameters.AddWithValue("@operation", item.Operation.ToString());
        cmd.Parameters.AddWithValue("@left_operand", item.LeftOperand);
        cmd.Parameters.AddWithValue("@right_operand", item.RightOperand);
        cmd.Parameters.AddWithValue("@total_attempts", item.TotalAttempts);
        cmd.Parameters.AddWithValue("@correct_attempts", item.CorrectAttempts);
        cmd.Parameters.AddWithValue("@incorrect_attempts", item.IncorrectAttempts);
        cmd.Parameters.AddWithValue("@consecutive_correct", item.ConsecutiveCorrectStreak);
        cmd.Parameters.AddWithValue("@last_latency_ms", item.LastLatencyMs);
        cmd.Parameters.AddWithValue("@rolling_latency_ms", item.RollingLatencyMs);
        cmd.Parameters.AddWithValue("@fluent_streak", item.FluentStreak);
        cmd.Parameters.AddWithValue("@is_mastered", item.IsProvisionallyMastered ? 1 : 0);
        cmd.Parameters.AddWithValue("@needs_remediation", item.NeedsRemediation ? 1 : 0);
        cmd.Parameters.AddWithValue("@remediation_due_order", item.RemediationDueOrder);
        cmd.Parameters.AddWithValue("@last_practiced_order", item.LastPracticedOrder);
        cmd.Parameters.AddWithValue("@last_practiced_at", item.LastPracticedAt?.ToString("O") ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // =========================================================================
    // PERSONA C — STRONG / FLUENT LEARNER
    // =========================================================================

    [Fact]
    public async Task PersonaC_StrongFluentLearner_AdvancesThroughAllStagesWithoutArbitraryQuotas()
    {
        var path = GetDatabasePath("persona_c");
        var clock = new ScriptedLatencyClock { Latency = TimeSpan.FromMilliseconds(750) };

        using (var store = new SqliteLearnerStore(path))
        {
            var session = new TrainingSession(store, clock, practiceMode: PracticeMode.CurriculumManaged);
            await session.InitializeAsync(startTiming: false);

            var seenPositions = new List<long>();

            // C1. Stage 1 -> Stage 2 after complete ADD-D01 introduction
            while (session.Progression.CurriculumStage == CurriculumStage.Stage1_Addition)
            {
                var fact = session.CurrentFact;
                Assert.Equal(ArithmeticOperation.Addition, fact.Operation);

                session.SubmitAnswer(fact.CorrectResult);
                var commitRes = await session.CommitCurrentEvaluationAsync();
                Assert.True(commitRes.IsSuccess);
                seenPositions.Add(session.Progression.PracticePosition);

                if (session.Progression.CurriculumStage == CurriculumStage.Stage2_Subtraction)
                {
                    break;
                }

                if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
                {
                    session.ContinuePractice(startTiming: false);
                }
            }

            Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
            Assert.True(CurriculumUnlockPolicy.IsPrerequisiteFullyIntroduced(CurriculumStage.Stage1_Addition, session.ItemStates));

            if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
            {
                session.ContinuePractice(startTiming: false);
            }

            // C2. Stage 2 -> Stage 3 after complete SUB-D01 introduction
            while (session.Progression.CurriculumStage == CurriculumStage.Stage2_Subtraction)
            {
                var fact = session.CurrentFact;
                Assert.Contains(fact.Operation, (IReadOnlyList<ArithmeticOperation>)[ArithmeticOperation.Addition, ArithmeticOperation.Subtraction]);

                session.SubmitAnswer(fact.CorrectResult);
                var commitRes = await session.CommitCurrentEvaluationAsync();
                Assert.True(commitRes.IsSuccess);
                seenPositions.Add(session.Progression.PracticePosition);

                if (session.Progression.CurriculumStage == CurriculumStage.Stage3_Multiplication)
                {
                    break;
                }

                if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
                {
                    session.ContinuePractice(startTiming: false);
                }
            }

            Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);
            Assert.True(CurriculumUnlockPolicy.IsPrerequisiteFullyIntroduced(CurriculumStage.Stage2_Subtraction, session.ItemStates));

            if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
            {
                session.ContinuePractice(startTiming: false);
            }

            // C3. Stage 3 -> Stage 4 after complete MUL-D01 introduction
            while (session.Progression.CurriculumStage == CurriculumStage.Stage3_Multiplication)
            {
                var fact = session.CurrentFact;
                Assert.Contains(fact.Operation, (IReadOnlyList<ArithmeticOperation>)[ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication]);

                session.SubmitAnswer(fact.CorrectResult);
                var commitRes = await session.CommitCurrentEvaluationAsync();
                Assert.True(commitRes.IsSuccess);
                seenPositions.Add(session.Progression.PracticePosition);

                if (session.Progression.CurriculumStage == CurriculumStage.Stage4_Division)
                {
                    break;
                }

                if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
                {
                    session.ContinuePractice(startTiming: false);
                }
            }

            // C4. No arbitrary question quota
            // C5. No pace calibration required for unlock
            Assert.Equal(CurriculumStage.Stage4_Division, session.Progression.CurriculumStage);

            // C6. All four operations unlocked
            var unlockedOps = CurriculumUnlockPolicy.GetUnlockedOperations(session.Progression.CurriculumStage);
            Assert.Equal(4, unlockedOps.Count);

            // C7 & C8. PracticePosition remains contiguous and without duplicates
            for (var i = 0; i < seenPositions.Count; i++)
            {
                Assert.Equal(i + 1, seenPositions[i]);
            }
            Assert.Equal(seenPositions.Distinct().Count(), seenPositions.Count);
        }

        // C10. Restart retains Stage 4
        using (var reopened = new SqliteLearnerStore(path))
        {
            var reopenedSession = new TrainingSession(reopened, clock, practiceMode: PracticeMode.CurriculumManaged);
            await reopenedSession.InitializeAsync(startTiming: false);
            Assert.Equal(CurriculumStage.Stage4_Division, reopenedSession.Progression.CurriculumStage);
        }
    }
}
