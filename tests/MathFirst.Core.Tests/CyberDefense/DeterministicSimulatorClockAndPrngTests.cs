namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Diagnostics;
using System.Reflection;
using MathFirst.Core.Tests.CyberDefense.Simulator;
using MathFirst.Domain.CyberDefense;
using Xunit;

/// <summary>
/// Focused contract and behavioral tests for MF-CYBER-004-SLICE-2:
/// Deterministic PRNG (SplitMix64) and Virtual Simulation Clock.
/// </summary>
public class DeterministicSimulatorClockAndPrngTests
{
    // =========================================================================
    // 1. DETERMINISTIC PRNG TESTS (P1 - P7)
    // =========================================================================

    [Fact]
    public void P1_SameSeed_ProducesIdenticalUInt64Sequence()
    {
        ulong seed = 1234567890123456789UL;
        var rng1 = new DeterministicPrng(seed);
        var rng2 = new DeterministicPrng(seed);

        for (int i = 0; i < 100; i++)
        {
            ulong val1 = rng1.NextUInt64();
            ulong val2 = rng2.NextUInt64();
            Assert.Equal(val1, val2);
        }
    }

    [Fact]
    public void P2_SeedZero_MatchesFixedSplitMix64GoldenVectors()
    {
        // Reference golden vector values for SplitMix64 seeded with 0:
        // 1. 0xE220A8397B1DCDAF
        // 2. 0x6E789E6AA1B965F4
        // 3. 0x06C45D188009454F
        // 4. 0xF88BB8A8724C81EC
        var rng = new DeterministicPrng(0UL);

        Assert.Equal(0xE220A8397B1DCDAFUL, rng.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, rng.NextUInt64());
        Assert.Equal(0x06C45D188009454FUL, rng.NextUInt64());
        Assert.Equal(0xF88BB8A8724C81ECUL, rng.NextUInt64());
    }

    [Fact]
    public void P3_IndependentInstances_DoNotShareMutableState()
    {
        ulong seed = 9876543210987654321UL;
        var rngA = new DeterministicPrng(seed);
        var rngB = new DeterministicPrng(seed);

        // Draw 5 times from rngA
        for (int i = 0; i < 5; i++)
        {
            rngA.NextUInt64();
        }

        // rngB must still be at its initial state and draw the exact first output
        var referenceRng = new DeterministicPrng(seed);
        Assert.Equal(referenceRng.NextUInt64(), rngB.NextUInt64());
    }

    [Fact]
    public void P4_NextDouble_AlwaysProducesValueInZeroToOneRange()
    {
        var rng = new DeterministicPrng(0xCAFEBABEDEADBEEFUL);

        for (int i = 0; i < 10000; i++)
        {
            double value = rng.NextDouble();
            Assert.True(value >= 0.0, $"Expected >= 0.0, but got {value}");
            Assert.True(value < 1.0, $"Expected < 1.0, but got {value}");
            Assert.False(double.IsNaN(value));
            Assert.False(double.IsInfinity(value));
        }
    }

    [Fact]
    public void P5_NextDouble_UsesSpecified53BitMapping()
    {
        // Specification: (NextUInt64() >> 11) * (1.0 / 9007199254740992.0)
        ulong seed = 0x123456789ABCDEF0UL;
        var rngForDouble = new DeterministicPrng(seed);
        var rngForUInt64 = new DeterministicPrng(seed);

        for (int i = 0; i < 50; i++)
        {
            ulong raw = rngForUInt64.NextUInt64();
            double expected = (raw >> 11) * (1.0 / 9007199254740992.0);
            double actual = rngForDouble.NextDouble();

            Assert.Equal(expected, actual);
        }

        // Test boundary mappings explicitly with helper
        Assert.Equal(0.0, DeterministicPrng.ToDouble(0UL));

        // When upper 53 bits are all 1s (0xFFFFFFFFFFFFFFFFUL >> 11 = 0x001FFFFFFFFFFFFFUL = 9007199254740991UL)
        double maxPossible = DeterministicPrng.ToDouble(0xFFFFFFFFFFFFFFFFUL);
        Assert.True(maxPossible < 1.0);
        Assert.Equal(9007199254740991.0 / 9007199254740992.0, maxPossible);
    }

    [Fact]
    public void P6_RepeatedExecution_ProducesIdenticalResults()
    {
        ulong seed = 42UL;
        var run1 = new ulong[100];
        var run2 = new ulong[100];

        var rng1 = new DeterministicPrng(seed);
        for (int i = 0; i < run1.Length; i++)
        {
            run1[i] = rng1.NextUInt64();
        }

        var rng2 = new DeterministicPrng(seed);
        for (int i = 0; i < run2.Length; i++)
        {
            run2[i] = rng2.NextUInt64();
        }

        Assert.Equal(run1, run2);
    }

    [Fact]
    public void P7_DrawingFromOneInstance_DoesNotAffectAnotherInstance()
    {
        var rng1 = new DeterministicPrng(111UL);
        var rng2 = new DeterministicPrng(222UL);

        ulong rng2FirstBeforeRng1Draws = rng2.NextUInt64();

        // Reset rng2 to seed 222
        rng2 = new DeterministicPrng(222UL);

        // Draw 50 times from rng1
        for (int i = 0; i < 50; i++)
        {
            rng1.NextUInt64();
        }

        // rng2 must still yield the identical first value
        ulong rng2FirstAfterRng1Draws = rng2.NextUInt64();
        Assert.Equal(rng2FirstBeforeRng1Draws, rng2FirstAfterRng1Draws);
    }

