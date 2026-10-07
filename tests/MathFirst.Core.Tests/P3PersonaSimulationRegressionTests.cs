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

            // A4. Resolve one of the two weaknesses (resolve add:0+0), leaving exactly one isolated weak ADD-D01 fact (add:1+1)
            session.ItemStates["add:0+0"].NeedsRemediation = false;
            session.ItemStates["add:0+0"].CorrectAttempts++;

            // With only 1 weak fact left, HasBroadWeakness drops to false and Stage2 may unlock upon next accepted turn
            Assert.False(session.HasBroadWeakness);
            Assert.Equal(1, CurriculumUnlockPolicy.CountPrerequisiteWeakFacts(CurriculumStage.Stage1_Addition, session.ItemStates));

            var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.NotNull(eval.ChangeSet);
            Assert.Equal(CurriculumStage.Stage2_Subtraction, eval.ChangeSet.UpdatedProgression.CurriculumStage);

            await session.CommitCurrentEvaluationAsync();
            Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);

            if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
            {
                session.ContinuePractice(startTiming: false);
            }

            // A5. After Stage2 is earned, create a second eligible ADD weakness again:
            session.ItemStates["add:0+1"].NeedsRemediation = true;
            Assert.True(session.HasBroadWeakness);

            // A6. Stage2 remains retained (never relocks to Stage 1)
            Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);

            // A7. Even if SUB prerequisite (sub:0-0, sub:1-0, sub:1-1) becomes introduced with clean answers while broad weakness is active:
            var subPrereqs = CurriculumUnlockPolicy.GetPrerequisiteFactIds(CurriculumStage.Stage2_Subtraction);
            foreach (var subFactId in subPrereqs)
            {
                var parts = subFactId.Split([':', '-']);
                var l = int.Parse(parts[1]);
                var r = int.Parse(parts[2]);
                var item = ItemLearningState.CreateNew(new ArithmeticFact(ArithmeticOperation.Subtraction, l, r));
                item.TotalAttempts = 1;
                item.CorrectAttempts = 1;
                item.NeedsRemediation = false;
                session.ItemStates[subFactId] = item;
            }

            // Prerequisite is fully introduced, but broad weakness is active -> Stage3 remains blocked
            Assert.True(CurriculumUnlockPolicy.IsPrerequisiteFullyIntroduced(CurriculumStage.Stage2_Subtraction, session.ItemStates));
            Assert.True(session.HasBroadWeakness);

            var nextStageBlocked = CurriculumUnlockPolicy.EvaluateNextStage(
                CurriculumStage.Stage2_Subtraction,
                session.ItemStates,
                hasBroadWeakness: session.HasBroadWeakness);
            Assert.Equal(CurriculumStage.Stage2_Subtraction, nextStageBlocked);

            // A8. Resolve enough weakness to fall below broad threshold (resolve add:0+1)
            session.ItemStates["add:0+1"].NeedsRemediation = false;
            Assert.False(session.HasBroadWeakness);

            // A9. Once SUB prerequisite is ready and broad weakness is resolved: Stage3 unlocks
            var evalStage3 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.NotNull(evalStage3.ChangeSet);
            Assert.Equal(CurriculumStage.Stage3_Multiplication, evalStage3.ChangeSet.UpdatedProgression.CurriculumStage);
            await session.CommitCurrentEvaluationAsync();
            Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);

            if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
            {
                session.ContinuePractice(startTiming: false);
            }

            // A10. Systemic weakness block before Stage4: 2 weak facts across active ops (1 ADD + 1 SUB)
            session.ItemStates["sub:1-0"].NeedsRemediation = true;
            // add:1+1 is already weak, so total active weak facts = 2 -> HasBroadWeakness = true
            Assert.True(session.HasBroadWeakness);

            // Introduce MUL prerequisite (mul:0*0, mul:0*1, mul:1*0, mul:1*1)
            var mulPrereqs = CurriculumUnlockPolicy.GetPrerequisiteFactIds(CurriculumStage.Stage3_Multiplication);
            foreach (var mulFactId in mulPrereqs)
            {
                var parts = mulFactId.Split([':', '*']);
                var l = int.Parse(parts[1]);
                var r = int.Parse(parts[2]);
                var item = ItemLearningState.CreateNew(new ArithmeticFact(ArithmeticOperation.Multiplication, l, r));
                item.TotalAttempts = 1;
                item.CorrectAttempts = 1;
                item.NeedsRemediation = false;
                session.ItemStates[mulFactId] = item;
            }

            // Stage4 blocked by broad weakness
            Assert.True(session.HasBroadWeakness);
            var nextStage4Blocked = CurriculumUnlockPolicy.EvaluateNextStage(
                CurriculumStage.Stage3_Multiplication,
                session.ItemStates,
                hasBroadWeakness: session.HasBroadWeakness);
            Assert.Equal(CurriculumStage.Stage3_Multiplication, nextStage4Blocked);

            // A11. Resolve weakness (resolve sub:1-0) -> HasBroadWeakness false -> Stage4 unlocks
            session.ItemStates["sub:1-0"].NeedsRemediation = false;
            Assert.False(session.HasBroadWeakness);

            var evalStage4 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.NotNull(evalStage4.ChangeSet);
            Assert.Equal(CurriculumStage.Stage4_Division, evalStage4.ChangeSet.UpdatedProgression.CurriculumStage);
            await session.CommitCurrentEvaluationAsync();
            Assert.Equal(CurriculumStage.Stage4_Division, session.Progression.CurriculumStage);

            // A12. No timeout/latency-only rule prevents progress
            Assert.Equal(CurriculumStage.Stage4_Division, session.Progression.CurriculumStage);
        }

        // Verify durability of Stage4 on cold reload
        using (var reopened = new SqliteLearnerStore(path))
        {
            var reopenedSession = new TrainingSession(reopened, clock, practiceMode: PracticeMode.CurriculumManaged);
            await reopenedSession.InitializeAsync(startTiming: false);
            Assert.Equal(CurriculumStage.Stage4_Division, reopenedSession.Progression.CurriculumStage);
        }
    }

    // =========================================================================
    // PERSONA B — PERSISTENT ISOLATED WEAK FACT (NO DEADLOCK REGRESSION)
    // =========================================================================

    [Fact]
    public async Task PersonaB_PersistentIsolatedWeakFact_ProgressesToStage4WithoutDeadlockOrBandMutation()
    {
        var path = GetDatabasePath("persona_b");
        var clock = new ScriptedLatencyClock { Latency = TimeSpan.FromMilliseconds(1200) };

        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();

            // Seed 4 ADD-D01 facts in database: add:0+0, add:0+1, add:1+0 are clean, add:1+1 is persistently weak
            var addItems = new List<ItemLearningState>
            {
                new() { FactId = "add:0+0", Operation = ArithmeticOperation.Addition, LeftOperand = 0, RightOperand = 0, TotalAttempts = 5, CorrectAttempts = 5, NeedsRemediation = false },
                new() { FactId = "add:0+1", Operation = ArithmeticOperation.Addition, LeftOperand = 0, RightOperand = 1, TotalAttempts = 5, CorrectAttempts = 5, NeedsRemediation = false },
                new() { FactId = "add:1+0", Operation = ArithmeticOperation.Addition, LeftOperand = 1, RightOperand = 0, TotalAttempts = 5, CorrectAttempts = 5, NeedsRemediation = false },
                new() { FactId = "add:1+1", Operation = ArithmeticOperation.Addition, LeftOperand = 1, RightOperand = 1, TotalAttempts = 5, CorrectAttempts = 0, IncorrectAttempts = 5, NeedsRemediation = true }
            };
            foreach (var item in addItems)
            {
                await SeedItemStateAsync(path, item);
            }

            var session = new TrainingSession(store, clock, practiceMode: PracticeMode.CurriculumManaged);
            await session.InitializeAsync(startTiming: false);

            // B1. ADD-D01 complete with 1 weak fact (add:1+1)
            Assert.True(session.ItemStates["add:1+1"].NeedsRemediation);
            Assert.Equal(1, CurriculumUnlockPolicy.CountPrerequisiteWeakFacts(CurriculumStage.Stage1_Addition, session.ItemStates));
            Assert.False(session.HasBroadWeakness);

            // B2. Stage2 unlocks
            Assert.True(CurriculumUnlockPolicy.CanAdvance(CurriculumStage.Stage1_Addition, session.ItemStates, session.HasBroadWeakness));

            // Answer current fact: if add:1+1, answer wrong; otherwise answer right
            await SubmitPersonaBTurnAsync(session);
            Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);

            // B3. ADD BandIndex remains 0 (strict Dense progression requires all latest correctness; not mutated)
            Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);

            // B4. Learner is NOT trapped at Stage 1
            Assert.NotEqual(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);

            // B5. At Stage 2, SUB prerequisite becomes clean and only the one ADD weakness remains -> Stage3 unlocks
            var subPrereqs = CurriculumUnlockPolicy.GetPrerequisiteFactIds(CurriculumStage.Stage2_Subtraction);
            foreach (var factId in subPrereqs)
            {
                var parts = factId.Split([':', '-']);
                var l = int.Parse(parts[1]);
                var r = int.Parse(parts[2]);
                var item = new ItemLearningState
                {
                    FactId = factId,
                    Operation = ArithmeticOperation.Subtraction,
                    LeftOperand = l,
                    RightOperand = r,
                    TotalAttempts = 3,
                    CorrectAttempts = 3,
                    NeedsRemediation = false
                };
                await SeedItemStateAsync(path, item);
                session.ItemStates[factId] = item;
            }

            Assert.False(session.HasBroadWeakness);
            Assert.Equal(0, CurriculumUnlockPolicy.CountPrerequisiteWeakFacts(CurriculumStage.Stage2_Subtraction, session.ItemStates));

            await SubmitPersonaBTurnAsync(session);
            Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);

            // B6. At Stage 3, MUL prerequisite becomes clean and only the one ADD weakness remains -> Stage4 unlocks
            var mulPrereqs = CurriculumUnlockPolicy.GetPrerequisiteFactIds(CurriculumStage.Stage3_Multiplication);
            foreach (var factId in mulPrereqs)
            {
                var parts = factId.Split([':', '*']);
                var l = int.Parse(parts[1]);
                var r = int.Parse(parts[2]);
                var item = new ItemLearningState
                {
                    FactId = factId,
                    Operation = ArithmeticOperation.Multiplication,
                    LeftOperand = l,
                    RightOperand = r,
                    TotalAttempts = 3,
                    CorrectAttempts = 3,
                    NeedsRemediation = false
                };
                await SeedItemStateAsync(path, item);
                session.ItemStates[factId] = item;
            }

            Assert.False(session.HasBroadWeakness);
            Assert.Equal(0, CurriculumUnlockPolicy.CountPrerequisiteWeakFacts(CurriculumStage.Stage3_Multiplication, session.ItemStates));

            await SubmitPersonaBTurnAsync(session);
            Assert.Equal(CurriculumStage.Stage4_Division, session.Progression.CurriculumStage);

            // B7-B10. Persist/restart, Stage4 remains Stage4, persistent ADD fact remains NeedsRemediation, BandIndex intact
            Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
            Assert.True(session.ItemStates["add:1+1"].NeedsRemediation);
        }

        // B7. Reopen
        using (var reopened = new SqliteLearnerStore(path))
        {
            var reopenedSession = new TrainingSession(reopened, clock, practiceMode: PracticeMode.CurriculumManaged);
            await reopenedSession.InitializeAsync(startTiming: false);

            // B8. Stage4 remains Stage4
            Assert.Equal(CurriculumStage.Stage4_Division, reopenedSession.Progression.CurriculumStage);

            // B9. The persistent ADD fact remains NeedsRemediation / historical evidence preserved
            Assert.True(reopenedSession.ItemStates["add:1+1"].NeedsRemediation);
            Assert.Equal(5, reopenedSession.ItemStates["add:1+1"].IncorrectAttempts);

            // B10. BandIndex not mutated
            Assert.Equal(0, reopenedSession.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
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
