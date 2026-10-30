namespace MathFirst.Core.Tests;

using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class CurriculumUnlockPolicyTests
{
    private readonly ArithmeticCurriculum _curriculum = new();

    #region 1-5: CURRICULUM STAGE MAPPING

    [Fact]
    public void Contract01_Stage1_MapsExactlyToAddition()
    {
        var operations = CurriculumUnlockPolicy.GetUnlockedOperations(CurriculumStage.Stage1_Addition);

        Assert.Equal([ArithmeticOperation.Addition], operations);
    }

    [Fact]
    public void Contract02_Stage2_MapsExactlyToAdditionAndSubtraction()
    {
        var operations = CurriculumUnlockPolicy.GetUnlockedOperations(CurriculumStage.Stage2_Subtraction);

        Assert.Equal([ArithmeticOperation.Addition, ArithmeticOperation.Subtraction], operations);
    }

    [Fact]
    public void Contract03_Stage3_MapsExactlyToAdditionSubtractionMultiplication()
    {
        var operations = CurriculumUnlockPolicy.GetUnlockedOperations(CurriculumStage.Stage3_Multiplication);

        Assert.Equal(
            [ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication],
            operations);
    }

    [Fact]
    public void Contract04_Stage4_MapsExactlyToAllFourOperations()
    {
        var operations = CurriculumUnlockPolicy.GetUnlockedOperations(CurriculumStage.Stage4_Division);

        Assert.Equal(
            [
                ArithmeticOperation.Addition,
                ArithmeticOperation.Subtraction,
                ArithmeticOperation.Multiplication,
                ArithmeticOperation.Division
            ],
            operations);
    }

    [Fact]
    public void Contract05_Stage4_CannotAdvanceFurther()
    {
        var itemStates = CreateMasteredStates(ArithmeticOperation.Multiplication);
        var progressions = CreateProgressions(add: 10, sub: 10, mul: 10, div: 10);

        var canAdvance = CurriculumUnlockPolicy.CanAdvance(
            CurriculumStage.Stage4_Division,
            itemStates,
            hasBroadWeakness: false,
            progressions);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage4_Division,
            itemStates,
            hasBroadWeakness: false,
            progressions);

        Assert.False(canAdvance);
        Assert.Equal(CurriculumStage.Stage4_Division, nextStage);
    }

    [Fact]
    public void StageMapping_InvalidStage_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CurriculumUnlockPolicy.GetUnlockedOperations((CurriculumStage)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CurriculumUnlockPolicy.EvaluateNextStage((CurriculumStage)999, new Dictionary<string, ItemLearningState>(), false));
    }

    #endregion

    #region 6-11: STAGE 1 -> 2 (ADDITION D10 MASTERY)

    [Fact]
    public void Contract06_Stage1To2_NoAdditionEvidence_StaysStage1()
    {
        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            new Dictionary<string, ItemLearningState>(),
            hasBroadWeakness: false,
            CreateProgressions());

        Assert.Equal(CurriculumStage.Stage1_Addition, nextStage);
    }

    [Fact]
    public void Contract07_Stage1To2_LegacyIntroductoryD01Evidence_NoLongerUnlocks()
    {
        var itemStates = CreateLegacyIntroducedItemStates(CurriculumStage.Stage1_Addition, weakCount: 0);
        Assert.True(CurriculumUnlockPolicy.IsPrerequisiteFullyIntroduced(CurriculumStage.Stage1_Addition, itemStates));

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            itemStates,
            hasBroadWeakness: false,
            CreateProgressions(add: 1));

        Assert.Equal(CurriculumStage.Stage1_Addition, nextStage);
    }

    [Fact]
    public void Contract08_Stage1To2_CompleteAdditionD10Mastery_AdvancesToStage2()
    {
        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            CreateMasteredStates(ArithmeticOperation.Addition),
            hasBroadWeakness: false,
            CreateProgressions(add: 10));

        Assert.Equal(CurriculumStage.Stage2_Subtraction, nextStage);
    }

    [Fact]
    public void Contract09_Stage1To2_OneUnresolvedMasteryFact_StaysStage1()
    {
        var itemStates = CreateMasteredStates(ArithmeticOperation.Addition);
        MarkMissed(itemStates["add:7+3"]);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            itemStates,
            hasBroadWeakness: false,
            CreateProgressions(add: 10));

        Assert.Equal(CurriculumStage.Stage1_Addition, nextStage);
    }

    [Fact]
    public void Contract10_Stage1To2_MissingProgressions_FailsClosed()
    {
        var itemStates = CreateMasteredStates(ArithmeticOperation.Addition);

        Assert.False(CurriculumUnlockPolicy.CanAdvance(CurriculumStage.Stage1_Addition, itemStates, hasBroadWeakness: false));
        Assert.Equal(
            CurriculumStage.Stage1_Addition,
            CurriculumUnlockPolicy.EvaluateNextStage(CurriculumStage.Stage1_Addition, itemStates, hasBroadWeakness: false));
    }

    [Fact]
    public void Contract11_Stage1To2_CompleteMastery_BroadWeaknessTrue_StaysStage1()
    {
        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            CreateMasteredStates(ArithmeticOperation.Addition),
            hasBroadWeakness: true,
            CreateProgressions(add: 10));

        Assert.Equal(CurriculumStage.Stage1_Addition, nextStage);
    }

    #endregion

    #region 12-15: STAGE 2 -> 3 (SUBTRACTION D10 MASTERY)

    [Fact]
    public void Contract12_Stage2To3_CompleteSubtractionD10Mastery_AdvancesToStage3()
    {
        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage2_Subtraction,
            CreateMasteredStates(ArithmeticOperation.Subtraction),
            hasBroadWeakness: false,
            CreateProgressions(sub: 10));

        Assert.Equal(CurriculumStage.Stage3_Multiplication, nextStage);
    }

    [Theory]
    [InlineData("sub:10-9")]
    [InlineData("sub:10-10")]
    public void Contract13_Stage2To3_BoundaryFactUnresolved_StaysStage2(string boundaryFactId)
    {
        var itemStates = CreateMasteredStates(ArithmeticOperation.Subtraction);
        MarkMissed(itemStates[boundaryFactId]);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage2_Subtraction,
            itemStates,
            hasBroadWeakness: false,
            CreateProgressions(sub: 10));

        Assert.Equal(CurriculumStage.Stage2_Subtraction, nextStage);
    }

    [Fact]
    public void Contract14_Stage2To3_SubtractionStillInsideD10_StaysStage2()
    {
        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage2_Subtraction,
            CreateMasteredStates(ArithmeticOperation.Subtraction),
            hasBroadWeakness: false,
            CreateProgressions(add: 25, sub: 9));

        Assert.Equal(CurriculumStage.Stage2_Subtraction, nextStage);
    }

    [Fact]
    public void Contract15_Stage2To3_AggregateBroadWeakness_StaysStage2()
    {
        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage2_Subtraction,
            CreateMasteredStates(ArithmeticOperation.Subtraction),
            hasBroadWeakness: true,
            CreateProgressions(sub: 10));

        Assert.Equal(CurriculumStage.Stage2_Subtraction, nextStage);
    }

    #endregion

    #region 16-19: STAGE 3 -> 4 (MULTIPLICATION D10 MASTERY)

    [Fact]
    public void Contract16_Stage3To4_CompleteMultiplicationD10Mastery_AdvancesToStage4()
    {
        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage3_Multiplication,
            CreateMasteredStates(ArithmeticOperation.Multiplication),
            hasBroadWeakness: false,
            CreateProgressions(mul: 10));

        Assert.Equal(CurriculumStage.Stage4_Division, nextStage);
    }

    [Fact]
    public void Contract17_Stage3To4_Unattempted10Times10_StaysStage3()
    {
        var itemStates = CreateMasteredStates(ArithmeticOperation.Multiplication);
        itemStates.Remove("mul:10*10");

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage3_Multiplication,
            itemStates,
            hasBroadWeakness: false,
            CreateProgressions(mul: 10));

        Assert.Equal(CurriculumStage.Stage3_Multiplication, nextStage);
    }

    [Fact]
    public void Contract18_Stage3To4_OtherOperationsAdvancedButMultiplicationNot_StaysStage3()
    {
        var itemStates = CreateMasteredStates(ArithmeticOperation.Addition);
        foreach (var (factId, state) in CreateMasteredStates(ArithmeticOperation.Subtraction))
        {
            itemStates[factId] = state;
        }

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage3_Multiplication,
            itemStates,
            hasBroadWeakness: false,
            CreateProgressions(add: 30, sub: 20, mul: 2));

        Assert.Equal(CurriculumStage.Stage3_Multiplication, nextStage);
    }

    [Fact]
    public void Contract19_Stage3To4_AggregateBroadWeakness_StaysStage3()
    {
        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage3_Multiplication,
            CreateMasteredStates(ArithmeticOperation.Multiplication),
            hasBroadWeakness: true,
            CreateProgressions(mul: 10));

        Assert.Equal(CurriculumStage.Stage3_Multiplication, nextStage);
    }

    #endregion

    #region 20-24: MONOTONICITY

    [Fact]
    public void Contract20_Stage2_CannotEvaluateBackwardToStage1()
    {
        // Even with every introductory ADD fact weak and broad weakness true, Stage 2 does not regress to Stage 1.
        var itemStates = new Dictionary<string, ItemLearningState>
        {
            ["add:0+0"] = CreateItemState(ArithmeticOperation.Addition, 0, 0, totalAttempts: 1, needsRemediation: true),
            ["add:0+1"] = CreateItemState(ArithmeticOperation.Addition, 0, 1, totalAttempts: 1, needsRemediation: true),
            ["add:1+0"] = CreateItemState(ArithmeticOperation.Addition, 1, 0, totalAttempts: 1, needsRemediation: true),
            ["add:1+1"] = CreateItemState(ArithmeticOperation.Addition, 1, 1, totalAttempts: 1, needsRemediation: true)
        };

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage2_Subtraction,
            itemStates,
            hasBroadWeakness: true,
            CreateProgressions());

        Assert.True(nextStage >= CurriculumStage.Stage2_Subtraction);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, nextStage);
    }

    [Fact]
    public void Contract21_Stage3_CannotEvaluateBackward()
    {
        var itemStates = new Dictionary<string, ItemLearningState>();

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage3_Multiplication,
            itemStates,
            hasBroadWeakness: true,
            CreateProgressions());

        Assert.True(nextStage >= CurriculumStage.Stage3_Multiplication);
        Assert.Equal(CurriculumStage.Stage3_Multiplication, nextStage);
    }

    [Fact]
    public void Contract22_Stage4_CannotEvaluateBackward()
    {
        var itemStates = new Dictionary<string, ItemLearningState>();

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage4_Division,
            itemStates,
            hasBroadWeakness: true,
            CreateProgressions());

        Assert.True(nextStage >= CurriculumStage.Stage4_Division);
        Assert.Equal(CurriculumStage.Stage4_Division, nextStage);
    }

    [Fact]
    public void Contract23_Stage1_CannotSkipDirectlyToStage3Or4()
    {
        // Even if ADD, SUB, and MUL are all mastered through D10 with zero errors:
        var itemStates = CreateMasteredStates(ArithmeticOperation.Addition);
        foreach (var operation in new[] { ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication })
        {
            foreach (var (factId, state) in CreateMasteredStates(operation))
            {
                itemStates[factId] = state;
            }
        }

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            itemStates,
            hasBroadWeakness: false,
            CreateProgressions(add: 10, sub: 10, mul: 10));

        Assert.Equal(CurriculumStage.Stage2_Subtraction, nextStage);
    }

    [Fact]
    public void Contract24_Stage2_CannotSkipDirectlyToStage4()
    {
        // Even if SUB and MUL are both mastered through D10 with zero errors:
        var itemStates = CreateMasteredStates(ArithmeticOperation.Subtraction);
        foreach (var (factId, state) in CreateMasteredStates(ArithmeticOperation.Multiplication))
        {
            itemStates[factId] = state;
        }

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage2_Subtraction,
            itemStates,
            hasBroadWeakness: false,
            CreateProgressions(sub: 10, mul: 10));

        Assert.Equal(CurriculumStage.Stage3_Multiplication, nextStage);
    }

    #endregion

    #region 25-32: BROAD WEAKNESS

    [Fact]
    public void Contract25_BroadWeakness_OneEligibleRemediationFact_ReturnsFalse()
    {
        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, totalAttempts: 1, needsRemediation: true)
        };
        var activeOperations = new[] { ArithmeticOperation.Addition };
        var progressions = CreateDefaultProgressions();
        var gate = GuidedNumberSpaceGate.Unrestricted;

        var result = BroadWeaknessPolicy.HasBroadWeakness(
            itemStates,
            activeOperations,
            progressions,
            _curriculum,
            gate);

        Assert.False(result);
    }

    [Fact]
    public void Contract26_BroadWeakness_TwoEligibleRemediationFactsInSameOperation_ReturnsTrue()
    {
        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, totalAttempts: 1, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Addition, 0, 1, totalAttempts: 1, needsRemediation: true)
        };
        var activeOperations = new[] { ArithmeticOperation.Addition };
        var progressions = CreateDefaultProgressions();
        var gate = GuidedNumberSpaceGate.Unrestricted;

        var result = BroadWeaknessPolicy.HasBroadWeakness(
            itemStates,
            activeOperations,
            progressions,
            _curriculum,
            gate);

        Assert.True(result);
    }

    [Fact]
    public void Contract27_BroadWeakness_OneAddAndOneSubEligibleRemediationFacts_ReturnsTrue()
    {
        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, totalAttempts: 1, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Subtraction, 0, 0, totalAttempts: 1, needsRemediation: true)
        };
        var activeOperations = new[] { ArithmeticOperation.Addition, ArithmeticOperation.Subtraction };
        var progressions = CreateDefaultProgressions();
        var gate = GuidedNumberSpaceGate.Unrestricted;

        var result = BroadWeaknessPolicy.HasBroadWeakness(
            itemStates,
            activeOperations,
            progressions,
            _curriculum,
            gate);

        Assert.True(result);
    }

    [Fact]
    public void Contract28_BroadWeakness_RemediationFactFromInactiveOperation_IsIgnored()
    {
        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, totalAttempts: 1, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Subtraction, 0, 0, totalAttempts: 1, needsRemediation: true)
        };
        // Subtraction is inactive!
        var activeOperations = new[] { ArithmeticOperation.Addition };
        var progressions = CreateDefaultProgressions();
        var gate = GuidedNumberSpaceGate.Unrestricted;

        var result = BroadWeaknessPolicy.HasBroadWeakness(
            itemStates,
            activeOperations,
            progressions,
            _curriculum,
            gate);

        Assert.False(result);
    }

    [Fact]
    public void Contract29_BroadWeakness_FutureIneligibleOwnedFact_IsIgnored()
    {
        // BandIndex is 0 for Addition. Fact 2+2 belongs to ADD-D02 (Band 1), so it's ineligible for band 0.
        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, totalAttempts: 1, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Addition, 2, 2, totalAttempts: 1, needsRemediation: true)
        };
        var activeOperations = new[] { ArithmeticOperation.Addition };
        var progressions = CreateDefaultProgressions(); // BandIndex = 0
        var gate = GuidedNumberSpaceGate.Unrestricted;

        var result = BroadWeaknessPolicy.HasBroadWeakness(
            itemStates,
            activeOperations,
            progressions,
            _curriculum,
            gate);

        Assert.False(result);
    }

    [Fact]
    public void Contract30_BroadWeakness_FactBlockedByEffectiveGuidedNumberSpaceGate_IsIgnored()
    {
        // ADD-D01 has ceiling = 1. mul:5*5 has CorrectResult = 25 > 1, so gate blocks it.
        var gate = GuidedNumberSpaceGate.ForGuided(_curriculum.Addition, unlockedAdditionBandIndex: 0, multiplicationBandIndex: 0, divisionBandIndex: 0);

        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, totalAttempts: 1, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Multiplication, 5, 5, totalAttempts: 1, needsRemediation: true)
        };
        var activeOperations = new[] { ArithmeticOperation.Addition, ArithmeticOperation.Multiplication };
        var progressions = new Dictionary<ArithmeticOperation, OperationProgression>
        {
            [ArithmeticOperation.Addition] = new(ArithmeticOperation.Addition, 0, 0),
            [ArithmeticOperation.Multiplication] = new(ArithmeticOperation.Multiplication, 4, 0) // band 4 includes 5*5
        };

        var result = BroadWeaknessPolicy.HasBroadWeakness(
            itemStates,
            activeOperations,
            progressions,
            _curriculum,
            gate);

        Assert.False(result);
    }

    [Fact]
    public void Contract31_BroadWeakness_ResolvedNeedsRemediationFalseFact_IsIgnored()
    {
        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, totalAttempts: 5, needsRemediation: false),
            CreateItemState(ArithmeticOperation.Addition, 0, 1, totalAttempts: 5, needsRemediation: false),
            CreateItemState(ArithmeticOperation.Addition, 1, 0, totalAttempts: 5, needsRemediation: true)
        };
        var activeOperations = new[] { ArithmeticOperation.Addition };
        var progressions = CreateDefaultProgressions();
        var gate = GuidedNumberSpaceGate.Unrestricted;

        var result = BroadWeaknessPolicy.HasBroadWeakness(
            itemStates,
            activeOperations,
            progressions,
            _curriculum,
            gate);

        Assert.False(result);
    }

    [Fact]
    public void Contract32_BroadWeakness_ThresholdMatchesLearningPolicyBroadWeaknessThreshold()
    {
        Assert.Equal(2, LearningPolicy.BroadWeaknessThreshold);

        var oneWeak = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, totalAttempts: 1, needsRemediation: true)
        };
        var twoWeak = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, totalAttempts: 1, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Addition, 0, 1, totalAttempts: 1, needsRemediation: true)
        };
        var activeOperations = new[] { ArithmeticOperation.Addition };
        var progressions = CreateDefaultProgressions();
        var gate = GuidedNumberSpaceGate.Unrestricted;

        Assert.False(BroadWeaknessPolicy.HasBroadWeakness(oneWeak, activeOperations, progressions, _curriculum, gate));
        Assert.True(BroadWeaknessPolicy.HasBroadWeakness(twoWeak, activeOperations, progressions, _curriculum, gate));
    }

    #endregion

    #region 33-35: GENERIC BOUNDARY

    [Fact]
    public void Contract33_PracticeOperationPreferencePolicy_NullFallback_RemainsAllOperations()
    {
        var normalized = PracticeOperationPreferencePolicy.NormalizeEnabledOperations(null);

        Assert.Equal(PracticeOperationPreferencePolicy.AllOperations, normalized);
    }

    [Fact]
    public void Contract34_PracticeOperationPreferencePolicy_EmptyFallback_RemainsAllOperations()
    {
        var normalized = PracticeOperationPreferencePolicy.NormalizeEnabledOperations([]);

        Assert.Equal(PracticeOperationPreferencePolicy.AllOperations, normalized);
    }

    [Fact]
    public void Contract35_PracticeOperationPreferencePolicy_ExplicitNonEmptySubset_RemainsPreserved()
    {
        var subset = new[] { ArithmeticOperation.Subtraction, ArithmeticOperation.Division };
        var normalized = PracticeOperationPreferencePolicy.NormalizeEnabledOperations(subset);

        Assert.Equal([ArithmeticOperation.Subtraction, ArithmeticOperation.Division], normalized);
    }

    #endregion

    #region 36-41: UNLOCK POLICY BOUNDARY & ERROR CONTRACTS

    [Fact]
    public void Contract36_GetPrerequisiteFactIds_InvalidStageAndStage4_BehaveExpectedly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CurriculumUnlockPolicy.GetPrerequisiteFactIds((CurriculumStage)999));

        var stage4Prereqs = CurriculumUnlockPolicy.GetPrerequisiteFactIds(CurriculumStage.Stage4_Division);
        Assert.Empty(stage4Prereqs);
    }

    [Fact]
    public void Contract37_GetPrerequisiteOperation_InvalidStageAndStage4_BehaveExpectedly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CurriculumUnlockPolicy.GetPrerequisiteOperation((CurriculumStage)999));

        var stage4PrereqOp = CurriculumUnlockPolicy.GetPrerequisiteOperation(CurriculumStage.Stage4_Division);
        Assert.Null(stage4PrereqOp);
    }

    [Fact]
    public void Contract38_IsPrerequisiteFullyIntroduced_NullAndInvalidStageAndStage4_BehaveExpectedly()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CurriculumUnlockPolicy.IsPrerequisiteFullyIntroduced(CurriculumStage.Stage1_Addition, null!));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CurriculumUnlockPolicy.IsPrerequisiteFullyIntroduced((CurriculumStage)999, new Dictionary<string, ItemLearningState>()));

        var stage4Introduced = CurriculumUnlockPolicy.IsPrerequisiteFullyIntroduced(
            CurriculumStage.Stage4_Division,
            new Dictionary<string, ItemLearningState>());
        Assert.False(stage4Introduced);
    }

    [Fact]
    public void Contract39_CountPrerequisiteWeakFacts_NullAndInvalidStageAndStage4_BehaveExpectedly()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CurriculumUnlockPolicy.CountPrerequisiteWeakFacts(CurriculumStage.Stage1_Addition, null!));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CurriculumUnlockPolicy.CountPrerequisiteWeakFacts((CurriculumStage)999, new Dictionary<string, ItemLearningState>()));

        var stage4WeakCount = CurriculumUnlockPolicy.CountPrerequisiteWeakFacts(
            CurriculumStage.Stage4_Division,
            new Dictionary<string, ItemLearningState>());
        Assert.Equal(0, stage4WeakCount);
    }

    [Fact]
    public void Contract40_CanAdvance_InvalidStageAndNullAndStage4_BehaveExpectedly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CurriculumUnlockPolicy.CanAdvance((CurriculumStage)999, isPrerequisiteMastered: true, hasBroadWeakness: false));

        Assert.False(CurriculumUnlockPolicy.CanAdvance(
            CurriculumStage.Stage4_Division,
            isPrerequisiteMastered: true,
            hasBroadWeakness: false));

        Assert.True(CurriculumUnlockPolicy.CanAdvance(CurriculumStage.Stage1_Addition, isPrerequisiteMastered: true, hasBroadWeakness: false));
        Assert.False(CurriculumUnlockPolicy.CanAdvance(CurriculumStage.Stage1_Addition, isPrerequisiteMastered: false, hasBroadWeakness: false));
        Assert.False(CurriculumUnlockPolicy.CanAdvance(CurriculumStage.Stage1_Addition, isPrerequisiteMastered: true, hasBroadWeakness: true));

        Assert.Throws<ArgumentNullException>(() =>
            CurriculumUnlockPolicy.CanAdvance(CurriculumStage.Stage1_Addition, null!, hasBroadWeakness: false));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CurriculumUnlockPolicy.CanAdvance((CurriculumStage)999, new Dictionary<string, ItemLearningState>(), hasBroadWeakness: false));
    }

    [Fact]
    public void Contract41_EvaluateNextStage_InvalidStageAndNullAndStage4_BehaveExpectedly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CurriculumUnlockPolicy.EvaluateNextStage((CurriculumStage)999, isPrerequisiteMastered: true, hasBroadWeakness: false));

        var stage4Next = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage4_Division,
            isPrerequisiteMastered: true,
            hasBroadWeakness: false);
        Assert.Equal(CurriculumStage.Stage4_Division, stage4Next);

        Assert.Throws<ArgumentNullException>(() =>
            CurriculumUnlockPolicy.EvaluateNextStage(CurriculumStage.Stage1_Addition, null!, hasBroadWeakness: false));
    }

    #endregion

    #region 42-50: ADR-0013 D10 MASTERY EVIDENCE

    [Fact]
    public void Contract42_MasteryFactSets_AreTheCanonicalD01ToD10OwnedFrontiers()
    {
        Assert.Equal(10, CurriculumUnlockPolicy.PrerequisiteMasteryBandCount);

        var addition = CurriculumUnlockPolicy.GetMasteryFactIds(ArithmeticOperation.Addition);
        var subtraction = CurriculumUnlockPolicy.GetMasteryFactIds(ArithmeticOperation.Subtraction);
        var multiplication = CurriculumUnlockPolicy.GetMasteryFactIds(ArithmeticOperation.Multiplication);

        Assert.Equal(121, addition.Count);
        Assert.Equal(66, subtraction.Count);
        Assert.Equal(121, multiplication.Count);
        Assert.Empty(CurriculumUnlockPolicy.GetMasteryFactIds(ArithmeticOperation.Division));

        Assert.Equal(addition.Count, addition.Distinct().Count());
        Assert.Contains("add:4+1", addition);
        Assert.Contains("add:10+10", addition);
        Assert.DoesNotContain("add:11+0", addition);
        Assert.Contains("sub:10-9", subtraction);
        Assert.Contains("sub:10-10", subtraction);
        Assert.DoesNotContain("sub:11-1", subtraction);
        Assert.Contains("mul:10*10", multiplication);
        Assert.DoesNotContain("mul:11*1", multiplication);

        foreach (var operation in new[] { ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication })
        {
            var ownership = new AcquisitionOwnershipResolver(_curriculum.GetCurriculum(operation));
            Assert.All(CurriculumUnlockPolicy.GetMasteryFactIds(operation), factId =>
            {
                Assert.True(ownership.TryGetOwner(factId, 9, out var owner));
                Assert.InRange(owner, 0, 9);
            });
            Assert.Equal(
                Enumerable.Range(0, 10).Sum(band => ownership.GetOwnedFrontier(band).Count),
                CurriculumUnlockPolicy.GetMasteryFactIds(operation).Count);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => CurriculumUnlockPolicy.GetMasteryFactIds((ArithmeticOperation)99));
    }

    [Fact]
    public void Contract43_RequiredBoundaryFacts_PinTheOwnerApprovedD10Boundaries()
    {
        Assert.Equal(["add:10+10"], CurriculumUnlockPolicy.GetRequiredBoundaryFactIds(CurriculumStage.Stage1_Addition));
        Assert.Equal(["sub:10-9", "sub:10-10"], CurriculumUnlockPolicy.GetRequiredBoundaryFactIds(CurriculumStage.Stage2_Subtraction));
        Assert.Equal(["mul:10*10"], CurriculumUnlockPolicy.GetRequiredBoundaryFactIds(CurriculumStage.Stage3_Multiplication));
        Assert.Empty(CurriculumUnlockPolicy.GetRequiredBoundaryFactIds(CurriculumStage.Stage4_Division));
        Assert.Throws<ArgumentOutOfRangeException>(() => CurriculumUnlockPolicy.GetRequiredBoundaryFactIds((CurriculumStage)999));

        foreach (var stage in new[] { CurriculumStage.Stage1_Addition, CurriculumStage.Stage2_Subtraction, CurriculumStage.Stage3_Multiplication })
        {
            var operation = CurriculumUnlockPolicy.GetPrerequisiteOperation(stage)!.Value;
            Assert.All(CurriculumUnlockPolicy.GetRequiredBoundaryFactIds(stage), factId =>
                Assert.Contains(factId, CurriculumUnlockPolicy.GetMasteryFactIds(operation)));
        }
    }

    [Fact]
    public void Contract44_FactMastery_RequiresAttemptedCorrectUnremediatedLatestCorrectEvidence()
    {
        Assert.False(CurriculumUnlockPolicy.IsFactMasteryDemonstrated(null));

        var unattempted = CreateItemState(ArithmeticOperation.Addition, 4, 1, totalAttempts: 0);
        Assert.False(CurriculumUnlockPolicy.IsFactMasteryDemonstrated(unattempted));

        var exposedOnly = CreateItemState(ArithmeticOperation.Addition, 4, 1, totalAttempts: 1);
        Assert.False(CurriculumUnlockPolicy.IsFactMasteryDemonstrated(exposedOnly));

        var cleanFirstTry = CreateItemState(ArithmeticOperation.Addition, 4, 1, totalAttempts: 0);
        MarkCorrect(cleanFirstTry);
        Assert.True(CurriculumUnlockPolicy.IsFactMasteryDemonstrated(cleanFirstTry));

        var latestWrong = CreateItemState(ArithmeticOperation.Addition, 4, 1, totalAttempts: 0);
        MarkCorrect(latestWrong);
        MarkCorrect(latestWrong);
        MarkMissed(latestWrong);
        Assert.False(CurriculumUnlockPolicy.IsFactMasteryDemonstrated(latestWrong));

        var inconsistentRemediation = CreateItemState(ArithmeticOperation.Addition, 4, 1, totalAttempts: 0);
        MarkCorrect(inconsistentRemediation);
        inconsistentRemediation.NeedsRemediation = true;
        Assert.False(CurriculumUnlockPolicy.IsFactMasteryDemonstrated(inconsistentRemediation));
    }

    [Fact]
    public void Contract45_FactMastery_RepeatedlyMissedFactStaysUnresolvedUntilALaterCorrectRetrieval_RegardlessOfLatency()
    {
        var recovered = CreateItemState(ArithmeticOperation.Addition, 4, 1, totalAttempts: 0);
        MarkMissed(recovered);
        MarkMissed(recovered);
        MarkMissed(recovered);
        Assert.False(CurriculumUnlockPolicy.IsFactMasteryDemonstrated(recovered));

        MarkCorrect(recovered);
        recovered.LastLatencyMs = 25_000;
        recovered.RollingLatencyMs = 25_000;
        recovered.FluentStreak = 0;
        recovered.IsProvisionallyMastered = false;
        Assert.True(CurriculumUnlockPolicy.IsFactMasteryDemonstrated(recovered));
    }

    [Fact]
    public void Contract46_MasteryEvidence_ReportsBandCountsAndUnresolvedFactsInCurriculumOrder()
    {
        var itemStates = CreateMasteredStates(ArithmeticOperation.Addition);
        MarkMissed(itemStates["add:4+1"]);
        itemStates.Remove("add:10+10");

        var evidence = CurriculumUnlockPolicy.EvaluatePrerequisiteMastery(
            CurriculumStage.Stage1_Addition,
            itemStates,
            CreateProgressions(add: 10));

        Assert.Equal(CurriculumStage.Stage1_Addition, evidence.CurrentStage);
        Assert.Equal(ArithmeticOperation.Addition, evidence.PrerequisiteOperation);
        Assert.Equal(10, evidence.BandIndex);
        Assert.True(evidence.HasCompletedMasteryBands);
        Assert.Equal(121, evidence.MasteryFactCount);
        Assert.Equal(119, evidence.DemonstratedFactCount);
        Assert.Equal(["add:4+1", "add:10+10"], evidence.UnresolvedFactIds);
        Assert.Equal(["add:10+10"], evidence.UnresolvedBoundaryFactIds);
        Assert.False(evidence.IsSatisfied);
    }

    [Fact]
    public void Contract47_MasteryEvidence_Stage4AndMissingProgressions_AreNeverSatisfied()
    {
        var stage4 = CurriculumUnlockPolicy.EvaluatePrerequisiteMastery(
            CurriculumStage.Stage4_Division,
            new Dictionary<string, ItemLearningState>(),
            CreateProgressions(add: 10, sub: 10, mul: 10, div: 10));
        Assert.Null(stage4.PrerequisiteOperation);
        Assert.False(stage4.IsSatisfied);

        var noProgressions = CurriculumUnlockPolicy.EvaluatePrerequisiteMastery(
            CurriculumStage.Stage1_Addition,
            CreateMasteredStates(ArithmeticOperation.Addition),
            progressions: null);
        Assert.Equal(-1, noProgressions.BandIndex);
        Assert.Empty(noProgressions.UnresolvedFactIds);
        Assert.False(noProgressions.IsSatisfied);

        Assert.Throws<ArgumentNullException>(() =>
            CurriculumUnlockPolicy.EvaluatePrerequisiteMastery(CurriculumStage.Stage1_Addition, null!, CreateProgressions()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CurriculumUnlockPolicy.EvaluatePrerequisiteMastery((CurriculumStage)999, new Dictionary<string, ItemLearningState>(), CreateProgressions()));
    }

    [Fact]
    public void Contract48_DivisionAndSkillsBeyondD10_DoNotSubstituteForPrerequisiteMastery()
    {
        // Advanced Addition (past D10) with every fact above D10 mastered does not compensate for one D10 gap.
        var itemStates = CreateMasteredStates(ArithmeticOperation.Addition);
        MarkMissed(itemStates["add:9+10"]);
        var advanced = ItemLearningState.CreateNew(new ArithmeticFact(ArithmeticOperation.Addition, 11, 7));
        MarkCorrect(advanced);
        itemStates[advanced.FactId] = advanced;

        Assert.False(CurriculumUnlockPolicy.IsPrerequisiteMastered(
            CurriculumStage.Stage1_Addition,
            itemStates,
            CreateProgressions(add: 40)));
    }

    [Fact]
    public void Contract49_LegacyMigrationPredicates_RetainTheirAdr0012Semantics()
    {
        // Schema V8 -> V9 migration still evaluates the historical introductory predicates unchanged.
        Assert.Equal(["add:0+0", "add:0+1", "add:1+0", "add:1+1"], CurriculumUnlockPolicy.GetPrerequisiteFactIds(CurriculumStage.Stage1_Addition));
        Assert.Equal(["sub:0-0", "sub:1-0", "sub:1-1"], CurriculumUnlockPolicy.GetPrerequisiteFactIds(CurriculumStage.Stage2_Subtraction));
        Assert.Equal(["mul:0*0", "mul:0*1", "mul:1*0", "mul:1*1"], CurriculumUnlockPolicy.GetPrerequisiteFactIds(CurriculumStage.Stage3_Multiplication));

        var oneWeak = CreateLegacyIntroducedItemStates(CurriculumStage.Stage1_Addition, weakCount: 1);
        Assert.True(CurriculumUnlockPolicy.IsPrerequisiteFullyIntroduced(CurriculumStage.Stage1_Addition, oneWeak));
        Assert.Equal(1, CurriculumUnlockPolicy.CountPrerequisiteWeakFacts(CurriculumStage.Stage1_Addition, oneWeak));
        Assert.True(CurriculumUnlockPolicy.IsPrerequisiteFullyIntroduced(
            CurriculumStage.Stage2_Subtraction,
            new Dictionary<string, ItemLearningState>(),
            CreateProgressions(sub: 1)));
        Assert.False(CurriculumUnlockPolicy.IsPrerequisiteFullyIntroduced(
            CurriculumStage.Stage1_Addition,
            new Dictionary<string, ItemLearningState> { ["add:0+0"] = CreateItemState(ArithmeticOperation.Addition, 0, 0, totalAttempts: 1) }));
    }

    #endregion

    #region Helpers

    private Dictionary<string, ItemLearningState> CreateMasteredStates(ArithmeticOperation operation)
    {
        var ownership = new AcquisitionOwnershipResolver(_curriculum.GetCurriculum(operation));
        var dict = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
        for (var bandIndex = 0; bandIndex < CurriculumUnlockPolicy.PrerequisiteMasteryBandCount; bandIndex++)
        {
            foreach (var fact in ownership.GetOwnedFrontier(bandIndex))
            {
                var state = ItemLearningState.CreateNew(fact);
                MarkCorrect(state);
                dict[fact.Id] = state;
            }
        }

        return dict;
    }

    private static Dictionary<string, ItemLearningState> CreateLegacyIntroducedItemStates(
        CurriculumStage currentStage,
        int weakCount)
    {
        var dict = new Dictionary<string, ItemLearningState>();
        var factIds = CurriculumUnlockPolicy.GetPrerequisiteFactIds(currentStage);

        var weakRemaining = weakCount;
        foreach (var factId in factIds)
        {
            var parts = factId.Split([':', '+', '-', '*', '/']);
            var op = parts[0] switch
            {
                "add" => ArithmeticOperation.Addition,
                "sub" => ArithmeticOperation.Subtraction,
                "mul" => ArithmeticOperation.Multiplication,
                "div" => ArithmeticOperation.Division,
                _ => throw new InvalidOperationException($"Unknown fact prefix in {factId}")
            };
            var left = int.Parse(parts[1]);
            var right = int.Parse(parts[2]);

            var isWeak = weakRemaining > 0;
            if (isWeak)
            {
                weakRemaining--;
            }

            dict[factId] = CreateItemState(op, left, right, totalAttempts: 1, needsRemediation: isWeak);
        }

        return dict;
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

    private static ItemLearningState CreateItemState(
        ArithmeticOperation operation,
        int left,
        int right,
        int totalAttempts,
        bool needsRemediation = false)
    {
        var fact = new ArithmeticFact(operation, left, right);
        var state = ItemLearningState.CreateNew(fact);
        state.TotalAttempts = totalAttempts;
        state.NeedsRemediation = needsRemediation;
        return state;
    }

    private static Dictionary<ArithmeticOperation, OperationProgression> CreateDefaultProgressions() =>
        Enum.GetValues<ArithmeticOperation>().ToDictionary(
            op => op,
            op => new OperationProgression(op, 0, 0));

    private static Dictionary<ArithmeticOperation, OperationProgression> CreateProgressions(int add = 0, int sub = 0, int mul = 0, int div = 0) => new()
    {
        [ArithmeticOperation.Addition] = new(ArithmeticOperation.Addition, add, 0),
        [ArithmeticOperation.Subtraction] = new(ArithmeticOperation.Subtraction, sub, 0),
        [ArithmeticOperation.Multiplication] = new(ArithmeticOperation.Multiplication, mul, 0),
        [ArithmeticOperation.Division] = new(ArithmeticOperation.Division, div, 0)
    };

    #endregion
}
