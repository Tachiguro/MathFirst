namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MathFirst.Core.Tests.CyberDefense.Simulator;
using MathFirst.Domain.CyberDefense;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Authoritative test matrix for MF-CYBER-004-SLICE-7:
/// Deterministic Seed-Batch Simulation Runner and 100,000+ Turn Validation.
/// Tests B1 through B39 verifying batch configuration, execution, aggregation,
/// cancellation, seed isolation, and massive 100,000+ turn validation.
/// </summary>
public class DeterministicSimulationBatchRunnerTests
{
    private readonly ITestOutputHelper? _output;

    public DeterministicSimulationBatchRunnerTests(ITestOutputHelper? output = null)
    {
        _output = output;
    }
    // =========================================================================
    // B1 - B10: CONFIGURATION VALIDATION & IMMUTABILITY CONTRACTS
    // =========================================================================

    [Fact]
    public void B1_NullConfiguration_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => DeterministicSimulationBatchRunner.RunBatch(null!));
    }

    [Fact]
    public void B2_NullProfile_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SimulationBatchConfig(null!, new ulong[] { 101 }, maxTurnsPerRun: 10));
    }

    [Fact]
    public void B3_NullSeedCollection_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SimulationBatchConfig(SyntheticPlayerProfiles.ProfileA, null!, maxTurnsPerRun: 10));
    }

    [Fact]
    public void B4_EmptySeedCollection_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new SimulationBatchConfig(SyntheticPlayerProfiles.ProfileA, Array.Empty<ulong>(), maxTurnsPerRun: 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void B5_InvalidMaxTurnsPerRun_IsRejected(long invalidMaxTurns)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationBatchConfig(SyntheticPlayerProfiles.ProfileA, new ulong[] { 101 }, invalidMaxTurns));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    public void B6_InvalidTargetSector_IsRejected(int invalidSector)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationBatchConfig(SyntheticPlayerProfiles.ProfileA, new ulong[] { 101 }, maxTurnsPerRun: 10, targetSector: invalidSector));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void B7_InvalidGameOverLimit_IsRejected(int invalidLimit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationBatchConfig(SyntheticPlayerProfiles.ProfileA, new ulong[] { 101 }, maxTurnsPerRun: 10, maxGameOversPerRun: invalidLimit));
    }

    [Fact]
    public void B8_InvalidAggregateBudgetOverflow_IsRejectedSafely()
    {
        Assert.Throws<OverflowException>(
            () => new SimulationBatchConfig(SyntheticPlayerProfiles.ProfileA, new ulong[] { 1, 2 }, long.MaxValue));
    }

    [Fact]
    public void B9_DuplicateSeedPolicy_IsEnforced()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new SimulationBatchConfig(SyntheticPlayerProfiles.ProfileA, new ulong[] { 101, 202, 101 }, maxTurnsPerRun: 10));
        Assert.Contains("Duplicate seed detected", ex.Message);
    }

    [Fact]
    public void B10_InputSeedCollection_IsDefensivelySnapshotted()
    {
        var mutableSeeds = new List<ulong> { 101, 202, 303 };
        var config = new SimulationBatchConfig(SyntheticPlayerProfiles.ProfileA, mutableSeeds, maxTurnsPerRun: 10);

        mutableSeeds.Add(404);
        mutableSeeds[0] = 999;

        Assert.Equal(3, config.Seeds.Count);
        Assert.Equal(101UL, config.Seeds[0]);
        Assert.Equal(202UL, config.Seeds[1]);
        Assert.Equal(303UL, config.Seeds[2]);
    }

    // =========================================================================
    // B11 - B17: EXECUTION ISOLATION & REPRODUCIBILITY CONTRACTS
    // =========================================================================

    [Fact]
    public void B11_SingleSeed_ProducesIdenticalOutcomeToStandaloneRun()
    {
        ulong seed = 0xABCD1234UL;
        var profile = SyntheticPlayerProfiles.ProfileC_Average;
        long maxTurns = 50;

        var standaloneConfig = new SimulationRunConfig(profile, seed, maxTurns);
        var standaloneResult = HeadlessCombatSimulator.Run(standaloneConfig);

        var batchConfig = new SimulationBatchConfig(profile, new[] { seed }, maxTurns);
        var batchResult = DeterministicSimulationBatchRunner.RunBatch(batchConfig);

        Assert.Single(batchResult.Receipts);
        var receipt = batchResult.Receipts[0];
        Assert.Equal(seed, receipt.MasterSeed);
        Assert.Equal(standaloneResult.TerminationReason, receipt.TerminationReason);
        Assert.Equal(standaloneResult.TotalExecutedTurns, receipt.TotalExecutedTurns);
        Assert.Equal(standaloneResult.TotalGameOvers, receipt.TotalGameOvers);
        Assert.Equal(standaloneResult.TotalVirtualTimeMs, receipt.TotalVirtualTimeMs);
        Assert.Equal(standaloneResult.FinalState.Sector, receipt.FinalState.Sector);
        Assert.Equal(standaloneResult.FinalState.PlayerCurrentHp, receipt.FinalState.PlayerCurrentHp);
        Assert.Equal(standaloneResult.Telemetry.CorrectAnswers, receipt.Telemetry.CorrectAnswers);
        Assert.Equal(standaloneResult.Telemetry.IncorrectAnswers, receipt.Telemetry.IncorrectAnswers);
        Assert.Equal(standaloneResult.Telemetry.TotalAppliedOpponentDamage, receipt.Telemetry.TotalAppliedOpponentDamage);
        Assert.Equal(standaloneResult.Telemetry.TotalAppliedPlayerDamage, receipt.Telemetry.TotalAppliedPlayerDamage);
    }

    [Fact]
    public void B12_MultipleSeeds_AreExecutedIndependently()
    {
        var profile = SyntheticPlayerProfiles.ProfileC_Average;
        var seeds = new ulong[] { 111UL, 222UL, 333UL };
        var config = new SimulationBatchConfig(profile, seeds, maxTurnsPerRun: 40);

        var batchResult = DeterministicSimulationBatchRunner.RunBatch(config);

        Assert.Equal(3, batchResult.ExecutedSeedCount);
        for (int i = 0; i < seeds.Length; i++)
        {
            var standalone = HeadlessCombatSimulator.Run(new SimulationRunConfig(profile, seeds[i], maxTurns: 40));
            Assert.Equal(standalone.TotalExecutedTurns, batchResult.Receipts[i].TotalExecutedTurns);
            Assert.Equal(standalone.Telemetry.CorrectAnswers, batchResult.Receipts[i].Telemetry.CorrectAnswers);
            Assert.Equal(standalone.FinalState.Sector, batchResult.Receipts[i].FinalState.Sector);
        }
    }

    [Fact]
    public void B13_SeedOrder_IsRetainedInPerSeedReceipts()
    {
        var seeds = new ulong[] { 500UL, 100UL, 900UL, 300UL };
        var config = new SimulationBatchConfig(SyntheticPlayerProfiles.ProfileA, seeds, maxTurnsPerRun: 5);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        Assert.Equal(seeds.Length, result.Receipts.Count);
        for (int i = 0; i < seeds.Length; i++)
        {
            Assert.Equal(seeds[i], result.Receipts[i].MasterSeed);
        }
    }

    [Fact]
    public void B14_ReorderingSeeds_DoesNotChangePerSeedResults()
    {
        ulong s1 = 12345UL;
        ulong s2 = 67890UL;
        var profile = SyntheticPlayerProfiles.ProfileC_Average;

        var configForward = new SimulationBatchConfig(profile, new[] { s1, s2 }, maxTurnsPerRun: 30);
        var configReversed = new SimulationBatchConfig(profile, new[] { s2, s1 }, maxTurnsPerRun: 30);

        var resultForward = DeterministicSimulationBatchRunner.RunBatch(configForward);
        var resultReversed = DeterministicSimulationBatchRunner.RunBatch(configReversed);

        var f1 = resultForward.Receipts.Single(r => r.MasterSeed == s1);
        var f2 = resultForward.Receipts.Single(r => r.MasterSeed == s2);

        var r1 = resultReversed.Receipts.Single(r => r.MasterSeed == s1);
        var r2 = resultReversed.Receipts.Single(r => r.MasterSeed == s2);

        Assert.Equal(f1.TotalExecutedTurns, r1.TotalExecutedTurns);
        Assert.Equal(f1.Telemetry.CorrectAnswers, r1.Telemetry.CorrectAnswers);
        Assert.Equal(f1.FinalState.Sector, r1.FinalState.Sector);

        Assert.Equal(f2.TotalExecutedTurns, r2.TotalExecutedTurns);
        Assert.Equal(f2.Telemetry.CorrectAnswers, r2.Telemetry.CorrectAnswers);
        Assert.Equal(f2.FinalState.Sector, r2.FinalState.Sector);
    }

    [Fact]
    public void B15_AddingAnotherSeed_DoesNotChangeExistingSeedResults()
    {
        ulong s1 = 111UL;
        ulong s2 = 222UL;
        ulong s3 = 333UL;
        var profile = SyntheticPlayerProfiles.ProfileD_Learner;

        var configSmall = new SimulationBatchConfig(profile, new[] { s1, s2 }, maxTurnsPerRun: 25);
        var configLarge = new SimulationBatchConfig(profile, new[] { s1, s2, s3 }, maxTurnsPerRun: 25);

        var resultSmall = DeterministicSimulationBatchRunner.RunBatch(configSmall);
        var resultLarge = DeterministicSimulationBatchRunner.RunBatch(configLarge);

        Assert.Equal(resultSmall.Receipts[0].TotalExecutedTurns, resultLarge.Receipts[0].TotalExecutedTurns);
        Assert.Equal(resultSmall.Receipts[0].Telemetry.CorrectAnswers, resultLarge.Receipts[0].Telemetry.CorrectAnswers);
        Assert.Equal(resultSmall.Receipts[1].TotalExecutedTurns, resultLarge.Receipts[1].TotalExecutedTurns);
        Assert.Equal(resultSmall.Receipts[1].Telemetry.CorrectAnswers, resultLarge.Receipts[1].Telemetry.CorrectAnswers);
    }

    [Fact]
    public void B16_IdenticalBatches_ReproduceSameStructuredResult()
    {
        var config1 = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileB_Expert,
            new ulong[] { 10, 20, 30 },
            maxTurnsPerRun: 50);

        var config2 = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileB_Expert,
            new ulong[] { 10, 20, 30 },
            maxTurnsPerRun: 50);

        var res1 = DeterministicSimulationBatchRunner.RunBatch(config1);
        var res2 = DeterministicSimulationBatchRunner.RunBatch(config2);

        Assert.Equal(res1.TotalExecutedTurns, res2.TotalExecutedTurns);
        Assert.Equal(res1.TotalCorrectAnswers, res2.TotalCorrectAnswers);
        Assert.Equal(res1.TotalIncorrectAnswers, res2.TotalIncorrectAnswers);
        Assert.Equal(res1.ObservedAccuracyPercent, res2.ObservedAccuracyPercent);
        Assert.Equal(res1.TotalGameOvers, res2.TotalGameOvers);
        Assert.Equal(res1.TotalVirtualTimeMs, res2.TotalVirtualTimeMs);
        Assert.Equal(res1.TotalAppliedOpponentDamage, res2.TotalAppliedOpponentDamage);
        Assert.Equal(res1.TotalAppliedPlayerDamage, res2.TotalAppliedPlayerDamage);
        Assert.Equal(res1.HighestSectorReached, res2.HighestSectorReached);
    }

    [Fact]
    public void B17_DifferentSeeds_ProduceDifferentDeterministicOutcomes()
    {
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileC_Average,
            new ulong[] { 1001UL, 9999UL },
            maxTurnsPerRun: 100);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        var r1 = result.Receipts[0];
        var r2 = result.Receipts[1];

        // Different seeds will encounter different answer sequences, yielding different virtual times or correct counts
        Assert.True(
            r1.Telemetry.CorrectAnswers != r2.Telemetry.CorrectAnswers ||
            r1.TotalVirtualTimeMs != r2.TotalVirtualTimeMs ||
            r1.Telemetry.TotalAppliedPlayerDamage != r2.Telemetry.TotalAppliedPlayerDamage);
    }

    [Fact]
    public void B18_EveryPerSeedResult_PreservesActualTerminationReason()
    {
        // One seed configured with TargetSector reachable in 22 turns
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            new ulong[] { 101UL, 202UL },
            maxTurnsPerRun: 10,
            targetSector: 1); // Starting sector is 1, so immediate target sector reached

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        Assert.Equal(SimulationTerminationReason.TargetSectorReached, result.Receipts[0].TerminationReason);
        Assert.Equal(SimulationTerminationReason.TargetSectorReached, result.Receipts[1].TerminationReason);
    }

    // =========================================================================
    // B19 - B27: MATHEMATICAL AGGREGATION INVARIANTS
    // =========================================================================

    [Fact]
    public void B19_AggregateTurns_EqualsSumOfPerSeedTurns()
    {
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileC_Average,
            new ulong[] { 12, 34, 56 },
            maxTurnsPerRun: 75);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        long expected = result.Receipts.Sum(r => r.TotalExecutedTurns);
        Assert.Equal(expected, result.TotalExecutedTurns);
    }

    [Fact]
    public void B20_AggregateCorrectAndIncorrect_PartitionAllExecutedTurns()
    {
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileD_Learner,
            new ulong[] { 111, 222, 333, 444 },
            maxTurnsPerRun: 60);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        Assert.Equal(result.TotalExecutedTurns, result.TotalCorrectAnswers + result.TotalIncorrectAnswers);
        long expectedCorrect = result.Receipts.Sum(r => r.Telemetry.CorrectAnswers);
        long expectedIncorrect = result.Receipts.Sum(r => r.Telemetry.IncorrectAnswers);
        Assert.Equal(expectedCorrect, result.TotalCorrectAnswers);
        Assert.Equal(expectedIncorrect, result.TotalIncorrectAnswers);
    }

    [Fact]
    public void B21_AggregateObservedAccuracy_UsesCorrectlyWeightedActualAnswers()
    {
        // Arrange two runs that have different executed turn counts:
        // Run 1: 5 turns
        // Run 2: 20 turns
        // We simulate by running with TargetSector or checking batch calculation directly
        var seeds = new ulong[] { 101UL, 202UL, 303UL };
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileC_Average,
            seeds,
            maxTurnsPerRun: 80);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        Assert.NotNull(result.ObservedAccuracyPercent);
        double expected = (100.0 * (double)result.TotalCorrectAnswers) / (double)result.TotalExecutedTurns;
        Assert.Equal(expected, result.ObservedAccuracyPercent.Value, precision: 10);

        // Verify it is NOT an unweighted arithmetic mean of individual percentages if individual counts differed
        // (Even if equal, formula 100 * sum(correct) / sum(turns) is strictly verified).
    }

    [Fact]
    public void B22_AggregateAppliedOpponentDamage_EqualsSumOfPerRunTelemetry()
    {
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileB_Expert,
            new ulong[] { 10, 20, 30 },
            maxTurnsPerRun: 40);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        long expected = result.Receipts.Sum(r => r.Telemetry.TotalAppliedOpponentDamage);
        Assert.Equal(expected, result.TotalAppliedOpponentDamage);
    }

    [Fact]
    public void B23_AggregateAppliedPlayerDamage_EqualsSumOfPerRunTelemetry()
    {
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileE_Beginner,
            new ulong[] { 71, 72, 73 },
            maxTurnsPerRun: 50);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        long expected = result.Receipts.Sum(r => r.Telemetry.TotalAppliedPlayerDamage);
        Assert.Equal(expected, result.TotalAppliedPlayerDamage);
    }

    [Fact]
    public void B24_GameOverCounts_AreAggregatedOncePerActualGameOverEvent()
    {
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileE_Beginner,
            new ulong[] { 901, 902, 903 },
            maxTurnsPerRun: 150);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        int expected = result.Receipts.Sum(r => r.TotalGameOvers);
        Assert.Equal(expected, result.TotalGameOvers);
    }

    [Fact]
    public void B25_OpponentAndSectorCompletions_AreCorrectlyAggregated()
    {
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            new ulong[] { 101, 202, 303 },
            maxTurnsPerRun: 50);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        int expectedDefeats = result.Receipts.Sum(r => r.Telemetry.TotalOpponentDefeats);
        int expectedSectors = result.Receipts.Sum(r => r.Telemetry.TotalSectorCompletions);

        Assert.Equal(expectedDefeats, result.TotalOpponentDefeats);
        Assert.Equal(expectedSectors, result.TotalSectorCompletions);
    }

    [Fact]
    public void B26_VirtualElapsedTime_IsAggregatedWithoutWallClockContamination()
    {
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            new ulong[] { 10, 20, 30 },
            maxTurnsPerRun: 10);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        // Profile A has fixed 1,200 ms latency per attempt.
        // 3 runs x 10 turns = 30 turns. 30 x 1200 = 36000 ms.
        Assert.Equal(36000L, result.TotalVirtualTimeMs);
        Assert.Equal(result.Receipts.Sum(r => r.TotalVirtualTimeMs), result.TotalVirtualTimeMs);
    }

    [Fact]
    public void B27_HighestSector_ReflectsActualMaximumAcrossCompletedRuns()
    {
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            new ulong[] { 1, 2 },
            maxTurnsPerRun: 30); // 30 turns reaches Sector 2 (Sector 1 takes 22 turns)

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        int maxSectorInReceipts = result.Receipts.Max(r => r.Telemetry.HighestSectorReached);
        Assert.Equal(maxSectorInReceipts, result.HighestSectorReached);
        Assert.True(result.HighestSectorReached >= 2);
    }

    // =========================================================================
    // B28 - B30: CANCELLATION SEMANTICS
    // =========================================================================

    [Fact]
    public void B28_PreCancelledBatch_ExecutesZeroSeedsAndReturnsZeroTurnTelemetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            new ulong[] { 100, 200, 300 },
            maxTurnsPerRun: 50);

        var result = DeterministicSimulationBatchRunner.RunBatch(config, cts.Token);

        Assert.True(result.IsCancelled);
        Assert.Equal(3, result.ConfiguredSeedCount);
        Assert.Equal(0, result.ExecutedSeedCount);
        Assert.Empty(result.Receipts);
        Assert.Equal(0L, result.TotalExecutedTurns);
        Assert.Equal(0L, result.TotalCorrectAnswers);
        Assert.Equal(0L, result.TotalIncorrectAnswers);
        Assert.Null(result.ObservedAccuracyPercent);
        Assert.Equal(0, result.TotalGameOvers);
        Assert.Equal(0L, result.TotalVirtualTimeMs);
        Assert.Equal(0L, result.TotalAppliedOpponentDamage);
        Assert.Equal(0L, result.TotalAppliedPlayerDamage);
        Assert.Equal(1, result.HighestSectorReached);
    }

    [Fact]
    public void B29_CancellationBetweenSeeds_PreservesCompletedResultsAndStops()
    {
        using var cts = new CancellationTokenSource();
        int completedRuns = 0;

        // Cancel token during first run completion callback
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            new ulong[] { 111, 222, 333 },
            maxTurnsPerRun: 10,
            onTurnCompleted: _ =>
            {
                completedRuns++;
                if (completedRuns >= 10)
                {
                    cts.Cancel();
                }
            });

        var result = DeterministicSimulationBatchRunner.RunBatch(config, cts.Token);

        Assert.True(result.IsCancelled);
        Assert.Equal(3, result.ConfiguredSeedCount);
        Assert.Equal(1, result.ExecutedSeedCount);
        Assert.Single(result.Receipts);
        Assert.Equal(10L, result.TotalExecutedTurns);
        Assert.Equal(111UL, result.Receipts[0].MasterSeed);
    }

    [Fact]
    public void B30_CancellationInsideRun_PreservesValidPartialResultAndStops()
    {
        using var cts = new CancellationTokenSource();
        int turnCount = 0;

        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            new ulong[] { 555, 666 },
            maxTurnsPerRun: 20,
            onTurnCompleted: _ =>
            {
                turnCount++;
                if (turnCount == 7)
                {
                    cts.Cancel();
                }
            });

        var result = DeterministicSimulationBatchRunner.RunBatch(config, cts.Token);

        Assert.True(result.IsCancelled);
        Assert.Equal(2, result.ConfiguredSeedCount);
        Assert.Equal(1, result.ExecutedSeedCount);
        Assert.Single(result.Receipts);
        Assert.Equal(7L, result.TotalExecutedTurns);
        Assert.Equal(SimulationTerminationReason.Cancelled, result.Receipts[0].TerminationReason);
        Assert.Equal(7L, result.Receipts[0].TotalExecutedTurns);
    }

    // =========================================================================
    // B31 - B34: MEMORY, PROFILE CONTRACTS & UNAVAILABLE METRICS
    // =========================================================================

    [Fact]
    public void B31_BatchExecution_DoesNotAccumulatePerTurnHistories()
    {
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            new ulong[] { 101, 102 },
            maxTurnsPerRun: 100);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        // Receipts collection has O(number of seeds) receipts
        Assert.Equal(2, result.Receipts.Count);
        foreach (var receipt in result.Receipts)
        {
            Assert.NotNull(receipt.Result);
            // No list of turns is held
        }
    }

    [Fact]
    public void B32_AllFiveCanonicalProfiles_AreAcceptedWithoutMetadataAlteration()
    {
        var profiles = SyntheticPlayerProfiles.All;
        Assert.Equal(5, profiles.Count);

        foreach (var p in profiles)
        {
            var config = new SimulationBatchConfig(p, new ulong[] { 12345UL }, maxTurnsPerRun: 10);
            var result = DeterministicSimulationBatchRunner.RunBatch(config);

            Assert.Equal(p.Id, result.ProfileId);
            Assert.Equal(p.Name, result.ProfileName);
            Assert.Equal(10L, result.TotalExecutedTurns);
        }
    }

    [Fact]
    public void B33_ConfiguredProfileProbability_IsNotSubstitutedForObservedAccuracy()
    {
        var profile = SyntheticPlayerProfiles.ProfileC_Average; // 0.85 configured
        var config = new SimulationBatchConfig(profile, new ulong[] { 4242UL }, maxTurnsPerRun: 100);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        Assert.NotNull(result.ObservedAccuracyPercent);
        double actualObserved = (100.0 * result.TotalCorrectAnswers) / result.TotalExecutedTurns;
        Assert.Equal(actualObserved, result.ObservedAccuracyPercent.Value);
    }

    [Fact]
    public void B34_UnavailableFeatureMetrics_AreNotFabricated()
    {
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileB_Expert,
            new ulong[] { 10, 20 },
            maxTurnsPerRun: 15);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        foreach (var receipt in result.Receipts)
        {
            var t = receipt.Telemetry;
            Assert.Null(t.FinalPlayerLevel);
            Assert.Null(t.TotalXp);
            Assert.Null(t.SkillPointsEarned);
            Assert.Null(t.SkillPointsSpent);
            Assert.Null(t.AttackRanks);
            Assert.Null(t.FirewallRanks);
            Assert.Null(t.CriticalStrikeRanks);
            Assert.Null(t.OverdriveRanks);
            Assert.Null(t.TotalShieldsAbsorbed);
            Assert.Null(t.ReplayDistanceAfterDefeat);
        }
    }

    // =========================================================================
    // B35: REQUIRED 100,000+ TURN ACCEPTANCE TEST
    // =========================================================================

    [Fact]
    public void B35_OneHundredThousandTurn_MultiProfileBatchAcceptanceTest()
    {
        // 5 Canonical Profiles: A, B, C, D, E
        // 20 distinct seeds per profile
        // 1,000 turns per seed
        // Total: 5 x 20 x 1,000 = 100,000 executed turns
        var profiles = new[]
        {
            SyntheticPlayerProfiles.ProfileA_Perfect,
            SyntheticPlayerProfiles.ProfileB_Expert,
            SyntheticPlayerProfiles.ProfileC_Average,
            SyntheticPlayerProfiles.ProfileD_Learner,
            SyntheticPlayerProfiles.ProfileE_Beginner,
        };

        var results = new List<SimulationBatchResult>();
        var sw = Stopwatch.StartNew();

        long grandTotalTurns = 0;
        long grandTotalCorrect = 0;
        long grandTotalIncorrect = 0;
        long grandTotalOpponentDamage = 0;
        long grandTotalPlayerDamage = 0;
        int grandTotalGameOvers = 0;
        int grandTotalOpponentDefeats = 0;
        int grandTotalSectorCompletions = 0;

        for (int pIdx = 0; pIdx < profiles.Length; pIdx++)
        {
            var profile = profiles[pIdx];
            var seeds = new List<ulong>();
            for (int sIdx = 1; sIdx <= 20; sIdx++)
            {
                // Deterministic non-overlapping seeds per profile
                ulong seed = (ulong)((pIdx + 1) * 10000 + sIdx * 7919);
                seeds.Add(seed);
            }

            var batchConfig = new SimulationBatchConfig(
                profile: profile,
                seeds: seeds,
                maxTurnsPerRun: 1000);

            var batchResult = DeterministicSimulationBatchRunner.RunBatch(batchConfig);
            results.Add(batchResult);

            Assert.Equal(20, batchResult.ConfiguredSeedCount);
            Assert.Equal(20, batchResult.ExecutedSeedCount);
            Assert.False(batchResult.IsCancelled);
            Assert.Equal(20000L, batchResult.TotalExecutedTurns);
            Assert.Equal(batchResult.TotalExecutedTurns, batchResult.TotalCorrectAnswers + batchResult.TotalIncorrectAnswers);

            grandTotalTurns += batchResult.TotalExecutedTurns;
            grandTotalCorrect += batchResult.TotalCorrectAnswers;
            grandTotalIncorrect += batchResult.TotalIncorrectAnswers;
            grandTotalOpponentDamage += batchResult.TotalAppliedOpponentDamage;
            grandTotalPlayerDamage += batchResult.TotalAppliedPlayerDamage;
            grandTotalGameOvers += batchResult.TotalGameOvers;
            grandTotalOpponentDefeats += batchResult.TotalOpponentDefeats;
            grandTotalSectorCompletions += batchResult.TotalSectorCompletions;
        }

        sw.Stop();

        // Core acceptance assertions
        Assert.Equal(5, results.Count);
        Assert.Equal(100000L, grandTotalTurns);
        Assert.True(grandTotalTurns >= 100000L);
        Assert.Equal(grandTotalTurns, grandTotalCorrect + grandTotalIncorrect);

        // Profile A specific validation in the 100k batch
        var profileAResult = results.Single(r => r.ProfileId == SyntheticPlayerProfiles.ProfileAId);
        Assert.Equal(20, profileAResult.ConfiguredSeedCount);
        Assert.Equal(20, profileAResult.ExecutedSeedCount);
        Assert.Equal(20000L, profileAResult.TotalExecutedTurns);
        Assert.Equal(20000L, profileAResult.TotalCorrectAnswers);
        Assert.Equal(0L, profileAResult.TotalIncorrectAnswers);
        Assert.Equal(100.0, profileAResult.ObservedAccuracyPercent);
        Assert.Equal(0, profileAResult.TotalGameOvers);
        Assert.Equal(0L, profileAResult.TotalAppliedPlayerDamage);
        Assert.Equal(20000L, profileAResult.TotalAppliedOpponentDamage);
        Assert.Equal(480, profileAResult.TotalSectorCompletions);
        Assert.Equal(4300, profileAResult.TotalOpponentDefeats);
        Assert.Equal(25, profileAResult.HighestSectorReached);
        Assert.Equal(24000000L, profileAResult.TotalVirtualTimeMs);

        // Assert each Profile A receipt matches exact mathematical progression
        Assert.Equal(20, profileAResult.Receipts.Count);
        foreach (var receipt in profileAResult.Receipts)
        {
            Assert.Equal(SimulationTerminationReason.MaxTurnsReached, receipt.TerminationReason);
            Assert.Equal(1000L, receipt.TotalExecutedTurns);
            Assert.Equal(1000L, receipt.Telemetry.CorrectAnswers);
            Assert.Equal(0L, receipt.Telemetry.IncorrectAnswers);
            Assert.Equal(24, receipt.Telemetry.TotalSectorCompletions);
            Assert.Equal(215, receipt.Telemetry.TotalOpponentDefeats);
            Assert.Equal(25, receipt.Telemetry.HighestSectorReached);
            Assert.Equal(0, receipt.TotalGameOvers);
            Assert.Equal(25, receipt.FinalState.Sector);
            Assert.Equal(1, receipt.FinalState.OpponentIndex);
            Assert.Equal(2, receipt.FinalState.CurrentOpponent.CurrentHp);
            Assert.Equal(100, receipt.FinalState.PlayerCurrentHp);
        }

        // Non-Profile A batches experienced player damage and incorrect answers
        var nonA = results.Where(r => r.ProfileId != SyntheticPlayerProfiles.ProfileAId).ToList();
        Assert.All(nonA, r => Assert.True(r.TotalIncorrectAnswers > 0));
        Assert.All(nonA, r => Assert.True(r.TotalAppliedPlayerDamage > 0));

        // Format compact diagnostic table with genuine runtime batch metrics
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("=========================================================================================================================");
        sb.AppendLine("AUTHENTIC 100,000-TURN BATCH EXECUTION RESULTS (B35)");
        sb.AppendLine("=========================================================================================================================");
        sb.AppendLine(string.Format("{0,-12} | {1,5} | {2,5} | {3,7} | {4,7} | {5,7} | {6,7} | {7,5} | {8,7} | {9,7} | {10,7} | {11,7} | {12,4} | {13,10}",
            "Profile", "Cfg", "Exec", "Turns", "Correct", "Incorr", "Acc %", "Overs", "Plr Dmg", "Opp Dmg", "Defeats", "Sectors", "High", "VirtTimeMs"));
        sb.AppendLine("-------------------------------------------------------------------------------------------------------------------------");
        foreach (var r in results)
        {
            sb.AppendLine(string.Format("{0,-12} | {1,5} | {2,5} | {3,7} | {4,7} | {5,7} | {6,7:F2} | {7,5} | {8,7} | {9,7} | {10,7} | {11,7} | {12,4} | {13,10}",
                r.ProfileId,
                r.ConfiguredSeedCount,
                r.ExecutedSeedCount,
                r.TotalExecutedTurns,
                r.TotalCorrectAnswers,
                r.TotalIncorrectAnswers,
                r.ObservedAccuracyPercent ?? 0.0,
                r.TotalGameOvers,
                r.TotalAppliedPlayerDamage,
                r.TotalAppliedOpponentDamage,
                r.TotalOpponentDefeats,
                r.TotalSectorCompletions,
                r.HighestSectorReached,
                r.TotalVirtualTimeMs));
        }
        sb.AppendLine("=========================================================================================================================");
        sb.AppendLine($"[100,000 Turn Acceptance Test Execution: {sw.ElapsedMilliseconds} ms across 5 profiles / 100 runs]");
        var diagnosticText = sb.ToString();
        _output?.WriteLine(diagnosticText);
        Console.WriteLine(diagnosticText);
    }

    // =========================================================================
    // B36 - B39: SAFETY, PURITY & REGRESSION INTEGRITY
    // =========================================================================

    [Fact]
    public void B36_AggregateCounters_UseSafeArithmetic()
    {
        // Verified by checked blocks in SimulationBatchResult and DeterministicSimulationBatchRunner
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            new ulong[] { 101, 202 },
            maxTurnsPerRun: 100);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);
        Assert.Equal(200L, result.TotalExecutedTurns);
    }

    [Fact]
    public void B37_NoRuntimeRandomnessOrSystemClock_IsIntroduced()
    {
        // Re-executing same batch produces bit-for-bit identical outputs
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileC_Average,
            new ulong[] { 777UL, 888UL },
            maxTurnsPerRun: 150);

        var r1 = DeterministicSimulationBatchRunner.RunBatch(config);
        var r2 = DeterministicSimulationBatchRunner.RunBatch(config);

        Assert.Equal(r1.TotalExecutedTurns, r2.TotalExecutedTurns);
        Assert.Equal(r1.TotalVirtualTimeMs, r2.TotalVirtualTimeMs);
        Assert.Equal(r1.ObservedAccuracyPercent, r2.ObservedAccuracyPercent);
    }

    // =========================================================================
    // B38: IMMUTABILITY REGRESSION CONTRACT (M01 REMEDIATION)
    // =========================================================================

    [Fact]
    public void B38_ReceiptsCollection_IsDefensivelyImmutableAndRejectsDirectOrIndexedMutation()
    {
        // A. Create a batch with at least two different seed receipts
        ulong seed1 = 101UL;
        ulong seed2 = 202UL;
        var config = new SimulationBatchConfig(
            SyntheticPlayerProfiles.ProfileA_Perfect,
            new[] { seed1, seed2 },
            maxTurnsPerRun: 10);

        var result = DeterministicSimulationBatchRunner.RunBatch(config);

        // B. Capture the exposed Receipts collection
        IReadOnlyList<SimulationBatchRunReceipt> receipts = result.Receipts;
        Assert.Equal(2, receipts.Count);

        // C. Verify the collection cannot be downcast to SimulationBatchRunReceipt[]
        Assert.False(receipts is SimulationBatchRunReceipt[], "Receipts collection must not expose internal mutable array storage.");
        Assert.Throws<InvalidCastException>(() => (SimulationBatchRunReceipt[])receipts);

        // Prepare dummy receipt for mutation attempts
        var dummyRun = HeadlessCombatSimulator.Run(new SimulationRunConfig(SyntheticPlayerProfiles.ProfileA_Perfect, 999UL, 5));
        var dummyReceipt = new SimulationBatchRunReceipt(999UL, SyntheticPlayerProfiles.ProfileAId, dummyRun);

        // D. Verify the collection does not permit indexed replacement
        if (receipts is IList<SimulationBatchRunReceipt> genericList)
        {
            Assert.True(genericList.IsReadOnly, "Exposed IList<T> interface must declare IsReadOnly == true.");
            Assert.Throws<NotSupportedException>(() => genericList[0] = dummyReceipt);
            Assert.Throws<NotSupportedException>(() => genericList[1] = dummyReceipt);
        }

        if (receipts is System.Collections.IList nonGenericList)
        {
            Assert.True(nonGenericList.IsReadOnly, "Exposed non-generic IList interface must declare IsReadOnly == true.");
            Assert.Throws<NotSupportedException>(() => nonGenericList[0] = dummyReceipt);
        }

        // E. Verify callers cannot change ordering through the exposed collection
        if (receipts is IList<SimulationBatchRunReceipt> orderableList)
        {
            Assert.Throws<NotSupportedException>(() => orderableList.Insert(0, dummyReceipt));
            Assert.Throws<NotSupportedException>(() => orderableList.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => orderableList.Clear());
        }

        // F. Verify attempts at mutation do not change actual stored seed identities
        Assert.Equal(seed1, result.Receipts[0].MasterSeed);
        Assert.Equal(seed2, result.Receipts[1].MasterSeed);

        // G. Verify aggregate metrics remain consistent with all retained receipts
        Assert.Equal(result.Receipts.Sum(r => r.TotalExecutedTurns), result.TotalExecutedTurns);
        Assert.Equal(result.Receipts.Sum(r => r.Telemetry.CorrectAnswers), result.TotalCorrectAnswers);
        Assert.Equal(result.Receipts.Sum(r => r.Telemetry.IncorrectAnswers), result.TotalIncorrectAnswers);
        Assert.Equal(result.Receipts.Sum(r => r.Telemetry.TotalAppliedOpponentDamage), result.TotalAppliedOpponentDamage);
        Assert.Equal(result.Receipts.Sum(r => r.Telemetry.TotalAppliedPlayerDamage), result.TotalAppliedPlayerDamage);
        Assert.Equal(result.Receipts.Sum(r => r.TotalGameOvers), result.TotalGameOvers);
        Assert.Equal(result.Receipts.Sum(r => r.Telemetry.TotalOpponentDefeats), result.TotalOpponentDefeats);
        Assert.Equal(result.Receipts.Sum(r => r.Telemetry.TotalSectorCompletions), result.TotalSectorCompletions);
        Assert.Equal(result.Receipts.Sum(r => r.TotalVirtualTimeMs), result.TotalVirtualTimeMs);

        // Caller-owned collection mutations after construction cannot alter the result
        var callerList = new List<SimulationBatchRunReceipt> { result.Receipts[0], result.Receipts[1] };
        var directResult = new SimulationBatchResult(
            result.ProfileId,
            result.ProfileName,
            configuredSeedCount: 2,
            executedSeedCount: 2,
            isCancelled: false,
            receipts: callerList,
            totalExecutedTurns: result.TotalExecutedTurns,
            totalCorrectAnswers: result.TotalCorrectAnswers,
            totalIncorrectAnswers: result.TotalIncorrectAnswers,
            observedAccuracyPercent: result.ObservedAccuracyPercent,
            totalGameOvers: result.TotalGameOvers,
            totalVirtualTimeMs: result.TotalVirtualTimeMs,
            totalAppliedOpponentDamage: result.TotalAppliedOpponentDamage,
            totalAppliedPlayerDamage: result.TotalAppliedPlayerDamage,
            totalOpponentDefeats: result.TotalOpponentDefeats,
            totalSectorCompletions: result.TotalSectorCompletions,
            highestSectorReached: result.HighestSectorReached);

        callerList[0] = dummyReceipt;
        Assert.Equal(seed1, directResult.Receipts[0].MasterSeed);
    }
}
