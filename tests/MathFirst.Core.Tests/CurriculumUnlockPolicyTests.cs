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
        var itemStates = CreateFullyIntroducedItemStates(CurriculumStage.Stage4_Division, weakCount: 0);

        var canAdvance = CurriculumUnlockPolicy.CanAdvance(
            CurriculumStage.Stage4_Division,
            itemStates,
            hasBroadWeakness: false);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage4_Division,
            itemStates,
            hasBroadWeakness: false);

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

    #region 6-11: STAGE 1 -> 2

    [Fact]
    public void Contract06_Stage1To2_NoAddD01Coverage_StaysStage1()
    {
        var itemStates = new Dictionary<string, ItemLearningState>(); // 0 attempts on all facts

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            itemStates,
            hasBroadWeakness: false);

        Assert.Equal(CurriculumStage.Stage1_Addition, nextStage);
    }

    [Fact]
    public void Contract07_Stage1To2_PartialAddD01Coverage_StaysStage1()
    {
        // ADD-D01 has 4 facts: add:0+0, add:0+1, add:1+0, add:1+1. Only 3 introduced:
        var itemStates = new Dictionary<string, ItemLearningState>
        {
            ["add:0+0"] = CreateItemState(ArithmeticOperation.Addition, 0, 0, totalAttempts: 1),
            ["add:0+1"] = CreateItemState(ArithmeticOperation.Addition, 0, 1, totalAttempts: 1),
            ["add:1+0"] = CreateItemState(ArithmeticOperation.Addition, 1, 0, totalAttempts: 1)
        };

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            itemStates,
            hasBroadWeakness: false);

        Assert.Equal(CurriculumStage.Stage1_Addition, nextStage);
    }

    [Fact]
    public void Contract08_Stage1To2_FullCoverage_ZeroWeakFacts_CleanEvidence_AdvancesToStage2()
    {
        var itemStates = CreateFullyIntroducedItemStates(CurriculumStage.Stage1_Addition, weakCount: 0);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            itemStates,
            hasBroadWeakness: false);

        Assert.Equal(CurriculumStage.Stage2_Subtraction, nextStage);
    }

    [Fact]
    public void Contract09_Stage1To2_FullCoverage_ExactlyOneWeakFact_CleanEvidence_AdvancesToStage2()
    {
        var itemStates = CreateFullyIntroducedItemStates(CurriculumStage.Stage1_Addition, weakCount: 1);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            itemStates,
            hasBroadWeakness: false);

        Assert.Equal(CurriculumStage.Stage2_Subtraction, nextStage);
    }

    [Fact]
    public void Contract10_Stage1To2_FullCoverage_TwoWeakFacts_StaysStage1()
    {
        var itemStates = CreateFullyIntroducedItemStates(CurriculumStage.Stage1_Addition, weakCount: 2);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            itemStates,
            hasBroadWeakness: false);

        Assert.Equal(CurriculumStage.Stage1_Addition, nextStage);
    }

    [Fact]
    public void Contract11_Stage1To2_FullCoverage_BroadWeaknessTrue_StaysStage1()
    {
        var itemStates = CreateFullyIntroducedItemStates(CurriculumStage.Stage1_Addition, weakCount: 0);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            itemStates,
            hasBroadWeakness: true);

        Assert.Equal(CurriculumStage.Stage1_Addition, nextStage);
    }

    #endregion

    #region 12-15: STAGE 2 -> 3

    [Fact]
    public void Contract12_Stage2To3_FullSubD01Coverage_ZeroWeak_CleanEvidence_AdvancesToStage3()
    {
        var itemStates = CreateFullyIntroducedItemStates(CurriculumStage.Stage2_Subtraction, weakCount: 0);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage2_Subtraction,
            itemStates,
            hasBroadWeakness: false);

        Assert.Equal(CurriculumStage.Stage3_Multiplication, nextStage);
    }

    [Fact]
    public void Contract13_Stage2To3_FullSubD01Coverage_OneWeak_CleanEvidence_AdvancesToStage3()
    {
        var itemStates = CreateFullyIntroducedItemStates(CurriculumStage.Stage2_Subtraction, weakCount: 1);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage2_Subtraction,
            itemStates,
            hasBroadWeakness: false);

        Assert.Equal(CurriculumStage.Stage3_Multiplication, nextStage);
    }

    [Fact]
    public void Contract14_Stage2To3_TwoPrerequisiteWeakFacts_StaysStage2()
    {
        var itemStates = CreateFullyIntroducedItemStates(CurriculumStage.Stage2_Subtraction, weakCount: 2);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage2_Subtraction,
            itemStates,
            hasBroadWeakness: false);

        Assert.Equal(CurriculumStage.Stage2_Subtraction, nextStage);
    }

    [Fact]
    public void Contract15_Stage2To3_AggregateBroadWeakness_StaysStage2()
    {
        var itemStates = CreateFullyIntroducedItemStates(CurriculumStage.Stage2_Subtraction, weakCount: 0);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage2_Subtraction,
            itemStates,
            hasBroadWeakness: true);

        Assert.Equal(CurriculumStage.Stage2_Subtraction, nextStage);
    }

    #endregion

    #region 16-19: STAGE 3 -> 4

    [Fact]
    public void Contract16_Stage3To4_FullMulD01Coverage_ZeroWeak_CleanEvidence_AdvancesToStage4()
    {
        var itemStates = CreateFullyIntroducedItemStates(CurriculumStage.Stage3_Multiplication, weakCount: 0);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage3_Multiplication,
            itemStates,
            hasBroadWeakness: false);

        Assert.Equal(CurriculumStage.Stage4_Division, nextStage);
    }

    [Fact]
    public void Contract17_Stage3To4_FullMulD01Coverage_OneWeak_CleanEvidence_AdvancesToStage4()
    {
        var itemStates = CreateFullyIntroducedItemStates(CurriculumStage.Stage3_Multiplication, weakCount: 1);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage3_Multiplication,
            itemStates,
            hasBroadWeakness: false);

        Assert.Equal(CurriculumStage.Stage4_Division, nextStage);
    }

    [Fact]
    public void Contract18_Stage3To4_TwoPrerequisiteWeakFacts_StaysStage3()
    {
        var itemStates = CreateFullyIntroducedItemStates(CurriculumStage.Stage3_Multiplication, weakCount: 2);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage3_Multiplication,
            itemStates,
            hasBroadWeakness: false);

        Assert.Equal(CurriculumStage.Stage3_Multiplication, nextStage);
    }

    [Fact]
    public void Contract19_Stage3To4_AggregateBroadWeakness_StaysStage3()
    {
        var itemStates = CreateFullyIntroducedItemStates(CurriculumStage.Stage3_Multiplication, weakCount: 0);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage3_Multiplication,
            itemStates,
            hasBroadWeakness: true);

        Assert.Equal(CurriculumStage.Stage3_Multiplication, nextStage);
    }

    #endregion

    #region 20-24: MONOTONICITY

    [Fact]
    public void Contract20_Stage2_CannotEvaluateBackwardToStage1()
    {
        // Even with 4 weak ADD facts and broad weakness true, Stage 2 does not regress to Stage 1.
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
            hasBroadWeakness: true);

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
            hasBroadWeakness: true);

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
            hasBroadWeakness: true);

        Assert.True(nextStage >= CurriculumStage.Stage4_Division);
        Assert.Equal(CurriculumStage.Stage4_Division, nextStage);
    }

    [Fact]
    public void Contract23_Stage1_CannotSkipDirectlyToStage3Or4()
    {
        // Even if ADD, SUB, and MUL are all fully practiced with zero errors:
        var itemStates = new Dictionary<string, ItemLearningState>();
        PopulateIntroducedBands(itemStates, CurriculumStage.Stage1_Addition);
        PopulateIntroducedBands(itemStates, CurriculumStage.Stage2_Subtraction);
        PopulateIntroducedBands(itemStates, CurriculumStage.Stage3_Multiplication);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage1_Addition,
            itemStates,
            hasBroadWeakness: false);

        Assert.Equal(CurriculumStage.Stage2_Subtraction, nextStage);
    }

    [Fact]
    public void Contract24_Stage2_CannotSkipDirectlyToStage4()
    {
        // Even if SUB and MUL are all fully practiced with zero errors:
        var itemStates = new Dictionary<string, ItemLearningState>();
        PopulateIntroducedBands(itemStates, CurriculumStage.Stage2_Subtraction);
        PopulateIntroducedBands(itemStates, CurriculumStage.Stage3_Multiplication);

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage2_Subtraction,
            itemStates,
            hasBroadWeakness: false);

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

    #region Helpers

    private static Dictionary<string, ItemLearningState> CreateFullyIntroducedItemStates(
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

    private static void PopulateIntroducedBands(
        Dictionary<string, ItemLearningState> dict,
        CurriculumStage stage)
    {
        var factIds = CurriculumUnlockPolicy.GetPrerequisiteFactIds(stage);
        foreach (var factId in factIds)
        {
            if (!dict.ContainsKey(factId))
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
                dict[factId] = CreateItemState(op, left, right, totalAttempts: 1, needsRemediation: false);
            }
        }
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

    #endregion
}
