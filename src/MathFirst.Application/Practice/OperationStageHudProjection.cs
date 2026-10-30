namespace MathFirst.Application.Practice;

using MathFirst.Application.Diagnostics;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

/// <summary>
/// Presentation projection for the per-operation arithmetic Stage strip. It selects which operations are shown
/// and reads each one's own persisted progression; it never changes selection, mastery or scheduling.
/// </summary>
public static class OperationStageHudProjection
{
    public static IReadOnlyList<OperationProgressDiagnostics> Create(
        LearnerProgression progression,
        ArithmeticCurriculum curriculum,
        PracticeMode practiceMode,
        IEnumerable<ArithmeticOperation> preferenceEnabledOperations)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(curriculum);
        ArgumentNullException.ThrowIfNull(preferenceEnabledOperations);

        // CurriculumManaged shows exactly what the curriculum has unlocked, never the practice preference.
        // Custom keeps the preference semantics the session itself uses for selection.
        var shown = practiceMode == PracticeMode.CurriculumManaged
            ? CurriculumUnlockPolicy.GetUnlockedOperations(progression.CurriculumStage).ToHashSet()
            : PracticeOperationPreferencePolicy.NormalizeEnabledOperations(preferenceEnabledOperations).ToHashSet();

        // Canonical order comes from the diagnostics; each entry carries that operation's own persisted stage.
        return LearningProgressDiagnostics.Create(progression, curriculum).Operations
            .Where(progress => shown.Contains(progress.Operation))
            .ToArray();
    }
}
