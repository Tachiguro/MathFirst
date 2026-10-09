namespace MathFirst.Application.Gameplay;

using System;
using System.Collections.Generic;
using System.Linq;
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

        var existingReceipt = await _gameplayStore.GetReceiptAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (existingReceipt is not null)
        {
            return CyberDefenseConsumptionResult.AlreadyConsumed(existingReceipt);
        }

        var intent = await _gameplayStore.GetPendingIntentAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (intent is null)
        {
            return CyberDefenseConsumptionResult.NoPendingIntent(submissionId);
        }

        var evidence = await _learnerStore.GetCommittedAttemptEvidenceAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (evidence is null)
        {
            return CyberDefenseConsumptionResult.UncommittedLearnerAttempt(submissionId);
        }

        if (!string.Equals(intent.FactId, evidence.FactId, StringComparison.Ordinal) ||
            intent.IsCorrect != evidence.IsCorrect ||
            intent.ResponseLatencyMs != evidence.ResponseLatencyMs)
        {
            throw new InvalidOperationException(
                $"Pending intent for submission '{submissionId}' does not match committed learner evidence.");
        }

        var currentEpoch = await _gameplayStore.GetResetEpochAsync(cancellationToken).ConfigureAwait(false);
        if (intent.ResetEpoch != currentEpoch)
        {
            throw new InvalidOperationException(
                $"Pending intent reset epoch {intent.ResetEpoch} does not match current store reset epoch {currentEpoch}.");
        }

        var receipt = await _gameplayStore.ApplyAttemptTransactionAsync(intent, cancellationToken).ConfigureAwait(false);
        return CyberDefenseConsumptionResult.Success(receipt);
    }

    public async Task<CyberDefenseRecoveryResult> RecoverPendingIntentsAsync(CancellationToken cancellationToken = default)
    {
        var currentEpoch = await _gameplayStore.GetResetEpochAsync(cancellationToken).ConfigureAwait(false);
        var pendingIntents = await _gameplayStore.GetPendingIntentsAsync(currentEpoch, cancellationToken).ConfigureAwait(false);

        if (pendingIntents.Count == 0)
        {
            return CyberDefenseRecoveryResult.Empty();
        }

        var confirmedList = new List<(CyberDefensePendingIntentRecord Intent, CommittedLearnerAttemptEvidence Evidence)>();
        var receipts = new List<CyberDefenseReceiptRecord>();

        foreach (var intent in pendingIntents)
        {
            var existingReceipt = await _gameplayStore.GetReceiptAsync(intent.SubmissionId, cancellationToken).ConfigureAwait(false);
            if (existingReceipt is not null)
            {
                await _gameplayStore.ClearPendingIntentAsync(intent.SubmissionId, cancellationToken).ConfigureAwait(false);
                receipts.Add(existingReceipt);
                continue;
            }

            var evidence = await _learnerStore.GetCommittedAttemptEvidenceAsync(intent.SubmissionId, cancellationToken).ConfigureAwait(false);
            if (evidence is null)
            {
                // Learner commit not verified yet or in flight. Stop recovery to maintain sequence order.
                break;
            }

            if (!string.Equals(intent.FactId, evidence.FactId, StringComparison.Ordinal) ||
                intent.IsCorrect != evidence.IsCorrect ||
                intent.ResponseLatencyMs != evidence.ResponseLatencyMs)
            {
                throw new InvalidOperationException(
                    $"Pending intent for submission '{intent.SubmissionId}' does not match committed learner evidence.");
            }

            confirmedList.Add((intent, evidence));
        }

        var sortedConfirmed = confirmedList
            .OrderBy(x => x.Evidence.PracticePosition ?? long.MaxValue)
            .ThenBy(x => x.Evidence.Timestamp)
            .ThenBy(x => x.Intent.SubmissionId, StringComparer.Ordinal)
            .ToList();

        foreach (var item in sortedConfirmed)
        {
            var receipt = await _gameplayStore.ApplyAttemptTransactionAsync(item.Intent, cancellationToken).ConfigureAwait(false);
            receipts.Add(receipt);
        }

        return CyberDefenseRecoveryResult.Completed(receipts);
    }
}
