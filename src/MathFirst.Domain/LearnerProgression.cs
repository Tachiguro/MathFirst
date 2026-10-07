namespace MathFirst.Domain;

using MathFirst.Domain.Curriculum;

public sealed class LearnerProgression
{
    public const int DefaultSchemaVersion = 9;

    public long PracticePosition { get; set; }
    public CurriculumStage CurriculumStage { get; set; } = CurriculumStage.Stage1_Addition;
    public Dictionary<ArithmeticOperation, OperationProgression> OperationProgressions { get; set; } = CreateInitialOperationProgressions();
    public long StoreRevision { get; set; } = 1;
    public int SchemaVersion { get; set; } = DefaultSchemaVersion;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static LearnerProgression CreateFresh() => new()
    {
        PracticePosition = 0,
        CurriculumStage = CurriculumStage.Stage1_Addition,
        OperationProgressions = CreateInitialOperationProgressions(),
        StoreRevision = 1,
        SchemaVersion = DefaultSchemaVersion,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public static Dictionary<ArithmeticOperation, OperationProgression> CreateInitialOperationProgressions() =>
        Enum.GetValues<ArithmeticOperation>().ToDictionary(
            operation => operation,
            operation => new OperationProgression(operation, bandIndex: 0, bandStartedPracticePosition: 0));
}
