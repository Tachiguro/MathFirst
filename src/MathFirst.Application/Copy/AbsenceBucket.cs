namespace MathFirst.Application.Copy;

/// <summary>
/// Classifies how long the learner has been absent from practice,
/// derived from the authoritative latest accepted-practice timestamp.
/// Used to select contextual "return" copy without exposing raw timestamps.
///
/// Thresholds:
///   Recent       — less than 30 minutes, or no prior accepted practice
///   ShortAbsence — 30 minutes to less than 3 days
///   LongAbsence  — 3 days or more
/// </summary>
public enum AbsenceBucket
{
    /// <summary>No meaningful elapsed absence; less than 30 minutes or no prior practice.</summary>
    Recent,

    /// <summary>Returned after at least 30 minutes and less than 3 days.</summary>
    ShortAbsence,

    /// <summary>Returned after 3 days or more.</summary>
    LongAbsence,
}

/// <summary>
/// Stateless helper that classifies an absence duration into an <see cref="AbsenceBucket"/>.
/// The helper receives a frozen current timestamp and performs no I/O or mutations.
/// </summary>
public static class AbsenceBucketHelper
{
    private static readonly TimeSpan RecentThreshold = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ShortAbsenceThreshold = TimeSpan.FromDays(3);

    /// <summary>
    /// Classifies the absence duration given the timestamp of the learner's last accepted attempt.
    /// </summary>
    /// <param name="lastPracticedAt">
    /// The UTC timestamp of the most recently accepted attempt, or <c>null</c> if no attempt
    /// has ever been recorded.
    /// </param>
    /// <param name="now">The single current timestamp captured for this gate activation.</param>
    /// <returns>The appropriate <see cref="AbsenceBucket"/>.</returns>
    public static AbsenceBucket Classify(DateTimeOffset? lastPracticedAt, DateTimeOffset now)
    {
        if (lastPracticedAt is null)
        {
            return AbsenceBucket.Recent;
        }

        var elapsed = now - lastPracticedAt.Value;

        if (elapsed < RecentThreshold)
        {
            return AbsenceBucket.Recent;
        }

        if (elapsed < ShortAbsenceThreshold)
        {
            return AbsenceBucket.ShortAbsence;
        }

        return AbsenceBucket.LongAbsence;
    }
}
