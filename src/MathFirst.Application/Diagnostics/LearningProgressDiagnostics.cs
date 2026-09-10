namespace MathFirst.Application.Diagnostics;

using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

public sealed record LearningProgressDiagnostics(
    long PracticePosition,
    IReadOnlyList<OperationProgressDiagnostics> Operations)
{
    private static readonly ArithmeticOperation[] CanonicalOperations =
    [
        ArithmeticOperation.Addition,
        ArithmeticOperation.Subtraction,
        ArithmeticOperation.Multiplication,
        ArithmeticOperation.Division
    ];

    public static LearningProgressDiagnostics Create(
        LearnerProgression progression,
        ArithmeticCurriculum curriculum)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(curriculum);

        var operations = CanonicalOperations
            .Select(operation => CreateOperationDiagnostics(progression, curriculum, operation))
            .ToArray();

        return new LearningProgressDiagnostics(progression.PracticePosition, Array.AsReadOnly(operations));
    }

    private static OperationProgressDiagnostics CreateOperationDiagnostics(
        LearnerProgression progression,
        ArithmeticCurriculum curriculum,
        ArithmeticOperation operation)
    {
        if (!progression.OperationProgressions.TryGetValue(operation, out var operationProgression) ||
            operationProgression.Operation != operation)
        {
            return new OperationProgressDiagnostics(operation, null, null, null);
        }

        var operationCurriculum = curriculum.GetCurriculum(operation);
        var curriculumBandId = operationCurriculum.TryGetBand(operationProgression.BandIndex, out var band)
            ? band!.Id.Value
            : null;

        return new OperationProgressDiagnostics(
            operation,
            operationProgression.BandIndex,
            curriculumBandId,
            operationProgression.BandStartedPracticePosition);
    }
}

public sealed record OperationProgressDiagnostics(
    ArithmeticOperation Operation,
    int? BandIndex,
    string? CurriculumBandId,
    long? BandStartedPracticePosition)
{
    public int? PresentationStage => CurriculumBandId is not null && BandIndex is >= 0 and < int.MaxValue
        ? BandIndex + 1
        : null;
}
