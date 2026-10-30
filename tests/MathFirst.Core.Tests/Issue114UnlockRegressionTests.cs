namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>
/// Issue #114 RED/GREEN regressions: operation unlocks require D10 prerequisite mastery (ADR-0013), not the
/// ADR-0012 introductory D01 exposure. These tests intentionally use only the long-standing public
/// <see cref="CurriculumUnlockPolicy.EvaluateNextStage(CurriculumStage, IReadOnlyDictionary{string, ItemLearningState}, bool, IReadOnlyDictionary{ArithmeticOperation, OperationProgression}?)"/>
/// and <see cref="TrainingSession"/> surfaces so they compile and fail against the previous policy.
/// </summary>
public sealed class Issue114UnlockRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue114Red_" + Guid.NewGuid().ToString("N"));

    public Issue114UnlockRegressionTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void R01_IntroductoryD01Only_DoesNotUnlockSubtraction()
    {
        var states = MasteredStates(ArithmeticOperation.Addition, maxLevel: 1);

        var next = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            states,
            hasBroadWeakness: false,
            Progressions(addBand: 1));

        Assert.Equal(CurriculumStage.Stage1_Addition, next);
    }

    [Fact]
    public void R02_AdditionBandPastD10_WithBoundary10Plus10Unresolved_DoesNotUnlockSubtraction()
    {
        var states = MasteredStates(ArithmeticOperation.Addition, maxLevel: 10);
        MarkMissed(states["add:10+10"]);

        var next = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            states,
            hasBroadWeakness: false,
            Progressions(addBand: 10));

        Assert.Equal(CurriculumStage.Stage1_Addition, next);
    }

    [Fact]
    public void R03_RepeatedlyIncorrect4Plus1_BlocksSubtractionEvenAfterD10BandAdvance()
    {
        var states = MasteredStates(ArithmeticOperation.Addition, maxLevel: 10);
        var weak = states["add:4+1"];
        MarkMissed(weak);
        MarkMissed(weak);
        MarkMissed(weak);

        var next = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            states,
            hasBroadWeakness: false,
            Progressions(addBand: 10));

        Assert.Equal(CurriculumStage.Stage1_Addition, next);
    }

    [Fact]
    public void R04_RepeatedlyMissedFact_UnlocksOnlyAfterALaterCorrectRetrievalClearsRemediation()
    {
        var states = MasteredStates(ArithmeticOperation.Addition, maxLevel: 10);
        var weak = states["add:4+1"];
        MarkCorrect(weak);
        MarkMissed(weak);
        MarkMissed(weak);

        Assert.Equal(
            CurriculumStage.Stage1_Addition,
            CurriculumUnlockPolicy.EvaluateNextStage(CurriculumStage.Stage1_Addition, states, false, Progressions(addBand: 10)));

        // A stale remediation flag alone must not count as resolved, even with earlier correct answers.
        weak.ConsecutiveCorrectStreak = 1;
        Assert.Equal(
            CurriculumStage.Stage1_Addition,
            CurriculumUnlockPolicy.EvaluateNextStage(CurriculumStage.Stage1_Addition, states, false, Progressions(addBand: 10)));
        weak.ConsecutiveCorrectStreak = 0;

        MarkCorrect(weak);

        Assert.Equal(
            CurriculumStage.Stage2_Subtraction,
            CurriculumUnlockPolicy.EvaluateNextStage(CurriculumStage.Stage1_Addition, states, false, Progressions(addBand: 10)));
    }

    [Fact]
    public void R05_AllAdditionFactsMasteredButBandStillD10_DoesNotUnlockSubtraction()
    {
        var states = MasteredStates(ArithmeticOperation.Addition, maxLevel: 10);

        var next = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            states,
            hasBroadWeakness: false,
            Progressions(addBand: 9));

        Assert.Equal(CurriculumStage.Stage1_Addition, next);
    }

    [Fact]
    public void R06_SubtractionPastD10_WithUnattempted10Minus10_DoesNotUnlockMultiplication()
    {
        var states = MasteredStates(ArithmeticOperation.Subtraction, maxLevel: 10);
        states.Remove("sub:10-10");

        var next = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage2_Subtraction,
            states,
            hasBroadWeakness: false,
            Progressions(subBand: 10));

        Assert.Equal(CurriculumStage.Stage2_Subtraction, next);
    }

    [Fact]
    public void R07_MultiplicationPastD10_With10Times10Missed_DoesNotUnlockDivision()
    {
        var states = MasteredStates(ArithmeticOperation.Multiplication, maxLevel: 10);
        MarkMissed(states["mul:10*10"]);

        var next = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage3_Multiplication,
            states,
            hasBroadWeakness: false,
            Progressions(mulBand: 10));

        Assert.Equal(CurriculumStage.Stage3_Multiplication, next);
    }

    [Theory]
    [InlineData(CurriculumStage.Stage1_Addition, ArithmeticOperation.Addition, CurriculumStage.Stage2_Subtraction)]
    [InlineData(CurriculumStage.Stage2_Subtraction, ArithmeticOperation.Subtraction, CurriculumStage.Stage3_Multiplication)]
    [InlineData(CurriculumStage.Stage3_Multiplication, ArithmeticOperation.Multiplication, CurriculumStage.Stage4_Division)]
    public void R08_CompleteD10MasteryWithoutBroadWeakness_UnlocksExactlyTheNextStage(
        CurriculumStage current,
        ArithmeticOperation prerequisite,
        CurriculumStage expected)
    {
        var states = MasteredStates(prerequisite, maxLevel: 10);
        var progressions = Progressions(
            addBand: prerequisite == ArithmeticOperation.Addition ? 10 : 0,
            subBand: prerequisite == ArithmeticOperation.Subtraction ? 10 : 0,
            mulBand: prerequisite == ArithmeticOperation.Multiplication ? 10 : 0);

        Assert.Equal(expected, CurriculumUnlockPolicy.EvaluateNextStage(current, states, false, progressions));
        Assert.Equal(current, CurriculumUnlockPolicy.EvaluateNextStage(current, states, true, progressions));
    }

    [Fact]
    public async Task R09_FreshFluentLearner_RemainsAdditionOnlyForItsFirstHundredAttempts()
    {
        using var store = new SqliteLearnerStore(Path.Combine(_directory, "fresh.db"));
        var session = new TrainingSession(store, new FixedLatencyClock(), practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        for (var attempt = 1; attempt <= 100; attempt++)
        {
            Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
            Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
            await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);
            if (session.InteractionState == SessionInteractionState.SessionCheckIn)
            {
                await session.ContinuePracticeAsync(startTiming: false);
            }
        }
    }

    private static Dictionary<string, ItemLearningState> MasteredStates(ArithmeticOperation operation, int maxLevel)
    {
        var curriculum = new ArithmeticCurriculum().GetCurriculum(operation);
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
        for (var bandIndex = 0; bandIndex < maxLevel; bandIndex++)
        {
            foreach (var fact in ownership.GetOwnedFrontier(bandIndex))
            {
                var state = ItemLearningState.CreateNew(fact);
                MarkCorrect(state);
                states[fact.Id] = state;
            }
        }

        return states;
    }

    private static void MarkCorrect(ItemLearningState state)
    {
        state.TotalAttempts++;
        state.CorrectAttempts++;
        state.ConsecutiveCorrectStreak++;
        state.NeedsRemediation = false;
    }

    private static void MarkMissed(ItemLearningState state)
    {
        state.TotalAttempts++;
        state.IncorrectAttempts++;
        state.ConsecutiveCorrectStreak = 0;
        state.NeedsRemediation = true;
    }

    private static Dictionary<ArithmeticOperation, OperationProgression> Progressions(int addBand = 0, int subBand = 0, int mulBand = 0) => new()
    {
        [ArithmeticOperation.Addition] = new(ArithmeticOperation.Addition, addBand, 0),
        [ArithmeticOperation.Subtraction] = new(ArithmeticOperation.Subtraction, subBand, 0),
        [ArithmeticOperation.Multiplication] = new(ArithmeticOperation.Multiplication, mulBand, 0),
        [ArithmeticOperation.Division] = new(ArithmeticOperation.Division, 0, 0)
    };

    private sealed class FixedLatencyClock : IClock
    {
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(900);
    }
}
