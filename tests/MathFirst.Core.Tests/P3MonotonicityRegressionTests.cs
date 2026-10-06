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

public sealed class P3MonotonicityRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstP3Monotonicity_" + Guid.NewGuid().ToString("N"));

    public P3MonotonicityRegressionTests() => Directory.CreateDirectory(_directory);

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

    private string GetDatabasePath(string name = "monotonicity") =>
        Path.Combine(_directory, $"{name}_{Guid.NewGuid():N}.db");

    // =========================================================================
    // M1: STAGE 2 NEVER RELOCKS
    // =========================================================================

    [Fact]
    public async Task M1_Stage2NeverRelocks_WhenSecondWeaknessOccursLater()
    {
        var path = GetDatabasePath("m1");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage2_Subtraction;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        prog.OperationProgressions[ArithmeticOperation.Subtraction] = new OperationProgression(ArithmeticOperation.Subtraction, 0, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        // Seed 2 weak ADD facts (0+0, 0+1)
        session.ItemStates["add:0+0"] = ItemLearningState.CreateNew(new ArithmeticFact(ArithmeticOperation.Addition, 0, 0));
        session.ItemStates["add:0+0"].NeedsRemediation = true;
        session.ItemStates["add:0+0"].TotalAttempts = 1;

        session.ItemStates["add:0+1"] = ItemLearningState.CreateNew(new ArithmeticFact(ArithmeticOperation.Addition, 0, 1));
        session.ItemStates["add:0+1"].NeedsRemediation = true;
        session.ItemStates["add:0+1"].TotalAttempts = 1;

        Assert.True(session.HasBroadWeakness);

        // Submit answer on current fact
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.NotNull(eval.ChangeSet);

        // Stage 2 remains Stage 2, Stage 3 is blocked
        Assert.Equal(CurriculumStage.Stage2_Subtraction, eval.ChangeSet.UpdatedProgression.CurriculumStage);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
    }

    // =========================================================================
    // M2: CROSS-OPERATION BROAD WEAKNESS
    // =========================================================================

    [Fact]
    public async Task M2_CrossOperationBroadWeakness_BlocksStage3UntilOneWeaknessCleared()
    {
        var path = GetDatabasePath("m2");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage2_Subtraction;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        prog.OperationProgressions[ArithmeticOperation.Subtraction] = new OperationProgression(ArithmeticOperation.Subtraction, 0, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        // Introduce all SUB-D01 prerequisite facts
        var subPrereqs = CurriculumUnlockPolicy.GetPrerequisiteFactIds(CurriculumStage.Stage2_Subtraction);
        foreach (var id in subPrereqs)
        {
            var parts = id.Split([':', '-']);
            var item = ItemLearningState.CreateNew(new ArithmeticFact(ArithmeticOperation.Subtraction, int.Parse(parts[1]), int.Parse(parts[2])));
            item.TotalAttempts = 1;
            item.CorrectAttempts = 1;
            item.NeedsRemediation = false;
            session.ItemStates[id] = item;
        }

        // 1 ADD weakness (add:0+0) + 1 SUB weakness (sub:0-0)
        session.ItemStates["add:0+0"] = ItemLearningState.CreateNew(new ArithmeticFact(ArithmeticOperation.Addition, 0, 0));
        session.ItemStates["add:0+0"].NeedsRemediation = true;
        session.ItemStates["add:0+0"].TotalAttempts = 1;

        session.ItemStates["sub:0-0"].NeedsRemediation = true;

        Assert.True(session.HasBroadWeakness);

        // Stage 3 is blocked, Stage 2 is retained
        var evalBlocked = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, evalBlocked.ChangeSet!.UpdatedProgression.CurriculumStage);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);

        // Clear the ADD weakness
        session.ItemStates["add:0+0"].NeedsRemediation = false;
        Assert.False(session.HasBroadWeakness);

        if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
        {
            session.ContinuePractice(startTiming: false);
        }

        // Now Stage 3 unlocks because SUB prerequisite has <= 1 weak fact and broad weakness is false
        var evalUnlocked = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.Equal(CurriculumStage.Stage3_Multiplication, evalUnlocked.ChangeSet!.UpdatedProgression.CurriculumStage);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);
    }

    // =========================================================================
    // M3: STAGE 3 NEVER RELOCKS
    // =========================================================================

    [Fact]
    public async Task M3_Stage3NeverRelocks_WhenSystemicWeaknessOccursAcrossActiveOperations()
    {
        var path = GetDatabasePath("m3");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        prog.OperationProgressions[ArithmeticOperation.Subtraction] = new OperationProgression(ArithmeticOperation.Subtraction, 0, 0);
        prog.OperationProgressions[ArithmeticOperation.Multiplication] = new OperationProgression(ArithmeticOperation.Multiplication, 0, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        // Create weaknesses across ADD and SUB
        session.ItemStates["add:0+0"] = ItemLearningState.CreateNew(new ArithmeticFact(ArithmeticOperation.Addition, 0, 0));
        session.ItemStates["add:0+0"].NeedsRemediation = true;
        session.ItemStates["add:0+0"].TotalAttempts = 1;

        session.ItemStates["sub:0-0"] = ItemLearningState.CreateNew(new ArithmeticFact(ArithmeticOperation.Subtraction, 0, 0));
        session.ItemStates["sub:0-0"].NeedsRemediation = true;
        session.ItemStates["sub:0-0"].TotalAttempts = 1;

        Assert.True(session.HasBroadWeakness);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.Equal(CurriculumStage.Stage3_Multiplication, eval.ChangeSet!.UpdatedProgression.CurriculumStage);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);
    }

    // =========================================================================
    // M4: STAGE 4 NEVER RELOCKS
    // =========================================================================

    [Fact]
    public async Task M4_Stage4NeverRelocks_WhenMultipleRemediationsOccurAcrossAllOperations()
    {
        var path = GetDatabasePath("m4");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage4_Division;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        prog.OperationProgressions[ArithmeticOperation.Subtraction] = new OperationProgression(ArithmeticOperation.Subtraction, 0, 0);
        prog.OperationProgressions[ArithmeticOperation.Multiplication] = new OperationProgression(ArithmeticOperation.Multiplication, 0, 0);
        prog.OperationProgressions[ArithmeticOperation.Division] = new OperationProgression(ArithmeticOperation.Division, 0, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        // Add 4 weak facts across all 4 operations
        var weakOps = new[] { ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication, ArithmeticOperation.Division };
        foreach (var op in weakOps)
        {
            var fact = new ArithmeticFact(op, 0, op == ArithmeticOperation.Division ? 1 : 0);
            var item = ItemLearningState.CreateNew(fact);
            item.NeedsRemediation = true;
            item.TotalAttempts = 1;
            session.ItemStates[fact.Id] = item;
        }

        Assert.True(session.HasBroadWeakness);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.Equal(CurriculumStage.Stage4_Division, eval.ChangeSet!.UpdatedProgression.CurriculumStage);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(CurriculumStage.Stage4_Division, session.Progression.CurriculumStage);
    }

    // =========================================================================
    // M5: RESTART MONOTONICITY
    // =========================================================================

    [Theory]
    [InlineData(CurriculumStage.Stage2_Subtraction)]
    [InlineData(CurriculumStage.Stage3_Multiplication)]
    [InlineData(CurriculumStage.Stage4_Division)]
    public async Task M5_RestartMonotonicity_PreservesEarnedStageEvenWithLaterWeaknesses(CurriculumStage earnedStage)
    {
        var path = GetDatabasePath($"m5_{earnedStage}");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var prog = LearnerProgression.CreateFresh();
            prog.CurriculumStage = earnedStage;
            await store.SaveProgressionAsync(prog);

            var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
            await session.InitializeAsync(startTiming: false);

            // Inject heavy weaknesses that would violate unlock criteria
            for (var i = 0; i < 4; i++)
            {
                var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, i);
                var item = ItemLearningState.CreateNew(fact);
                item.NeedsRemediation = true;
                item.TotalAttempts = 2;
                session.ItemStates[fact.Id] = item;
            }

            Assert.True(session.HasBroadWeakness);
            Assert.Equal(earnedStage, session.Progression.CurriculumStage);
        }

        // Reopen store and session from disk
        using (var reopenedStore = new SqliteLearnerStore(path))
        {
            var reopenedSession = new TrainingSession(reopenedStore, practiceMode: PracticeMode.CurriculumManaged);
            await reopenedSession.InitializeAsync(startTiming: false);

            Assert.Equal(earnedStage, reopenedSession.Progression.CurriculumStage);
        }
    }

    // =========================================================================
    // M6: PERSISTENCE FAILURE AT UNLOCK BOUNDARY
    // =========================================================================

    [Fact]
    public async Task M6_PersistenceFailureAtUnlockBoundary_PreservesLiveStageAndDurablePositionWithoutPhantomUnlock()
    {
        var path = GetDatabasePath("m6");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage1_Addition;
        prog.PracticePosition = 5;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.NotNull(eval.ChangeSet);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, eval.ChangeSet.UpdatedProgression.CurriculumStage);

        // Before commit, live session must still be Stage 1 and PracticePosition 5
        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
        Assert.Equal(5, session.Progression.PracticePosition);

        // Inject store revision mismatch to cause revision conflict on commit
        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE schema_info SET value = '99' WHERE key = 'store_revision';";
            await cmd.ExecuteNonQueryAsync();
        }

        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.False(commitResult.IsSuccess);

        // After failed commit, live session MUST NOT advance stage or practice position
        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
        Assert.Equal(5, session.Progression.PracticePosition);

        // Verify durable DB state
        await using (var verifyConn = new SqliteConnection($"Data Source={path}"))
        {
            await verifyConn.OpenAsync();
            using var queryCmd = verifyConn.CreateCommand();
            queryCmd.CommandText = "SELECT curriculum_stage, practice_position FROM learner_progression WHERE id = 1;";
            using var reader = await queryCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0)); // Still Stage 1
            Assert.Equal(5, reader.GetInt64(1)); // Still Position 5
        }
    }

    // =========================================================================
    // M7: IMMEDIATE RESTART AFTER UNLOCK
    // =========================================================================

    [Fact]
    public async Task M7_ImmediateRestartAfterUnlock_NewStageAndOperationSurviveBeforeAnyNewOperationAttempt()
    {
        var path = GetDatabasePath("m7");

        // Unlock Stage 2 and commit
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();

            var prog = LearnerProgression.CreateFresh();
            prog.CurriculumStage = CurriculumStage.Stage1_Addition;
            prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
            await store.SaveProgressionAsync(prog);

            var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
            await session.InitializeAsync(startTiming: false);

            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            var commitResult = await session.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess);
            Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
            // Immediately dispose WITHOUT submitting any subtraction attempt
        }

        // Restart store and session
        using (var restartedStore = new SqliteLearnerStore(path))
        {
            var restartedSession = new TrainingSession(restartedStore, practiceMode: PracticeMode.CurriculumManaged);
            await restartedSession.InitializeAsync(startTiming: false);

            // Stage 2 remains unlocked
            Assert.Equal(CurriculumStage.Stage2_Subtraction, restartedSession.Progression.CurriculumStage);

            // Subtraction is in the unlocked operations
            var unlockedOps = CurriculumUnlockPolicy.GetUnlockedOperations(restartedSession.Progression.CurriculumStage);
            Assert.Contains(ArithmeticOperation.Subtraction, unlockedOps);
            Assert.Contains(ArithmeticOperation.Addition, unlockedOps);
        }
    }
}
