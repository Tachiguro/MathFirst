namespace MathFirst.Domain;

public sealed class LearnerProgression
{
    public const int DefaultSchemaVersion = 5;

    public ArithmeticOperation CurrentOperation { get; set; } = ArithmeticOperation.Addition;
    public ArithmeticOperation CurrentIntroductionTurn { get; set; } = ArithmeticOperation.Addition;
    public long PracticePosition { get; set; } = 0;

    // V5 authoritative independent operation progression. The V4 members below are
    // retained temporarily for source compatibility, but are not used by the V5 runtime.
    public Dictionary<ArithmeticOperation, OperationProgression> OperationProgressions { get; set; } = CreateInitialOperationProgressions();

    public int CurrentMaxOperand
    {
        get => GetMaxOperand(CurrentOperation);
        set => SetMaxOperand(CurrentOperation, value);
    }

    public Dictionary<ArithmeticOperation, int> OperationMaxOperands { get; set; } = new()
    {
        [ArithmeticOperation.Addition] = ArithmeticCatalog.DefaultInitialMaxOperand,
        [ArithmeticOperation.Subtraction] = ArithmeticCatalog.DefaultInitialMaxOperand,
        [ArithmeticOperation.Multiplication] = ArithmeticCatalog.DefaultInitialMaxOperand,
        [ArithmeticOperation.Division] = ArithmeticCatalog.DefaultInitialMaxOperand
    };

    public int CompletedCheckpointLevel { get; set; } = 0;
    public int? ActiveCheckpointLevel { get; set; } = null;
    public int CheckpointAttemptCount { get; set; } = 0;
    public int CheckpointCorrectCount { get; set; } = 0;

    public bool IsInCheckpoint => ActiveCheckpointLevel.HasValue && ActiveCheckpointLevel.Value > 0;

    public bool IsAllIntroductionsComplete =>
        CompletedCheckpointLevel >= ArithmeticCatalog.MaxV1Operand
        && OperationMaxOperands.Values.All(m => m >= ArithmeticCatalog.MaxV1Operand);

    public long StoreRevision { get; set; } = 1;
    public int SchemaVersion { get; set; } = DefaultSchemaVersion;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int GetMaxOperand(ArithmeticOperation operation)
    {
        return OperationMaxOperands.TryGetValue(operation, out var max) ? max : ArithmeticCatalog.DefaultInitialMaxOperand;
    }

    public void SetMaxOperand(ArithmeticOperation operation, int maxOperand)
    {
        OperationMaxOperands[operation] = Math.Clamp(maxOperand, ArithmeticCatalog.DefaultInitialMaxOperand, ArithmeticCatalog.MaxV1Operand);
    }

    public static LearnerProgression CreateFresh() =>
        new()
        {
            CurrentOperation = ArithmeticOperation.Addition,
            CurrentIntroductionTurn = ArithmeticOperation.Addition,
            PracticePosition = 0,
            OperationProgressions = CreateInitialOperationProgressions(),
            OperationMaxOperands = new Dictionary<ArithmeticOperation, int>
            {
                [ArithmeticOperation.Addition] = ArithmeticCatalog.DefaultInitialMaxOperand,
                [ArithmeticOperation.Subtraction] = ArithmeticCatalog.DefaultInitialMaxOperand,
                [ArithmeticOperation.Multiplication] = ArithmeticCatalog.DefaultInitialMaxOperand,
                [ArithmeticOperation.Division] = ArithmeticCatalog.DefaultInitialMaxOperand
            },
            CompletedCheckpointLevel = 0,
            ActiveCheckpointLevel = null,
            CheckpointAttemptCount = 0,
            CheckpointCorrectCount = 0,
            StoreRevision = 1,
            SchemaVersion = DefaultSchemaVersion,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    public static Dictionary<ArithmeticOperation, OperationProgression> CreateInitialOperationProgressions() =>
        Enum.GetValues<ArithmeticOperation>().ToDictionary(
            operation => operation,
            operation => new OperationProgression(operation, bandIndex: 0, bandStartedPracticePosition: 0));
}
