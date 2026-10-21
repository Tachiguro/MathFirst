using System.Numerics;
using System.Reflection;
using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseDamageCompositionPolicyTests
{
    private static int Compose(int baseDamage, int critical, int overdrive)
    {
        return CyberDefenseDamageCompositionPolicy.GetEffectiveDamage(baseDamage, critical, overdrive);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(1_000_000)]
    [InlineData(int.MaxValue)]
    public void ZeroBonuses_PreserveBaseDamage(int baseDamage)
    {
        Assert.Equal(baseDamage, Compose(baseDamage, 0, 0));
    }

    [Theory]
    [InlineData(1, 50, 2)]    // GDD example: ceil(1.5) = 2
    [InlineData(3, 50, 5)]    // ceil(4.5) = 5
    [InlineData(4, 50, 6)]    // exact 6.0
    [InlineData(1, 100, 2)]   // exact 2.0
    [InlineData(5, 100, 10)]
    [InlineData(7, 25, 9)]    // ceil(8.75)
    public void CriticalBonusAlone_AppliesToBaseDamage(int baseDamage, int critical, int expected)
    {
        Assert.Equal(expected, Compose(baseDamage, critical, 0));
    }

    [Theory]
    [InlineData(1, 50, 2)]
    [InlineData(3, 50, 5)]
    [InlineData(4, 50, 6)]
    [InlineData(9, 50, 14)]   // ceil(13.5)
    public void OverdriveBonusAlone_AppliesToBaseDamage(int baseDamage, int overdrive, int expected)
    {
        Assert.Equal(expected, Compose(baseDamage, 0, overdrive));
    }

    [Theory]
    [InlineData(1, 50, 50, 2)]    // ceil(2.0)
    [InlineData(3, 100, 50, 8)]   // ceil(7.5)
    [InlineData(3, 50, 50, 6)]    // exact 6.0
    [InlineData(10, 100, 50, 25)] // exact 25.0
    [InlineData(7, 100, 50, 18)]  // ceil(17.5)
    public void BothBonuses_AreAddedBeforeSingleRounding(int baseDamage, int critical, int overdrive, int expected)
    {
        Assert.Equal(expected, Compose(baseDamage, critical, overdrive));
    }

    [Theory]
    [InlineData(1, 1, 2)]    // ceil(1.01) = 2: any positive fraction rounds up
    [InlineData(1, 99, 2)]   // ceil(1.99) = 2
    [InlineData(1, 100, 2)]  // exact boundary: no extra rounding
    [InlineData(1, 101, 3)]  // ceil(2.01) = 3
    [InlineData(100, 1, 101)]
    [InlineData(99, 1, 100)] // ceil(99.99)
    public void CeilingRounding_RoundsUpAnyFractionAndNotExactIntegers(int baseDamage, int critical, int expected)
    {
        Assert.Equal(expected, Compose(baseDamage, critical, 0));
    }

    [Theory]
    [InlineData(2, 50, 50)]
    [InlineData(4, 25, 75)]
    [InlineData(20, 10, 90)]
    [InlineData(1_000, 33, 67)]
    public void ExactIntegerResults_NeedNoAdditionalRounding(int baseDamage, int critical, int overdrive)
    {
        int percentSum = critical + overdrive;
        Assert.Equal(0, baseDamage * percentSum % 100); // guard: the scenario really is exact
        Assert.Equal(baseDamage * (100 + percentSum) / 100, Compose(baseDamage, critical, overdrive));
    }

    [Fact]
    public void Stacking_IsAdditiveAndNotMultiplicative()
    {
        // Multiplicative would give 3 * 2.0 * 1.5 = 9; additive gives 3 * 2.5 = 7.5 -> 8.
        Assert.Equal(8, Compose(3, 100, 50));
        // Multiplicative would give 10 * 1.5 * 1.5 = 22.5 -> 23; additive gives 10 * 2.0 = 20.
        Assert.Equal(20, Compose(10, 50, 50));
        // Multiplicative would give 100 * 1.5 * 1.5 = 225; additive gives 200.
        Assert.Equal(200, Compose(100, 50, 50));
    }

    [Fact]
    public void NoIntermediateRounding_IsPerformedBetweenBonuses()
    {
        // Rounding after the first bonus would give ceil(ceil(3 * 1.5) * 1.5) = ceil(5 * 1.5) = 8, not 6.
        Assert.Equal(6, Compose(3, 50, 50));
        // Rounding after the first bonus would give ceil(ceil(1 * 1.5) * 1.5) = 3, not 2.
        Assert.Equal(2, Compose(1, 50, 50));
        // Sequential ceilings would give ceil(ceil(5 * 1.1) * 1.1) = 7; single rounding gives ceil(6.0) = 6.
        Assert.Equal(6, Compose(5, 10, 10));
    }

    [Fact]
    public void BonusOrder_DoesNotMatter()
    {
        for (int baseDamage = 1; baseDamage <= 40; baseDamage++)
        {
            Assert.Equal(Compose(baseDamage, 100, 50), Compose(baseDamage, 50, 100));
            Assert.Equal(Compose(baseDamage, 13, 74), Compose(baseDamage, 74, 13));
        }
    }

    [Fact]
    public void Result_NeverFallsBelowBaseDamage_AndIsAlwaysPositive()
    {
        for (int baseDamage = 1; baseDamage <= 50; baseDamage++)
        {
            for (int critical = 0; critical <= 150; critical += 25)
            {
                for (int overdrive = 0; overdrive <= 150; overdrive += 25)
                {
                    int result = Compose(baseDamage, critical, overdrive);
                    Assert.True(result >= baseDamage);
                    Assert.True(result > 0);
                }
            }
        }
    }

    [Fact]
    public void Result_MatchesArbitraryPrecisionCeilingOracle()
    {
        for (int baseDamage = 1; baseDamage <= 60; baseDamage++)
        {
            for (int critical = 0; critical <= 210; critical += 7)
            {
                for (int overdrive = 0; overdrive <= 210; overdrive += 11)
                {
                    BigInteger numerator = (BigInteger)baseDamage * (100 + critical + overdrive);
                    BigInteger expected = (numerator + 99) / 100;
                    Assert.Equal((int)expected, Compose(baseDamage, critical, overdrive));
                }
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NonPositiveBaseDamage_IsRejected(int baseDamage)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Compose(baseDamage, 0, 0));
        Assert.Equal("baseDamage", ex.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => Compose(baseDamage, 50, 50));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NegativeCriticalBonus_IsRejected(int negative)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Compose(10, negative, 0));
        Assert.Equal("criticalBonusPercent", ex.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => Compose(10, negative, 50));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NegativeOverdriveBonus_IsRejected(int negative)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Compose(10, 0, negative));
        Assert.Equal("overdriveBonusPercent", ex.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => Compose(10, 50, negative));
    }

    [Fact]
    public void NegativeBonuses_AreNotAllowedToOffsetEachOther()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Compose(10, 50, -50));
        Assert.Throws<ArgumentOutOfRangeException>(() => Compose(10, -50, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => Compose(10, -50, -50));
    }

    [Fact]
    public void BoundaryValues_AtTheIntegerLimitAreHandledWithoutWrapping()
    {
        // The largest base damage without bonuses is preserved exactly.
        Assert.Equal(int.MaxValue, Compose(int.MaxValue, 0, 0));

        // Exact fit: 1073741823 * 2.0 = 2147483646.
        Assert.Equal(2147483646, Compose(1073741823, 50, 50));

        // One unit further: 1073741824 * 2.0 = 2147483648 exceeds int.MaxValue.
        Assert.Throws<OverflowException>(() => Compose(1073741824, 50, 50));

        // Extreme percentages on a small base stay representable and are computed exactly.
        Assert.Equal(21_474_838, Compose(1, int.MaxValue, 0));               // ceil(2147483747 / 100)
        Assert.Equal(42_949_674, Compose(1, int.MaxValue, int.MaxValue));    // ceil(4294967394 / 100)
    }

    [Theory]
    [InlineData(int.MaxValue, 1, 0)]
    [InlineData(int.MaxValue, 0, 1)]
    [InlineData(int.MaxValue, 50, 50)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    [InlineData(1_000_000_000, 100, 50)]
    [InlineData(1_000_000_000, int.MaxValue, 0)]
    [InlineData(1_000_000_000, 0, int.MaxValue)]
    public void ResultOverflow_FailsSafelyInsteadOfWrapping(int baseDamage, int critical, int overdrive)
    {
        Assert.Throws<OverflowException>(() => Compose(baseDamage, critical, overdrive));
    }

    [Fact]
    public void LargeIntermediateProducts_DoNotWrapBeforeOverflowIsDetected()
    {
        // base * (100 + c + o) is about 2^63 or more here; it must still surface as OverflowException.
        Assert.Throws<OverflowException>(() => Compose(int.MaxValue, int.MaxValue, int.MaxValue));
        Assert.Throws<OverflowException>(() => Compose(int.MaxValue - 1, int.MaxValue, int.MaxValue));
    }

    [Fact]
    public void Calculations_AreDeterministicAcrossRepeatedCalls()
    {
        int first = Compose(7, 100, 50);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(first, Compose(7, 100, 50));
        }

        Assert.Equal(18, first);
    }

    [Fact]
    public void PublicSurface_IsStatelessAndTimingFree()
    {
        Type type = typeof(CyberDefenseDamageCompositionPolicy);
        Assert.True(type.IsAbstract && type.IsSealed, "Policy must be a static class.");

        // No hidden mutable state.
        Assert.DoesNotContain(
            type.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            f => !f.IsLiteral);
        Assert.Empty(type.GetProperties(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

        // Only integer inputs and results: no time, latency, duration or floating-point values.
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            Assert.Equal(typeof(int), method.ReturnType);
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                Assert.Equal(typeof(int), parameter.ParameterType);
            }
        }
    }
}
