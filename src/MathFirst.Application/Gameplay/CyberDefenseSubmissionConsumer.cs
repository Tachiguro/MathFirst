namespace MathFirst.Application.Gameplay;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;

public sealed class CyberDefenseSubmissionConsumer : ICyberDefenseSubmissionConsumer
{
    private readonly IGameplayStore _gameplayStore;
    private readonly ILearnerStore _learnerStore;

    public CyberDefenseSubmissionConsumer(
        IGameplayStore gameplayStore,
        ILearnerStore learnerStore)
    {
        _gameplayStore = gameplayStore ?? throw new ArgumentNullException(nameof(gameplayStore));
        _learnerStore = learnerStore ?? throw new ArgumentNullException(nameof(learnerStore));
    }

    public Task StageIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return _gameplayStore.StagePendingIntentAsync(intent, cancellationToken);
    }

    public async Task<CyberDefenseConsumptionResult> ConsumeAttemptAsync(string submissionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(submissionId))
        {
            throw new ArgumentException("Submission ID cannot be null or whitespace.", nameof(submissionId));
        }

        var resetIntent = await _gameplayStore.GetResetIntentAsync(cancellationToken).ConfigureAwait(false);
        if (resetIntent.IsPending)
        {
            throw new InvalidOperationException("Gameplay consumption is rejected while a reset intent is pending.");
        }

        var existingReceipt = await _gameplayStore.GetReceiptAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (existingReceipt is not null)
        {
            var leftoverIntent = await _gameplayStore.GetPendingIntentAsync(submissionId, cancellationToken).ConfigureAwait(false);
            if (leftoverIntent is not null)
            {
                if (!leftoverIntent.PracticePosition.HasValue)
                {
                    var leftoverEvidence = await _learnerStore.GetCommittedAttemptEvidenceAsync(submissionId, cancellationToken).ConfigureAwait(false);
                    if (leftoverEvidence is not null)
                    {
                        leftoverIntent = VerifyAndResolveIntentAgainstEvidence(leftoverIntent, leftoverEvidence);
                        if (leftoverIntent.PracticePosition.HasValue)
                        {
                            await _gameplayStore.StagePendingIntentAsync(leftoverIntent, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                existingReceipt = await _gameplayStore.ApplyAttemptTransactionAsync(leftoverIntent, cancellationToken).ConfigureAwait(false);
            }

            return CyberDefenseConsumptionResult.AlreadyConsumed(existingReceipt);
        }

        var intent = await _gameplayStore.GetPendingIntentAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (intent is null)
        {
            // Another consumer may have committed after our first receipt read and removed
            // the pending row atomically. Only the authoritative receipt establishes success.
            var concurrentReceipt = await _gameplayStore.GetReceiptAsync(submissionId, cancellationToken).ConfigureAwait(false);
            if (concurrentReceipt is not null)
            {
                return CyberDefenseConsumptionResult.AlreadyConsumed(concurrentReceipt);
            }

            return CyberDefenseConsumptionResult.NoPendingIntent(submissionId);
        }

        var evidence = await _learnerStore.GetCommittedAttemptEvidenceAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (evidence is null)
        {
            return CyberDefenseConsumptionResult.UncommittedLearnerAttempt(submissionId);
        }

        var resolvedTargetIntent = VerifyAndResolveIntentAgainstEvidence(intent, evidence);

        var currentEpoch = await _gameplayStore.GetResetEpochAsync(cancellationToken).ConfigureAwait(false);
        if (intent.ResetEpoch != currentEpoch)
        {
            throw new InvalidOperationException(
                $"Pending intent reset epoch {intent.ResetEpoch} does not match current store reset epoch {currentEpoch}.");
        }

        var pendingIntents = await _gameplayStore.GetPendingIntentsAsync(currentEpoch, cancellationToken).ConfigureAwait(false);
        var resolvedQueue = new List<CyberDefensePendingIntentRecord>(pendingIntents.Count);
        var reconciliations = new List<CyberDefensePendingIntentRecord>();
        bool foundTargetInPending = false;

        foreach (var pending in pendingIntents)
        {
            if (string.Equals(pending.SubmissionId, submissionId, StringComparison.Ordinal))
            {
                foundTargetInPending = true;
                resolvedQueue.Add(resolvedTargetIntent);
                if (!pending.PracticePosition.HasValue && resolvedTargetIntent.PracticePosition.HasValue)
                {
                    reconciliations.Add(resolvedTargetIntent);
                }
            }
            else
            {
                var otherEvidence = await _learnerStore.GetCommittedAttemptEvidenceAsync(pending.SubmissionId, cancellationToken).ConfigureAwait(false);
                if (otherEvidence is not null)
                {
                    var resolvedOther = VerifyAndResolveIntentAgainstEvidence(pending, otherEvidence);
                    resolvedQueue.Add(resolvedOther);
                    if (!pending.PracticePosition.HasValue && resolvedOther.PracticePosition.HasValue)
                    {
                        reconciliations.Add(resolvedOther);
                    }
                }
                else
                {
                    resolvedQueue.Add(pending);
                }
            }
        }

        if (!foundTargetInPending)
        {
            var concurrentReceipt = await _gameplayStore.GetReceiptAsync(submissionId, cancellationToken).ConfigureAwait(false);
            if (concurrentReceipt is not null)
            {
                return CyberDefenseConsumptionResult.AlreadyConsumed(concurrentReceipt);
            }

            return CyberDefenseConsumptionResult.NoPendingIntent(submissionId);
        }

        // Validate the whole queue before persisting any reconciliations. A timestamp or
        // submission ID cannot establish order when committed position evidence is missing.
        if (resolvedQueue.Count > 1 && resolvedQueue.Exists(p => !p.PracticePosition.HasValue))
        {
            throw new InvalidOperationException(
                "Cannot consume an ambiguously ordered pending queue without authoritative committed PracticePosition evidence.");
        }

        ValidateAndSortPendingQueue(resolvedQueue);

        if (resolvedQueue.Count > 0 && !string.Equals(resolvedQueue[0].SubmissionId, submissionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot consume pending intent '{submissionId}' out of order while head pending intent '{resolvedQueue[0].SubmissionId}' is unsettled.");
        }

        foreach (var reconciled in reconciliations)
        {
            if (await _gameplayStore.GetReceiptAsync(reconciled.SubmissionId, cancellationToken).ConfigureAwait(false) is null)
            {
                await _gameplayStore.StagePendingIntentAsync(reconciled, cancellationToken).ConfigureAwait(false);
            }
        }

        var receipt = await _gameplayStore.ApplyAttemptTransactionAsync(resolvedTargetIntent, cancellationToken).ConfigureAwait(false);
        return CyberDefenseConsumptionResult.Success(receipt);
    }

    public async Task<CyberDefenseRecoveryResult> RecoverPendingIntentsAsync(CancellationToken cancellationToken = default)
    {
        var resetIntent = await _gameplayStore.GetResetIntentAsync(cancellationToken).ConfigureAwait(false);
        if (resetIntent.IsPending)
        {
            throw new InvalidOperationException("Pending intent recovery cannot proceed while a reset intent is pending.");
        }

        var currentEpoch = await _gameplayStore.GetResetEpochAsync(cancellationToken).ConfigureAwait(false);
        var pendingIntents = await _gameplayStore.GetPendingIntentsAsync(currentEpoch, cancellationToken).ConfigureAwait(false);

        if (pendingIntents.Count == 0)
        {
            return CyberDefenseRecoveryResult.Empty();
        }

        var resolvedForValidation = new List<CyberDefensePendingIntentRecord>(pendingIntents.Count);
        var reconciliations = new List<CyberDefensePendingIntentRecord>();

        foreach (var intent in pendingIntents)
        {
            var existingReceipt = await _gameplayStore.GetReceiptAsync(intent.SubmissionId, cancellationToken).ConfigureAwait(false);
            if (existingReceipt is not null)
            {
                if (!intent.PracticePosition.HasValue)
                {
                    var receiptEvidence = await _learnerStore.GetCommittedAttemptEvidenceAsync(intent.SubmissionId, cancellationToken).ConfigureAwait(false);
                    if (receiptEvidence is not null)
                    {
                        var resolvedWithReceipt = VerifyAndResolveIntentAgainstEvidence(intent, receiptEvidence);
                        resolvedForValidation.Add(resolvedWithReceipt);
                        if (resolvedWithReceipt.PracticePosition.HasValue)
                        {
                            reconciliations.Add(resolvedWithReceipt);
                        }

                        continue;
                    }
                }

                resolvedForValidation.Add(intent);
                continue;
            }

            var evidence = await _learnerStore.GetCommittedAttemptEvidenceAsync(intent.SubmissionId, cancellationToken).ConfigureAwait(false);
            if (evidence is null)
            {
                resolvedForValidation.Add(intent);
                continue;
            }

            var resolved = VerifyAndResolveIntentAgainstEvidence(intent, evidence);
            resolvedForValidation.Add(resolved);
            if (!intent.PracticePosition.HasValue && resolved.PracticePosition.HasValue)
            {
                reconciliations.Add(resolved);
            }
        }

        ValidateAndSortPendingQueue(resolvedForValidation);

        foreach (var reconciled in reconciliations)
        {
            await _gameplayStore.StagePendingIntentAsync(reconciled, cancellationToken).ConfigureAwait(false);
        }

        if (reconciliations.Count > 0)
        {
            pendingIntents = await _gameplayStore.GetPendingIntentsAsync(currentEpoch, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            pendingIntents = resolvedForValidation;
        }

        var confirmedList = new List<CyberDefensePendingIntentRecord>();
        var receipts = new List<CyberDefenseReceiptRecord>();

        foreach (var intent in pendingIntents)
        {
            var existingReceipt = await _gameplayStore.GetReceiptAsync(intent.SubmissionId, cancellationToken).ConfigureAwait(false);
            if (existingReceipt is not null)
            {
                var replayed = await _gameplayStore.ApplyAttemptTransactionAsync(intent, cancellationToken).ConfigureAwait(false);
                receipts.Add(replayed);
                continue;
            }

            var evidence = await _learnerStore.GetCommittedAttemptEvidenceAsync(intent.SubmissionId, cancellationToken).ConfigureAwait(false);
            if (evidence is null)
            {
                // Learner commit not verified yet or in flight. Stop recovery at the first uncommitted head.
                break;
            }

            var verifiedIntent = VerifyAndResolveIntentAgainstEvidence(intent, evidence);
            if (!verifiedIntent.PracticePosition.HasValue && pendingIntents.Count > 1)
            {
                // When a legacy pending row has no practice_position in either the gameplay store or learner evidence,
                // logical order across multiple pending rows cannot be established; fail closed by stopping before mutation.
                throw new InvalidOperationException(
                    $"Pending intent for submission '{intent.SubmissionId}' lacks an authoritative practice_position required to establish queue order.");
            }

            confirmedList.Add(verifiedIntent);
        }

        foreach (var intent in confirmedList)
        {
            var receipt = await _gameplayStore.ApplyAttemptTransactionAsync(intent, cancellationToken).ConfigureAwait(false);
            receipts.Add(receipt);
        }

        return CyberDefenseRecoveryResult.Completed(receipts);
    }

    private static CyberDefensePendingIntentRecord VerifyAndResolveIntentAgainstEvidence(
        CyberDefensePendingIntentRecord intent,
        CommittedLearnerAttemptEvidence evidence)
    {
        if (!string.Equals(intent.SubmissionId, evidence.SubmissionId, StringComparison.Ordinal) ||
            !string.Equals(intent.FactId, evidence.FactId, StringComparison.Ordinal) ||
            intent.IsCorrect != evidence.IsCorrect ||
            intent.ResponseLatencyMs != evidence.ResponseLatencyMs)
        {
            throw new InvalidOperationException(
                $"Pending intent for submission '{intent.SubmissionId}' does not match committed learner evidence.");
        }

        if (evidence.PracticePosition is <= 0)
        {
            throw new InvalidOperationException(
                $"Committed learner evidence for submission '{intent.SubmissionId}' has invalid practice position {evidence.PracticePosition.Value}.");
        }

        if (intent.PracticePosition.HasValue)
        {
            if (!evidence.PracticePosition.HasValue || intent.PracticePosition.Value != evidence.PracticePosition.Value)
            {
                throw new InvalidOperationException(
                    $"Pending intent for submission '{intent.SubmissionId}' practice position {intent.PracticePosition.Value} does not match committed learner evidence practice position {evidence.PracticePosition?.ToString() ?? "null"}.");
            }

            return intent;
        }

        return evidence.PracticePosition.HasValue
            ? intent.WithPracticePosition(evidence.PracticePosition.Value)
            : intent;
    }

    private static void ValidateAndSortPendingQueue(List<CyberDefensePendingIntentRecord> intents)
    {
        var seenPositions = new HashSet<long>();
        foreach (var intent in intents)
        {
            if (intent.PracticePosition.HasValue && !seenPositions.Add(intent.PracticePosition.Value))
            {
                throw new InvalidOperationException(
                    $"Conflicting duplicate practice_position {intent.PracticePosition.Value} detected in pending queue for epoch {intent.ResetEpoch}.");
            }
        }

        intents.Sort(ComparePendingIntentsByLogicalOrder);
    }

    private static int ComparePendingIntentsByLogicalOrder(CyberDefensePendingIntentRecord a, CyberDefensePendingIntentRecord b)
    {
        if (string.Equals(a.SubmissionId, b.SubmissionId, StringComparison.Ordinal))
        {
            return 0;
        }

        if (a.PracticePosition.HasValue && b.PracticePosition.HasValue)
        {
            int posCompare = a.PracticePosition.Value.CompareTo(b.PracticePosition.Value);
            if (posCompare != 0)
            {
                return posCompare;
            }

            throw new InvalidOperationException(
                $"Conflicting duplicate practice_position {a.PracticePosition.Value} between submissions '{a.SubmissionId}' and '{b.SubmissionId}'.");
        }

        // Unpositioned legacy rows whose learner evidence is not yet available must never be overtaken by positioned rows.
        if (!a.PracticePosition.HasValue && b.PracticePosition.HasValue)
        {
            return -1;
        }

        if (a.PracticePosition.HasValue && !b.PracticePosition.HasValue)
        {
            return 1;
        }

        int timeCompare = a.CreatedAt.CompareTo(b.CreatedAt);
        if (timeCompare != 0)
        {
            return timeCompare;
        }

        return string.CompareOrdinal(a.SubmissionId, b.SubmissionId);
    }
}
