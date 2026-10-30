namespace MathFirst.Application.Gameplay;

using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;

/// <summary>Complete combat-answer evidence for one completed sector visit. Calm Mode is excluded.</summary>
public sealed record CyberDefenseSectorRecap(
    string CompletionSubmissionId, long ResetEpoch, int Sector,
    int AnswerCount, int CorrectAnswerCount, long ActiveAnsweringTimeMs,
    long XpGained, int LevelUps)
{
    public decimal AccuracyPercentage => 100m * CorrectAnswerCount / AnswerCount;

    /// <summary>True only when every displayed metric is internally consistent; otherwise no totals may be shown.</summary>
    public bool HasValidCounts => AnswerCount > 0 && CorrectAnswerCount >= 0 && CorrectAnswerCount <= AnswerCount &&
        ActiveAnsweringTimeMs >= 0 && XpGained >= 0 && LevelUps >= 0;

    /// <summary>Wrong answers derived from the real answer counts, never estimated.</summary>
    public int? MistakeCount => HasValidCounts ? AnswerCount - CorrectAnswerCount : null;

    /// <summary>
    /// Applied receipts earn strictly positive XP. Cumulative XP, rather than wall time, is their
    /// durable transaction order. The complete epoch must chain from zero without missing rewards.
    /// A sector visit starts after the most recent boss clear or game-over, excluding that boundary.
    /// </summary>
    public static CyberDefenseSectorRecap? FromReceipts(
        IReadOnlyList<CyberDefenseReceiptRecord> receipts, string completionSubmissionId, long resetEpoch)
    {
        var applied = receipts.Where(r => r.ReceiptKind == CyberDefenseReceiptKind.Applied).ToArray();
        if (applied.Any(r => r.ResetEpoch != resetEpoch || !r.IsEligible || r.ResponseLatencyMs <= 0 || r.AwardedXp is null or <= 0 ||
            r.CumulativeXpAfter is null || r.PlayerLevelAfter is null || r.TransitionResult is null)) return null;
        var ordered = applied.OrderBy(r => r.CumulativeXpAfter!.Value).ToArray();
        if (ordered.Select(r => r.SubmissionId).Distinct(StringComparer.Ordinal).Count() != ordered.Length) return null;

        try
        {
            long previousXp = 0, sectorStartXp = 0, activeTime = 0;
            int previousLevel = 1, sectorStartLevel = 1, sector = 1, answers = 0, correct = 0;
            foreach (var receipt in ordered)
            {
                var transition = receipt.TransitionResult!;
                if (checked(previousXp + receipt.AwardedXp!.Value) != receipt.CumulativeXpAfter ||
                    CyberDefenseLevelPolicy.GetLevel(receipt.CumulativeXpAfter!.Value) != receipt.PlayerLevelAfter)
                    return null;
                var expectedNextSector = transition.IsGameOver ? 1 : transition.IsSectorCompleted ? checked(sector + 1) : sector;
                if (transition.NextState.Sector != expectedNextSector ||
                    (transition.IsGameOver && transition.TerminalSnapshot?.Sector != sector)) return null;
                answers = checked(answers + 1);
                if (receipt.IsCorrect) correct = checked(correct + 1);
                activeTime = checked(activeTime + receipt.ResponseLatencyMs);
                previousXp = receipt.CumulativeXpAfter.Value;
                previousLevel = receipt.PlayerLevelAfter!.Value;
                if (receipt.SubmissionId == completionSubmissionId)
                {
                    return transition.IsSectorCompleted
                        ? new(completionSubmissionId, resetEpoch, sector, answers, correct, activeTime,
                            previousXp - sectorStartXp, previousLevel - sectorStartLevel)
                        : null;
                }
                if (transition.IsSectorCompleted || transition.IsGameOver)
                {
                    sector = expectedNextSector;
                    sectorStartXp = previousXp;
                    sectorStartLevel = previousLevel;
                    answers = correct = 0;
                    activeTime = 0;
                }
            }
        }
        catch (OverflowException) { return null; }
        return null;
    }
}
