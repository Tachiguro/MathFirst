namespace MathFirst.Application.Practice;

using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

public sealed record PracticeSelectionResult
{
    public ArithmeticOperation ScheduledOperation { get; }
    public PracticeSelectionRole RequestedRole { get; }
    public PracticeSelectionRole ResolvedRole { get; }
    public ArithmeticFact Fact { get; }
    public bool IsMaterialized { get; }
    public bool IsNewIntroduction { get; }
    public CurriculumBandId CurrentBandId { get; }
    public PracticeCooldownRelaxation CooldownRelaxation { get; }

    internal PracticeSelectionResult(
        ArithmeticOperation scheduledOperation,
        PracticeSelectionRole requestedRole,
        PracticeSelectionRole resolvedRole,
        ArithmeticFact fact,
        bool isMaterialized,
        bool isNewIntroduction,
        CurriculumBandId currentBandId,
        PracticeCooldownRelaxation cooldownRelaxation)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(currentBandId);
        ScheduledOperation = scheduledOperation;
        RequestedRole = requestedRole;
        ResolvedRole = resolvedRole;
        Fact = fact;
        IsMaterialized = isMaterialized;
        IsNewIntroduction = isNewIntroduction;
        CurrentBandId = currentBandId;
        CooldownRelaxation = cooldownRelaxation;
    }
}
