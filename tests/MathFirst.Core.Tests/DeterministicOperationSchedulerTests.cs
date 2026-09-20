namespace MathFirst.Core.Tests;

using MathFirst.Application.Practice;
using MathFirst.Domain;
using Xunit;

public sealed class DeterministicOperationSchedulerTests
{
    private static readonly ArithmeticOperation[] AllFour =
    [
        ArithmeticOperation.Addition,
        ArithmeticOperation.Subtraction,
        ArithmeticOperation.Multiplication,
        ArithmeticOperation.Division
    ];

    [Fact]
    public void Determinism_SamePositionAndEnabledSet_AlwaysReturnsSameOperation()
    {
        for (var p = 1L; p <= 100; p++)
        {
            var op1 = DeterministicOperationScheduler.GetScheduledOperation(p, AllFour);
            var op2 = DeterministicOperationScheduler.GetScheduledOperation(p, AllFour);
            Assert.Equal(op1, op2);
        }
    }

    [Fact]
    public void InputOrderIndependence_DifferentInputOrder_ProducesIdenticalSchedule()
    {
        var permutation1 = new[] { ArithmeticOperation.Multiplication, ArithmeticOperation.Addition, ArithmeticOperation.Division };
        var permutation2 = new[] { ArithmeticOperation.Division, ArithmeticOperation.Addition, ArithmeticOperation.Multiplication };

        for (var p = 1L; p <= 60; p++)
        {
            var op1 = DeterministicOperationScheduler.GetScheduledOperation(p, permutation1);
            var op2 = DeterministicOperationScheduler.GetScheduledOperation(p, permutation2);
            Assert.Equal(op1, op2);
        }
    }

