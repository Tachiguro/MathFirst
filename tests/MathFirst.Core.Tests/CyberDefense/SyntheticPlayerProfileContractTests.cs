namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Linq;
using System.Reflection;
using MathFirst.Core.Tests.CyberDefense.Simulator;
using MathFirst.Domain.CyberDefense;
using Xunit;

/// <summary>
/// Focused contract and behavioral tests for MF-CYBER-004-SLICE-3:
/// Synthetic Player Profiles A-E and Deterministic Attempt Sampling.
/// </summary>
public class SyntheticPlayerProfileContractTests
{
    // =========================================================================
    // 1. CANONICAL PROFILE DEFINITION CONTRACTS (P1 - P5)
    // =========================================================================

    [Fact]
    public void P1_ExactlyFiveDistinctCanonicalProfilesExist()
    {
        var profiles = SyntheticPlayerProfiles.All;
        Assert.NotNull(profiles);
        Assert.Equal(5, profiles.Count);
        Assert.Equal(5, profiles.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void P2_AllFiveIdsAndNamesAreStable()
    {
        Assert.Equal("ProfileA_Perfect", SyntheticPlayerProfiles.ProfileA_Perfect.Id);
        Assert.Equal("ProfileB_Expert", SyntheticPlayerProfiles.ProfileB_Expert.Id);
        Assert.Equal("ProfileC_Average", SyntheticPlayerProfiles.ProfileC_Average.Id);
        Assert.Equal("ProfileD_Learner", SyntheticPlayerProfiles.ProfileD_Learner.Id);
        Assert.Equal("ProfileE_Beginner", SyntheticPlayerProfiles.ProfileE_Beginner.Id);

        foreach (var profile in SyntheticPlayerProfiles.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(profile.Id));
            Assert.False(string.IsNullOrWhiteSpace(profile.Name));
        }

        Assert.Equal(5, SyntheticPlayerProfiles.All.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void P3_AccuracyProbabilitiesEqualExpectedPresets()
    {
        Assert.Equal(1.00, SyntheticPlayerProfiles.ProfileA_Perfect.AccuracyProbability);
        Assert.Equal(0.98, SyntheticPlayerProfiles.ProfileB_Expert.AccuracyProbability);
        Assert.Equal(0.85, SyntheticPlayerProfiles.ProfileC_Average.AccuracyProbability);
        Assert.Equal(0.70, SyntheticPlayerProfiles.ProfileD_Learner.AccuracyProbability);
        Assert.Equal(0.50, SyntheticPlayerProfiles.ProfileE_Beginner.AccuracyProbability);
    }

    [Fact]
    public void P4_UpgradeIntentMetadataMatchesGddAndIsNotExecutableInSlice3()
    {
        Assert.Equal(SyntheticUpgradePreference.None, SyntheticPlayerProfiles.ProfileA_Perfect.UpgradePreference);
        Assert.Equal(SyntheticUpgradePreference.AttackFocused, SyntheticPlayerProfiles.ProfileB_Expert.UpgradePreference);
        Assert.Equal(SyntheticUpgradePreference.Balanced, SyntheticPlayerProfiles.ProfileC_Average.UpgradePreference);
        Assert.Equal(SyntheticUpgradePreference.Defensive, SyntheticPlayerProfiles.ProfileD_Learner.UpgradePreference);
        Assert.Equal(SyntheticUpgradePreference.UnupgradedBaseline, SyntheticPlayerProfiles.ProfileE_Beginner.UpgradePreference);

        foreach (var profile in SyntheticPlayerProfiles.All)
        {
            Assert.False(profile.IsUpgradeStrategyExecutable, $"{profile.Id} must not report executable upgrade strategy in Slice 3.");
        }
    }

    [Fact]
    public void P5_AllProfilesUseBaselineAttackDamageOne()
    {
        foreach (var profile in SyntheticPlayerProfiles.All)
        {
            Assert.Equal(1, profile.EffectiveAttackDamage);
        }
    }

    // =========================================================================
    // 2. ATTEMPT SAMPLING & CORRECTNESS CONTRACTS (P6 - P12)
    // =========================================================================

    [Fact]
    public void P6_ProfileAAlwaysProducesCorrectAnswers()
    {
        var sampler = new SyntheticAttemptSampler(SyntheticPlayerProfiles.ProfileA_Perfect, masterSeed: 42UL);

        for (int i = 0; i < 500; i++)
        {
            var attempt = sampler.SampleAttempt();
            Assert.True(attempt.IsCorrect, $"Attempt {i} for Profile A was not correct.");
        }
    }

    [Fact]
    public void P7_AccuracyProbabilityZeroAlwaysProducesIncorrectAnswers()
    {
        var zeroAccuracyProfile = new SyntheticPlayerProfile(
            id: "ZeroAccuracy_Test",
            name: "Zero Accuracy Profile",
            accuracyProbability: 0.0,
            minSyntheticLatencyMilliseconds: 1000,
            maxSyntheticLatencyMilliseconds: 2000);

        var sampler = new SyntheticAttemptSampler(zeroAccuracyProfile, masterSeed: 12345UL);

        for (int i = 0; i < 500; i++)
        {
            var attempt = sampler.SampleAttempt();
            Assert.False(attempt.IsCorrect, $"Attempt {i} for zero accuracy profile was unexpectedly correct.");
        }
    }

    [Theory]
    [InlineData("ProfileB_Expert")]
    [InlineData("ProfileC_Average")]
    [InlineData("ProfileD_Learner")]
    [InlineData("ProfileE_Beginner")]
    public void P8_ProfilesBThroughE_ProduceReproducibleCorrectnessSequences(string profileId)
    {
        var profile = SyntheticPlayerProfiles.All.First(p => p.Id == profileId);
        const ulong masterSeed = 999988887777UL;

        var sampler1 = new SyntheticAttemptSampler(profile, masterSeed);
        var sampler2 = new SyntheticAttemptSampler(profile, masterSeed);

        for (int i = 0; i < 200; i++)
        {
            bool correct1 = sampler1.SampleCorrectness();
            bool correct2 = sampler2.SampleCorrectness();
            Assert.Equal(correct1, correct2);
        }
    }

    [Fact]
    public void P9_IndependentSamplersWithIdenticalInputs_ProduceIdenticalAttemptSequences()
    {
        var profile = SyntheticPlayerProfiles.ProfileC_Average;
        const ulong seed = 887766554433UL;

        var sampler1 = new SyntheticAttemptSampler(profile, seed);
        var sampler2 = new SyntheticAttemptSampler(profile, seed);

        for (int i = 0; i < 150; i++)
        {
            var r1 = sampler1.SampleAttempt();
            var r2 = sampler2.SampleAttempt();

            Assert.Equal(r1.IsCorrect, r2.IsCorrect);
            Assert.Equal(r1.SyntheticLatencyMilliseconds, r2.SyntheticLatencyMilliseconds);
        }
    }

    [Fact]
    public void P10_DifferentSeedsProduceValidReproducibleSequences()
    {
        var profile = SyntheticPlayerProfiles.ProfileC_Average;
        ulong seed1 = 11111UL;
        ulong seed2 = 22222UL;

        var sampler1A = new SyntheticAttemptSampler(profile, seed1);
        var sampler1B = new SyntheticAttemptSampler(profile, seed1);
        var sampler2 = new SyntheticAttemptSampler(profile, seed2);

        var list1A = Enumerable.Range(0, 100).Select(_ => sampler1A.SampleAttempt()).ToList();
        var list1B = Enumerable.Range(0, 100).Select(_ => sampler1B.SampleAttempt()).ToList();
        var list2 = Enumerable.Range(0, 100).Select(_ => sampler2.SampleAttempt()).ToList();

        Assert.Equal(list1A, list1B);

        bool anyDifference = list1A.Zip(list2).Any(pair => pair.First != pair.Second);
        Assert.True(anyDifference, "Different seeds unexpectedly produced identical sequences.");
    }

    [Fact]
    public void P11_ChangingLatencyConfiguration_DoesNotAlterCorrectnessOutcomes()
    {
        const ulong seed = 543216789UL;

        var profileNormal = new SyntheticPlayerProfile(
            "Test_Normal", "Normal", accuracyProbability: 0.85,
            minSyntheticLatencyMilliseconds: 2000, maxSyntheticLatencyMilliseconds: 4500);

        var profileDifferentLatency = new SyntheticPlayerProfile(
            "Test_DifferentLatency", "Diff Latency", accuracyProbability: 0.85,
            minSyntheticLatencyMilliseconds: 8000, maxSyntheticLatencyMilliseconds: 15000);

        var samplerNormal = new SyntheticAttemptSampler(profileNormal, seed);
        var samplerDiffLatency = new SyntheticAttemptSampler(profileDifferentLatency, seed);

        for (int i = 0; i < 200; i++)
        {
            var resNormal = samplerNormal.SampleAttempt();
            var resDiffLatency = samplerDiffLatency.SampleAttempt();

            Assert.Equal(resNormal.IsCorrect, resDiffLatency.IsCorrect);
        }
    }

    [Fact]
    public void P12_CorrectnessSampling_IsUnaffectedByAdvancingLatencyStream()
    {
        const ulong seed = 7788990011UL;
        var profile = SyntheticPlayerProfiles.ProfileD_Learner;

        var samplerOnlyCorrectness = new SyntheticAttemptSampler(profile, seed);
        var samplerWithInterleavedLatency = new SyntheticAttemptSampler(profile, seed);

        for (int i = 0; i < 100; i++)
        {
            samplerWithInterleavedLatency.SampleLatency();
            samplerWithInterleavedLatency.SampleLatency();
            samplerWithInterleavedLatency.LatencyPrng.NextUInt64();

            bool c1 = samplerOnlyCorrectness.SampleCorrectness();
            bool c2 = samplerWithInterleavedLatency.SampleCorrectness();

            Assert.Equal(c1, c2);
        }
    }

    // =========================================================================
    // 3. LATENCY SAMPLING & DISTRIBUTIONS (P13 - P16)
    // =========================================================================

    [Theory]
    [InlineData("ProfileA_Perfect", 1200, 1200)]
    [InlineData("ProfileB_Expert", 1000, 2000)]
    [InlineData("ProfileC_Average", 2000, 4500)]
    [InlineData("ProfileD_Learner", 3000, 6000)]
    [InlineData("ProfileE_Beginner", 4000, 8000)]
    public void P13_SampledLatenciesRemainInConfiguredProfileBounds(
        string profileId,
        long expectedMin,
        long expectedMax)
    {
        var profile = SyntheticPlayerProfiles.All.First(p => p.Id == profileId);
        Assert.Equal(expectedMin, profile.MinSyntheticLatencyMilliseconds);
        Assert.Equal(expectedMax, profile.MaxSyntheticLatencyMilliseconds);

        var sampler = new SyntheticAttemptSampler(profile, masterSeed: 334455UL);

        for (int i = 0; i < 500; i++)
        {
            long latency = sampler.SampleLatency();
            Assert.InRange(latency, expectedMin, expectedMax);
        }
    }

    [Fact]
    public void P14_ProfileALatencyIsAlways1200ms()
    {
        var sampler = new SyntheticAttemptSampler(SyntheticPlayerProfiles.ProfileA_Perfect, masterSeed: 98765UL);

        for (int i = 0; i < 300; i++)
        {
            var attempt = sampler.SampleAttempt();
            Assert.Equal(1200, attempt.SyntheticLatencyMilliseconds);
            Assert.Equal(1200, attempt.LatencyMilliseconds);
        }
    }

    [Fact]
    public void P15_VariableLatencyDistributionsProduceDeterministicSequences()
    {
        var profile = SyntheticPlayerProfiles.ProfileB_Expert;
        const ulong seed = 123456789UL;

        var s1 = new SyntheticAttemptSampler(profile, seed);
        var s2 = new SyntheticAttemptSampler(profile, seed);

        for (int i = 0; i < 100; i++)
        {
            long lat1 = s1.SampleLatency();
            long lat2 = s2.SampleLatency();
            Assert.Equal(lat1, lat2);
        }
    }

    [Fact]
    public void P16_ProfileProbabilitiesBehavePlausiblyOverBoundedDeterministicSample()
    {
        const ulong seed = 42UL;
        const int samples = 10000;

        var samplerB = new SyntheticAttemptSampler(SyntheticPlayerProfiles.ProfileB_Expert, seed);
        int correctB = Enumerable.Range(0, samples).Count(_ => samplerB.SampleCorrectness());
        double rateB = (double)correctB / samples;
        Assert.InRange(rateB, 0.97, 0.99);

        var samplerC = new SyntheticAttemptSampler(SyntheticPlayerProfiles.ProfileC_Average, seed);
        int correctC = Enumerable.Range(0, samples).Count(_ => samplerC.SampleCorrectness());
        double rateC = (double)correctC / samples;
        Assert.InRange(rateC, 0.83, 0.87);

        var samplerD = new SyntheticAttemptSampler(SyntheticPlayerProfiles.ProfileD_Learner, seed);
        int correctD = Enumerable.Range(0, samples).Count(_ => samplerD.SampleCorrectness());
        double rateD = (double)correctD / samples;
        Assert.InRange(rateD, 0.68, 0.72);

        var samplerE = new SyntheticAttemptSampler(SyntheticPlayerProfiles.ProfileE_Beginner, seed);
        int correctE = Enumerable.Range(0, samples).Count(_ => samplerE.SampleCorrectness());
        double rateE = (double)correctE / samples;
        Assert.InRange(rateE, 0.48, 0.52);
    }

    // =========================================================================
    // 4. VALIDATION & BOUNDARY CONTRACTS (P17 - P21)
    // =========================================================================

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(-100.0)]
    [InlineData(5.0)]
    public void P17_InvalidProbabilitiesAreRejected(double invalidProb)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SyntheticPlayerProfile(
                "id", "name", invalidProb, 1000, 2000));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void P18_NanAndInfinityAreRejected(double nonFiniteProb)
    {
        Assert.Throws<ArgumentException>(() =>
            new SyntheticPlayerProfile(
                "id", "name", nonFiniteProb, 1000, 2000));
    }

    [Fact]
    public void P19_InvalidLatencyRangesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SyntheticPlayerProfile("id", "name", 0.5, minSyntheticLatencyMilliseconds: -1, maxSyntheticLatencyMilliseconds: 1000));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SyntheticPlayerProfile("id", "name", 0.5, minSyntheticLatencyMilliseconds: 2000, maxSyntheticLatencyMilliseconds: 1000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void P20_InvalidEffectiveAttackDamageIsRejected(int invalidDamage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SyntheticPlayerProfile("id", "name", 0.5, 1000, 2000, effectiveAttackDamage: invalidDamage));
    }

