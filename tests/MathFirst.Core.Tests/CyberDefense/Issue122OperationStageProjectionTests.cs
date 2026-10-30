namespace MathFirst.Core.Tests.CyberDefense;

using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

/// <summary>
/// Issue #122: the arithmetic Stage strip is a pure projection of authoritative curriculum unlocking
/// (CurriculumManaged) or the practice preference (Custom). Each operation shows its own persisted stage.
/// </summary>
public sealed class Issue122OperationStageProjectionTests
{
    private static readonly ArithmeticCurriculum Curriculum = new();

    private static readonly ArithmeticOperation[] AllOperations =
        [ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication, ArithmeticOperation.Division];

    private static LearnerProgression Progression(CurriculumStage stage, params (ArithmeticOperation Operation, int Band)[] bands)
    {
        var progression = LearnerProgression.CreateFresh();
        progression.CurriculumStage = stage;
        foreach (var (operation, band) in bands)
        {
            progression.OperationProgressions[operation] = new OperationProgression(operation, band, 0);
        }
        return progression;
    }

    private static (ArithmeticOperation Operation, int? Stage)[] Curriculum_(LearnerProgression progression, params ArithmeticOperation[] preferences) =>
        Project(progression, PracticeMode.CurriculumManaged, preferences);

    private static (ArithmeticOperation Operation, int? Stage)[] Project(
        LearnerProgression progression, PracticeMode mode, params ArithmeticOperation[] preferences) =>
        OperationStageHudProjection.Create(progression, Curriculum, mode, preferences)
            .Select(p => (p.Operation, p.PresentationStage)).ToArray();

    [Fact]
    public void CurriculumManaged_AdditionOnlyStage_ShowsOnlyAddition_EvenWhenEveryOperationIsPreferred()
    {
        var shown = Curriculum_(Progression(CurriculumStage.Stage1_Addition, (ArithmeticOperation.Addition, 10)), AllOperations);

        Assert.Equal([(ArithmeticOperation.Addition, (int?)11)], shown);
    }

    [Fact]
    public void CurriculumManaged_NewlyUnlockedSubtraction_AppearsAfterAddition_WithoutAnyPreference()
    {
        // The pre-#122 bug: the strip was filtered by the practice preference even in curriculum mode.
        var shown = Curriculum_(
            Progression(CurriculumStage.Stage2_Subtraction, (ArithmeticOperation.Addition, 10)),
            ArithmeticOperation.Addition);

        Assert.Equal([(ArithmeticOperation.Addition, (int?)11), (ArithmeticOperation.Subtraction, (int?)1)], shown);
    }

    [Fact]
    public void CurriculumManaged_EachOperationShowsItsOwnStage_NotTheAdditionStage()
    {
        var shown = Curriculum_(
            Progression(CurriculumStage.Stage3_Multiplication,
                (ArithmeticOperation.Addition, 11), (ArithmeticOperation.Subtraction, 4), (ArithmeticOperation.Multiplication, 0)),
            ArithmeticOperation.Division);

        Assert.Equal(
            [(ArithmeticOperation.Addition, (int?)12), (ArithmeticOperation.Subtraction, (int?)5), (ArithmeticOperation.Multiplication, (int?)1)],
            shown);
    }

    [Theory]
    [InlineData(CurriculumStage.Stage1_Addition, 1)]
    [InlineData(CurriculumStage.Stage2_Subtraction, 2)]
    [InlineData(CurriculumStage.Stage3_Multiplication, 3)]
    [InlineData(CurriculumStage.Stage4_Division, 4)]
    public void CurriculumManaged_ShowsExactlyTheAuthoritativelyUnlockedOperations_InCanonicalOrder(CurriculumStage stage, int count)
    {
        var shown = Curriculum_(Progression(stage), ArithmeticOperation.Division, ArithmeticOperation.Addition).Select(p => p.Operation).ToArray();

        Assert.Equal(AllOperations.Take(count), shown);
        Assert.Equal(CurriculumUnlockPolicy.GetUnlockedOperations(stage), shown);
    }

    [Fact]
    public void CurriculumManaged_LockedOperationsAreNeverShown_EvenWithAdvancedStoredBands()
    {
        // A band stored for a still-locked operation (for example a legacy earned stage) must not unlock it.
        var shown = Curriculum_(
            Progression(CurriculumStage.Stage2_Subtraction, (ArithmeticOperation.Multiplication, 6), (ArithmeticOperation.Division, 3)),
            AllOperations);

        Assert.DoesNotContain(shown, p => p.Operation is ArithmeticOperation.Multiplication or ArithmeticOperation.Division);
    }

    [Fact]
    public void PriorEarnedStages_StayMonotonic_WhenTheOperationUnlocks()
    {
        var earned = Progression(CurriculumStage.Stage1_Addition, (ArithmeticOperation.Addition, 10), (ArithmeticOperation.Subtraction, 3));
        Assert.Equal([(ArithmeticOperation.Addition, (int?)11)], Curriculum_(earned));

        earned.CurriculumStage = CurriculumStage.Stage2_Subtraction;
        Assert.Equal([(ArithmeticOperation.Addition, (int?)11), (ArithmeticOperation.Subtraction, (int?)4)], Curriculum_(earned));
    }

    [Fact]
    public void UnlockedOperationWithoutStoredProgression_IsStillShown_WithAnUnavailableStage()
    {
        var progression = Progression(CurriculumStage.Stage2_Subtraction);
        progression.OperationProgressions.Remove(ArithmeticOperation.Subtraction);

        var shown = Curriculum_(progression);

        Assert.Equal([(ArithmeticOperation.Addition, (int?)1), (ArithmeticOperation.Subtraction, (int?)null)], shown);
    }

    [Fact]
    public void CustomPractice_KeepsPreferenceSemantics_InCanonicalOrder_RegardlessOfCurriculumStage()
    {
        var progression = Progression(CurriculumStage.Stage1_Addition, (ArithmeticOperation.Subtraction, 2), (ArithmeticOperation.Division, 5));

        var shown = Project(progression, PracticeMode.Custom, ArithmeticOperation.Division, ArithmeticOperation.Subtraction);

        Assert.Equal([(ArithmeticOperation.Subtraction, (int?)3), (ArithmeticOperation.Division, (int?)6)], shown);
    }

    [Fact]
    public void CustomPractice_EmptyPreferenceNormalizesLikeTheSession()
    {
        var shown = Project(Progression(CurriculumStage.Stage1_Addition), PracticeMode.Custom).Select(p => p.Operation);

        Assert.Equal(AllOperations, shown);
    }

    [Fact]
    public void Projection_DoesNotMutateTheProgressionItReads()
    {
        var progression = Progression(CurriculumStage.Stage2_Subtraction, (ArithmeticOperation.Addition, 10));
        var before = progression.OperationProgressions.ToDictionary(p => p.Key, p => p.Value);

        _ = Curriculum_(progression, ArithmeticOperation.Division);

        Assert.Equal(CurriculumStage.Stage2_Subtraction, progression.CurriculumStage);
        Assert.Equal(before, progression.OperationProgressions);
    }
}
