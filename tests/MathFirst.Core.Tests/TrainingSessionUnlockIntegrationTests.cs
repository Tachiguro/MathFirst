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

public sealed class TrainingSessionUnlockIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstUnlockIntegration_" + Guid.NewGuid().ToString("N"));

    public TrainingSessionUnlockIntegrationTests() => Directory.CreateDirectory(_directory);

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

    private string GetDatabasePath(string name = "test") =>
        Path.Combine(_directory, $"{name}_{Guid.NewGuid():N}.db");

    private sealed class StubPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<string, bool> _boolPrefs = new();
        public ThemePreference Theme { get; set; } = ThemePreference.System;
        public NumericKeypadLayout KeypadLayout { get; set; } = NumericKeypadLayout.Numpad;
        public string Language { get; set; } = "system";
        public bool HapticFeedbackEnabled { get; set; } = true;
        public PracticeTimeSetting TimeSetting { get; set; } = PracticeTimeSetting.Standard;

        public ThemePreference GetThemePreference() => Theme;
        public void SetThemePreference(ThemePreference preference) => Theme = preference;
        public NumericKeypadLayout GetNumericKeypadLayout() => KeypadLayout;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) => KeypadLayout = layout;
        public string GetLanguagePreference() => Language;
        public void SetLanguagePreference(string languageCode) => Language = languageCode;
        public bool GetHapticFeedbackEnabled() => HapticFeedbackEnabled;
        public void SetHapticFeedbackEnabled(bool enabled) => HapticFeedbackEnabled = enabled;

        public bool GetOperationEnabled(ArithmeticOperation operation) =>
            _boolPrefs.GetValueOrDefault($"op_{operation}", true);

        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) =>
            _boolPrefs[$"op_{operation}"] = enabled;

        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations()
        {
            var list = new List<ArithmeticOperation>();
            foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
            {
                if (GetOperationEnabled(op))
                {
                    list.Add(op);
                }
            }
            return PracticeOperationPreferencePolicy.NormalizeEnabledOperations(list);
        }

        public void SetEnabledOperations(IReadOnlyList<ArithmeticOperation> operations)
        {
            foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
            {
                SetOperationEnabled(op, operations.Contains(op));
            }
        }

        public PracticeTimeSetting GetPracticeTimeSetting() => TimeSetting;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) => TimeSetting = setting;

        public void ResetPracticePreferences() => _boolPrefs.Clear();
        public void ResetAllPreferences() => _boolPrefs.Clear();
    }

    // =========================================================================
    // R01 - R05: ACTIVE OPERATIONS PER CURRICULUM STAGE CONTRACTS
    // =========================================================================

    [Fact]
    public async Task R01_CurriculumManaged_FreshSession_SchedulesAdditionOnly()
    {
        var path = GetDatabasePath("r01");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
    }

    [Fact]
    public async Task R02_CurriculumManaged_IgnoresPreferences_EvenIfPreferencesEnableAllFour()
    {
        var path = GetDatabasePath("r02");
        using var store = new SqliteLearnerStore(path);
        var prefStore = new StubPreferenceStore();
        prefStore.SetEnabledOperations([ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication, ArithmeticOperation.Division]);

        var session = new TrainingSession(store, preferenceStore: prefStore, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
    }

    [Fact]
    public async Task R03_CurriculumManaged_Stage2_ActiveOperations_AdditionAndSubtraction()
    {
        var path = GetDatabasePath("r03");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage2_Subtraction;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
        var seenOps = new HashSet<ArithmeticOperation>();
        for (var i = 0; i < 20; i++)
        {
            seenOps.Add(session.CurrentFact.Operation);
            var unlockedOps = CurriculumUnlockPolicy.GetUnlockedOperations(session.Progression.CurriculumStage);
            Assert.Contains(session.CurrentFact.Operation, unlockedOps);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync();
        }

        Assert.Contains(ArithmeticOperation.Addition, seenOps);
        Assert.Contains(ArithmeticOperation.Subtraction, seenOps);
    }

    [Fact]
    public async Task R04_CurriculumManaged_Stage3_ActiveOperations_AdditionSubtractionMultiplication()
    {
        var path = GetDatabasePath("r04");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);
        var seenOps = new HashSet<ArithmeticOperation>();
        for (var i = 0; i < 30; i++)
        {
            seenOps.Add(session.CurrentFact.Operation);
            var unlockedOps = CurriculumUnlockPolicy.GetUnlockedOperations(session.Progression.CurriculumStage);
            Assert.Contains(session.CurrentFact.Operation, unlockedOps);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync();
        }

        Assert.Contains(ArithmeticOperation.Addition, seenOps);
        Assert.Contains(ArithmeticOperation.Subtraction, seenOps);
        Assert.Contains(ArithmeticOperation.Multiplication, seenOps);
    }

    [Fact]
    public async Task R05_CurriculumManaged_Stage4_ActiveOperations_AllFour()
    {
        var path = GetDatabasePath("r05");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage4_Division;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        Assert.Equal(CurriculumStage.Stage4_Division, session.Progression.CurriculumStage);
        var seenOps = new HashSet<ArithmeticOperation>();
        for (var i = 0; i < 40; i++)
        {
            seenOps.Add(session.CurrentFact.Operation);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync();
        }

        Assert.Contains(ArithmeticOperation.Addition, seenOps);
        Assert.Contains(ArithmeticOperation.Subtraction, seenOps);
        Assert.Contains(ArithmeticOperation.Multiplication, seenOps);
        Assert.Contains(ArithmeticOperation.Division, seenOps);
    }

    // =========================================================================
    // R06 - R08: ATOMIC STAGE ADVANCEMENT TRANSITION CONTRACTS
    // =========================================================================

    [Fact]
    public async Task R06_AtomicUnlock_AdditionD01Completed_CandidateStage1ToStage2()
    {
        var path = GetDatabasePath("r06");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var items = new List<ItemLearningState>
        {
            new() { FactId = "add:0+0", Operation = ArithmeticOperation.Addition, LeftOperand = 0, RightOperand = 0, TotalAttempts = 1, CorrectAttempts = 1, NeedsRemediation = false },
            new() { FactId = "add:0+1", Operation = ArithmeticOperation.Addition, LeftOperand = 0, RightOperand = 1, TotalAttempts = 1, CorrectAttempts = 1, NeedsRemediation = false },
            new() { FactId = "add:1+0", Operation = ArithmeticOperation.Addition, LeftOperand = 1, RightOperand = 0, TotalAttempts = 1, CorrectAttempts = 1, NeedsRemediation = false },
        };
        foreach (var item in items)
        {
            await SeedItemStateAsync(path, item);
        }

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);

        for (var i = 0; i < 20 && session.Progression.CurriculumStage == CurriculumStage.Stage1_Addition; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            if (session.Progression.CurriculumStage == CurriculumStage.Stage2_Subtraction)
            {
                break;
            }
            await session.AdvanceToNextFactAsync();
        }

        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
    }

    [Fact]
    public async Task R07_AtomicUnlock_SubtractionD01Completed_Stage2ToStage3()
    {
        var path = GetDatabasePath("r07");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage2_Subtraction;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        prog.OperationProgressions[ArithmeticOperation.Subtraction] = new OperationProgression(ArithmeticOperation.Subtraction, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.NotNull(eval.ChangeSet);
        Assert.Equal(CurriculumStage.Stage3_Multiplication, eval.ChangeSet.UpdatedProgression.CurriculumStage);

        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);
    }

    [Fact]
    public async Task R08_AtomicUnlock_MultiplicationD01Completed_Stage3ToStage4()
    {
        var path = GetDatabasePath("r08");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        prog.OperationProgressions[ArithmeticOperation.Subtraction] = new OperationProgression(ArithmeticOperation.Subtraction, 1, 0);
        prog.OperationProgressions[ArithmeticOperation.Multiplication] = new OperationProgression(ArithmeticOperation.Multiplication, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.NotNull(eval.ChangeSet);
        Assert.Equal(CurriculumStage.Stage4_Division, eval.ChangeSet.UpdatedProgression.CurriculumStage);

        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(CurriculumStage.Stage4_Division, session.Progression.CurriculumStage);
    }

    // =========================================================================
    // R09 - R14: BROAD WEAKNESS & MONOTONICITY CONTRACTS
    // =========================================================================

    [Fact]
    public async Task R09_AtomicUnlock_OneIsolatedPrerequisiteWeakFact_AllowsUnlockIfBroadWeaknessFalse()
    {
        var path = GetDatabasePath("r09");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var items = new List<ItemLearningState>
        {
            new() { FactId = "add:0+0", Operation = ArithmeticOperation.Addition, LeftOperand = 0, RightOperand = 0, TotalAttempts = 1, CorrectAttempts = 1, NeedsRemediation = false },
            new() { FactId = "add:0+1", Operation = ArithmeticOperation.Addition, LeftOperand = 0, RightOperand = 1, TotalAttempts = 1, CorrectAttempts = 1, NeedsRemediation = false },
            new() { FactId = "add:1+0", Operation = ArithmeticOperation.Addition, LeftOperand = 1, RightOperand = 0, TotalAttempts = 1, CorrectAttempts = 1, NeedsRemediation = false },
            new() { FactId = "add:1+1", Operation = ArithmeticOperation.Addition, LeftOperand = 1, RightOperand = 1, TotalAttempts = 1, CorrectAttempts = 0, NeedsRemediation = true },
        };
        foreach (var item in items)
        {
            await SeedItemStateAsync(path, item);
        }

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        Assert.False(session.HasBroadWeakness);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.NotNull(eval.ChangeSet);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, eval.ChangeSet.UpdatedProgression.CurriculumStage);
    }

    [Fact]
    public async Task R10_AtomicUnlock_TwoAggregateEligibleRemediationFacts_BlocksUnlock()
    {
        var path = GetDatabasePath("r10");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var items = new List<ItemLearningState>
        {
            new() { FactId = "add:0+0", Operation = ArithmeticOperation.Addition, LeftOperand = 0, RightOperand = 0, TotalAttempts = 1, CorrectAttempts = 1, NeedsRemediation = false },
            new() { FactId = "add:0+1", Operation = ArithmeticOperation.Addition, LeftOperand = 0, RightOperand = 1, TotalAttempts = 1, CorrectAttempts = 0, NeedsRemediation = true },
            new() { FactId = "add:1+0", Operation = ArithmeticOperation.Addition, LeftOperand = 1, RightOperand = 0, TotalAttempts = 1, CorrectAttempts = 0, NeedsRemediation = true },
            new() { FactId = "add:1+1", Operation = ArithmeticOperation.Addition, LeftOperand = 1, RightOperand = 1, TotalAttempts = 1, CorrectAttempts = 0, NeedsRemediation = true },
        };
        foreach (var item in items)
        {
            await SeedItemStateAsync(path, item);
        }

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        Assert.True(session.HasBroadWeakness);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.NotNull(eval.ChangeSet);
        Assert.Equal(CurriculumStage.Stage1_Addition, eval.ChangeSet.UpdatedProgression.CurriculumStage);
    }

    [Fact]
    public async Task R11_CrossOperation_1AddPlus1Sub_BlocksStage3Unlock()
    {
        var path = GetDatabasePath("r11");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage2_Subtraction;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        prog.OperationProgressions[ArithmeticOperation.Subtraction] = new OperationProgression(ArithmeticOperation.Subtraction, 0, 0);
        await store.SaveProgressionAsync(prog);

        var items = new List<ItemLearningState>
        {
            new() { FactId = "add:0+0", Operation = ArithmeticOperation.Addition, LeftOperand = 0, RightOperand = 0, TotalAttempts = 1, NeedsRemediation = true },
            new() { FactId = "sub:0-0", Operation = ArithmeticOperation.Subtraction, LeftOperand = 0, RightOperand = 0, TotalAttempts = 1, NeedsRemediation = true },
            new() { FactId = "sub:1-0", Operation = ArithmeticOperation.Subtraction, LeftOperand = 1, RightOperand = 0, TotalAttempts = 1, NeedsRemediation = true }
        };
        foreach (var item in items)
        {
            await SeedItemStateAsync(path, item);
        }

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        Assert.True(session.HasBroadWeakness);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.NotNull(eval.ChangeSet);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, eval.ChangeSet.UpdatedProgression.CurriculumStage);
    }

    [Fact]
    public async Task R12_MonotonicStage_Stage2RemainsStage2_AfterLaterBroadWeakness()
    {
        var path = GetDatabasePath("r12");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage2_Subtraction;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        for (var i = 0; i < 5; i++)
        {
            session.SubmitAnswer(999);
            await session.CommitCurrentEvaluationAsync();
            session.AcknowledgeFeedback();
        }

        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
    }

    [Fact]
    public async Task R13_MonotonicStage_Stage3RemainsStage3_AfterLaterBroadWeakness()
    {
        var path = GetDatabasePath("r13");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        for (var i = 0; i < 5; i++)
        {
            session.SubmitAnswer(999);
            await session.CommitCurrentEvaluationAsync();
            session.AcknowledgeFeedback();
        }

        Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);
    }

    [Fact]
    public async Task R14_MonotonicStage_Stage4NeverRegresses()
    {
        var path = GetDatabasePath("r14");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage4_Division;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        for (var i = 0; i < 5; i++)
        {
            session.SubmitAnswer(999);
            await session.CommitCurrentEvaluationAsync();
            session.AcknowledgeFeedback();
        }

        Assert.Equal(CurriculumStage.Stage4_Division, session.Progression.CurriculumStage);
    }

    // =========================================================================
    // R15 - R18: ATOMIC COMMIT & DURABILITY CONTRACTS
    // =========================================================================

    [Fact]
    public async Task R15_AtomicCommit_StageTransitionNotPublishedBeforePersistenceSuccess()
    {
        var path = GetDatabasePath("r15");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage1_Addition;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.NotNull(eval.ChangeSet);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, eval.ChangeSet.UpdatedProgression.CurriculumStage);

        // BEFORE Commit, live Progression must still report Stage 1
        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);

        await session.CommitCurrentEvaluationAsync();

        // AFTER Commit, live Progression reports Stage 2
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
    }

    [Fact]
    public async Task R16_AtomicCommit_PersistenceFailure_LiveStageUnchanged()
    {
        var path = GetDatabasePath("r16");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage1_Addition;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.NotNull(eval.ChangeSet);

        // Mutate store revision behind session's back to cause RevisionConflict
        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE schema_info SET value = '99' WHERE key = 'store_revision';";
            await cmd.ExecuteNonQueryAsync();
        }

        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.False(commitResult.IsSuccess);

        // Live stage must remain Stage 1
        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
    }

    [Fact]
    public async Task R17_AtomicCommit_SuccessfulRetry_AdvancesStageExactlyOnce()
    {
        var path = GetDatabasePath("r17");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage1_Addition;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var res1 = await session.CommitCurrentEvaluationAsync();
        Assert.True(res1.IsSuccess);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);

        var res2 = await session.CommitCurrentEvaluationAsync();
        Assert.True(res2.IsSuccess);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
    }

    [Fact]
    public async Task R18_Durability_ImmediateRestartAfterUnlock_NewStageSurvives()
    {
        var path = GetDatabasePath("r18");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();

            var prog = LearnerProgression.CreateFresh();
            prog.CurriculumStage = CurriculumStage.Stage1_Addition;
            prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
            await store.SaveProgressionAsync(prog);

            var session1 = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
            await session1.InitializeAsync();

            session1.SubmitAnswer(session1.CurrentFact.CorrectResult);
            var res = await session1.CommitCurrentEvaluationAsync();
            Assert.True(res.IsSuccess);
            Assert.Equal(CurriculumStage.Stage2_Subtraction, session1.Progression.CurriculumStage);
        }

        // Restart store and session
        using (var store2 = new SqliteLearnerStore(path))
        {
            var session2 = new TrainingSession(store2, practiceMode: PracticeMode.CurriculumManaged);
            await session2.InitializeAsync();

            Assert.Equal(CurriculumStage.Stage2_Subtraction, session2.Progression.CurriculumStage);
        }
    }

    // =========================================================================
    // R19 - R22: SCHEDULER, POSITION & HISTORICAL DATA CONTRACTS
    // =========================================================================

    [Fact]
    public async Task R19_UnlockTiming_NextQuestionAfterStage1ToStage2_SchedulesExpandedOperationSet()
    {
        var path = GetDatabasePath("r19");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage1_Addition;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        var answeredFact = session.CurrentFact;
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        // Answered fact and feedback remain intact
        Assert.Equal(answeredFact, session.CurrentFact);

        // Next fact uses newly unlocked Stage 2
        await session.AdvanceToNextFactAsync();
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
    }

    [Fact]
    public async Task R20_PracticePosition_ContinuousAcrossUnlock()
    {
        var path = GetDatabasePath("r20");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage1_Addition;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        Assert.Equal(0, session.Progression.PracticePosition);

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        Assert.Equal(1, session.Progression.PracticePosition);

        await session.AdvanceToNextFactAsync();
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        Assert.Equal(2, session.Progression.PracticePosition);
    }

    [Fact]
    public async Task R21_RoleOrdinals_PerOperationAcceptedAttemptCountPlus1()
    {
        var path = GetDatabasePath("r21");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        Assert.Equal(0, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        Assert.Equal(1, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));
    }

    [Fact]
    public async Task R22_LegacyDormantHistory_PreservedAndReusedWhenOperationUnlocked()
    {
        var path = GetDatabasePath("r22");
        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info VALUES ('schema_version', '9');
                INSERT INTO schema_info VALUES ('store_revision', '1');

                CREATE TABLE learner_progression (id INTEGER PRIMARY KEY CHECK (id = 1), practice_position INTEGER NOT NULL DEFAULT 0, curriculum_stage INTEGER NOT NULL DEFAULT 1, updated_at TEXT NOT NULL);
                INSERT INTO learner_progression VALUES (1, 10, 1, '2026-10-06T00:00:00Z');

                CREATE TABLE operation_progression (operation TEXT PRIMARY KEY, band_index INTEGER NOT NULL, band_started_practice_position INTEGER NOT NULL);
                INSERT INTO operation_progression VALUES ('Addition', 1, 0);
                INSERT INTO operation_progression VALUES ('Subtraction', 2, 0);
                INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
                INSERT INTO operation_progression VALUES ('Division', 0, 0);

                CREATE TABLE item_learning_state (fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL, correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL, consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL, rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL, is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL, remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL, last_practiced_at TEXT);
                CREATE TABLE attempt_history (submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, submitted_answer INTEGER, correct_answer INTEGER NOT NULL, is_correct INTEGER NOT NULL, is_fluent INTEGER NOT NULL CHECK (is_fluent IN (0, 1)), outcome TEXT NOT NULL, response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL, practice_position INTEGER, attempt_context_version INTEGER, presented_deadline_ms INTEGER, expected_pace_ms INTEGER, resolved_role TEXT, operation_band_before INTEGER, is_interrupted INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE fsrs_card_state (fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL, step INTEGER, stability REAL, difficulty REAL, due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER, last_rating INTEGER);

                INSERT INTO item_learning_state VALUES ('sub:1-1', 'Subtraction', 1, 1, 5, 5, 0, 5, 900, 900, 5, 1, 0, 0, 5, '2026-10-06T05:00:00Z');

                -- 5 historical subtraction attempts
                INSERT INTO attempt_history VALUES ('sub_h1', 'sub:1-1', 'Subtraction', 1, 1, 0, 0, 1, 1, 'Correct', 900, '2026-10-06T01:00:00Z', 1, 1, null, 1000, 'New', 0, 0);
                INSERT INTO attempt_history VALUES ('sub_h2', 'sub:2-1', 'Subtraction', 2, 1, 1, 1, 1, 1, 'Correct', 900, '2026-10-06T02:00:00Z', 2, 1, null, 1000, 'New', 0, 0);
                INSERT INTO attempt_history VALUES ('sub_h3', 'sub:3-1', 'Subtraction', 3, 1, 2, 2, 1, 1, 'Correct', 900, '2026-10-06T03:00:00Z', 3, 1, null, 1000, 'New', 0, 0);
                INSERT INTO attempt_history VALUES ('sub_h4', 'sub:4-1', 'Subtraction', 4, 1, 3, 3, 1, 1, 'Correct', 900, '2026-10-06T04:00:00Z', 4, 1, null, 1000, 'New', 0, 0);
                INSERT INTO attempt_history VALUES ('sub_h5', 'sub:5-1', 'Subtraction', 5, 1, 4, 4, 1, 1, 'Correct', 900, '2026-10-06T05:00:00Z', 5, 1, null, 1000, 'New', 0, 0);
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        // Historical count for Subtraction is 5, not reset to 0
        Assert.Equal(5, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction));
    }

    // =========================================================================
    // G01 - G08: GUIDED NUMBER SPACE GATE & CUSTOM MODE CONTRACTS
    // =========================================================================

    [Fact]
    public async Task G01_CurriculumManaged_Stage3_ReceivesGuidedGate()
    {
        var path = GetDatabasePath("g01");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0); // Addition ceiling = 2
        prog.OperationProgressions[ArithmeticOperation.Multiplication] = new OperationProgression(ArithmeticOperation.Multiplication, 0, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        var additionCurriculum = new ArithmeticCurriculum().GetCurriculum(ArithmeticOperation.Addition);
        for (var i = 0; i < 20; i++)
        {
            var effectiveGate = GuidedNumberSpaceGate.ForGuided(additionCurriculum, session.Progression.OperationProgressions);
            Assert.True(effectiveGate.Allows(session.CurrentFact), $"Fact {session.CurrentFact.Id} must be allowed by Guided gate.");
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync();
        }
    }

    [Fact]
    public async Task G02_CurriculumManaged_Stage3_MultiplicationBelowBand3_RespectsAdditionCeiling()
    {
        var path = GetDatabasePath("g02");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        prog.OperationProgressions[ArithmeticOperation.Multiplication] = new OperationProgression(ArithmeticOperation.Multiplication, 0, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        var additionCurriculum = new ArithmeticCurriculum().GetCurriculum(ArithmeticOperation.Addition);
        for (var i = 0; i < 20; i++)
        {
            var effectiveGate = GuidedNumberSpaceGate.ForGuided(additionCurriculum, session.Progression.OperationProgressions);
            Assert.True(effectiveGate.Allows(session.CurrentFact), $"Fact {session.CurrentFact.Id} must be allowed by Guided gate.");
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync();
        }
    }

    [Fact]
    public async Task G03_CurriculumManaged_MultiplicationBand3OrAbove_DecoupledFromAdditionCeiling()
    {
        var path = GetDatabasePath("g03");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0); // Addition ceiling = 1
        prog.OperationProgressions[ArithmeticOperation.Multiplication] = new OperationProgression(ArithmeticOperation.Multiplication, 3, 0); // Band 3 (G3 decoupled!)
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        var seenMulOperandsGreaterThan1 = false;
        for (var i = 0; i < 30; i++)
        {
            if (session.CurrentFact.Operation == ArithmeticOperation.Multiplication)
            {
                if (session.CurrentFact.LeftOperand > 1 || session.CurrentFact.RightOperand > 1)
                {
                    seenMulOperandsGreaterThan1 = true;
                }
            }
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync();
        }

        Assert.True(seenMulOperandsGreaterThan1, "Multiplication band 3 should be decoupled from Addition ceiling 1.");
    }

    [Fact]
    public async Task G04_CurriculumManaged_Stage4_DivisionBelowBand3_RespectsAdditionCeiling()
    {
        var path = GetDatabasePath("g04");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage4_Division;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        prog.OperationProgressions[ArithmeticOperation.Division] = new OperationProgression(ArithmeticOperation.Division, 0, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync();

        var additionCurriculum = new ArithmeticCurriculum().GetCurriculum(ArithmeticOperation.Addition);
        for (var i = 0; i < 30; i++)
        {
            var effectiveGate = GuidedNumberSpaceGate.ForGuided(additionCurriculum, session.Progression.OperationProgressions);
            Assert.True(effectiveGate.Allows(session.CurrentFact), $"Fact {session.CurrentFact.Id} must be allowed by Guided gate.");
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync();
        }
    }

    [Fact]
    public async Task G05_CustomMode_AdditionSubtractionMultiplication_RemainsUnrestricted()
    {
        var path = GetDatabasePath("g05");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prefStore = new StubPreferenceStore();
        prefStore.SetEnabledOperations([ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication]);

        var prog = LearnerProgression.CreateFresh();
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0); // Addition ceiling = 1
        prog.OperationProgressions[ArithmeticOperation.Multiplication] = new OperationProgression(ArithmeticOperation.Multiplication, 2, 0); // Band 2
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, preferenceStore: prefStore, practiceMode: PracticeMode.Custom);
        await session.InitializeAsync();

        var seenMulOperandsGreaterThan1 = false;
        for (var i = 0; i < 30; i++)
        {
            if (session.CurrentFact.Operation == ArithmeticOperation.Multiplication)
            {
                if (session.CurrentFact.LeftOperand > 1 || session.CurrentFact.RightOperand > 1)
                {
                    seenMulOperandsGreaterThan1 = true;
                }
            }
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync();
        }

        Assert.True(seenMulOperandsGreaterThan1, "Custom ADD+SUB+MUL mode should remain Unrestricted.");
    }

    [Fact]
    public async Task G06_CustomMode_AllFour_ReceivesGuidedGate()
    {
        var path = GetDatabasePath("g06");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prefStore = new StubPreferenceStore();
        prefStore.SetEnabledOperations([ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication, ArithmeticOperation.Division]);

        var prog = LearnerProgression.CreateFresh();
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        prog.OperationProgressions[ArithmeticOperation.Multiplication] = new OperationProgression(ArithmeticOperation.Multiplication, 0, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, preferenceStore: prefStore, practiceMode: PracticeMode.Custom);
        await session.InitializeAsync();

        var additionCurriculum = new ArithmeticCurriculum().GetCurriculum(ArithmeticOperation.Addition);
        for (var i = 0; i < 20; i++)
        {
            var effectiveGate = GuidedNumberSpaceGate.ForGuided(additionCurriculum, session.Progression.OperationProgressions);
            Assert.True(effectiveGate.Allows(session.CurrentFact), $"Fact {session.CurrentFact.Id} must be allowed by Guided gate in Custom all-four mode.");
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync();
        }
    }

    [Fact]
    public async Task G07_CustomMode_EnabledOperations_ControlledByPreferences()
    {
        var path = GetDatabasePath("g07");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prefStore = new StubPreferenceStore();
        prefStore.SetEnabledOperations([ArithmeticOperation.Multiplication]);

        var session = new TrainingSession(store, preferenceStore: prefStore, practiceMode: PracticeMode.Custom);
        await session.InitializeAsync();

        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
    }

    [Fact]
    public void G08_CustomMode_NullOrEmptyPreferences_FallsBackToAllOperations()
    {
        var normalizedNull = PracticeOperationPreferencePolicy.NormalizeEnabledOperations(null);
        var normalizedEmpty = PracticeOperationPreferencePolicy.NormalizeEnabledOperations([]);

        Assert.Equal(PracticeOperationPreferencePolicy.AllOperations, normalizedNull);
        Assert.Equal(PracticeOperationPreferencePolicy.AllOperations, normalizedEmpty);
    }

    private static async Task SeedItemStateAsync(string dbPath, ItemLearningState item)
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbPath };
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(builder.ToString());
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
}