    [Fact]
    public void P21_NoSharedRandomStateBetweenDifferentSamplers()
    {
        var profile = SyntheticPlayerProfiles.ProfileC_Average;
        const ulong seed = 123456789UL;

        var sampler1 = new SyntheticAttemptSampler(profile, seed);

        for (int i = 0; i < 50; i++)
        {
            sampler1.SampleAttempt();
        }

        var sampler2 = new SyntheticAttemptSampler(profile, seed);
        var samplerReference = new SyntheticAttemptSampler(profile, seed);

        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(samplerReference.SampleAttempt(), sampler2.SampleAttempt());
        }
    }

    // =========================================================================
    // 5. ISOLATION & REGRESSION CONTRACTS (P22 - P24)
    // =========================================================================

    [Fact]
    public void P22_NoSystemClockOrNetworkingDependencyExists()
    {
        var types = new[]
        {
            typeof(SyntheticPlayerProfile),
            typeof(SyntheticPlayerProfiles),
            typeof(SyntheticAttemptResult),
            typeof(SyntheticAttemptSampler)
        };

        foreach (var type in types)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in methods)
            {
                Assert.DoesNotContain("System.Net", method.DeclaringType?.Namespace ?? string.Empty);
                Assert.DoesNotContain("System.Diagnostics.Stopwatch", method.ToString());
            }
        }
    }

    [Fact]
    public void P23_SimulatorDelegatesDirectlyToProductionStateMachineWithoutModifyingIt()
    {
        var run = CyberDefenseRunState.InitialRun();
        var result = HeadlessCombatSimulator.Step(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.Equal(1, result.AppliedOpponentDamage);
        Assert.Equal(0, result.ExcessOpponentDamage);
        Assert.Equal(1, result.NextState.CurrentOpponent.CurrentHp);
        Assert.False(result.IsOpponentDefeated);
    }

    [Fact]
    public void P24_Slice1AndSlice2ContractsRemainIntact()
    {
        var rng = new DeterministicPrng(0UL);
        Assert.Equal(0xE220A8397B1DCDAFUL, rng.NextUInt64());

        var clock = new VirtualSimulationClock();
        clock.Advance(1200);
        Assert.Equal(1200, clock.ElapsedMilliseconds);
    }

    [Fact]
    public void VirtualClock_CanAdvanceSampledLatency()
    {
        var sampler = new SyntheticAttemptSampler(SyntheticPlayerProfiles.ProfileA_Perfect, 12345UL);
        var clock = new VirtualSimulationClock();

        var attempt = sampler.SampleAttempt();
        clock.Advance(attempt.SyntheticLatencyMilliseconds);

        Assert.Equal(1200, clock.ElapsedMilliseconds);
    }

    [Theory]
    [InlineData(null, "Name")]
    [InlineData("", "Name")]
    [InlineData("   ", "Name")]
    [InlineData("Id", null)]
    [InlineData("Id", "")]
    [InlineData("Id", "   ")]
    public void Profile_RejectsEmptyOrWhitespaceIdOrName(string? id, string? name)
    {
        Assert.Throws<ArgumentException>(() =>
            new SyntheticPlayerProfile(id!, name!, 0.85, 1000, 2000));
    }
}
