namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using Xunit;

public sealed class WindowsV1HardeningAndSimulationTests : IDisposable
{
    private readonly string _testDbDir;

    public WindowsV1HardeningAndSimulationTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstHardening_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbDir))
            {
                Directory.Delete(_testDbDir, recursive: true);
            }
        }
        catch
        {
            // Best effort temp cleanup
        }
    }

    private string GetTempDbPath() => Path.Combine(_testDbDir, $"hardening_{Guid.NewGuid():N}.db");

    // =========================================================================
    // 1. ARITHMETIC CATALOG EXHAUSTIVE TEST (418 FACTS)
    // =========================================================================
    [Fact]
    public void Catalog_ExhaustiveAudit_All418FactsConformToV1Specification()
    {
        var allFacts = ArithmeticCatalog.GetAllFacts(ArithmeticCatalog.MaxV1Operand);
        Assert.Equal(418, allFacts.Count);

        var additionFacts = ArithmeticCatalog.GetFacts(ArithmeticOperation.Addition, ArithmeticCatalog.MaxV1Operand);
        var subtractionFacts = ArithmeticCatalog.GetFacts(ArithmeticOperation.Subtraction, ArithmeticCatalog.MaxV1Operand);
        var multiplicationFacts = ArithmeticCatalog.GetFacts(ArithmeticOperation.Multiplication, ArithmeticCatalog.MaxV1Operand);
        var divisionFacts = ArithmeticCatalog.GetFacts(ArithmeticOperation.Division, ArithmeticCatalog.MaxV1Operand);

        Assert.Equal(121, additionFacts.Count);       // 11 * 11
        Assert.Equal(66, subtractionFacts.Count);     // 1+2+...+11
        Assert.Equal(121, multiplicationFacts.Count);  // 11 * 11
        Assert.Equal(110, divisionFacts.Count);       // 10 * 11 (divisor 1..10, quotient 0..10)

        var idSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fact in allFacts)
        {
            Assert.False(string.IsNullOrWhiteSpace(fact.Id));
            Assert.True(idSet.Add(fact.Id), $"Duplicate FactId detected: {fact.Id}");
            Assert.InRange(fact.LeftOperand, 0, 100);
            Assert.InRange(fact.RightOperand, 0, 10);

            switch (fact.Operation)
            {
                case ArithmeticOperation.Addition:
                    Assert.Equal(fact.LeftOperand + fact.RightOperand, fact.CorrectResult);
                    Assert.Equal("+", fact.DisplaySymbol);
                    Assert.StartsWith("add:", fact.Id);
                    break;

                case ArithmeticOperation.Subtraction:
                    Assert.True(fact.LeftOperand >= fact.RightOperand, "Subtraction result must be non-negative.");
                    Assert.Equal(fact.LeftOperand - fact.RightOperand, fact.CorrectResult);
                    Assert.Equal("\u2212", fact.DisplaySymbol);
                    Assert.StartsWith("sub:", fact.Id);
                    break;

                case ArithmeticOperation.Multiplication:
                    Assert.Equal(fact.LeftOperand * fact.RightOperand, fact.CorrectResult);
                    Assert.Equal("\u00D7", fact.DisplaySymbol);
                    Assert.StartsWith("mul:", fact.Id);
                    break;

                case ArithmeticOperation.Division:
                    Assert.True(fact.RightOperand > 0, "Division by zero is strictly forbidden.");
                    Assert.Equal(0, fact.LeftOperand % fact.RightOperand); // Exact division
                    Assert.Equal(fact.LeftOperand / fact.RightOperand, fact.CorrectResult);
                    Assert.Equal("\u00F7", fact.DisplaySymbol);
                    Assert.StartsWith("div:", fact.Id);
                    break;
            }
        }
    }

    // =========================================================================
    // 2. COMPLETE PROGRESSION SIMULATION (Level 1 -> Level 10 -> Mixed Practice)
    // =========================================================================
    [Fact]
    public void Simulation_FullProgression_ReachesLevel10Checkpoint_AndTransitionsToAdaptiveMixedPractice()
    {
        var progression = LearnerProgression.CreateFresh();
        var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);
        var scheduler = new FsrsSchedulerAdapter();
        var selector = new AdaptivePracticeSelector(new Random(42));

        var exposedFactIds = new HashSet<string>(StringComparer.Ordinal);
        var completedCheckpoints = new List<int>();
        var totalAttempts = 0;

        while (!progression.IsAllIntroductionsComplete)
        {
            var fact = selector.SelectNextFact(progression, itemStates, fsrsStates, totalAttempts, progression.PracticePosition);
            exposedFactIds.Add(fact.Id);
            totalAttempts++;
            progression.PracticePosition++;

            // Learner answers correctly with fluent latency (800ms)
            var latencyMs = 800L;
            var rating = FsrsRatingMapper.MapRating(AttemptOutcome.Correct, latencyMs);
            fsrsStates.TryGetValue(fact.Id, out var existingCard);
            fsrsStates[fact.Id] = scheduler.ReviewCard(existingCard, fact.Id, rating, progression.PracticePosition, latencyMs);

            if (!itemStates.TryGetValue(fact.Id, out var state))
            {
                state = ItemLearningState.CreateNew(fact);
                itemStates[fact.Id] = state;
            }
            state.TotalAttempts++;
            state.CorrectAttempts++;
            state.ConsecutiveCorrectStreak++;
            state.LastLatencyMs = latencyMs;
            state.IsProvisionallyMastered = LearningPolicy.EvaluateItemMastery(state);

            if (progression.IsInCheckpoint)
            {
                progression.CheckpointAttemptCount++;
                progression.CheckpointCorrectCount++;

                if (progression.CheckpointAttemptCount >= LearningPolicy.DefaultCheckpointAttemptCount)
                {
                    var finishedLevel = progression.ActiveCheckpointLevel ?? progression.CurrentMaxOperand;
                    completedCheckpoints.Add(finishedLevel);
                    progression.CompletedCheckpointLevel = finishedLevel;
                    progression.ActiveCheckpointLevel = null;
                    progression.CheckpointAttemptCount = 0;
                    progression.CheckpointCorrectCount = 0;
                }
            }

            LearningPolicy.SynchronizeProgression(progression, itemStates);

            // Safety assert against infinite loop
            Assert.True(totalAttempts < 5000, "Progression simulation exceeded expected step boundary.");
        }

        // Validate progression outcomes
        Assert.True(progression.IsAllIntroductionsComplete);
        Assert.Equal(10, progression.CompletedCheckpointLevel);
        Assert.Equal(10, completedCheckpoints.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, completedCheckpoints);

        // All 418 facts must have been exposed at least once!
        Assert.Equal(418, exposedFactIds.Count);

        // Subsequent practice is open-ended Adaptive Mixed Practice without Level 11 expansion
        var (phase, _, _, _) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);
        Assert.Equal(ProgressionPhase.MixedPractice, phase);
        Assert.Equal(10, progression.GetMaxOperand(ArithmeticOperation.Addition));
        Assert.Equal(10, progression.GetMaxOperand(ArithmeticOperation.Subtraction));
        Assert.Equal(10, progression.GetMaxOperand(ArithmeticOperation.Multiplication));
        Assert.Equal(10, progression.GetMaxOperand(ArithmeticOperation.Division));
    }

    // =========================================================================
    // 3. LONG-RUN SYNTHETIC LEARNER SIMULATIONS (5,000 ATTEMPTS EACH)
    // =========================================================================
    [Theory]
    [InlineData("StrongLearner", 42)]
    [InlineData("MixedLearner", 123)]
    [InlineData("StrugglingLearner", 999)]
    public void Simulation_LongRunLearnerProfiles_5000Attempts_MaintainsNumericAndStateSafety(string profile, int seed)
    {
        var progression = LearnerProgression.CreateFresh();
        var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);
        var scheduler = new FsrsSchedulerAdapter();
        var selector = new AdaptivePracticeSelector(new Random(seed));
        var random = new Random(seed);

        const int targetAttempts = 5000;

        for (var step = 1; step <= targetAttempts; step++)
        {
            var fact = selector.SelectNextFact(progression, itemStates, fsrsStates, step, progression.PracticePosition);
            progression.PracticePosition++;

            // Determine synthetic outcome & latency based on profile
            AttemptOutcome outcome;
            long latencyMs;

            switch (profile)
            {
                case "StrongLearner":
                    outcome = (random.NextDouble() < 0.96) ? AttemptOutcome.Correct : AttemptOutcome.Incorrect;
                    latencyMs = outcome == AttemptOutcome.Correct ? random.Next(400, 1200) : random.Next(1500, 3000);
                    break;

                case "MixedLearner":
                    var r = random.NextDouble();
                    if (r < 0.80)
                    {
                        outcome = AttemptOutcome.Correct;
                        latencyMs = random.Next(600, 2600);
                    }
                    else if (r < 0.95)
                    {
                        outcome = AttemptOutcome.Incorrect;
                        latencyMs = random.Next(1200, 4000);
                    }
                    else
                    {
                        outcome = AttemptOutcome.Timeout;
                        latencyMs = 30000;
                    }
                    break;

                case "StrugglingLearner":
                default:
                    var sr = random.NextDouble();
                    if (sr < 0.55)
                    {
                        outcome = AttemptOutcome.Correct;
                        latencyMs = random.Next(1000, 3500);
                    }
                    else if (sr < 0.85)
                    {
                        outcome = AttemptOutcome.Incorrect;
                        latencyMs = random.Next(1500, 5000);
                    }
                    else
                    {
                        outcome = AttemptOutcome.Timeout;
                        latencyMs = 30000;
                    }
                    break;
            }

            var rating = FsrsRatingMapper.MapRating(outcome, latencyMs);
            fsrsStates.TryGetValue(fact.Id, out var existingCard);
            var updatedCard = scheduler.ReviewCard(existingCard, fact.Id, rating, progression.PracticePosition, latencyMs);
            fsrsStates[fact.Id] = updatedCard;

            // Update item state
            if (!itemStates.TryGetValue(fact.Id, out var state))
            {
                state = ItemLearningState.CreateNew(fact);
                itemStates[fact.Id] = state;
            }
            state.TotalAttempts++;
            state.LastLatencyMs = latencyMs;

            if (outcome == AttemptOutcome.Correct)
            {
                state.CorrectAttempts++;
                state.ConsecutiveCorrectStreak++;
                state.NeedsRemediation = false;
            }
            else
            {
                state.IncorrectAttempts++;
                state.ConsecutiveCorrectStreak = 0;
                state.NeedsRemediation = true;
                state.RemediationDueOrder = step + LearningPolicy.RemediationInterveningCount;
            }
            state.IsProvisionallyMastered = LearningPolicy.EvaluateItemMastery(state);

            if (progression.IsInCheckpoint)
            {
                progression.CheckpointAttemptCount++;
                if (outcome == AttemptOutcome.Correct) progression.CheckpointCorrectCount++;

                if (progression.CheckpointAttemptCount >= LearningPolicy.DefaultCheckpointAttemptCount)
                {
                    progression.CompletedCheckpointLevel = progression.ActiveCheckpointLevel ?? progression.CurrentMaxOperand;
                    progression.ActiveCheckpointLevel = null;
                    progression.CheckpointAttemptCount = 0;
                    progression.CheckpointCorrectCount = 0;
                }
            }

            LearningPolicy.SynchronizeProgression(progression, itemStates);
        }

        // Numeric safety assertions
        Assert.Equal(targetAttempts, progression.PracticePosition);

        foreach (var (factId, card) in fsrsStates)
        {
            Assert.False(string.IsNullOrEmpty(factId));
            Assert.True(card.DuePracticePosition >= 0, $"DuePracticePosition was negative: {card.DuePracticePosition}");

            if (card.Stability.HasValue)
            {
                Assert.False(double.IsNaN(card.Stability.Value), "Stability was NaN.");
                Assert.False(double.IsInfinity(card.Stability.Value), "Stability was Infinity.");
                Assert.True(card.Stability.Value > 0, "Stability must be strictly positive.");
            }

            if (card.Difficulty.HasValue)
            {
                Assert.False(double.IsNaN(card.Difficulty.Value), "Difficulty was NaN.");
                Assert.False(double.IsInfinity(card.Difficulty.Value), "Difficulty was Infinity.");
                Assert.InRange(card.Difficulty.Value, 1.0, 10.0);
            }
        }
    }

    // =========================================================================
    // 4. FSRS CONFIGURATION & TASK-TIME DETERMINISM
    // =========================================================================
    [Fact]
    public void Fsrs_ConfigurationAndTaskTimeDeterminism_MatchesPackageInvariants()
    {
        var adapter = new FsrsSchedulerAdapter();
        Assert.Equal(21, adapter.Parameters.Count);
        Assert.Equal(0.95, adapter.DesiredRetention);
        Assert.False(adapter.EnableFuzzing);

        // Determinism test: same task sequence at different virtual calendar dates
        var factId = "add:3+4";
        var baseDate1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var baseDate2 = new DateTime(2040, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        var adapter1 = new FsrsSchedulerAdapter(virtualEpoch: baseDate1);
        var adapter2 = new FsrsSchedulerAdapter(virtualEpoch: baseDate2);

        FsrsCardState? card1 = null;
        FsrsCardState? card2 = null;

        var attempts = new[]
        {
            (FsrsRating.Easy, 1L, 600L),
            (FsrsRating.Good, 5L, 1200L),
            (FsrsRating.Hard, 18L, 2600L),
            (FsrsRating.Again, 25L, 4000L),
            (FsrsRating.Good, 28L, 1100L)
        };

        foreach (var (rating, pos, lat) in attempts)
        {
            card1 = adapter1.ReviewCard(card1, factId, rating, pos, lat);
            card2 = adapter2.ReviewCard(card2, factId, rating, pos, lat);

            Assert.Equal(card1.State, card2.State);
            Assert.Equal(card1.Stability, card2.Stability);
            Assert.Equal(card1.Difficulty, card2.Difficulty);
            Assert.Equal(card1.DuePracticePosition, card2.DuePracticePosition);
            Assert.Equal(card1.LastReviewPracticePosition, card2.LastReviewPracticePosition);
        }
    }

    // =========================================================================
    // 5. DIVERSITY & MIRROR COOLDOWN AUDIT
    // =========================================================================
    [Fact]
    public void Selector_DiversityRules_RespectsCooldownsAndRelaxesWithoutDeadlock()
    {
        var progression = new LearnerProgression
        {
            OperationMaxOperands = new Dictionary<ArithmeticOperation, int>
            {
                [ArithmeticOperation.Addition] = 5,
                [ArithmeticOperation.Subtraction] = 5,
                [ArithmeticOperation.Multiplication] = 5,
                [ArithmeticOperation.Division] = 5
            },
            CompletedCheckpointLevel = 5
        };

        var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);
        var selector = new AdaptivePracticeSelector(new Random(42));

        var history = new List<ArithmeticFact>();

        for (var step = 1; step <= 200; step++)
        {
            var next = selector.SelectNextFact(progression, itemStates, fsrsStates, step, step);
            history.Add(next);

            // Verify no immediate consecutive exact duplicate
            if (history.Count >= 2)
            {
                Assert.NotEqual(history[^2].Id, next.Id);
            }
        }

        Assert.Equal(200, history.Count);
    }

    // =========================================================================
    // 6. HIGH-VOLUME SQLITE CONFORMANCE (5,000 COMMITS & SNAPSHOT RELOAD)
    // =========================================================================
    [Fact]
    public async Task Persistence_HighVolume_5000Commits_ReloadsAccuratelyWithoutCorruption()
    {
        var dbPath = GetTempDbPath();
        var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var progression = LearnerProgression.CreateFresh();
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 2, 3);
        var itemState = ItemLearningState.CreateNew(fact);
        var fsrsState = new FsrsCardState(fact.Id, Guid.NewGuid(), State: 1, Step: null, Stability: 2.5, Difficulty: 4.0, DuePracticePosition: 10, LastReviewPracticePosition: 1, LastRating: FsrsRating.Good);

        var currentRevision = 1L;

        for (var i = 1; i <= 5000; i++)
        {
            var subId = Guid.NewGuid().ToString("N");
            progression.PracticePosition = i;
            itemState.TotalAttempts = i;
            itemState.CorrectAttempts = i;

            var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 2, 3, 5, 5, true, 800, DateTimeOffset.UtcNow);
            var changeSet = new SubmissionChangeSet(subId, currentRevision, attempt, itemState, progression, fsrsState);

            var result = await store.CommitSubmissionAsync(changeSet);
            Assert.True(result.IsSuccess);
            Assert.Equal(currentRevision + 1, result.NewRevision);
            currentRevision = result.NewRevision!.Value;
        }

        // Close and reopen
        await store.CloseAsync();
        store.Dispose();

        var reopenedStore = new SqliteLearnerStore(dbPath);
        await reopenedStore.InitializeAsync();
        var snapshot = await reopenedStore.LoadSnapshotAsync();

        Assert.Equal(5001, snapshot.Revision);
        Assert.Equal(5000, snapshot.Progression.PracticePosition);
        Assert.Equal(50, snapshot.RecentAttempts.Count);
        Assert.Equal(5000, snapshot.ItemStates[fact.Id].TotalAttempts);
        Assert.Equal(10, snapshot.FsrsStates[fact.Id].DuePracticePosition);

        await reopenedStore.CloseAsync();
        reopenedStore.Dispose();
    }

    // =========================================================================
    // 7. RESET MATRIX END-TO-END
    // =========================================================================
    [Fact]
    public async Task ResetMatrix_ResetLearningProgress_ClearsLearnerDbAccurately()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 1, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 1, 1, 2, 2, true, 900, DateTimeOffset.UtcNow);
        var itemState = ItemLearningState.CreateNew(fact);
        var prog = LearnerProgression.CreateFresh();
        prog.PracticePosition = 15;
        prog.CompletedCheckpointLevel = 3;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, prog);
        await store.CommitSubmissionAsync(changeSet);

        // Execute Reset Learning Progress
        await store.ResetLearningProgressAsync();

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(1, snapshot.Revision);
        Assert.Equal(0, snapshot.Progression.PracticePosition);
        Assert.Equal(0, snapshot.Progression.CompletedCheckpointLevel);
        Assert.Empty(snapshot.ItemStates);
        Assert.Empty(snapshot.FsrsStates);
        Assert.Empty(snapshot.RecentAttempts);
    }
}
