namespace MathFirst.Core.Tests.CyberDefense
{
    using System.Globalization;
    using System.Reflection;
    using System.Runtime.ExceptionServices;
    using System.Threading.Channels;
    using MathFirst.Application;
    using MathFirst.Application.Copy;
    using MathFirst.Application.Gameplay;
    using MathFirst.Application.Lifecycle;
    using MathFirst.Application.Navigation;
    using MathFirst.Application.Persistence;
    using MathFirst.Application.Practice;
    using MathFirst.Domain;
    using MathFirst.Infrastructure.Sqlite.Gameplay;
    using Microsoft.AspNetCore.Components.Rendering;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    public sealed class CyberDefenseHomePresentationTests
    {
        [Theory(Timeout = 10000)]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task CorrectCompletion_PreparesEmptyRenewedInputAndQuestionBindingsOnce(bool boss, bool calm)
        {
            await using var fixture = await Fixture.Create(ordinary: !boss, calm: calm, upgrades: !calm);
            var revision = fixture.Session.FactInstanceRevision;
            var inputVersion = fixture.Field<int>("_inputRenderVersion");
            if (boss)
            {
                var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
                var submission = fixture.Submit();
                await Task.WhenAny(shown, submission);
                Assert.Equal(CyberDefensePresentationStage.Recap, fixture.Sequence.Stage);
                await fixture.RevealContinue();
                await fixture.Continue();
                await submission;
            }
            else await fixture.Submit();

            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            await fixture.Render();
            var input = System.Text.RegularExpressions.Regex.Match(await fixture.Html(), "<input[^>]*class=\"answer-input[^>]*>").Value;
            Assert.NotEmpty(input);
            Assert.DoesNotContain("value=\"" + fixture.LastSubmittedAnswer + "\"", input);
            Assert.Equal(string.Empty, fixture.Field<string>("_answerInput"));
            Assert.Equal(inputVersion + 1, fixture.Field<int>("_inputRenderVersion"));
            Assert.Equal(revision + 1, fixture.Field<long>("_lastPreparedFactInstanceRevision"));
            Assert.True(fixture.Field<bool>("_shouldFocusInput"));
            Assert.False(fixture.Sequence.IsBlocking);
            if (!calm)
            {
                await fixture.AssertQuestionBindings();
            }
            else Assert.DoesNotContain("cyber-defense-hud", await fixture.Html());
            await fixture.Invoke("RecoverFromPersistenceFailureAsync");
            Assert.Equal(inputVersion + 1, fixture.Field<int>("_inputRenderVersion"));
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            fixture.Clock.Advance(1234);
            await fixture.Submit();
            var attempt = (await fixture.LearnerSnapshot()).RecentAttempts.Last();
            Assert.Equal(1234, attempt.ResponseLatencyMs);
            if (!calm)
            {
                var receipt = await fixture.Store.GetReceiptAsync(attempt.SubmissionId);
                Assert.NotNull(receipt);
                Assert.Equal(2, receipt.BaseAttackDamage);
                Assert.Equal(1, receipt.BoundCriticalStrikeRank);
                Assert.Equal(1, receipt.BoundOverdriveRank);
            }
        }

        [Theory(Timeout = 10000)]
        [InlineData(false)]
        [InlineData(true)]
        public async Task BackgroundCeremonyCancellation_ExplicitResumeRecordsRealActiveAndPersistedLatency(bool afterRevalidationTick)
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            var inputVersion = fixture.Field<int>("_inputRenderVersion");
            var prepared = fixture.Field<long>("_lastPreparedFactInstanceRevision");
            fixture.Clock.Advance(2000);
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            Assert.Equal(CyberDefensePresentationStage.Recap, fixture.Sequence.Stage);
            if (afterRevalidationTick)
            {
                (await fixture.NextDelay()).TrySetResult();
                await fixture.NextDelay();
                Assert.Equal(CyberDefensePresentationStage.Recap, fixture.Sequence.Stage);
            }
            var ledger = await fixture.TakeLedger();
            var staleId = fixture.Sequence.PresentationId;
            await fixture.Dispatch(() => fixture.Session.SetAppForeground(false));
            await submission;
            fixture.DrainDelays();

            // Backgrounding cancels the unacknowledged recap: the committed answer must not advance by itself.
            await fixture.AssertHeld(revision, inputVersion, prepared, ledger);
            fixture.Clock.Advance(100000);
            Assert.Equal(2000, fixture.Session.LastEvaluation!.LatencyMs);
            Assert.False(fixture.Session.IsTimingActive);

            // Foreground recovery replays the unacknowledged recap; only its own Continue releases the question.
            var replay = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            await fixture.Dispatch(() => fixture.Session.SetAppForeground(true));
            await replay;
            var replayId = fixture.Sequence.PresentationId;
            Assert.NotEqual(staleId, replayId);
            await fixture.RevealContinue();
            await fixture.Continue(staleId);
            await Task.Delay(50);
            Assert.True(fixture.Sequence.IsBlocking);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.False(fixture.Session.IsTimingActive);
            Assert.Equal(ledger, await fixture.TakeLedger());
            await fixture.Continue(replayId);
            await fixture.WhenResumed(revision + 1);
            Assert.Equal(ledger, await fixture.TakeLedger());
            Assert.Equal(PracticeGateState.BackgroundResumeGate, fixture.Session.PracticeGate);
            Assert.False(fixture.Session.IsTimingActive);
            fixture.Clock.Advance(50000);
            Assert.Equal(0, fixture.Session.GetCurrentActiveElapsedMs());
            await fixture.Invoke("Submit");
            Assert.Single((await fixture.LearnerSnapshot()).RecentAttempts);
            await fixture.Invoke("StartOrResumePractice");
            fixture.Clock.Advance(3456);
            var elapsed = fixture.Session.GetCurrentActiveElapsedMs();
            await fixture.Submit();
            var snapshot = await fixture.LearnerSnapshot();
            var attempt = snapshot.RecentAttempts.Last();
            Assert.Equal(3456, attempt.ResponseLatencyMs);
            Assert.Equal(3456, elapsed);
            Assert.False(attempt.IsInterrupted);
            var receipt = await fixture.Store.GetReceiptAsync(attempt.SubmissionId);
            Assert.NotNull(receipt);
            Assert.Equal(3456, receipt.ResponseLatencyMs);
            Assert.Equal(revision + 2, fixture.Session.FactInstanceRevision);
            Assert.Equal(2, fixture.Victories);
        }

