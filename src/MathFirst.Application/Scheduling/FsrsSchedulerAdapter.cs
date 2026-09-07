namespace MathFirst.Application.Scheduling;

using System.Security.Cryptography;
using System.Text;
using FSRS.Core.Constants;
using FSRS.Core.Enums;
using FSRS.Core.Interfaces;
using FSRS.Core.Models;
using FSRS.Core.Services;

public sealed class FsrsSchedulerAdapter : IFsrsScheduler
{
    public const double DefaultV1DesiredRetention = 0.95;
    public static readonly DateTime DefaultVirtualEpoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IScheduler _scheduler;

    public double DesiredRetention => _scheduler.DesiredRetention;
    public IReadOnlyList<double> Parameters => _scheduler.Parameters;
    public bool EnableFuzzing => _scheduler.EnableFuzzing;
    public DateTime VirtualEpoch { get; }

    public FsrsSchedulerAdapter(
        double desiredRetention = DefaultV1DesiredRetention,
        double[]? parameters = null,
        DateTime? virtualEpoch = null)
    {
        VirtualEpoch = virtualEpoch ?? DefaultVirtualEpoch;
        var p = parameters ?? FsrsConstants.DefaultParameters;

        var diffCalc = new DifficultyCalculator();
        var stabCalc = new StabilityCalculator();
        var retCalc = new RetrievabilityCalculator();
        var intCalc = new IntervalCalculator();
        var fuzzServ = new FuzzingService();

        _scheduler = new Scheduler(
            desiredRetention,
            p,
            Array.Empty<TimeSpan>(),
            Array.Empty<TimeSpan>(),
            maximumInterval: 36500,
            enableFuzzing: false,
            retCalc,
            stabCalc,
            diffCalc,
            intCalc,
            fuzzServ);
    }

    public static Guid CreateDeterministicCardId(string factId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factId);
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("MathFirst:FsrsCard:" + factId));
        return new Guid(hash);
    }

    public FsrsCardState ReviewCard(
        FsrsCardState? existingState,
        string factId,
        FsrsRating rating,
        long reviewPracticePosition,
        long reviewDurationMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factId);

        var cardId = existingState?.CardId ?? CreateDeterministicCardId(factId);
        var virtualReviewTime = VirtualEpoch.AddDays(reviewPracticePosition);

        Card card;
        if (existingState is null)
        {
            card = new Card(
                cardId,
                State.Learning,
                step: null,
                stability: null,
                difficulty: null,
                due: virtualReviewTime,
                lastReview: null);
        }
        else
        {
            var dueTime = VirtualEpoch.AddDays(existingState.DuePracticePosition);
            var lastReviewTime = existingState.LastReviewPracticePosition.HasValue
                ? VirtualEpoch.AddDays(existingState.LastReviewPracticePosition.Value)
                : (DateTime?)null;

            card = new Card(
                cardId,
                (State)existingState.State,
                existingState.Step,
                existingState.Stability,
                existingState.Difficulty,
                due: dueTime,
                lastReview: lastReviewTime);
        }

        var fsrsRating = (Rating)rating;
        var duration = (int)Math.Clamp(reviewDurationMs, 0, int.MaxValue);

        var (updatedCard, _) = _scheduler.ReviewCard(card, fsrsRating, virtualReviewTime, duration);

        var dueDays = (updatedCard.Due - VirtualEpoch).TotalDays;
        var duePracticePosition = (long)Math.Ceiling(dueDays);

        // Normalize: Due position must not be earlier than next task slot
        duePracticePosition = Math.Max(reviewPracticePosition + 1, duePracticePosition);

        return new FsrsCardState(
            factId,
            cardId,
            (int)updatedCard.State,
            updatedCard.Step,
            updatedCard.Stability,
            updatedCard.Difficulty,
            duePracticePosition,
            reviewPracticePosition,
            rating);
    }
}
