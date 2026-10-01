namespace MathFirst.Application.Practice;

public sealed record AttemptPresentationContext(
    int ContextVersion,
    int? PresentedDeadlineMs,
    int ExpectedPaceMs,
    string ResolvedRole,
    int OperationBandBefore);