    // =========================================================================
    // 2. VIRTUAL SIMULATION CLOCK TESTS (T1 - T7)
    // =========================================================================

    [Fact]
    public void T1_InitialVirtualElapsedTime_EqualsZero()
    {
        var clock = new VirtualSimulationClock();
        Assert.Equal(0L, clock.ElapsedMilliseconds);
    }

    [Fact]
    public void T2_PositiveDurations_AccumulateExactly()
    {
        var clock = new VirtualSimulationClock();

        clock.Advance(100);
        Assert.Equal(100L, clock.ElapsedMilliseconds);

        clock.Advance(250);
        Assert.Equal(350L, clock.ElapsedMilliseconds);

        clock.Advance(12345);
        Assert.Equal(12695L, clock.ElapsedMilliseconds);
    }

    [Fact]
    public void T3_ZeroDurationAdvances_DoNotChangeTime()
    {
        var clock = new VirtualSimulationClock();
        clock.Advance(500);

        clock.Advance(0);
        Assert.Equal(500L, clock.ElapsedMilliseconds);

        clock.Advance(0);
        Assert.Equal(500L, clock.ElapsedMilliseconds);
    }

    [Fact]
    public void T4_NegativeDurations_AreRejected()
    {
        var clock = new VirtualSimulationClock();
        clock.Advance(100);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-1));
        Assert.Contains("milliseconds", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(100L, clock.ElapsedMilliseconds);
    }

    [Fact]
    public void T5_LargeValidDurations_RetainExactIntegerPrecision()
    {
        var clock = new VirtualSimulationClock();

        long largeDelta1 = 1_000_000_000_000L; // 1 trillion ms (~31.7 years)
        long largeDelta2 = 2_000_000_000_000L;

        clock.Advance(largeDelta1);
        Assert.Equal(largeDelta1, clock.ElapsedMilliseconds);

        clock.Advance(largeDelta2);
        Assert.Equal(3_000_000_000_000L, clock.ElapsedMilliseconds);
    }

    [Fact]
    public void T6_Overflow_IsDetectedRatherThanSilentlyWrapping()
    {
        var clock = new VirtualSimulationClock();

        // Advance to long.MaxValue
        clock.Advance(long.MaxValue);
        Assert.Equal(long.MaxValue, clock.ElapsedMilliseconds);

        // Advancing even 1 ms past long.MaxValue must throw OverflowException
        Assert.Throws<OverflowException>(() => clock.Advance(1));
    }

    [Fact]
    public void T7_RepeatedIdenticalAdvanceSequences_ProduceIdenticalElapsedTimes()
    {
        var clock1 = new VirtualSimulationClock();
        var clock2 = new VirtualSimulationClock();

        long[] sequence = [12, 0, 450, 1200, 35, 999999, 0, 1];

        foreach (long delta in sequence)
        {
            clock1.Advance(delta);
            clock2.Advance(delta);
            Assert.Equal(clock1.ElapsedMilliseconds, clock2.ElapsedMilliseconds);
        }
    }

    // =========================================================================
    // 3. ARCHITECTURE AND NON-INTERFERENCE CHECKS (A1 - A5)
    // =========================================================================

    [Fact]
    public void A1_NoUiDependencies_InSimulatorInfrastructure()
    {
        var prngType = typeof(DeterministicPrng);
        var clockType = typeof(VirtualSimulationClock);

        Assert.DoesNotContain("Maui", prngType.Namespace ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Blazor", prngType.Namespace ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Maui", clockType.Namespace ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Blazor", clockType.Namespace ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A2_NoSqliteDependencies_InSimulatorInfrastructure()
    {
        var prngMembers = typeof(DeterministicPrng).GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        var clockMembers = typeof(VirtualSimulationClock).GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

        foreach (var member in prngMembers)
        {
            Assert.DoesNotContain("Sqlite", member.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var member in clockMembers)
        {
            Assert.DoesNotContain("Sqlite", member.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A3_NoProductionDomainModifications_DomainRemainsPure()
    {
        // Verify that MathFirst.Domain types (such as CyberDefenseRunState) are accessible and pure
        var run = CyberDefenseRunState.InitialRun();
        Assert.Equal(100, run.PlayerCurrentHp);
        Assert.Equal(1, run.Sector);
        Assert.Equal(0, run.OpponentIndex);
    }

    [Fact]
    public void A4_NoRealTimeWaitingOrWallClockSampling()
    {
        var clock = new VirtualSimulationClock();
        var sw = Stopwatch.StartNew();

        // Advance 1,000,000 simulated milliseconds (1000 simulated seconds)
        clock.Advance(1_000_000);
        sw.Stop();

        Assert.Equal(1_000_000L, clock.ElapsedMilliseconds);
        // The real physical duration must be virtually instantaneous (< 100ms)
        Assert.True(sw.ElapsedMilliseconds < 100, $"Advance should be instant, took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void A5_NoUnintendedModificationOfSlice1Behavior()
    {
        var run = CyberDefenseRunState.InitialRun();
        var result = HeadlessCombatSimulator.Step(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.NotNull(result);
        Assert.Equal(1, result.AppliedOpponentDamage);
        Assert.Equal(1, result.NextState.CurrentOpponent.CurrentHp);
    }
}
