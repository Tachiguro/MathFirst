namespace MathFirst.Application.Copy;

/// <summary>
/// Identifies the reason a practice gate is being shown.
/// Used as the primary selector key for contextual copy.
/// Values map to copy pool keys in the format "{Trigger}.{Tone}.{Index}".
/// </summary>
public enum PracticeCopyTrigger
{
    /// <summary>
    /// The very first session of a brand-new learner (PracticePosition == 0, no item history).
    /// </summary>
    FirstEverReady,

    /// <summary>
    /// Normal start-of-session gate when the learner returns within a short time (SameSession absence bucket).
    /// </summary>
    InitialReady,

    /// <summary>
    /// Learner returns after a short absence (30 minutes to 3 days).
    /// </summary>
    ReturnShortAbsence,

    /// <summary>
    /// Learner returns after a longer absence (more than 3 days).
    /// </summary>
    ReturnLongAbsence,

    /// <summary>
    /// Learner explicitly paused practice and is resuming.
    /// </summary>
    ResumeManualPause,

    /// <summary>
    /// App was backgrounded (e.g. lock screen, switch app) and learner has returned.
    /// </summary>
    ResumeBackground,

    /// <summary>
    /// Neutral fallback for any InitialReadyGate state when no specific trigger applies.
    /// </summary>
    NeutralReady,

    /// <summary>
    /// Neutral fallback for ManualPause / BackgroundResumeGate when no specific trigger applies.
    /// </summary>
    NeutralPaused,
}
