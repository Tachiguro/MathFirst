namespace MathFirst.Application.Gameplay;

using System;

public enum UpgradePurchaseStatus
{
    Success,
    InsufficientSkillPoints,
    StaleRank,
    StaleResetEpoch,
    MaxRankReached,
    Busy,
    Disposed,
    Cancelled,
    Error,
    LockedLevel
}

public sealed record UpgradePurchaseResult
{
    public UpgradePurchaseStatus Status { get; }
    public bool IsSuccess => Status == UpgradePurchaseStatus.Success;
    public UpgradeTerminalStatus? StatusSnapshot { get; }
    public UpgradeTerminalStatus? UpdatedStatus => StatusSnapshot;
    public string? ErrorMessage { get; }
    public Exception? Exception { get; }

    public UpgradePurchaseResult(
        UpgradePurchaseStatus status,
        UpgradeTerminalStatus? statusSnapshot,
        string? errorMessage,
        Exception? exception)
    {
        Status = status;
        StatusSnapshot = statusSnapshot;
        ErrorMessage = errorMessage;
        Exception = exception;
    }

    public static UpgradePurchaseResult Success(UpgradeTerminalStatus updatedStatus) =>
        new(UpgradePurchaseStatus.Success, updatedStatus, null, null);

    public static UpgradePurchaseResult InsufficientSkillPoints(UpgradeTerminalStatus currentStatus, string? message = null) =>
        new(UpgradePurchaseStatus.InsufficientSkillPoints, currentStatus, message ?? "Insufficient skill points.", null);

    public static UpgradePurchaseResult StaleRank(UpgradeTerminalStatus currentStatus, string? message = null) =>
        new(UpgradePurchaseStatus.StaleRank, currentStatus, message ?? "Stale attack rank conflict.", null);

    public static UpgradePurchaseResult StaleResetEpoch(UpgradeTerminalStatus currentStatus, string? message = null) =>
        new(UpgradePurchaseStatus.StaleResetEpoch, currentStatus, message ?? "Stale reset epoch conflict.", null);

    public static UpgradePurchaseResult MaxRankReached(UpgradeTerminalStatus currentStatus, string? message = null) =>
        new(UpgradePurchaseStatus.MaxRankReached, currentStatus, message ?? "Maximum attack rank reached.", null);

    public static UpgradePurchaseResult Busy(UpgradeTerminalStatus? currentStatus = null, string? message = null) =>
        new(UpgradePurchaseStatus.Busy, currentStatus, message ?? "A purchase operation is already in progress.", null);

    public static UpgradePurchaseResult Disposed(UpgradeTerminalStatus? currentStatus = null, string? message = null) =>
        new(UpgradePurchaseStatus.Disposed, currentStatus, message ?? "The upgrade terminal has been disposed.", null);

    public static UpgradePurchaseResult Cancelled(UpgradeTerminalStatus? currentStatus = null, string? message = null) =>
        new(UpgradePurchaseStatus.Cancelled, currentStatus, message ?? "The purchase operation was cancelled.", null);

    public static UpgradePurchaseResult Error(string errorMessage, Exception? exception = null, UpgradeTerminalStatus? currentStatus = null) =>
        new(UpgradePurchaseStatus.Error, currentStatus, errorMessage, exception);

    public static UpgradePurchaseResult LockedLevel(UpgradeTerminalStatus currentStatus, string? message = null) =>
        new(UpgradePurchaseStatus.LockedLevel, currentStatus, message ?? "Player level requirement not met.", null);
}
