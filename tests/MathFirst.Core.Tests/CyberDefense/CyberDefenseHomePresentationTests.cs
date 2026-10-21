namespace MathFirst.Core.Tests.CyberDefense
{
    using System.Globalization;
    using System.Reflection;
    using System.Threading.Channels;
    using MathFirst.Application;
    using MathFirst.Application.Copy;
    using MathFirst.Application.Gameplay;
    using MathFirst.Application.Lifecycle;
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
                var shown = fixture.WhenStage(CyberDefensePresentationStage.Victory);
                var submission = fixture.Submit();
                await Task.WhenAny(shown, submission);
                Assert.Equal(CyberDefensePresentationStage.Victory, fixture.Sequence.Stage);
                (await fixture.NextDelay()).TrySetResult();
                (await fixture.NextDelay()).TrySetResult();
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
        [InlineData(CyberDefensePresentationStage.Victory)]
        [InlineData(CyberDefensePresentationStage.Recap)]
        public async Task BackgroundCeremonyCancellation_ExplicitResumeRecordsRealActiveAndPersistedLatency(CyberDefensePresentationStage stage)
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            fixture.Clock.Advance(2000);
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Victory);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            Assert.Equal(CyberDefensePresentationStage.Victory, fixture.Sequence.Stage);
            if (stage == CyberDefensePresentationStage.Recap)
            {
                (await fixture.NextDelay()).TrySetResult();
                await fixture.NextDelay();
                Assert.Equal(stage, fixture.Sequence.Stage);
            }
            await fixture.Dispatch(() => fixture.Session.SetAppForeground(false));
            await submission;
            Assert.Equal(revision + 1, fixture.Session.FactInstanceRevision);
            Assert.False(fixture.Sequence.IsBlocking);
            Assert.Equal(PracticeGateState.BackgroundResumeGate, fixture.Session.PracticeGate);
            fixture.Clock.Advance(100000);
            Assert.Equal(0, fixture.Session.GetCurrentActiveElapsedMs());
            await fixture.Dispatch(() => fixture.Session.SetAppForeground(true));
            fixture.Clock.Advance(50000);
            Assert.False(fixture.Session.IsTimingActive);
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
            Assert.Equal(1, fixture.Victories);
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
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Victory);
            var retry = fixture.Invoke("RecoverFromPersistenceFailureAsync");
            await Task.WhenAny(shown, retry);
            Assert.Equal(CyberDefensePresentationStage.Victory, fixture.Sequence.Stage);
            (await fixture.NextDelay()).TrySetResult();
            (await fixture.NextDelay()).TrySetResult();
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
        public async Task ColdStartup_RecoversBossReceipt_PresentsOnce_AndKeepsTheInitialPracticeGate()
        {
            await using var fixture = await Fixture.Create(recoveredStartup: true);
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Victory);
            await Task.WhenAny(shown, fixture.Rendering);
            Assert.Equal(CyberDefensePresentationStage.Victory, fixture.Sequence.Stage);
            Assert.False(fixture.Session.IsTimingActive);
            (await fixture.NextDelay()).TrySetResult();
            (await fixture.NextDelay()).TrySetResult();
            await fixture.Rendering;
            Assert.Equal(2, (await fixture.Store.GetRunStateAsync()).Sector);
            Assert.Equal(PracticeGateState.InitialReadyGate, fixture.Session.PracticeGate);
            Assert.False(fixture.Session.IsTimingActive);
            Assert.Equal(1, fixture.Victories);
        }

        [Fact(Timeout = 10000)]
        public async Task DisposalAndReentry_DoNotAdvanceDisposedViewOrReplayTheSameBossReceipt()
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            var inputVersion = fixture.Field<int>("_inputRenderVersion");
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Victory);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            Assert.Equal(CyberDefensePresentationStage.Victory, fixture.Sequence.Stage);
            await fixture.RemoveView();
            await submission;
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.Equal(inputVersion, fixture.Field<int>("_inputRenderVersion"));
            await fixture.MountView();
            Assert.True(fixture.Session.FactInstanceRevision > revision);
            Assert.Equal(1, fixture.Victories);
        }

        [Fact]
        public async Task BossVictory_HoldsRealHomeAdvanceAndActiveAnswerTime_UntilRecapFinishes()
        {
            await using var fixture = await Fixture.Create();
            var revision = fixture.Session.FactInstanceRevision;
            fixture.Clock.Advance(2000);
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Victory);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            Assert.Equal(CyberDefensePresentationStage.Victory, fixture.Sequence.Stage);
            Assert.False(submission.IsCompleted);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.False(fixture.Session.IsTimingActive);
            var html = await fixture.Html();
            Assert.Matches("<header[^>]*inert(?:=\"[^\"]*\")?[^>]*aria-hidden=\"true\"", html);
            Assert.Matches("<main[^>]*inert(?:=\"[^\"]*\")?[^>]*aria-hidden=\"true\"", html);
            Assert.Contains("combat-ceremony-overlay", html);
            Assert.Equal(2, (await fixture.Store.GetRunStateAsync()).Sector);
            Assert.Equal(176, (await fixture.Store.GetProgressionAsync()).CumulativeXp);
            fixture.Clock.Advance(500000);
            Assert.Equal(2000, fixture.Session.LastEvaluation!.LatencyMs);
            (await fixture.NextDelay()).TrySetResult();
            var recapDelay = await fixture.NextDelay();
            Assert.Equal(CyberDefensePresentationStage.Recap, fixture.Sequence.Stage);
            Assert.Equal(revision, fixture.Session.FactInstanceRevision);
            Assert.False(submission.IsCompleted);
            recapDelay.TrySetResult();
            await submission;
            Assert.True(fixture.Session.FactInstanceRevision > revision);
            Assert.Equal(0, fixture.Session.GetCurrentActiveElapsedMs());
            fixture.Clock.Advance(1000);
            Assert.Equal(1000, fixture.Session.GetCurrentActiveElapsedMs());
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
        public async Task ContextChangeDuringVictory_AbortsRecapWithoutLeakingTimeOrAdvancingOldGeneration(string change)
        {
            await using var fixture = await Fixture.Create();
            var inputVersion = fixture.Field<int>("_inputRenderVersion");
            var shown = fixture.WhenStage(CyberDefensePresentationStage.Victory);
            var submission = fixture.Submit();
            await Task.WhenAny(shown, submission);
            Assert.Equal(CyberDefensePresentationStage.Victory, fixture.Sequence.Stage);
            var delay = await fixture.NextDelay();
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
            delay.TrySetResult();
            await submission;
            Assert.False(fixture.Sequence.IsBlocking);
            if (change == "background")
            {
                Assert.False(fixture.Session.IsTimingActive);
                Assert.Equal(PracticeGateState.BackgroundResumeGate, fixture.Session.PracticeGate);
            }
            if (change == "mode") Assert.DoesNotContain("cyber-defense-hud", await fixture.Html());
            if (change == "reset")
            {
                Assert.Equal(revisionAfterChange, fixture.Session.FactInstanceRevision);
                Assert.Equal(inputVersion, fixture.Field<int>("_inputRenderVersion"));
            }
        }

        private sealed class Fixture : IAsyncDisposable
        {
            private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstHomeCeremony_" + Guid.NewGuid().ToString("N"));
            private readonly Channel<TaskCompletionSource> _delays = Channel.CreateUnbounded<TaskCompletionSource>();
            private readonly ServiceProvider _services;
            private readonly CyberDefenseCombatCoordinator _coordinator;
            private readonly InMemoryLearner _learner;
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
            public Task Render() => Dispatch(() => typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_home, null));
            public Task<LearnerSnapshot> LearnerSnapshot() => _learner.LoadSnapshotAsync();
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
                _learner = new InMemoryLearner();
                Session = new TrainingSession(_learner, Clock, preferenceStore: Preferences);
                _coordinator = new CyberDefenseCombatCoordinator(Store, new CyberDefenseSubmissionConsumer(Store, _learner));
                Sequence = new CyberDefensePresentationSequence((duration, ct) =>
                {
                    Assert.Equal(TimeSpan.FromSeconds(2), duration);
                    var delay = new TaskCompletionSource();
                    _delays.Writer.TryWrite(delay);
                    return delay.Task.WaitAsync(ct);
                });
                Sequence.Changed += () => { if (Sequence.Stage == CyberDefensePresentationStage.Victory) Victories++; };
                _services = new ServiceCollection().AddLogging()
                    .AddSingleton(Session).AddSingleton(Sequence).AddSingleton<IGameplayStore>(Store)
                    .AddSingleton<ICyberDefenseCombatCoordinator>(_coordinator)
                    .AddSingleton<ILocalizationService>(new LocalizationService())
                    .AddSingleton<IPreferenceStore>(Preferences).AddSingleton<ICyberDefenseModePreferences>(Preferences)
                    .AddSingleton(new PracticeGateCopyState(new PracticeCopySelector(new PracticeCopyLibrary()))).AddSingleton<IAppResetCoordinator>(new NoReset())
                    .AddSingleton<IHapticFeedbackService>(new HapticFeedbackService())
                    .AddSingleton<IJSRuntime>(new NoJs()).BuildServiceProvider();
            }

            public static async Task<Fixture> Create(bool victory = true, bool recoveredStartup = false, bool ordinary = false, bool calm = false, bool upgrades = false)
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
                    await f.Store.PurchaseFirewallUpgradeAsync(0, 0);
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
            public Task<TaskCompletionSource> NextDelay() => _delays.Reader.ReadAsync().AsTask();
            public void FailNextLearnerCommit() => _learner.FailNextCommit = true;
            public Task WhenStage(CyberDefensePresentationStage stage)
            {
                var ready = new TaskCompletionSource();
                void Check() { if (Sequence.Stage == stage) { Sequence.Changed -= Check; ready.TrySetResult(); } }
                Sequence.Changed += Check;
                Check();
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
        private sealed class InMemoryLearner : ILearnerStore
        {
            public bool FailNextCommit { get; set; }
            private LearnerSnapshot _snapshot = Fresh();
            private static LearnerSnapshot Fresh() => new(LearnerProgression.CreateFresh(), new Dictionary<string, ItemLearningState>(), [], 1, LearnerProgression.DefaultSchemaVersion);
            public string StoragePath => "synthetic://home-ceremony";
            public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken ct = default) => Task.FromResult(_snapshot);
            public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(ArithmeticOperation operation, long position, IReadOnlyList<string> ids, CancellationToken ct = default)
                => Task.FromResult(TestStoreEvidenceHelper.FilterLatestFrontierAttempts(_snapshot.RecentAttempts, operation, position, ids));
            public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet change, CancellationToken ct = default)
            {
                if (FailNextCommit) { FailNextCommit = false; return Task.FromResult(PersistenceResult.Unavailable("Synthetic failure")); }
                var items = _snapshot.ItemStates.ToDictionary(p => p.Key, p => p.Value);
                items[change.UpdatedItemState.FactId] = change.UpdatedItemState;
                _snapshot = new(change.UpdatedProgression, items, _snapshot.RecentAttempts.Append(change.Attempt).ToArray(), change.ExpectedRevision + 1, LearnerProgression.DefaultSchemaVersion);
                return Task.FromResult(PersistenceResult.Success(_snapshot.Revision));
            }
            public Task<CommittedLearnerAttemptEvidence?> GetCommittedAttemptEvidenceAsync(string id, CancellationToken ct = default)
            {
                var attempt = _snapshot.RecentAttempts.FirstOrDefault(a => a.SubmissionId == id);
                return Task.FromResult(attempt is null ? null : new CommittedLearnerAttemptEvidence(id, attempt.FactId, attempt.IsCorrect, attempt.ResponseLatencyMs, attempt.PracticePosition, attempt.Timestamp));
            }
            public Task ResetLearningProgressAsync(CancellationToken ct = default) { _snapshot = Fresh(); return Task.CompletedTask; }
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
        private sealed class Preferences : IPreferenceStore, ICyberDefenseModePreferences
        {
            private bool _cyber = true;
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
