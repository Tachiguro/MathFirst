namespace MathFirst.Application.Copy;

/// <summary>
/// Classifies how long the learner has been absent from practice,
/// derived from the most recent persisted <c>ItemLearningState.LastPracticedAt</c>.
/// Used to select contextual "return" copy without exposing raw timestamps.
///
/// Thresholds:
///   SameSession  — less than 30 minutes
///   RecentReturn — 30 minutes to 23 hours 59 minutes
///   ShortAbsence — 1 day to 2 days 23 hours 59 minutes
///   LongAbsence  — 3 days or more
/// </summary>
public enum AbsenceBucket
{
    /// <summary>No meaningful absence; same session or first-ever use.</summary>
    SameSession,

    /// <summary>Returned within the same rough day (30 min – 24 h).</summary>
    RecentReturn,

    /// <summary>Returned after 1 to 3 days.</summary>
    ShortAbsence,

    /// <summary>Returned after more than 3 days.</summary>
    LongAbsence,
}

/// <summary>
/// Stateless helper that classifies an absence duration into an <see cref="AbsenceBucket"/>.
/// The helper reads only the current wall-clock time (via <see cref="DateTimeOffset.UtcNow"/>)
/// and performs no I/O or mutations.
/// </summary>
public static class AbsenceBucketHelper
{
    private static readonly TimeSpan SameSessionThreshold = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ShortAbsenceThreshold = TimeSpan.FromDays(3);

    /// <summary>
    /// Classifies the absence duration given the timestamp of the learner's last accepted attempt.
    /// </summary>
    /// <param name="lastPracticedAt">
    /// The UTC timestamp of the most recently accepted attempt, or <c>null</c> if no attempt
    /// has ever been recorded (first-ever session or empty item states).
    /// </param>
    /// <returns>The appropriate <see cref="AbsenceBucket"/>.</returns>
    public static AbsenceBucket Classify(DateTimeOffset? lastPracticedAt)
    {
        if (lastPracticedAt is null)
        {
            return AbsenceBucket.SameSession;
        }

        var elapsed = DateTimeOffset.UtcNow - lastPracticedAt.Value;

        if (elapsed < SameSessionThreshold)
        {
            return AbsenceBucket.SameSession;
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return AbsenceBucket.RecentReturn;
        }

        if (elapsed < ShortAbsenceThreshold)
        {
            return AbsenceBucket.ShortAbsence;
        }

        return AbsenceBucket.LongAbsence;
    }
}