        [Fact(Timeout = 10000)]
        public async Task LearnerPersistenceRetry_PresentsTheRecoveredReceiptOnce_WithoutAnotherReward()
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            var inputVersion = fixture.Field<int>("_inputRenderVersion");
            fixture.FailNextLearnerCommit();
            await fixture.Submit();
            Assert.Equal(SessionInteractionState.PersistenceFailure, fixture.Session.InteractionState);
            Assert.Equal(136, (await fixture.Store.GetProgressionAsync()).CumulativeXp);
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var retry = fixture.Invoke("RecoverFromPersistenceFailureAsync");
            await Task.WhenAny(shown, retry);
            Assert.Equal(CyberDefensePresentationStage.Recap, fixture.Sequence.Stage);
            await fixture.RevealContinue();
            await Task.Delay(50);
            Assert.False(retry.IsCompleted);
            await fixture.Continue();
            await retry;
            await fixture.Invoke("RecoverFromPersistenceFailureAsync");
            Assert.Equal(176, (await fixture.Store.GetProgressionAsync()).CumulativeXp);
            Assert.Equal(1, fixture.Victories);
            Assert.Equal(SessionInteractionState.AwaitingAnswer, fixture.Session.InteractionState);
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            Assert.Equal(inputVersion + 1, fixture.Field<int>("_inputRenderVersion"));
            Assert.Equal(string.Empty, fixture.Field<string>("_answerInput"));
            Assert.True(fixture.Field<bool>("_shouldFocusInput"));
        }

        [Fact(Timeout = 10000)]
        public async Task RevisionConflictRecovery_PreparesAuthoritativeQuestionAndBindingsExactlyOnce()
        {
            await using var fixture = await Fixture.Create(ordinary: true, upgrades: true);
            var generation = fixture.Session.LearnerStateGenerationRevision;
            var revision = fixture.Session.FactInstanceRevision;
            var inputVersion = fixture.Field<int>("_inputRenderVersion");
            await fixture.CommitIndependentLearnerAttempt();
            var authoritative = await fixture.LearnerSnapshot();
            await fixture.Submit();
            Assert.Equal(PersistenceStatus.RevisionConflict, fixture.Session.LastPersistenceResult!.Status);
            Assert.Equal(SessionInteractionState.PersistenceFailure, fixture.Session.InteractionState);
            var rejectedSubmission = fixture.Session.LastEvaluation!.ChangeSet.SubmissionId;
            var progression = await fixture.Store.GetProgressionAsync();
            var run = await fixture.Store.GetRunStateAsync();

            await fixture.Invoke("RecoverFromPersistenceFailureAsync");

            // These assertions establish real successful reload/selection before testing Home preparation.
            Assert.Equal(SessionInteractionState.AwaitingAnswer, fixture.Session.InteractionState);
            Assert.Equal(generation + 1, fixture.Session.LearnerStateGenerationRevision);
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            Assert.Equal(authoritative.Revision, fixture.Session.Progression.StoreRevision);
            Assert.Equal(authoritative.Progression.PracticePosition, fixture.Session.Progression.PracticePosition);
            Assert.Null(fixture.Session.LastEvaluation);
            Assert.Null(fixture.Session.LastPersistenceResult);
            Assert.False(fixture.Session.IsCurrentSubmissionCommitted);
            var html = System.Net.WebUtility.HtmlDecode(await fixture.Html());
            var expression = System.Text.RegularExpressions.Regex.Match(html, "<span class=\"expression-problem\">(.*?)</span>\\s*<input", System.Text.RegularExpressions.RegexOptions.Singleline).Value;
            Assert.Contains($"<span class=\"operand\">{fixture.Session.CurrentFact.LeftOperand}</span>", expression);
            Assert.Contains($"<span class=\"operand\">{fixture.Session.CurrentFact.RightOperand}</span>", expression);
            Assert.Contains($"<span class=\"operator\">{fixture.Session.CurrentFact.DisplaySymbol}</span>", expression);
            Assert.Equal(inputVersion + 1, fixture.Field<int>("_inputRenderVersion"));
            Assert.Equal(revision + 1, fixture.Field<long>("_lastPreparedFactInstanceRevision"));
            var input = System.Text.RegularExpressions.Regex.Match(html, "<input[^>]*class=\"answer-input[^>]*>").Value;
            Assert.NotEmpty(input);
            Assert.DoesNotContain("value=\"" + fixture.LastSubmittedAnswer + "\"", input);
            Assert.Equal(string.Empty, fixture.Field<string>("_answerInput"));
            Assert.Equal(string.Empty, fixture.Session.CurrentAnswerInput);
            Assert.Null(fixture.Field<string?>("_submittedAnswerDisplay"));
            Assert.True(fixture.Field<bool>("_shouldFocusInput"));
            Assert.False(fixture.Field<bool>("_shouldFocusPrimaryOverlay"));
            await fixture.AssertQuestionBindings();

            // Reentry and an empty submit cannot reuse the rejected pending answer or award anything.
            await fixture.Invoke("RecoverFromPersistenceFailureAsync");
            await fixture.Invoke("Submit");
            Assert.Equal(inputVersion + 1, fixture.Field<int>("_inputRenderVersion"));
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            Assert.Single((await fixture.LearnerSnapshot()).RecentAttempts);
            Assert.Equal(2, fixture.LearnerCommitCalls);
            Assert.Null(await fixture.Store.GetReceiptAsync(rejectedSubmission));
            Assert.Equal(progression, await fixture.Store.GetProgressionAsync());
            Assert.Equal(run, await fixture.Store.GetRunStateAsync());
            Assert.Equal(0, fixture.Session.SessionTotalCount);
            Assert.Equal(0, fixture.Victories);

        }

        [Fact(Timeout = 10000)]
        public async Task RevisionConflictRecovery_ClearsOrphanPendingIntent_AndUnblocksSubsequentCombatProgression()
        {
            await using var fixture = await Fixture.Create(ordinary: true, upgrades: true);
            var generation = fixture.Session.LearnerStateGenerationRevision;
            var revision = fixture.Session.FactInstanceRevision;
            await fixture.CommitIndependentLearnerAttempt();
            await fixture.Submit();
            Assert.Equal(PersistenceStatus.RevisionConflict, fixture.Session.LastPersistenceResult!.Status);
            Assert.Equal(SessionInteractionState.PersistenceFailure, fixture.Session.InteractionState);
            var rejectedSubmission = fixture.Session.LastEvaluation!.ChangeSet.SubmissionId;
            Assert.NotNull(await fixture.Store.GetPendingIntentAsync(rejectedSubmission));

            await fixture.Invoke("RecoverFromPersistenceFailureAsync");

            Assert.Equal(SessionInteractionState.AwaitingAnswer, fixture.Session.InteractionState);
            Assert.Equal(generation + 1, fixture.Session.LearnerStateGenerationRevision);
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            Assert.True(fixture.Field<bool>("_shouldFocusInput"));
            Assert.Null(await fixture.Store.GetPendingIntentAsync(rejectedSubmission));

            await fixture.Submit(correct: true);

            var snapshot = await fixture.LearnerSnapshot();
            var committedAttempt = snapshot.RecentAttempts.Last();
            var receipt = await fixture.Store.GetReceiptAsync(committedAttempt.SubmissionId);
            Assert.NotNull(receipt);
            Assert.Equal(committedAttempt.SubmissionId, receipt.SubmissionId);
            Assert.Equal(CyberDefenseReceiptKind.Applied, receipt.ReceiptKind);
            Assert.NotNull(receipt.TransitionResult);
            Assert.True(receipt.TransitionResult.AppliedOpponentDamage > 0);
            Assert.True(receipt.AwardedXp > 0);
        }

        [Theory(Timeout = 10000)]
        [InlineData("reset-before-snapshot")]
        [InlineData("reset-after-snapshot")]
        [InlineData("dispose")]
        public async Task RevisionConflictRecovery_RejectsStaleResetOrDisposedCallback(string change)
        {
            await using var fixture = await Fixture.Create(ordinary: true, upgrades: true);
            await fixture.ResetIndependentLearner();
            await fixture.Submit();
            Assert.Equal(PersistenceStatus.RevisionConflict, fixture.Session.LastPersistenceResult!.Status);
            var generation = fixture.Session.LearnerStateGenerationRevision;
            var revision = fixture.Session.FactInstanceRevision;
            var inputVersion = fixture.Field<int>("_inputRenderVersion");
            var gate = fixture.GateNextRecoveryLoad(afterSnapshot: change == "reset-after-snapshot");
            var recovery = fixture.Invoke("RecoverFromPersistenceFailureAsync");
            await gate.Entered.Task;
            try
            {
                await fixture.Invoke("RecoverFromPersistenceFailureAsync");
                Assert.False(recovery.IsCompleted);
                if (change == "dispose") await fixture.RemoveView();
                else await fixture.DispatchAsync(() => fixture.Session.ResetLearningProgressAsync(startTiming: false));
            }
            finally { gate.Release.TrySetResult(); }
            await recovery;

            Assert.Equal(generation + (change == "dispose" ? 1 : 2), fixture.Session.LearnerStateGenerationRevision);
            Assert.Equal(inputVersion, fixture.Field<int>("_inputRenderVersion"));
            Assert.Equal(revision, fixture.Field<long>("_lastPreparedFactInstanceRevision"));
            fixture.AssertCurrentQuestionUnbound();
            Assert.Equal(1, fixture.LearnerCommitCalls);
        }
        [Fact(Timeout = 10000)]
        public async Task ColdStartup_RecoversBossReceipt_PresentsOnce_AndKeepsTheInitialPracticeGate()
        {
            await using var fixture = await Fixture.Create(recoveredStartup: true);
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            await Task.WhenAny(shown, fixture.Rendering);
            Assert.Equal(CyberDefensePresentationStage.Recap, fixture.Sequence.Stage);
            Assert.False(fixture.Session.IsTimingActive);
            await fixture.RevealContinue();
            await Task.Delay(50);
            Assert.False(fixture.Rendering.IsCompleted);
            Assert.False(fixture.Session.IsTimingActive);
            await fixture.Continue();
            await fixture.Rendering;
            Assert.Equal(2, (await fixture.Store.GetRunStateAsync()).Sector);
            Assert.Equal(PracticeGateState.InitialReadyGate, fixture.Session.PracticeGate);
            Assert.False(fixture.Session.IsTimingActive);
            Assert.Equal(1, fixture.Victories);
        }

        [Fact(Timeout = 10000)]
        public async Task DisposalAndReentry_DoNotAdvanceDisposedView_AndReplayTheUnacknowledgedBossRecapUntilContinue()
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            var inputVersion = fixture.Field<int>("_inputRenderVersion");
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            Assert.Equal(CyberDefensePresentationStage.Recap, fixture.Sequence.Stage);
            var ledger = await fixture.TakeLedger();
            await fixture.RemoveView();
            await submission;
            fixture.DrainDelays();
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.Equal(inputVersion, fixture.Field<int>("_inputRenderVersion"));
            Assert.Equal(SessionInteractionState.CorrectFeedback, fixture.Session.InteractionState);

            // Re-entry never advances the unacknowledged question on its own: the recap is presented again.
            var replay = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var remount = fixture.MountView();
            await Task.WhenAny(replay, remount);
            Assert.Equal(CyberDefensePresentationStage.Recap, fixture.Sequence.Stage);
            await fixture.RevealContinue();
            await Task.Delay(50);
            Assert.False(remount.IsCompleted);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            await fixture.Continue();
            await remount;
            Assert.True(fixture.Session.FactInstanceRevision > revision);
            Assert.Equal(ledger, await fixture.TakeLedger());
            Assert.Equal(2, fixture.Victories);
        }

        [Fact]
        public async Task BossVictory_HoldsRealHomeAdvanceAndActiveAnswerTime_UntilTheResultCardContinues()
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            fixture.Clock.Advance(2000);
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            Assert.Equal(CyberDefensePresentationStage.Recap, fixture.Sequence.Stage);
            Assert.False(submission.IsCompleted);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.False(fixture.Session.IsTimingActive);
            await fixture.Render();
            var html = await fixture.Html();
            Assert.Matches("<header[^>]*inert(?:=\"[^\"]*\")?[^>]*aria-hidden=\"true\"", html);
            Assert.Matches("<main[^>]*inert(?:=\"[^\"]*\")?[^>]*aria-hidden=\"true\"", html);
            Assert.Contains("combat-ceremony-overlay", html);
            Assert.Equal(2, (await fixture.Store.GetRunStateAsync()).Sector);
            Assert.Equal(176, (await fixture.Store.GetProgressionAsync()).CumulativeXp);
            fixture.Clock.Advance(500000);
            Assert.Equal(2000, fixture.Session.LastEvaluation!.LatencyMs);
            // Issue #138: Continue is on the card from the start; the only timer re-observes validity and releases nothing.
            Assert.True(fixture.Sequence.CanContinue);
            Assert.Contains("ceremony-continue", html);
            (await fixture.NextDelay()).TrySetResult();
            await fixture.NextDelay();
            Assert.Equal(TimeSpan.FromSeconds(2), fixture.LastDelay);
            Assert.Equal(CyberDefensePresentationStage.Recap, fixture.Sequence.Stage);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.False(submission.IsCompleted);
            await fixture.Render();
            Assert.Contains("ceremony-continue", await fixture.Html());
            await Task.Delay(50);
            Assert.False(submission.IsCompleted);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            await fixture.Continue();
            await submission;
            Assert.True(fixture.Session.FactInstanceRevision > revision);
            Assert.Equal(0, fixture.Session.GetCurrentActiveElapsedMs());
            fixture.Clock.Advance(1000);
            Assert.Equal(1000, fixture.Session.GetCurrentActiveElapsedMs());
        }

        [Fact(Timeout = 20000)]
        public async Task BossRecap_StaysThirtySimulatedSeconds_AndOnlyTheUserContinuesOnceWithoutNewRewardsOrAnswers()
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            fixture.Clock.Advance(2000);
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            var xp = (await fixture.Store.GetProgressionAsync()).CumulativeXp;
            var commits = fixture.LearnerCommitCalls;
            var run = await fixture.Store.GetRunStateAsync();

            await fixture.RevealContinue();
            await fixture.ElapseRevalidation(TimeSpan.FromSeconds(30));
            fixture.Clock.Advance(30000);
            Assert.False(submission.IsCompleted);
            Assert.Equal(CyberDefensePresentationStage.Recap, fixture.Sequence.Stage);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.False(fixture.Session.IsTimingActive);
            Assert.Equal(2000, fixture.Session.GetCurrentActiveElapsedMs());
            Assert.Equal(2000, fixture.Session.LastEvaluation!.LatencyMs);
            Assert.Contains("ceremony-continue", await fixture.Html());

            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.Continue()));
            await submission;
            await fixture.Continue();
            await fixture.Continue(fixture.Sequence.PresentationId + 1);
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            Assert.Equal(xp, (await fixture.Store.GetProgressionAsync()).CumulativeXp);
            Assert.Equal(commits, fixture.LearnerCommitCalls);
            Assert.Equal(run, await fixture.Store.GetRunStateAsync());
            Assert.Single((await fixture.LearnerSnapshot()).RecentAttempts);
            Assert.Equal(1, fixture.Victories);
            Assert.False(fixture.Sequence.IsBlocking);
            Assert.DoesNotContain("ceremony-continue", await fixture.Html());
        }

        [Theory(Timeout = 20000)]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GlobalEnterAndStaleContinue_CannotBypassTheModalAtAnyStage(bool afterRevalidationTick)
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            var commits = fixture.LearnerCommitCalls;
            await fixture.RevealContinue();
            if (afterRevalidationTick)
            {
                (await fixture.NextDelay()).TrySetResult();
                await fixture.NextDelay();
            }
            var id = fixture.Sequence.PresentationId;

            await fixture.PressEnter();
            await fixture.PressEnter();
            await fixture.Continue(id + 1);
            await fixture.Continue(0);
            await Task.Delay(50);

            Assert.False(submission.IsCompleted);
            Assert.True(fixture.Sequence.IsBlocking);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.Equal(commits, fixture.LearnerCommitCalls);
            Assert.False(fixture.Session.IsTimingActive);
            Assert.Single((await fixture.LearnerSnapshot()).RecentAttempts);
        }

        [Theory(Timeout = 20000)]
        [InlineData("background")]
        [InlineData("dispose")]
        [InlineData("reset")]
        [InlineData("mode")]
        public async Task ContextChangeWhileAwaitingContinue_ReleasesFailClosed_WithoutAdvancingOrLeavingAPendingContinue(string change)
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            var inputVersion = fixture.Field<int>("_inputRenderVersion");
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            await fixture.RevealContinue();
            var staleId = fixture.Sequence.PresentationId;

            if (change == "background") await fixture.Dispatch(() => fixture.Session.SetAppForeground(false));
            if (change == "dispose") await fixture.RemoveView();
            if (change == "mode") fixture.Preferences.SetCyberDefenseEnabled(false);
            if (change == "reset")
            {
                await fixture.DispatchAsync(() => fixture.Session.ResetLearningProgressAsync(startTiming: false));
                var reset = await fixture.Store.BeginOrGetResetIntentAsync();
                await fixture.Store.ResetGameplayStateAsync(reset.TargetEpoch);
                await fixture.Store.ClearResetIntentAsync(reset.TargetEpoch);
            }
            var revisionAfterChange = fixture.Session.FactInstanceRevision;
            var inputVersionAfterChange = fixture.Field<int>("_inputRenderVersion");
            if (change is "mode" or "reset") (await fixture.NextDelay()).TrySetResult();
            await submission;

            Assert.False(fixture.Sequence.IsBlocking);
            Assert.False(fixture.Sequence.CanContinue);
            await fixture.Continue(staleId);

            // The aborted ceremony must not prepare or advance the old question for any invalidation reason.
            Assert.Equal(revisionAfterChange, fixture.Session.FactInstanceRevision);
            Assert.Equal(inputVersionAfterChange, fixture.Field<int>("_inputRenderVersion"));
            if (change != "reset") Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            if (change != "reset") Assert.Equal(inputVersion, fixture.Field<int>("_inputRenderVersion"));
            if (change != "dispose" && change != "reset") Assert.Equal(SessionInteractionState.CorrectFeedback, fixture.Session.InteractionState);
            if (change == "background")
            {
                Assert.Equal(PracticeGateState.Running, fixture.Session.PracticeGate);
                Assert.False(fixture.Session.IsTimingActive);
                Assert.Single((await fixture.LearnerSnapshot()).RecentAttempts);
            }
            if (change == "reset")
            {
                // A stale receipt of the replaced learner generation is never replayed or re-acknowledged.
                await fixture.PressEnter();
                Assert.False(fixture.Sequence.IsBlocking);
            }
            Assert.Equal(1, fixture.Victories);
        }

        [Fact(Timeout = 20000)]
        public async Task SystemRestart_KeepsPermanentPurchasedUpgrades_AndNeedsManualContinueBeforeIncorrectFeedbackCompletes()
        {
            // No Firewall purchase: a purchased shield would absorb the final hit and the run would not end.
            await using var fixture = await Fixture.Create(victory: false, upgrades: true, withoutFirewall: true);
            var before = await fixture.Store.GetProgressionAsync();
            var modifiersBefore = await fixture.Store.GetCombatModifierSnapshotAsync();
            Assert.True(before.AttackRank > 0 && before.SpentSkillPoints > 0);
            Assert.True(modifiersBefore.CriticalStrikeRank > 0 && modifiersBefore.OverdriveRank > 0);
            var revision = fixture.Session.FactInstanceRevision;
            var shown = fixture.WhenStage(CyberDefensePresentationStage.SystemOverload);
            var submission = fixture.Submit(correct: false);
            await Task.WhenAny(shown, submission);
            var restarted = await fixture.Store.GetProgressionAsync();
            var modifiersRestarted = await fixture.Store.GetCombatModifierSnapshotAsync();
            Assert.Equal(before.AttackRank, restarted.AttackRank);
            Assert.Equal(before.FirewallRank, restarted.FirewallRank);
            Assert.Equal(modifiersBefore.CriticalStrikeRank, modifiersRestarted.CriticalStrikeRank);
            Assert.Equal(modifiersBefore.OverdriveRank, modifiersRestarted.OverdriveRank);
            Assert.Equal(before.SpentSkillPoints, restarted.SpentSkillPoints);

            await fixture.RevealContinue(boss: false);
            await fixture.ElapseRevalidation(TimeSpan.FromSeconds(30));
            Assert.False(submission.IsCompleted);
            Assert.Equal(CyberDefensePresentationStage.SystemOverload, fixture.Sequence.Stage);
            Assert.Contains("ceremony-continue", await fixture.Html());
            await fixture.Continue();
            await submission;

            var after = await fixture.Store.GetProgressionAsync();
            var modifiersAfter = await fixture.Store.GetCombatModifierSnapshotAsync();
            Assert.Equal(restarted, after);
            Assert.Equal(modifiersRestarted.CriticalStrikeRank, modifiersAfter.CriticalStrikeRank);
            Assert.Equal(modifiersRestarted.OverdriveRank, modifiersAfter.OverdriveRank);
            Assert.Equal(1, (await fixture.Store.GetRunStateAsync()).Sector);
            Assert.Equal(SessionInteractionState.IncorrectFeedback, fixture.Session.InteractionState);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.False(fixture.Session.IsTimingActive);
        }

        [Fact(Timeout = 20000)]
        public async Task BossRecap_KeepsTheGameMenuAndAndroidBackOut_UntilItsOwnContinue()
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            await fixture.RevealContinue();

            // Issue #125: the ceremony stays authoritative; no menu or pause gate competes with it,
            // and native Back is safely consumed without escaping or bypassing Continue.
            Assert.False(fixture.Property<bool>("CanOpenGameMenu"));
            Assert.True(fixture.Back.TryHandleBack());
            await fixture.Invoke("OpenGameMenu");
            Assert.Equal(PracticeGateState.Running, fixture.Session.PracticeGate);
            var html = await fixture.Html();
            Assert.Matches(@"<header class=""mathfirst-header""[^>]*\binert\b", html);
            Assert.DoesNotContain("game-menu-overlay", html);
            Assert.True(fixture.Sequence.IsBlocking);

            await fixture.Continue();
            await submission;
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            await fixture.Render();
            Assert.True(fixture.Property<bool>("CanOpenGameMenu"));
            await fixture.Invoke("OpenGameMenu");
            Assert.Equal(PracticeGateState.ManualPause, fixture.Session.PracticeGate);
            Assert.False(fixture.Session.IsTimingActive);
        }

        [Fact(Timeout = 20000)]
        public async Task SystemRestart_KeepsUpgradeDiscovered_AfterTheManualContinue()
        {
            await using var fixture = await Fixture.Create(victory: false, upgrades: true, withoutFirewall: true);
            await fixture.Render();
            Assert.DoesNotContain("upgrade-nav-btn", await fixture.Html());
            var shown = fixture.WhenStage(CyberDefensePresentationStage.SystemOverload);
            var submission = fixture.Submit(correct: false);
            await Task.WhenAny(shown, submission);
            await fixture.RevealContinue(boss: false);
            await fixture.Continue();
            await submission;
            await fixture.Invoke("AdvanceAsync");
            await fixture.Render();

            Assert.Equal(1, (await fixture.Store.GetRunStateAsync()).Sector);
            Assert.True(fixture.Field<UpgradeTerminalViewModel>("_upgradeTerminalViewModel").IsUpgradeDiscovered);
            Assert.DoesNotContain("upgrade-nav-btn", await fixture.Html());
            await fixture.Invoke("OpenGameMenu");
            await fixture.Render();
            Assert.Contains("pause-upgrade-btn", await fixture.Html());
        }

        [Fact]
        public async Task GameOver_AlreadyRebootedButStillRequiresExplicitIncorrectAnswerAcknowledgement()
        {
            await using var fixture = await Fixture.Create(victory: false);
            var revision = fixture.Session.FactInstanceRevision;
            var shown = fixture.WhenStage(CyberDefensePresentationStage.SystemOverload);
            var submission = fixture.Submit(correct: false);
            await Task.WhenAny(shown, submission);
            Assert.Equal(CyberDefensePresentationStage.SystemOverload, fixture.Sequence.Stage);
            Assert.Equal(100, (await fixture.Store.GetRunStateAsync()).PlayerCurrentHp);
            await fixture.Invoke("AdvanceAsync");
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            (await fixture.NextDelay()).TrySetResult();
            Assert.Equal(TimeSpan.FromSeconds(3), fixture.LastDelay);
            await fixture.WhenContinue();
            Assert.False(submission.IsCompleted);
            await fixture.Continue();
            await submission;
            Assert.Equal(SessionInteractionState.IncorrectFeedback, fixture.Session.InteractionState);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.False(fixture.Session.IsTimingActive);
            await fixture.Invoke("AdvanceAsync");
            Assert.True(fixture.Session.FactInstanceRevision > revision);
        }

        [Theory]
        [InlineData("background")]
        [InlineData("mode")]
        [InlineData("reset")]
        public async Task ContextChangeBeforeTheFirstRevalidation_AbortsTheBossCardWithoutLeakingTimeOrAdvancingOldGeneration(string change)
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            var inputVersion = fixture.Field<int>("_inputRenderVersion");
            var prepared = fixture.Field<long>("_lastPreparedFactInstanceRevision");
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            Assert.Equal(CyberDefensePresentationStage.Recap, fixture.Sequence.Stage);
            var ledger = await fixture.TakeLedger();
            var delay = await fixture.NextDelay();
            Assert.Equal(TimeSpan.FromSeconds(2), fixture.LastDelay);
            if (change == "background") await fixture.Dispatch(() => fixture.Session.SetAppForeground(false));
            if (change == "mode") fixture.Preferences.SetCyberDefenseEnabled(false);
            if (change == "reset")
            {
                await fixture.DispatchAsync(() => fixture.Session.ResetLearningProgressAsync(startTiming: false));
                var reset = await fixture.Store.BeginOrGetResetIntentAsync();
                await fixture.Store.ResetGameplayStateAsync(reset.TargetEpoch);
                await fixture.Store.ClearResetIntentAsync(reset.TargetEpoch);
            }
            var revisionAfterChange = fixture.Session.FactInstanceRevision;
            var inputVersionAfterChange = fixture.Field<int>("_inputRenderVersion");
            delay.TrySetResult();
            await submission;
            Assert.False(fixture.Sequence.IsBlocking);
            Assert.Equal(revisionAfterChange, fixture.Session.FactInstanceRevision);
            Assert.Equal(inputVersionAfterChange, fixture.Field<int>("_inputRenderVersion"));
            if (change == "background")
            {
                Assert.Equal(PracticeGateState.Running, fixture.Session.PracticeGate);
                await fixture.AssertHeld(revision, inputVersion, prepared, ledger);
            }
            if (change == "mode")
            {
                Assert.DoesNotContain("cyber-defense-hud", await fixture.Html());
                await fixture.AssertHeld(revision, inputVersion, prepared, ledger);
            }
            if (change == "reset")
            {
                Assert.Equal(inputVersion, fixture.Field<int>("_inputRenderVersion"));
                Assert.False(fixture.Session.IsTimingActive);
            }
        }

        private static async Task<(Task Submission, long Id)> SubmitBossAtContinueAsync(Fixture fixture)
        {
            fixture.Clock.Advance(2000);
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            await fixture.RevealContinue();
            return (submission, fixture.Sequence.PresentationId);
        }

        // Issue #121 review finding: Continue is the only authority that releases the boss recap into the next question.
        [Theory(Timeout = 20000)]
        [InlineData("clock-after-reveal")]
        [InlineData("sqlite-revalidation")]
        [InlineData("sqlite-after-continue")]
        public async Task TimerOrValidityReadFailure_FailsClosed_ThenExplicitRecoveryReplaysAndOnlyContinueAdvances(string failure)
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            var inputVersion = fixture.Field<int>("_inputRenderVersion");
            var prepared = fixture.Field<long>("_lastPreparedFactInstanceRevision");
            var (submission, staleId) = await SubmitBossAtContinueAsync(fixture);
            var ledger = await fixture.TakeLedger();

            var tick = await fixture.NextDelay();
            Assert.Equal(TimeSpan.FromSeconds(2), fixture.LastDelay);
            switch (failure)
            {
                case "clock-after-reveal":
                    tick.TrySetException(new InvalidOperationException("Synthetic clock failure"));
                    break;
                case "sqlite-revalidation":
                    fixture.FailValidityReads(true);
                    tick.TrySetResult();
                    break;
                default:
                    fixture.FailValidityReads(true);
                    await fixture.Continue();
                    break;
            }
            await submission;
            fixture.FailValidityReads(false);
            fixture.DrainDelays();
            if (failure.StartsWith("sqlite")) Assert.True(fixture.FailedValidityReads > 0);

            // The failure never acts as an acknowledgement.
            await fixture.AssertHeld(revision, inputVersion, prepared, ledger);
            await fixture.Continue(staleId);
            await Task.Delay(50);
            await fixture.AssertHeld(revision, inputVersion, prepared, ledger);
            fixture.Clock.Advance(100000);
            Assert.Equal(2000, fixture.Session.LastEvaluation!.LatencyMs);

            // Explicit recovery (Enter) replays the unacknowledged recap; the stale id cannot release it.
            var replay = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var recovery = fixture.PressEnter();
            await Task.WhenAny(replay, recovery);
            Assert.Equal(CyberDefensePresentationStage.Recap, fixture.Sequence.Stage);
            var replayId = fixture.Sequence.PresentationId;
            Assert.NotEqual(staleId, replayId);
            await fixture.RevealContinue();
            await fixture.Continue(staleId);
            await Task.Delay(50);
            Assert.False(recovery.IsCompleted);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.False(fixture.Session.IsTimingActive);
            await fixture.Continue(replayId);
            await recovery;
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            Assert.Equal(inputVersion + 1, fixture.Field<int>("_inputRenderVersion"));
            Assert.Equal(ledger, await fixture.TakeLedger());
            Assert.Equal(2, fixture.Victories);
        }

        [Theory(Timeout = 20000)]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ModeInvalidation_FailsClosed_AndRecoveryFollowsTheCurrentMode(bool backToCyberDefense)
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            var inputVersion = fixture.Field<int>("_inputRenderVersion");
            var prepared = fixture.Field<long>("_lastPreparedFactInstanceRevision");
            var (submission, _) = await SubmitBossAtContinueAsync(fixture);
            var ledger = await fixture.TakeLedger();
            fixture.Preferences.SetCyberDefenseEnabled(false);
            (await fixture.NextDelay()).TrySetResult();
            await submission;
            fixture.DrainDelays();
            await fixture.AssertHeld(revision, inputVersion, prepared, ledger);

            if (backToCyberDefense)
            {
                fixture.Preferences.SetCyberDefenseEnabled(true);
                var replay = fixture.WhenStage(CyberDefensePresentationStage.Recap);
                var recovery = fixture.PressEnter();
                await Task.WhenAny(replay, recovery);
                await fixture.RevealContinue();
                await Task.Delay(50);
                Assert.False(recovery.IsCompleted);
                Assert.Equal(revision, fixture.Session.FactInstanceRevision);
                await fixture.Continue();
                await recovery;
                Assert.Equal(2, fixture.Victories);
            }
            else
            {
                // Calm mode has no ceremony to acknowledge: the pre-existing non-blocking path applies.
                await fixture.PressEnter();
                Assert.Equal(1, fixture.Victories);
            }
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            Assert.Equal(ledger, await fixture.TakeLedger());
        }

        [Fact(Timeout = 20000)]
        public async Task RepeatedRecoveryTriggers_StartOneReplay_AndNeverDuplicateRewardsOrAnswers()
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            var (submission, _) = await SubmitBossAtContinueAsync(fixture);
            var ledger = await fixture.TakeLedger();
            (await fixture.NextDelay()).TrySetException(new InvalidOperationException("Synthetic clock failure"));
            await submission;
            fixture.DrainDelays();

            var replay = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var enters = Task.WhenAll(fixture.PressEnter(), fixture.PressEnter(), fixture.PressEnter());
            await Task.WhenAny(replay, enters);
            // Further Enter presses and foreground events while the replay is running start nothing new.
            await fixture.PressEnter();
            await fixture.Dispatch(() => fixture.Session.SetAppForeground(true));
            await fixture.RevealContinue();
            await Task.Delay(50);
            Assert.False(enters.IsCompleted);
            Assert.Equal(2, fixture.Victories);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            await fixture.Continue();
            await enters;
            await fixture.WhenResumed(revision + 1);
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            Assert.Equal(ledger, await fixture.TakeLedger());
            Assert.Equal(2, fixture.Victories);
        }

        [Fact(Timeout = 20000)]
        public async Task ReentryWithUnreadableReceipt_HoldsTheCommittedAnswer_UntilTheRecapIsAcknowledged()
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            var (submission, _) = await SubmitBossAtContinueAsync(fixture);
            var ledger = await fixture.TakeLedger();
            await fixture.RemoveView();
            await submission;
            fixture.DrainDelays();

            // An unreadable receipt cannot prove that no recap is owed, so re-entry must not advance.
            fixture.FailReceiptReads(true);
            await fixture.MountView();
            Assert.True(fixture.FailedValidityReads > 0);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.Equal(SessionInteractionState.CorrectFeedback, fixture.Session.InteractionState);
            Assert.Equal(ledger, await fixture.TakeLedger());
            await fixture.Render();
            Assert.Contains("held-answer-title", await fixture.Html());
            await fixture.PressEnter();
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);

            fixture.FailReceiptReads(false);
            var replay = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var recovery = fixture.PressEnter();
            await Task.WhenAny(replay, recovery);
            await fixture.RevealContinue();
            await Task.Delay(50);
            Assert.False(recovery.IsCompleted);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            await fixture.Continue();
            await recovery;
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            Assert.Equal(ledger, await fixture.TakeLedger());
            Assert.Equal(2, fixture.Victories);
        }

        [Fact(Timeout = 20000)]
        public async Task PersistenceRetry_CancelledInTheBackground_KeepsTheCommittedAnswerAndDoesNotRenewTheInput()
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            var inputVersion = fixture.Field<int>("_inputRenderVersion");
            var prepared = fixture.Field<long>("_lastPreparedFactInstanceRevision");
            fixture.FailNextLearnerCommit();
            await fixture.Submit();
            Assert.Equal(SessionInteractionState.PersistenceFailure, fixture.Session.InteractionState);
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var retry = fixture.Invoke("RecoverFromPersistenceFailureAsync");
            await Task.WhenAny(shown, retry);
            await fixture.Dispatch(() => fixture.Session.SetAppForeground(false));
            await retry;
            fixture.DrainDelays();
            var ledger = await fixture.TakeLedger();
            await fixture.AssertHeld(revision, inputVersion, prepared, ledger);
            Assert.Equal(176, (await fixture.Store.GetProgressionAsync()).CumulativeXp);

            var replay = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            await fixture.Dispatch(() => fixture.Session.SetAppForeground(true));
            await replay;
            await fixture.RevealContinue();
            await Task.Delay(50);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            await fixture.Continue();
            await fixture.WhenResumed(revision + 1);
            Assert.Equal(ledger, await fixture.TakeLedger());
            Assert.Equal(2, fixture.Victories);
        }

        [Fact(Timeout = 20000)]
        public async Task SystemRestart_CancelledInTheBackground_KeepsTheIncorrectFeedbackContract_WithoutReplayOrAutoAdvance()
        {
            await using var fixture = await Fixture.Create(victory: false);
            var revision = fixture.Session.FactInstanceRevision;
            var shown = fixture.WhenStage(CyberDefensePresentationStage.SystemOverload);
            var submission = fixture.Submit(correct: false);
            await Task.WhenAny(shown, submission);
            var ledger = await fixture.TakeLedger();
            await fixture.Dispatch(() => fixture.Session.SetAppForeground(false));
            await submission;
            fixture.DrainDelays();
            await fixture.Dispatch(() => fixture.Session.SetAppForeground(true));
            await Task.Delay(50);

            // An incorrect answer never auto-advances: its own feedback Continue is still the only way forward.
            Assert.False(fixture.Sequence.IsBlocking);
            Assert.Equal(SessionInteractionState.IncorrectFeedback, fixture.Session.InteractionState);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.False(fixture.Session.IsTimingActive);
            Assert.Equal(ledger, await fixture.TakeLedger());
            await fixture.Invoke("AdvanceAsync");
            Assert.True(fixture.Session.FactInstanceRevision > revision);
            Assert.Equal(ledger, await fixture.TakeLedger());
        }

        // Issue #136: the owner reported a repeated "Sector complete" presentation. Since Issue #138 one sector completion
        // presents one boss result card ("Boss defeated!" and a single completion heading together), through Continue, a
        // Home remount, a later background/foreground cycle and further answers. (Re-presentation after a cancelled,
        // unacknowledged card is the separate Issue #121 recovery contract covered above.)
        [Theory(Timeout = 10000)]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Issue136_OneSectorCompletion_PresentsOneBossResultCardWithOneCompletionHeading(bool remount)
        {
            await using var fixture = await Fixture.Create();
            var stages = new List<CyberDefensePresentationStage>();
            fixture.Sequence.Changed += () => { if (stages.Count == 0 || stages[^1] != fixture.Sequence.Stage) stages.Add(fixture.Sequence.Stage); };
            var revision = fixture.Session.FactInstanceRevision;
            static int Count(string html, string text) => System.Text.RegularExpressions.Regex.Matches(html, System.Text.RegularExpressions.Regex.Escape(text)).Count;

            var shown = fixture.WhenStage(CyberDefensePresentationStage.Recap);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            await fixture.Render();
            var card = await fixture.Html();
            Assert.Equal(1, Count(card, "class=\"combat-ceremony-overlay\""));
            Assert.Equal(1, Count(card, "role=\"dialog\""));
            Assert.Equal(1, Count(card, ">Boss defeated!<"));
            Assert.Equal(1, Count(card, "Sector 1 complete"));
            Assert.Equal(1, Count(card, "ceremony-continue"));

            // Revalidation ticks neither replace nor duplicate the card.
            (await fixture.NextDelay()).TrySetResult();
            await fixture.NextDelay();
            await fixture.Render();
            Assert.Equal(card, await fixture.Html());

            await fixture.Continue();
            await submission;
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            await fixture.Render();
            var after = await fixture.Html();
            Assert.Equal(0, Count(after, "Sector 1 complete"));
            Assert.DoesNotContain("combat-ceremony", after);
            Assert.DoesNotContain("held-answer-title", after);

            if (remount)
            {
                await fixture.RemoveView();
                await fixture.MountView();
            }
            await fixture.Dispatch(() => fixture.Session.SetAppForeground(false));
            await fixture.Dispatch(() => fixture.Session.SetAppForeground(true));
            await Task.Delay(50);
            Assert.False(fixture.Sequence.IsBlocking);
            await fixture.Invoke("StartOrResumePractice");
            for (var i = 0; i < 3; i++)
            {
                var next = fixture.Session.FactInstanceRevision;
                await fixture.Submit().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(next + 1, fixture.Session.FactInstanceRevision);
            }

            await fixture.Render();
            Assert.Equal(0, Count(await fixture.Html(), "Sector 1 complete"));
            Assert.Equal(1, fixture.Victories);
            Assert.Equal([CyberDefensePresentationStage.Recap, CyberDefensePresentationStage.None], stages);
        }

        // Issue #138: the first presented ceremony of a sector completion is the whole boss result card: one dialog with
        // "Boss defeated!", "Sector 1 complete", the real recap metrics and an enabled Continue, which the user may tap
        // at once. The tap advances exactly once without new rewards, answers or commits.
        [Fact(Timeout = 10000)]
        public async Task Issue138_BossCompletion_FirstRenderIsTheCompleteResultCard_AndImmediateContinueAdvancesOnce()
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            static int Count(string html, string text) => System.Text.RegularExpressions.Regex.Matches(html, System.Text.RegularExpressions.Regex.Escape(text)).Count;
            fixture.Clock.Advance(2000);

            var presented = fixture.WhenPresented();
            var submission = fixture.Submit();
            await Task.WhenAny(presented, submission);
            Assert.True(presented.IsCompleted);
            Assert.Equal((CyberDefensePresentationStage.Recap, true), await presented);
            Assert.False(submission.IsCompleted);
            Assert.False(fixture.Session.IsTimingActive);

            await fixture.Render();
            var html = await fixture.Html();
            Assert.Equal(1, Count(html, "class=\"combat-ceremony-overlay\""));
            Assert.Equal(1, Count(html, "role=\"dialog\""));
            Assert.Equal(1, Count(html, ">Boss defeated!<"));
            Assert.Equal(1, Count(html, ">Sector 1 complete<"));
            Assert.Equal(1, Count(html, "class=\"recap-metrics\""));
            Assert.Contains("XP gained: ", html);
            var button = System.Text.RegularExpressions.Regex.Match(html, "<button[^>]*ceremony-continue[^>]*>").Value;
            Assert.NotEmpty(button);
            Assert.DoesNotContain("disabled", button);
            Assert.Matches("<main[^>]*inert(?:=\"[^\"]*\")?[^>]*aria-hidden=\"true\"", html);
            var ledger = await fixture.TakeLedger();
            Assert.Equal(2000, fixture.Session.LastEvaluation!.LatencyMs);

            // Immediate Continue: no timer has elapsed.
            await fixture.Continue();
            await submission;
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            await fixture.Continue();
            await fixture.PressEnter();
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            Assert.Equal(ledger, await fixture.TakeLedger());
            Assert.Single((await fixture.LearnerSnapshot()).RecentAttempts);
            Assert.False(fixture.Sequence.IsBlocking);
            await fixture.Render();
            Assert.DoesNotContain("combat-ceremony", await fixture.Html());
        }

        private sealed class Fixture : IAsyncDisposable
        {
            private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstHomeCeremony_" + Guid.NewGuid().ToString("N"));
            private readonly Channel<(TimeSpan Duration, TaskCompletionSource Gate)> _delays = Channel.CreateUnbounded<(TimeSpan Duration, TaskCompletionSource Gate)>();
            private readonly ServiceProvider _services;
            private readonly CyberDefenseCombatCoordinator _coordinator;
            private readonly InMemoryLearner _learner;
            private readonly FaultingGameplayStore _faults;
            private HtmlRenderer _renderer = null!;
            private global::MathFirst.Core.Tests.Home _home = null!;
            private Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent _root;
            private bool _viewRemoved;
            public Task Rendering { get; private set; } = Task.CompletedTask;
            public int Victories { get; private set; }
            public FakeClock Clock { get; } = new();
            public Preferences Preferences { get; } = new();
            public SqliteGameplayStore Store { get; }
            public TrainingSession Session { get; }
            public CyberDefensePresentationSequence Sequence { get; }
            public string LastSubmittedAnswer { get; private set; } = string.Empty;
            public T Field<T>(string name) => (T)typeof(global::MathFirst.Core.Tests.Home)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_home)!;
            public T Property<T>(string name) => (T)typeof(global::MathFirst.Core.Tests.Home)
                .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_home)!;
            public AppBackNavigationCoordinator Back { get; } = new();
            public Task Render() => Dispatch(() => typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_home, null));
            public Task<LearnerSnapshot> LearnerSnapshot() => _learner.LoadSnapshotAsync();
            public void FailValidityReads(bool fail) => _faults.FailProgressionReads = fail;
            public void FailReceiptReads(bool fail) => _faults.FailReceiptReads = fail;
            public int FailedValidityReads => _faults.FailedReads;
            public int DrainDelays() { var drained = 0; while (_delays.Reader.TryRead(out _)) drained++; return drained; }

            /// <summary>Everything a cancelled or failed presentation must leave untouched: rewards, run state, answers and commits.</summary>
            public sealed record Ledger(object Progression, object Run, int Attempts, int Commits);
            public async Task<Ledger> TakeLedger() => new(await Store.GetProgressionAsync(), await Store.GetRunStateAsync(),
                (await LearnerSnapshot()).RecentAttempts.Count, LearnerCommitCalls);

            /// <summary>The committed CorrectFeedback question stays exactly where it was: no advance, no input renewal, no timing.</summary>
            public async Task AssertHeld(long revision, int inputVersion, long preparedRevision, Ledger ledger)
            {
                Assert.False(Sequence.IsBlocking);
                Assert.False(Sequence.CanContinue);
                Assert.Equal(SessionInteractionState.CorrectFeedback, Session.InteractionState);
                Assert.True(Session.IsCurrentSubmissionCommitted);
                Assert.Equal(revision, Session.FactInstanceRevision);
                Assert.Equal(inputVersion, Field<int>("_inputRenderVersion"));
                Assert.Equal(preparedRevision, Field<long>("_lastPreparedFactInstanceRevision"));
                Assert.False(Session.IsTimingActive);
                Assert.Equal(ledger, await TakeLedger());

                // The held answer is never a dead end: the explicit Continue recovery stays visible.
                await Render();
                Assert.Contains("held-answer-title", await Html());
            }

            /// <summary>Waits until a fire-and-forget recovery has advanced the question and released the submit latch.</summary>
            public async Task WhenResumed(long revision)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (Session.FactInstanceRevision < revision || Field<bool>("_isSubmitting"))
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("The recovery did not complete.");
                    await Task.Delay(5);
                }
            }
            public Task ResetIndependentLearner() => _learner.ResetLearningProgressAsync();
            public int LearnerCommitCalls => _learner.CommitCalls;
            public async Task CommitIndependentLearnerAttempt()
            {
                var independent = new TrainingSession(_learner, Clock, preferenceStore: Preferences);
                await independent.InitializeAsync();
                independent.SubmitAnswer(independent.CurrentFact.CorrectResult);
                Assert.True((await independent.CommitCurrentEvaluationAsync()).IsSuccess);
            }
            public RecoveryLoadGate GateNextRecoveryLoad(bool afterSnapshot)
            {
                var gate = new RecoveryLoadGate();
                if (afterSnapshot) _learner.SelectionGate = gate;
                else _learner.RuntimeGate = gate;
                return gate;
            }
            public void AssertCurrentQuestionUnbound()
            {
                var revision = Session.FactInstanceRevision;
                Assert.Equal(-1, _coordinator.GetBoundEffectiveAttackDamageOrDefault(revision, -1));
                Assert.Equal(-1, _coordinator.GetBoundFirewallRankOrDefault(revision, -1));
                var ranks = _coordinator.GetBoundCombatModifierRanksOrDefault(revision);
                Assert.Equal(0, ranks.BoundCriticalStrikeRank);
                Assert.Equal(0, ranks.BoundOverdriveRank);
            }
            public async Task AssertQuestionBindings()
            {
                var revision = Session.FactInstanceRevision;
                await _coordinator.AwaitQuestionBoundFirewallActivationAsync(revision);
                var ranks = _coordinator.GetBoundCombatModifierRanksOrDefault(revision);
                Assert.Equal(1, ranks.BoundCriticalStrikeRank);
                Assert.Equal(1, ranks.BoundOverdriveRank);
                Assert.Equal(2, _coordinator.GetBoundEffectiveAttackDamageOrDefault(revision));
                Assert.Equal(1, _coordinator.GetBoundFirewallRankOrDefault(revision));
                Assert.Equal(1, (await Store.GetRunStateAsync()).ShieldCapacity);
            }

            private Fixture()
            {
                Directory.CreateDirectory(_directory);
                Store = new SqliteGameplayStore(Path.Combine(_directory, "gameplay.db"));
                _faults = FaultingGameplayStore.Wrap(Store);
                _learner = new InMemoryLearner();
                Session = new TrainingSession(_learner, Clock, preferenceStore: Preferences);
                _coordinator = new CyberDefenseCombatCoordinator(Store, new CyberDefenseSubmissionConsumer(Store, _learner));
                Sequence = new CyberDefensePresentationSequence((duration, ct) =>
                {
                    var delay = new TaskCompletionSource();
                    _delays.Writer.TryWrite((duration, delay));
                    return delay.Task.WaitAsync(ct);
                });
                // Counts boss result card presentations (entries into Recap), not the Changed events within one card.
                var shownStage = CyberDefensePresentationStage.None;
                Sequence.Changed += () =>
                {
                    if (Sequence.Stage == CyberDefensePresentationStage.Recap && shownStage != CyberDefensePresentationStage.Recap) Victories++;
                    shownStage = Sequence.Stage;
                };
                _services = new ServiceCollection().AddLogging()
                    .AddSingleton(Session).AddSingleton(Sequence).AddSingleton<IGameplayStore>(_faults.Proxy)
                    .AddSingleton<ICyberDefenseCombatCoordinator>(_coordinator)
                    .AddSingleton<ILocalizationService>(new LocalizationService())
                    .AddSingleton<IPreferenceStore>(Preferences).AddSingleton<ICyberDefenseModePreferences>(Preferences)
                    .AddSingleton<ICyberDefenseTutorialPreferences>(Preferences)
                    .AddSingleton(new CyberDefenseSessionState(Preferences))
                    .AddSingleton(new PracticeGateCopyState(new PracticeCopySelector(new PracticeCopyLibrary()))).AddSingleton<IAppResetCoordinator>(new NoReset())
                    .AddSingleton<IAppBackNavigationCoordinator>(Back)
                    .AddSingleton<IHapticFeedbackService>(new HapticFeedbackService())
                    .AddSingleton<IJSRuntime>(new NoJs()).BuildServiceProvider();
            }

            public static async Task<Fixture> Create(bool victory = true, bool recoveredStartup = false, bool ordinary = false, bool calm = false, bool upgrades = false, bool withoutFirewall = false)
            {
                var f = new Fixture();
                f.Preferences.SetCyberDefenseEnabled(!calm);
                await f.Store.InitializeAsync();
                for (var i = 0; i < (ordinary ? 0 : victory ? 21 : 24); i++)
                {
                    await f.Store.ApplyAttemptTransactionAsync(new CyberDefensePendingIntentRecord("setup-" + i, "1+1", victory, true, 1000, 0, DateTimeOffset.UnixEpoch));
                }
                if (upgrades)
                {
                    using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={f.Store.StoragePath}");
                    await connection.OpenAsync();
                    using var command = connection.CreateCommand();
                    command.CommandText = "UPDATE gameplay_progression SET cumulative_xp = @xp, player_level = 80, earned_sp = 79, spent_sp = 0, available_sp = 79 WHERE id = 1;";
                    command.Parameters.AddWithValue("@xp", MathFirst.Domain.CyberDefense.CyberDefenseLevelPolicy.GetCumulativeXpThreshold(80));
                    await command.ExecuteNonQueryAsync();
                    await f.Store.PurchaseAttackUpgradeAsync(0, 0);
                    if (!withoutFirewall) await f.Store.PurchaseFirewallUpgradeAsync(0, 0);
                    await f.Store.PurchaseCriticalStrikeUpgradeAsync(0, 0);
                    await f.Store.PurchaseOverdriveUpgradeAsync(0, 0);
                }
                if (recoveredStartup)
                {
                    var interruptedSession = new TrainingSession(f._learner, f.Clock, preferenceStore: f.Preferences);
                    await interruptedSession.InitializeAsync();
                    var eval = interruptedSession.SubmitAnswer(interruptedSession.CurrentFact.CorrectResult);
                    await interruptedSession.CommitCurrentEvaluationAsync();
                    var a = eval.ChangeSet.Attempt;
                    await f._coordinator.StageIntentAsync(a.SubmissionId, a.FactId, true, a.ResponseLatencyMs, true, 1, 0, 0, a.PracticePosition);
                }
                else
                {
                    await f.Session.InitializeAsync();
                    await f._coordinator.InitializeAsync();
                }
                f.Rendering = f.MountView();
                if (!recoveredStartup) await f.Rendering;
                return f;
            }

            public async Task MountView()
            {
                _viewRemoved = false;
                _renderer = new HtmlRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
                _root = await _renderer.Dispatcher.InvokeAsync(() => _renderer.RenderComponentAsync<HomeHost>(
                    ParameterView.FromDictionary(new Dictionary<string, object?> { ["Capture"] = (Action<global::MathFirst.Core.Tests.Home>)(home => _home = home) })));
            }

            public async Task RemoveView()
            {
                if (_viewRemoved) return;
                _viewRemoved = true;
                var refresh = typeof(global::MathFirst.Core.Tests.Home).GetField("_terminalRefreshTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_home) as Task;
                await _renderer.DisposeAsync();
                if (refresh is not null) await refresh;
            }

            public Task Submit(bool correct = true) => _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var answer = Session.CurrentFact.CorrectResult + (correct ? 0 : 1);
                LastSubmittedAnswer = answer.ToString(CultureInfo.InvariantCulture);
                typeof(global::MathFirst.Core.Tests.Home).GetField("_answerInput", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_home, LastSubmittedAnswer);
                await InvokeCore("Submit");
            });
            private Task InvokeCore(string method) => typeof(global::MathFirst.Core.Tests.Home)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_home, null) as Task ?? Task.CompletedTask;
            public Task Invoke(string method) => _renderer.Dispatcher.InvokeAsync(() => InvokeCore(method));
            public Task Dispatch(Action action) => _renderer.Dispatcher.InvokeAsync(action);
            public Task DispatchAsync(Func<Task> action) => _renderer.Dispatcher.InvokeAsync(action);
            public Task<string> Html() => _renderer.Dispatcher.InvokeAsync(() => _root.ToHtmlString());
            public TimeSpan LastDelay { get; private set; }
            public async Task<TaskCompletionSource> NextDelay()
            {
                var next = await _delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                LastDelay = next.Duration;
                return next.Gate;
            }

            /// <summary>
            /// Waits for Continue. The boss result card offers it from its first presentation without any timer (Issue #138);
            /// System Restart (<paramref name="boss"/> false) elapses its 3 s reveal delay first.
            /// </summary>
            public async Task RevealContinue(bool boss = true)
            {
                if (boss)
                {
                    // No fake delay is elapsed here: Continue must be available without one.
                    await WhenContinue();
                    return;
                }
                var reveal = await NextDelay();
                Assert.Equal(TimeSpan.FromSeconds(3), LastDelay);
                reveal.TrySetResult();
                await WhenContinue();
            }

            /// <summary>Elapses fake revalidation ticks; the ceremony must still be waiting for the user afterwards.</summary>
            public async Task ElapseRevalidation(TimeSpan total)
            {
                for (var elapsed = TimeSpan.Zero; elapsed < total;)
                {
                    var tick = await NextDelay();
                    elapsed += LastDelay;
                    tick.TrySetResult();
                }
            }

            public Task WhenContinue()
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Check() { if (Sequence.CanContinue) { Sequence.Changed -= Check; ready.TrySetResult(); } }
                Sequence.Changed += Check;
                Check();
                return ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }

            public Task Continue(long? presentationId = null) => _renderer.Dispatcher.InvokeAsync(() =>
                typeof(global::MathFirst.Core.Tests.Home).GetMethod("HandleCeremonyContinue", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, new object[] { presentationId ?? Sequence.PresentationId }));

            public Task PressEnter() => _renderer.Dispatcher.InvokeAsync(() =>
                (Task)typeof(global::MathFirst.Core.Tests.Home).GetMethod("HandleGlobalKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, new object[] { new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" } })!);
            public void FailNextLearnerCommit() => _learner.FailNextCommit = true;
            public Task WhenStage(CyberDefensePresentationStage stage)
            {
                var ready = new TaskCompletionSource();
                void Check() { if (Sequence.Stage == stage) { Sequence.Changed -= Check; ready.TrySetResult(); } }
                Sequence.Changed += Check;
                Check();
                return ready.Task;
            }

            /// <summary>The stage and Continue availability of the first presentation that becomes visible.</summary>
            public Task<(CyberDefensePresentationStage Stage, bool CanContinue)> WhenPresented()
            {
                var ready = new TaskCompletionSource<(CyberDefensePresentationStage, bool)>();
                void Check() { if (Sequence.Stage != CyberDefensePresentationStage.None) { Sequence.Changed -= Check; ready.TrySetResult((Sequence.Stage, Sequence.CanContinue)); } }
                Sequence.Changed += Check;
                return ready.Task;
            }
            public async ValueTask DisposeAsync()
            {
                await RemoveView();
                _coordinator.Dispose();
                _learner.Dispose();
                Store.Dispose();
                await _services.DisposeAsync();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(_directory, recursive: true);
            }
        }

        /// <summary>Forwards to the real SQLite store and fails the validity read (<c>GetProgressionAsync</c>) on demand.</summary>
        public class FaultingGameplayStore : DispatchProxy
        {
            private IGameplayStore _inner = null!;
            private int _failedReads;
            public volatile bool FailProgressionReads;
            public volatile bool FailReceiptReads;
            public int FailedReads => Volatile.Read(ref _failedReads);
            public IGameplayStore Proxy => (IGameplayStore)(object)this;
            public static FaultingGameplayStore Wrap(IGameplayStore inner)
            {
                var proxy = (FaultingGameplayStore)(object)Create<IGameplayStore, FaultingGameplayStore>();
                proxy._inner = inner;
                return proxy;
            }
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (FailProgressionReads && targetMethod!.Name == nameof(IGameplayStore.GetProgressionAsync))
                {
                    Interlocked.Increment(ref _failedReads);
                    return Task.FromException<CyberDefenseProgressionRecord>(new InvalidOperationException("Synthetic transient SQLite read failure"));
                }
                if (FailReceiptReads && targetMethod!.Name == nameof(IGameplayStore.GetReceiptAsync))
                {
                    Interlocked.Increment(ref _failedReads);
                    return Task.FromException<CyberDefenseReceiptRecord?>(new InvalidOperationException("Synthetic transient SQLite read failure"));
                }
                try { return targetMethod!.Invoke(_inner, args); }
                catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
            }
        }

        public sealed class HomeHost : ComponentBase
        {
            [Parameter] public Action<global::MathFirst.Core.Tests.Home> Capture { get; set; } = null!;
            protected override void BuildRenderTree(RenderTreeBuilder builder)
            {
                builder.OpenComponent<global::MathFirst.Core.Tests.Home>(0);
                builder.AddComponentReferenceCapture(1, home => Capture((global::MathFirst.Core.Tests.Home)home));
                builder.CloseComponent();
            }
        }

        private sealed class NoReset : IAppResetCoordinator
        {
            public Task ExecuteFullResetAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task<bool> ReconcileStartupResetStateAsync(CancellationToken ct = default) => Task.FromResult(false);
        }
        private sealed class RecoveryLoadGate
        {
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public async Task Wait(CancellationToken ct)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
        }
        private sealed class InMemoryLearner : ILearnerStore
        {
            public bool FailNextCommit { get; set; }
            public int CommitCalls { get; private set; }
            public RecoveryLoadGate? RuntimeGate { get; set; }
            public RecoveryLoadGate? SelectionGate { get; set; }
            private LearnerSnapshot _snapshot = Fresh();
            private static LearnerSnapshot Fresh(long revision = 1)
            {
                var progression = LearnerProgression.CreateFresh();
                progression.StoreRevision = revision;
                return new(progression, new Dictionary<string, ItemLearningState>(), [], revision, LearnerProgression.DefaultSchemaVersion);
            }
            public string StoragePath => "synthetic://home-ceremony";
            public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken ct = default) => Task.FromResult(_snapshot);
            public async Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken ct = default)
            {
                var snapshot = _snapshot;
                var gate = RuntimeGate;
                RuntimeGate = null;
                if (gate is not null) await gate.Wait(ct);
                return snapshot;
            }
            public async Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(PracticeSelectionEvidenceRequest request, CancellationToken ct = default)
            {
                var gate = SelectionGate;
                SelectionGate = null;
                if (gate is not null) await gate.Wait(ct);
                return PracticeSelectionEvidence.FromSnapshot(_snapshot, request);
            }
            public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(ArithmeticOperation operation, long position, IReadOnlyList<string> ids, CancellationToken ct = default)
                => Task.FromResult(TestStoreEvidenceHelper.FilterLatestFrontierAttempts(_snapshot.RecentAttempts, operation, position, ids));
            public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet change, CancellationToken ct = default)
            {
                CommitCalls++;
                if (FailNextCommit) { FailNextCommit = false; return Task.FromResult(PersistenceResult.Unavailable("Synthetic failure")); }
                if (change.ExpectedRevision != _snapshot.Revision) return Task.FromResult(PersistenceResult.Conflict("Synthetic optimistic revision conflict"));
                var items = _snapshot.ItemStates.ToDictionary(p => p.Key, p => p.Value);
                items[change.UpdatedItemState.FactId] = change.UpdatedItemState;
                _snapshot = new(change.UpdatedProgression, items, _snapshot.RecentAttempts.Append(change.Attempt).ToArray(), change.ExpectedRevision + 1, LearnerProgression.DefaultSchemaVersion);
                _snapshot.Progression.StoreRevision = _snapshot.Revision;
                return Task.FromResult(PersistenceResult.Success(_snapshot.Revision));
            }
            public Task<CommittedLearnerAttemptEvidence?> GetCommittedAttemptEvidenceAsync(string id, CancellationToken ct = default)
            {
                var attempt = _snapshot.RecentAttempts.FirstOrDefault(a => a.SubmissionId == id);
                return Task.FromResult(attempt is null ? null : new CommittedLearnerAttemptEvidence(id, attempt.FactId, attempt.IsCorrect, attempt.ResponseLatencyMs, attempt.PracticePosition, attempt.Timestamp));
            }
            public Task ResetLearningProgressAsync(CancellationToken ct = default) { _snapshot = Fresh(_snapshot.Revision + 1); return Task.CompletedTask; }
            public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;
            public void Dispose() { }
        }
        private sealed class NoJs : IJSRuntime
        {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        }
        private sealed class FakeClock : IClock
        {
            private long _now;
            public long GetTimestamp() => _now;
            public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(_now - startTimestamp);
            public void Advance(long milliseconds) => _now += milliseconds;
        }
        private sealed class Preferences : IPreferenceStore, ICyberDefenseModePreferences, ICyberDefenseTutorialPreferences
        {
            private bool _cyber = true;
            public bool GetTutorialCompleted() => true;
            public void SetTutorialCompleted(bool completed) { }
            public bool GetCyberDefenseEnabled() => _cyber;
            public void SetCyberDefenseEnabled(bool enabled) => _cyber = enabled;
            public ThemePreference GetThemePreference() => ThemePreference.System;
            public void SetThemePreference(ThemePreference preference) { }
            public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
            public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
            public string GetLanguagePreference() => "en";
            public void SetLanguagePreference(string languageCode) { }
            public bool GetOperationEnabled(ArithmeticOperation operation) => operation == ArithmeticOperation.Addition;
            public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) { }
            public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => [ArithmeticOperation.Addition];
            public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
            public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
            public bool GetHapticFeedbackEnabled() => false;
            public void SetHapticFeedbackEnabled(bool enabled) { }
            public void ResetPracticePreferences() { }
            public void ResetAllPreferences() { }
        }
    }
}

// Linked Razor components retain Home's production imports. The components themselves are
// compiled into the test namespace by the Razor SDK; these markers satisfy those imports only.
namespace MathFirst.App.Components.Training { internal sealed class TestImportMarker { } }
