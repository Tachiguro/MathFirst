namespace MathFirst.Application.Practice;

using MathFirst.Application.Scheduling;
using MathFirst.Domain;

public sealed class AdaptivePracticeSelector
{
    private readonly Random _random;
    private readonly List<string> _recentFactIds = new();
    private readonly List<string> _recentCanonicalKeys = new();
    private readonly List<ArithmeticOperation> _recentOperations = new();
    private string? _lastSelectedFactId;

    public AdaptivePracticeSelector(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    public static string GetCanonicalMirrorKey(ArithmeticFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.Operation is ArithmeticOperation.Addition or ArithmeticOperation.Multiplication)
        {
            var min = Math.Min(fact.LeftOperand, fact.RightOperand);
            var max = Math.Max(fact.LeftOperand, fact.RightOperand);
            return $"{fact.Operation}:{min}:{max}";
        }
        return fact.Id;
    }

    public ArithmeticFact SelectNextFact(
        LearnerProgression progression,
        IReadOnlyDictionary<string, ItemLearningState> itemStates,
        int currentSessionOrder,
        long fluentThresholdMs = LearningPolicy.DefaultFluentResponseThresholdMs)
    {
        return SelectNextFact(progression, itemStates, null, currentSessionOrder, progression.PracticePosition, fluentThresholdMs);
    }

    public ArithmeticFact SelectNextFact(
        LearnerProgression progression,
        IReadOnlyDictionary<string, ItemLearningState> itemStates,
        IReadOnlyDictionary<string, FsrsCardState>? fsrsStates,
        int currentSessionOrder,
        long currentPracticePosition,
        long fluentThresholdMs = LearningPolicy.DefaultFluentResponseThresholdMs)
    {
        var allActiveFacts = ArithmeticCatalog.GetActiveFacts(progression);
        if (allActiveFacts.Count == 0)
        {
            throw new InvalidOperationException("No active facts available in current progression.");
        }

        if (allActiveFacts.Count == 1)
        {
            RecordSelection(allActiveFacts[0]);
            return allActiveFacts[0];
        }

        // 1. Due same-session wrong-answer remediation (highest priority)
        var dueRemediation = allActiveFacts
            .Where(f => itemStates.TryGetValue(f.Id, out var st)
                        && st.NeedsRemediation
                        && currentSessionOrder >= st.RemediationDueOrder)
            .ToList();

        if (dueRemediation.Count > 0)
        {
            var minRemediationDue = dueRemediation.Min(f => itemStates[f.Id].RemediationDueOrder);
            var earliestDueGroup = dueRemediation.Where(f => itemStates[f.Id].RemediationDueOrder == minRemediationDue).ToList();
            var chosenRemediation = SelectDiverseCandidate(earliestDueGroup);
            RecordSelection(chosenRemediation);
            return chosenRemediation;
        }

        // 2. Determine Progression Phase
        var (phase, currentTurn, turnMaxOp, unexposedFacts) = LearningPolicy.DetermineProgressionPhase(progression, itemStates);

        // Tier 2: Current introduction turn unexposed facts (first exposure priority)
        if (phase is ProgressionPhase.IntroducingAddition
            or ProgressionPhase.IntroducingSubtraction
            or ProgressionPhase.IntroducingMultiplication
            or ProgressionPhase.IntroducingDivision)
        {
            if (unexposedFacts.Count > 0)
            {
                var chosenIntro = SelectDiverseCandidate(unexposedFacts);
                RecordSelection(chosenIntro);
                return chosenIntro;
            }
        }

        // Tier 3: Bounded Mixed Checkpoint Phase
        if (phase == ProgressionPhase.Checkpoint)
        {
            var checkpointEligibleFacts = ArithmeticCatalog.GetAllFacts(turnMaxOp);
            if (checkpointEligibleFacts.Count > 0)
            {
                // Sub-prioritization within checkpoint:
                // a) Overdue FSRS cards among eligible facts
                if (fsrsStates is { Count: > 0 })
                {
                    var overdueInCheckpoint = checkpointEligibleFacts
                        .Where(f => fsrsStates.TryGetValue(f.Id, out var c) && c.DuePracticePosition <= currentPracticePosition)
                        .ToList();

                    if (overdueInCheckpoint.Count > 0)
                    {
                        var minDue = overdueInCheckpoint.Min(f => fsrsStates[f.Id].DuePracticePosition);
                        var mostOverdueGroup = overdueInCheckpoint.Where(f => fsrsStates[f.Id].DuePracticePosition == minDue).ToList();
                        var chosenOverdue = SelectDiverseCandidate(mostOverdueGroup);
                        RecordSelection(chosenOverdue);
                        return chosenOverdue;
                    }
                }

                // b) Balanced operation sampling / weak facts
                var chosenCheckpointFact = SelectDiverseCandidate(checkpointEligibleFacts);
                RecordSelection(chosenCheckpointFact);
                return chosenCheckpointFact;
            }
        }

        // Tier 4: Due FSRS Cards (spaced repetition priority in open-ended practice)
        if (fsrsStates is { Count: > 0 })
        {
            var dueFsrsFacts = allActiveFacts
                .Where(f => fsrsStates.TryGetValue(f.Id, out var card) && card.DuePracticePosition <= currentPracticePosition)
                .ToList();

            if (dueFsrsFacts.Count > 0)
            {
                var minDue = dueFsrsFacts.Min(f => fsrsStates[f.Id].DuePracticePosition);
                var mostOverdueGroup = dueFsrsFacts.Where(f => fsrsStates[f.Id].DuePracticePosition == minDue).ToList();
                var chosenDue = SelectDiverseCandidate(mostOverdueGroup);
                RecordSelection(chosenDue);
                return chosenDue;
            }
        }

        // Tier 5: Mixed Practice Fallback (when no remediation is due and no FSRS cards are overdue)
        if (fsrsStates is { Count: > 0 })
        {
            var unpracticedInFsrs = allActiveFacts.Where(f => !fsrsStates.ContainsKey(f.Id)).ToList();
            if (unpracticedInFsrs.Count > 0)
            {
                var chosenUnpracticed = SelectDiverseCandidate(unpracticedInFsrs);
                RecordSelection(chosenUnpracticed);
                return chosenUnpracticed;
            }

            var minUpcomingDue = allActiveFacts.Min(f => fsrsStates[f.Id].DuePracticePosition);
            var earliestUpcoming = allActiveFacts.Where(f => fsrsStates[f.Id].DuePracticePosition == minUpcomingDue).ToList();
            var chosenUpcoming = SelectDiverseCandidate(earliestUpcoming);
            RecordSelection(chosenUpcoming);
            return chosenUpcoming;
        }

        var fallback = SelectDiverseCandidate(allActiveFacts);
        RecordSelection(fallback);
        return fallback;
    }

