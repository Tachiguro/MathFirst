namespace MathFirst.Core.Tests;

using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class PracticeSequenceDiversityTests
{
    [Fact]
    public void IsLadderContinuation_DetectsAdditionLadders()
    {
        var prev1 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 9);
        var ladderRight = new ArithmeticFact(ArithmeticOperation.Addition, 1, 9);
        var nonLadder = new ArithmeticFact(ArithmeticOperation.Addition, 5, 4);

        Assert.True(AdaptivePracticeSelector.IsLadderContinuation(ladderRight, prev1));
        Assert.False(AdaptivePracticeSelector.IsLadderContinuation(nonLadder, prev1));

        var prev2 = new ArithmeticFact(ArithmeticOperation.Addition, 9, 0);
        var ladderLeft = new ArithmeticFact(ArithmeticOperation.Addition, 9, 1);
        Assert.True(AdaptivePracticeSelector.IsLadderContinuation(ladderLeft, prev2));
    }

    [Fact]
    public void IsLadderContinuation_DetectsMultiplicationLadders()
    {
        var prev = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 7);
        var ladder = new ArithmeticFact(ArithmeticOperation.Multiplication, 3, 7);
        var nonLadder = new ArithmeticFact(ArithmeticOperation.Multiplication, 5, 6);

        Assert.True(AdaptivePracticeSelector.IsLadderContinuation(ladder, prev));
        Assert.False(AdaptivePracticeSelector.IsLadderContinuation(nonLadder, prev));
    }

    [Fact]
    public void IsLadderContinuation_DetectsSubtractionLadders()
    {
        var prev = new ArithmeticFact(ArithmeticOperation.Subtraction, 9, 0);
        var ladder = new ArithmeticFact(ArithmeticOperation.Subtraction, 9, 1);
        var nonLadder = new ArithmeticFact(ArithmeticOperation.Subtraction, 5, 2);

        Assert.True(AdaptivePracticeSelector.IsLadderContinuation(ladder, prev));
        Assert.False(AdaptivePracticeSelector.IsLadderContinuation(nonLadder, prev));
    }

    [Fact]
    public void IsLadderContinuation_DetectsDivisionLadders()
    {
        var prev = new ArithmeticFact(ArithmeticOperation.Division, 18, 3); // result 6
        var ladder1 = new ArithmeticFact(ArithmeticOperation.Division, 21, 3); // result 7, same divisor 3
        var ladder2 = new ArithmeticFact(ArithmeticOperation.Division, 15, 3); // result 5, same divisor 3
        var ladder3 = new ArithmeticFact(ArithmeticOperation.Division, 18, 2); // same dividend 18, adjacent divisor 2
        var ladder4 = new ArithmeticFact(ArithmeticOperation.Division, 24, 4); // same result 6, adjacent divisor 4
        var nonLadder = new ArithmeticFact(ArithmeticOperation.Division, 40, 8); // result 5, divisor 8

        Assert.True(AdaptivePracticeSelector.IsLadderContinuation(ladder1, prev));
        Assert.True(AdaptivePracticeSelector.IsLadderContinuation(ladder2, prev));
        Assert.True(AdaptivePracticeSelector.IsLadderContinuation(ladder3, prev));
        Assert.True(AdaptivePracticeSelector.IsLadderContinuation(ladder4, prev));
        Assert.False(AdaptivePracticeSelector.IsLadderContinuation(nonLadder, prev));
    }

    [Fact]
    public void DeterministicSelection_IsReproducibleAcrossCalls()
    {
        var band = new CurriculumBandId("ADD-D09");
        var facts = new ArithmeticCurriculum().Addition.Bands[8].Frontier;
        var recent = new[] { new ArithmeticFact(ArithmeticOperation.Addition, 0, 9) };

        var first = AdaptivePracticeSelector.SelectTargetCandidate(
            facts,
            recent,
            ArithmeticOperation.Addition,
            band,
            PracticeSelectionRole.New,
            10);

        var second = AdaptivePracticeSelector.SelectTargetCandidate(
            facts,
            recent,
            ArithmeticOperation.Addition,
            band,
            PracticeSelectionRole.New,
            10);

        Assert.Equal(first.Fact.Id, second.Fact.Id);
        Assert.Equal(first.Relaxation, second.Relaxation);
    }

    [Fact]
    public void SourceOrderIndependence_ReversedOrShuffledPoolProducesIdenticalSelection()
    {
        var band = new CurriculumBandId("ADD-D09");
        var facts = new ArithmeticCurriculum().Addition.Bands[8].Frontier;
        var reversedFacts = facts.Reverse().ToArray();
        var recent = new[] { new ArithmeticFact(ArithmeticOperation.Addition, 0, 9) };

        var fromOriginal = AdaptivePracticeSelector.SelectTargetCandidate(
            facts,
            recent,
            ArithmeticOperation.Addition,
            band,
            PracticeSelectionRole.New,
            42);

        var fromReversed = AdaptivePracticeSelector.SelectTargetCandidate(
            reversedFacts,
            recent,
            ArithmeticOperation.Addition,
            band,
            PracticeSelectionRole.New,
            42);

        Assert.Equal(fromOriginal.Fact.Id, fromReversed.Fact.Id);
        Assert.Equal(fromOriginal.Relaxation, fromReversed.Relaxation);
    }

    [Fact]
    public void AdditionRegression_DoesNotWalkMonotonicLadderWhenAlternativesExist()
    {
        var band = new CurriculumBandId("ADD-D09");
        var facts = new ArithmeticCurriculum().Addition.Bands[8].Frontier;
        var prev = new ArithmeticFact(ArithmeticOperation.Addition, 0, 9);
        var recent = new[] { prev };

        // Candidates include 1+9 (ladder) and other non-ladder facts like 2+9, 3+9, 9+1 etc.
        var selected = AdaptivePracticeSelector.SelectTargetCandidate(
            facts,
            recent,
            ArithmeticOperation.Addition,
            band,
            PracticeSelectionRole.New,
            1);

        // Selected fact must NOT be 1+9 (direct ladder continuation of 0+9)
        Assert.NotEqual("add:1+9", selected.Fact.Id);
        Assert.NotEqual("add:0+9", selected.Fact.Id); // Exact cooldown
        Assert.NotEqual("add:9+0", selected.Fact.Id); // Mirror cooldown
        Assert.Equal(PracticeCooldownRelaxation.None, selected.Relaxation);
    }

    [Fact]
    public void MultiplicationRegression_DoesNotWalkMonotonicLadderWhenAlternativesExist()
    {
        var band = new CurriculumBandId("MUL-D07");
        var facts = new ArithmeticCurriculum().Multiplication.Bands[6].Frontier;
        var prev = new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 7);
        var recent = new[] { prev };

        var selected = AdaptivePracticeSelector.SelectTargetCandidate(
            facts,
            recent,
            ArithmeticOperation.Multiplication,
            band,
            PracticeSelectionRole.New,
            1);

        // 3*7 and 1*7 are ladders from 2*7
        Assert.NotEqual("mul:3*7", selected.Fact.Id);
        Assert.NotEqual("mul:1*7", selected.Fact.Id);
        Assert.NotEqual("mul:2*7", selected.Fact.Id); // Exact cooldown
        Assert.NotEqual("mul:7*2", selected.Fact.Id); // Mirror cooldown
        Assert.Equal(PracticeCooldownRelaxation.None, selected.Relaxation);
    }

    [Fact]
    public void SubtractionRegression_DoesNotWalkMonotonicLadderWhenAlternativesExist()
    {
        var band = new CurriculumBandId("SUB-D10");
        var facts = new ArithmeticCurriculum().Subtraction.Bands[9].Frontier;
        var prev = new ArithmeticFact(ArithmeticOperation.Subtraction, 9, 0);
        var recent = new[] { prev };

        var selected = AdaptivePracticeSelector.SelectTargetCandidate(
            facts,
            recent,
            ArithmeticOperation.Subtraction,
            band,
            PracticeSelectionRole.New,
            1);

        Assert.NotEqual("sub:9-1", selected.Fact.Id);
        Assert.NotEqual("sub:9-0", selected.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.None, selected.Relaxation);
    }

    [Fact]
    public void DivisionRegression_DoesNotWalkMonotonicLadderWhenAlternativesExist()
    {
        var band = new CurriculumBandId("DIV-D03");
        var facts = new ArithmeticCurriculum().Division.Bands[2].Frontier;
        var prev = new ArithmeticFact(ArithmeticOperation.Division, 18, 3); // result 6
        var recent = new[] { prev };

        var selected = AdaptivePracticeSelector.SelectTargetCandidate(
            facts,
            recent,
            ArithmeticOperation.Division,
            band,
            PracticeSelectionRole.New,
            1);

        Assert.NotEqual("div:21/3", selected.Fact.Id); // result 7
        Assert.NotEqual("div:15/3", selected.Fact.Id); // result 5
        Assert.NotEqual("div:18/3", selected.Fact.Id); // exact cooldown
        Assert.Equal(PracticeCooldownRelaxation.None, selected.Relaxation);
    }

    [Fact]
    public void SmallPoolLiveness_AntiLadderRelaxesWhenOnlyLadderCandidatesExist()
    {
        var band = new CurriculumBandId("ADD-D01");
        // Pool containing only 1+9
        var facts = new[] { new ArithmeticFact(ArithmeticOperation.Addition, 1, 9) };
        var prev = new ArithmeticFact(ArithmeticOperation.Addition, 0, 9);
        var recent = new[] { prev };

        var selected = AdaptivePracticeSelector.SelectTargetCandidate(
            facts,
            recent,
            ArithmeticOperation.Addition,
            band,
            PracticeSelectionRole.New,
            1);

        Assert.Equal("add:1+9", selected.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.None, selected.Relaxation);
    }

    [Fact]
    public void SmallPoolLiveness_MirrorRelaxesWhenOnlyMirrorCandidatesExist()
    {
        var band = new CurriculumBandId("ADD-D01");
        // Pool containing only 9+0, while 0+9 was recent
        var facts = new[] { new ArithmeticFact(ArithmeticOperation.Addition, 9, 0) };
        var prev = new ArithmeticFact(ArithmeticOperation.Addition, 0, 9);
        var recent = new[] { prev };

        var selected = AdaptivePracticeSelector.SelectTargetCandidate(
            facts,
            recent,
            ArithmeticOperation.Addition,
            band,
            PracticeSelectionRole.New,
            1);

        Assert.Equal("add:9+0", selected.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.Mirror, selected.Relaxation);
    }

    [Fact]
    public void SmallPoolLiveness_ExactRelaxesWhenOnlyExactSameCandidateExists()
    {
        var band = new CurriculumBandId("ADD-D01");
        // Pool containing only 0+9, while 0+9 was recent
        var facts = new[] { new ArithmeticFact(ArithmeticOperation.Addition, 0, 9) };
        var prev = new ArithmeticFact(ArithmeticOperation.Addition, 0, 9);
        var recent = new[] { prev };

        var selected = AdaptivePracticeSelector.SelectTargetCandidate(
            facts,
            recent,
            ArithmeticOperation.Addition,
            band,
            PracticeSelectionRole.New,
            1);

        Assert.Equal("add:0+9", selected.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.Exact, selected.Relaxation);
    }

    [Fact]
    public void MirrorCooldown_UsesMirrorFactCooldownDistancePolicyWindow()
    {
        var band = new CurriculumBandId("ADD-D01");
        var forward = new ArithmeticFact(ArithmeticOperation.Addition, 2, 5);
        var mirror = new ArithmeticFact(ArithmeticOperation.Addition, 5, 2);
        var other1 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var other2 = new ArithmeticFact(ArithmeticOperation.Addition, 1, 1);
        var other3 = new ArithmeticFact(ArithmeticOperation.Addition, 3, 3);

        // When forward (2+5) is within MirrorFactCooldownDistance (e.g. 3 positions back):
        // history: [forward, other1, other2] -> mirror (5+2) must be excluded if non-mirror candidate exists
        var recentInside = new[] { forward, other1, other2 };
        var pool = new[] { mirror, other3 };

        var selectedInside = AdaptivePracticeSelector.SelectTargetCandidate(
            pool,
            recentInside,
            ArithmeticOperation.Addition,
            band,
            PracticeSelectionRole.Due,
            10);

        Assert.Equal("add:3+3", selectedInside.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.None, selectedInside.Relaxation);

        // When forward (2+5) is outside MirrorFactCooldownDistance (4 positions back):
        // history: [forward, other1, other2, other3] -> mirror (5+2) is eligible
        var recentOutside = new[] { forward, other1, other2, other3 };
        var selectedOutside = AdaptivePracticeSelector.SelectTargetCandidate(
            new[] { mirror },
            recentOutside,
            ArithmeticOperation.Addition,
            band,
            PracticeSelectionRole.Due,
            10);

        Assert.Equal("add:5+2", selectedOutside.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.None, selectedOutside.Relaxation);
    }
}