    [Fact]
    public void DisabledOperations_NeverEmitted()
    {
        var enabled = new[] { ArithmeticOperation.Addition, ArithmeticOperation.Division };
        var disabled = new[] { ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication };

        for (var p = 1L; p <= 200; p++)
        {
            var op = DeterministicOperationScheduler.GetScheduledOperation(p, enabled);
            Assert.Contains(op, enabled);
            Assert.DoesNotContain(op, disabled);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ExactBagFairness_EachConsecutiveBagContainsEachEnabledOperationExactlyOnce(int k)
    {
        var enabled = AllFour.Take(k).ToArray();
        const int bagCount = 100;

        for (var bagIndex = 0; bagIndex < bagCount; bagIndex++)
        {
            var bagOperations = new List<ArithmeticOperation>(k);
            for (var slot = 0; slot < k; slot++)
            {
                var position = (bagIndex * (long)k) + slot + 1;
                var op = DeterministicOperationScheduler.GetScheduledOperation(position, enabled);
                bagOperations.Add(op);
            }

            Assert.Equal(k, bagOperations.Count);
            Assert.Equal(k, bagOperations.Distinct().Count());
            Assert.All(enabled, op => Assert.Contains(op, bagOperations));
        }
    }

    [Fact]
    public void SingleOperation_AlwaysEmitsThatOperation()
    {
        foreach (var op in AllFour)
        {
            var enabled = new[] { op };
            for (var p = 1L; p <= 50; p++)
            {
                Assert.Equal(op, DeterministicOperationScheduler.GetScheduledOperation(p, enabled));
            }
        }
    }

    [Fact]
    public void TwoOperationAntiRoundRobin_AllPairs_VaryBagOrderAndContainBoundaryAdjacency()
    {
        for (var i = 0; i < AllFour.Length; i++)
        {
            for (var j = i + 1; j < AllFour.Length; j++)
            {
                var pair = new[] { AllFour[i], AllFour[j] };
                var sequence = new List<ArithmeticOperation>(200);

                for (var p = 1L; p <= 200; p++)
                {
                    sequence.Add(DeterministicOperationScheduler.GetScheduledOperation(p, pair));
                }

                // 1. Every 2-position bag contains both operations exactly once
                for (var bag = 0; bag < 100; bag++)
                {
                    var bagOps = sequence.Skip(bag * 2).Take(2).ToArray();
                    Assert.Contains(pair[0], bagOps);
                    Assert.Contains(pair[1], bagOps);
                }

                // 2. Sequence is not purely canonical ABABAB...
                var canonicalAb = Enumerable.Range(0, 100).SelectMany(_ => pair).ToArray();
                Assert.NotEqual(canonicalAb, sequence);

                // 3. Sequence is not purely reverse BABABA...
                var reverseBa = Enumerable.Range(0, 100).SelectMany(_ => new[] { pair[1], pair[0] }).ToArray();
                Assert.NotEqual(reverseBa, sequence);

                // 4. Same-operation bag-boundary adjacency occurs (e.g., ... B | B ...)
                var foundBoundaryAdjacency = false;
                for (var bag = 0; bag < 99; bag++)
                {
                    var lastOfCurrentBag = sequence[(bag * 2) + 1];
                    var firstOfNextBag = sequence[(bag * 2) + 2];
                    if (lastOfCurrentBag == firstOfNextBag)
                    {
                        foundBoundaryAdjacency = true;
                        break;
                    }
                }

                Assert.True(
                    foundBoundaryAdjacency,
                    $"Pair {pair[0]} and {pair[1]} must produce at least one same-operation bag boundary adjacency.");
            }
        }
    }

    [Fact]
    public void ThreeAndFourOperationDiversity_ProducesMultiplePermutationsAcrossBags()
    {
        // 3 operations
        var threeOps = new[] { ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication };
        var threePermutations = new HashSet<string>(StringComparer.Ordinal);
        for (var bag = 0; bag < 50; bag++)
        {
            var ops = Enumerable.Range(0, 3)
                .Select(s => DeterministicOperationScheduler.GetScheduledOperation((bag * 3L) + s + 1, threeOps));
            threePermutations.Add(string.Join("-", ops));
        }
        Assert.True(threePermutations.Count > 1, "3-operation scheduler must produce more than 1 bag permutation.");

        // 4 operations
        var fourPermutations = new HashSet<string>(StringComparer.Ordinal);
        for (var bag = 0; bag < 50; bag++)
        {
            var ops = Enumerable.Range(0, 4)
                .Select(s => DeterministicOperationScheduler.GetScheduledOperation((bag * 4L) + s + 1, AllFour));
            fourPermutations.Add(string.Join("-", ops));
        }
        Assert.True(fourPermutations.Count > 1, "4-operation scheduler must produce more than 1 bag permutation.");
    }

    [Fact]
    public void RestartEquivalence_RecomputingFromScratchReproducesExactSequence()
    {
        var run1 = Enumerable.Range(1, 120)
            .Select(p => DeterministicOperationScheduler.GetScheduledOperation(p, AllFour))
            .ToArray();
        var run2 = Enumerable.Range(1, 120)
            .Select(p => DeterministicOperationScheduler.GetScheduledOperation(p, AllFour))
            .ToArray();

        Assert.Equal(run1, run2);
    }

    [Fact]
    public void RoleSchedulingPreservation_TenAttemptRoleCycleUnchanged()
    {
        for (var p = 1L; p <= 80; p++)
        {
            var expectedOrdinal = ((p - 1) / 4) + 1;
            var role = AdaptivePracticeSelector.GetRequestedRole(expectedOrdinal);
            var expectedRole = ((expectedOrdinal - 1) % 10) switch
            {
                0 => PracticeSelectionRole.New,
                1 => PracticeSelectionRole.Due,
                2 => PracticeSelectionRole.New,
                3 => PracticeSelectionRole.Maintenance,
                4 => PracticeSelectionRole.Frontier,
                5 => PracticeSelectionRole.New,
                6 => PracticeSelectionRole.Due,
                7 => PracticeSelectionRole.New,
                8 => PracticeSelectionRole.Due,
                9 => PracticeSelectionRole.Frontier,
                _ => throw new InvalidOperationException()
            };

            Assert.Equal(expectedRole, role);
        }
    }

    [Fact]
    public void ArgumentValidation_ThrowsOnNonPositivePositionOrNegativeBagIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeterministicOperationScheduler.GetScheduledOperation(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeterministicOperationScheduler.GetScheduledOperation(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeterministicOperationScheduler.OrderBag(AllFour, -1));
        Assert.Throws<ArgumentNullException>(() => DeterministicOperationScheduler.OrderBag(null!, 0));
    }
}
