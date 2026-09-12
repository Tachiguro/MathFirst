namespace MathFirst.Application.Practice;

using MathFirst.Application.Diagnostics;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

public static class PracticeProgressOverview
{
    public static IReadOnlyList<OperationProgressDiagnostics> Create(
        LearnerProgression progression,
        ArithmeticCurriculum curriculum,
        IEnumerable<ArithmeticOperation> enabledOperations,
        bool hasCompletedPracticeHistory)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(curriculum);
        ArgumentNullException.ThrowIfNull(enabledOperations);

        if (!hasCompletedPracticeHistory)
        {
            return [];
        }

        var enabledSet = PracticeOperationPreferencePolicy
            .NormalizeEnabledOperations(enabledOperations)
            .ToHashSet();

        return LearningProgressDiagnostics
            .Create(progression, curriculum)
            .Operations
            .Where(progress => enabledSet.Contains(progress.Operation))
            .ToArray();
    }
}