    public void ResetLastSelected()
    {
        _lastSelectedFactId = null;
        _recentFactIds.Clear();
        _recentCanonicalKeys.Clear();
        _recentOperations.Clear();
    }

    private ArithmeticFact SelectDiverseCandidate(IReadOnlyList<ArithmeticFact> candidates)
    {
        if (candidates.Count == 0)
        {
            throw new ArgumentException("Candidate list cannot be empty.", nameof(candidates));
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        // Tiered filter eliminating penalties where viable alternatives exist
        // Level 1: No exact duplicate (last 3), no mirror pair (last 3), no operation streak (last 2)
        var level1 = candidates
            .Where(f => !_recentFactIds.Contains(f.Id)
                        && !_recentCanonicalKeys.Contains(GetCanonicalMirrorKey(f))
                        && !IsOperationStreak(f.Operation))
            .ToList();

        if (level1.Count > 0)
        {
            return level1[_random.Next(level1.Count)];
        }

        // Level 2: No exact duplicate, no mirror pair (relaxing operation streak)
        var level2 = candidates
            .Where(f => !_recentFactIds.Contains(f.Id)
                        && !_recentCanonicalKeys.Contains(GetCanonicalMirrorKey(f)))
            .ToList();

        if (level2.Count > 0)
        {
            return level2[_random.Next(level2.Count)];
        }

        // Level 3: No exact duplicate (relaxing mirror pair)
        var level3 = candidates
            .Where(f => !_recentFactIds.Contains(f.Id))
            .ToList();

        if (level3.Count > 0)
        {
            return level3[_random.Next(level3.Count)];
        }

        // Level 4: Not immediate previous fact
        var level4 = candidates
            .Where(f => f.Id != _lastSelectedFactId)
            .ToList();

        if (level4.Count > 0)
        {
            return level4[_random.Next(level4.Count)];
        }

        // Level 5: Any candidate
        return candidates[_random.Next(candidates.Count)];
    }

    private bool IsOperationStreak(ArithmeticOperation operation)
    {
        if (_recentOperations.Count < LearningPolicy.MaxPreferredOperationStreak)
        {
            return false;
        }

        return _recentOperations
            .TakeLast(LearningPolicy.MaxPreferredOperationStreak)
            .All(op => op == operation);
    }

    private void RecordSelection(ArithmeticFact fact)
    {
        _lastSelectedFactId = fact.Id;

        _recentFactIds.Add(fact.Id);
        if (_recentFactIds.Count > LearningPolicy.ExactFactCooldownDistance)
        {
            _recentFactIds.RemoveAt(0);
        }

        _recentCanonicalKeys.Add(GetCanonicalMirrorKey(fact));
        if (_recentCanonicalKeys.Count > LearningPolicy.MirrorFactCooldownDistance)
        {
            _recentCanonicalKeys.RemoveAt(0);
        }

        _recentOperations.Add(fact.Operation);
        if (_recentOperations.Count > 10)
        {
            _recentOperations.RemoveAt(0);
        }
    }
}
