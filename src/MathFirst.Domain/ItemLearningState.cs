namespace MathFirst.Domain;

public sealed class ItemLearningState
{
    public string FactId { get; init; } = string.Empty;
    public ArithmeticOperation Operation { get; init; }
    public int LeftOperand { get; init; }
    public int RightOperand { get; init; }
    public int TotalAttempts { get; set; }
    public int CorrectAttempts { get; set; }
    public int IncorrectAttempts { get; set; }
    public int ConsecutiveCorrectStreak { get; set; }
    public long LastLatencyMs { get; set; }
    public long RollingLatencyMs { get; set; }
    public int FluentStreak { get; set; }
    public bool IsProvisionallyMastered { get; set; }
    public bool NeedsRemediation { get; set; }
    public int RemediationDueOrder { get; set; }
    public int LastPracticedOrder { get; set; }
    public DateTimeOffset? LastPracticedAt { get; set; }

    public static ItemLearningState CreateNew(ArithmeticFact fact) =>
        new()
        {
            FactId = fact.Id,
            Operation = fact.Operation,
            LeftOperand = fact.LeftOperand,
            RightOperand = fact.RightOperand,
            TotalAttempts = 0,
            CorrectAttempts = 0,
            IncorrectAttempts = 0,
            ConsecutiveCorrectStreak = 0,
            LastLatencyMs = 0,
            RollingLatencyMs = 0,
            FluentStreak = 0,
            IsProvisionallyMastered = false,
            NeedsRemediation = false,
            RemediationDueOrder = 0,
            LastPracticedOrder = 0,
            LastPracticedAt = null
        };
}
