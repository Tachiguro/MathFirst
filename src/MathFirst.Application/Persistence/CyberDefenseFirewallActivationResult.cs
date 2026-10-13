namespace MathFirst.Application.Persistence;

using System;
using MathFirst.Domain.CyberDefense;

public enum CyberDefenseFirewallActivationStatus
{
    Applied,
    AlreadyCurrent,
    DeferredPendingIntent,
    RejectedResetPending,
    RejectedEpochMismatch,
    Failed
}

public sealed record CyberDefenseFirewallActivationResult
{
    public CyberDefenseFirewallActivationStatus Status { get; }
    public CyberDefenseRunState? RunState { get; }
    public string? Reason { get; }

    private CyberDefenseFirewallActivationResult(
        CyberDefenseFirewallActivationStatus status,
        CyberDefenseRunState? runState,
        string? reason)
    {
        Status = status;
        RunState = runState;
        Reason = reason;
    }

    public static CyberDefenseFirewallActivationResult Applied(CyberDefenseRunState runState)
    {
        ArgumentNullException.ThrowIfNull(runState);
        return new CyberDefenseFirewallActivationResult(CyberDefenseFirewallActivationStatus.Applied, runState, null);
    }

    public static CyberDefenseFirewallActivationResult AlreadyCurrent(CyberDefenseRunState runState)
    {
        ArgumentNullException.ThrowIfNull(runState);
        return new CyberDefenseFirewallActivationResult(CyberDefenseFirewallActivationStatus.AlreadyCurrent, runState, null);
    }

    public static CyberDefenseFirewallActivationResult DeferredPendingIntent(CyberDefenseRunState runState, string reason)
    {
        ArgumentNullException.ThrowIfNull(runState);
        return new CyberDefenseFirewallActivationResult(CyberDefenseFirewallActivationStatus.DeferredPendingIntent, runState, reason);
    }

    public static CyberDefenseFirewallActivationResult RejectedResetPending(string reason)
    {
        return new CyberDefenseFirewallActivationResult(CyberDefenseFirewallActivationStatus.RejectedResetPending, null, reason);
    }

    public static CyberDefenseFirewallActivationResult RejectedEpochMismatch(string reason)
    {
        return new CyberDefenseFirewallActivationResult(CyberDefenseFirewallActivationStatus.RejectedEpochMismatch, null, reason);
    }

    public static CyberDefenseFirewallActivationResult Failed(string reason)
    {
        return new CyberDefenseFirewallActivationResult(CyberDefenseFirewallActivationStatus.Failed, null, reason);
    }
}
