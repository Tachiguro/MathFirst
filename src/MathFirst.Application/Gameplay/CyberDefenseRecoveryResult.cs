namespace MathFirst.Application.Gameplay;

using System;
using System.Collections.Generic;
using MathFirst.Application.Persistence;

public sealed record CyberDefenseRecoveryResult
{
    public int RecoveredCount => Receipts.Count;
    public IReadOnlyList<CyberDefenseReceiptRecord> Receipts { get; }

    public CyberDefenseRecoveryResult(IReadOnlyList<CyberDefenseReceiptRecord> receipts)
    {
        Receipts = receipts ?? Array.Empty<CyberDefenseReceiptRecord>();
    }

    public static CyberDefenseRecoveryResult Empty() =>
        new(Array.Empty<CyberDefenseReceiptRecord>());

    public static CyberDefenseRecoveryResult Completed(IReadOnlyList<CyberDefenseReceiptRecord> receipts) =>
        new(receipts);
}
