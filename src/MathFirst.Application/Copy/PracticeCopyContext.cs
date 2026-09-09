namespace MathFirst.Application.Copy;

using MathFirst.Domain;

/// <summary>
/// Immutable presentation-only snapshot of the context in which a practice gate is shown.
/// Populated read-only from <see cref="MathFirst.Application.TrainingSession"/> state.
///
/// Invariants:
/// - Construction performs NO mutations on TrainingSession.
/// - No persistence I/O is triggered.
/// - No attempt is consumed, no timing is affected, no selection evidence is changed.
/// </summary>
/// <param name="Trigger">The resolved trigger that identifies why this gate is shown.</param>
/// <param name="PracticePosition">Global lifetime accepted-attempt count from <c>Session.Progression.PracticePosition</c>.</param>
/// <param name="SessionCorrectCount">Correct accepted submissions in the current app session.</param>
/// <param name="SessionTotalCount">Total accepted submissions in the current app session.</param>
/// <param name="AbsenceBucket">Derived absence duration bucket.</param>
/// <param name="Locale">Active UI locale code ("en", "de", or "ru").</param>
public sealed record PracticeCopyContext(
    PracticeCopyTrigger Trigger,
    long PracticePosition,
    int SessionCorrectCount,
    int SessionTotalCount,
    AbsenceBucket AbsenceBucket,
    string Locale)
{
    /// <summary>
    /// Builds a <see cref="PracticeCopyContext"/> from live <see cref="TrainingSession"/> state.
    /// This is a pure read operation — no mutations are performed.
    /// </summary>
    public static PracticeCopyContext FromSession(
        TrainingSession session,
        string locale,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);

        var practicePosition = session.Progression.PracticePosition;
        var lastPracticedAt = session.LatestAcceptedPracticeAt;
        var absenceBucket = AbsenceBucketHelper.Classify(lastPracticedAt, now);

        var trigger = ResolveTrigger(session.PracticeGate, lastPracticedAt, absenceBucket);

        return new PracticeCopyContext(
            Trigger: trigger,
            PracticePosition: practicePosition,
            SessionCorrectCount: session.SessionCorrectCount,
            SessionTotalCount: session.SessionTotalCount,
            AbsenceBucket: absenceBucket,
            Locale: LanguagePreferencePolicy.Normalize(locale));
    }

    private static PracticeCopyTrigger ResolveTrigger(
        PracticeGateState gate,
        DateTimeOffset? lastPracticedAt,
        AbsenceBucket absenceBucket)
    {
        return gate switch
        {
            PracticeGateState.ManualPause => PracticeCopyTrigger.ResumeManualPause,
            PracticeGateState.BackgroundResumeGate => PracticeCopyTrigger.ResumeBackground,
            PracticeGateState.InitialReadyGate when lastPracticedAt is null => PracticeCopyTrigger.InitialReady,
            PracticeGateState.InitialReadyGate => absenceBucket switch
            {
                AbsenceBucket.LongAbsence => PracticeCopyTrigger.ReturnLongAbsence,
                AbsenceBucket.ShortAbsence => PracticeCopyTrigger.ReturnShortAbsence,
                _ => PracticeCopyTrigger.InitialReady,
            },
            _ => PracticeCopyTrigger.NeutralReady,
        };
    }
}
