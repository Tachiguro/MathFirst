namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

public sealed class TeachingLockAndVisualFeedbackTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstSlice3Tests_" + Guid.NewGuid().ToString("N"));

    public TeachingLockAndVisualFeedbackTests() => Directory.CreateDirectory(_testDirectory);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void TeachingLockTracker_StartsLocked_AndEnforcesMinimum3SecondsVisibleTime()
    {
        var clock = new IncrementingClock(1_000_000);
        var tracker = new TeachingLockTracker(clock, lockDurationMs: 3000);

        tracker.Activate();

        Assert.True(tracker.IsActive);
        Assert.False(tracker.IsUnlocked);
        Assert.False(tracker.TryAcknowledge());
        Assert.Equal(3, tracker.RemainingSecondsCeiling);

        // After 1000 ms -> 2 seconds remaining
        clock.Advance(TimeSpan.FromMilliseconds(1000));
        Assert.False(tracker.IsUnlocked);
        Assert.False(tracker.TryAcknowledge());
        Assert.Equal(2, tracker.RemainingSecondsCeiling);

        // After 2000 ms total -> 1 second remaining
        clock.Advance(TimeSpan.FromMilliseconds(1000));
        Assert.False(tracker.IsUnlocked);
        Assert.False(tracker.TryAcknowledge());
        Assert.Equal(1, tracker.RemainingSecondsCeiling);

        // After 2999 ms total -> 1 second remaining, still locked
        clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(tracker.IsUnlocked);
        Assert.False(tracker.TryAcknowledge());
        Assert.Equal(1, tracker.RemainingSecondsCeiling);

        // At exactly 3000 ms total -> Unlocked
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(tracker.IsUnlocked);
        Assert.True(tracker.TryAcknowledge());
        Assert.Equal(0, tracker.RemainingSecondsCeiling);
    }

    [Fact]
    public void TeachingLockTracker_Backgrounding_FreezesElapsedVisibleTime()
    {
        var clock = new IncrementingClock(1_000_000);
        var tracker = new TeachingLockTracker(clock, lockDurationMs: 3000);

        tracker.Activate();

        // 1 second in foreground
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, tracker.RemainingSecondsCeiling);

        // App goes to background
        tracker.SetForeground(false);
        Assert.False(tracker.IsForeground);

        // 60 seconds elapse while backgrounded
        clock.Advance(TimeSpan.FromSeconds(60));

        // Must still be locked with 2 seconds remaining!
        Assert.False(tracker.IsUnlocked);
        Assert.Equal(2, tracker.RemainingSecondsCeiling);

        // App returns to foreground
        tracker.SetForeground(true);
        Assert.True(tracker.IsForeground);

        // Advance 1 second in foreground -> 1s remaining
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(tracker.IsUnlocked);
        Assert.Equal(1, tracker.RemainingSecondsCeiling);

        // Advance final 1 second in foreground -> unlocked
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(tracker.IsUnlocked);
        Assert.Equal(0, tracker.RemainingSecondsCeiling);
    }

    [Fact]
    public void TeachingLockTracker_Reactivation_StartsFreshFullLock()
    {
        var clock = new IncrementingClock(1_000_000);
        var tracker = new TeachingLockTracker(clock, lockDurationMs: 3000);

        tracker.Activate();
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True(tracker.IsUnlocked);

        // New teaching intervention triggers fresh activation
        tracker.Activate();
        Assert.False(tracker.IsUnlocked);
        Assert.Equal(3, tracker.RemainingSecondsCeiling);

        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True(tracker.IsUnlocked);
    }

    [Fact]
    public void TeachingLockTracker_Deactivation_CancelsState()
    {
        var clock = new IncrementingClock(1_000_000);
        var tracker = new TeachingLockTracker(clock, lockDurationMs: 3000);

        tracker.Activate();
        Assert.True(tracker.IsActive);

        tracker.Deactivate();
        Assert.False(tracker.IsActive);
        Assert.Equal(0, tracker.RemainingSecondsCeiling);
    }

    [Fact]
    public async Task TeachingIntervention_Acknowledgement_PreservesZeroMutationContract()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var clock = new IncrementingClock(1_000_000);
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();
        session.StartOrResumePractice();

        var fact = session.CurrentFact;
        Assert.NotNull(fact);

        // Simulate 2 consecutive errors on the same fact to trigger teaching intervention
        session.SubmitAnswer(fact.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();
        await session.AcknowledgeFeedbackAsync();

        // Advance until fact is encountered again or force second error
        session.SubmitAnswer(fact.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();

        if (session.InteractionState == SessionInteractionState.TeachingIntervention)
        {
            var initialPosition = session.Progression.PracticePosition;
            var initialCount = session.SessionTotalCount;

            // Acknowledge teaching overlay
            var ack = await session.AcknowledgeTeachingInterventionAsync();
            Assert.True(ack);

            // Assert zero learning mutation: position, count, and snapshot untouched by acknowledgement
            Assert.Equal(initialPosition, session.Progression.PracticePosition);
            Assert.Equal(initialCount, session.SessionTotalCount);
        }
    }

    [Fact]
    public void TeachingInterventionDialog_MarkupContract_WiresLockAndLocalization()
    {
        var dialog = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Shared", "TeachingInterventionDialog.razor"));

        Assert.Contains("TeachingLockTracker", dialog, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(!_isUnlocked)\"", dialog, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"@(!_isUnlocked)\"", dialog, StringComparison.Ordinal);
        Assert.Contains("Training_TeachingContinueLocked", dialog, StringComparison.Ordinal);
        Assert.Contains("Common_Continue", dialog, StringComparison.Ordinal);
        Assert.Contains("Session.AppForegroundStateChanged", dialog, StringComparison.Ordinal);
        Assert.Contains("PeriodicTimer", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_EnterKeyContract_BlocksEnterDuringLockedTeachingIntervention()
    {
        var home = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // Global keydown must check TeachingIntervention and require unlocked state
        Assert.Contains("else if (Session.InteractionState == SessionInteractionState.TeachingIntervention)", home, StringComparison.Ordinal);
        Assert.Contains("_teachingDialog?.IsUnlocked == true", home, StringComparison.Ordinal);

        // AdvanceAsync must have defense-in-depth lock check
        Assert.Contains("if (_teachingDialog is not null && !_teachingDialog.IsUnlocked)", home, StringComparison.Ordinal);

        // Uses TeachingInterventionDialog component
        Assert.Contains("<TeachingInterventionDialog", home, StringComparison.Ordinal);
    }

    [Fact]
    public void PauseButton_StylingContract_UsesDedicatedAmberPauseAndPreservesDangerElsewhere()
    {
        var home = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Home.razor"));
        var settings = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Settings.razor"));
        var styles = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "wwwroot", "app.css"));

        // Home Pause button uses button-pause, not button-danger
        Assert.Contains("class=\"button button-pause pause-practice-btn\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"button button-danger pause-practice-btn\"", home, StringComparison.Ordinal);

        // CSS defines button-pause and button-warning with amber/yellow palette
        Assert.Contains(".button-pause", styles, StringComparison.Ordinal);
        Assert.Contains(".button-warning", styles, StringComparison.Ordinal);
        Assert.Contains(".pause-practice-btn.button-pause", styles, StringComparison.Ordinal);
        Assert.DoesNotContain(".pause-practice-btn.button-danger", styles, StringComparison.Ordinal);
        Assert.Contains("--color-pause: #b26a00;", styles, StringComparison.Ordinal);
        Assert.Contains("--color-pause: #e09f3e;", styles, StringComparison.Ordinal);

        // Destructive reset controls in Settings preserve button-danger
        Assert.Contains("class=\"button button-danger\"", settings, StringComparison.Ordinal);
        Assert.Contains("ExecuteResetLearning", settings, StringComparison.Ordinal);
        Assert.Contains("ExecuteFullLocalReset", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void TimerTypography_Contract_RemovesTextStrokeAndPreservesCleanLegibility()
    {
        var styles = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "wwwroot", "app.css"));
        var timerComponent = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Shared", "PracticeCountdownTimer.razor"));

        // No -webkit-text-stroke anywhere in app.css
        Assert.DoesNotContain("-webkit-text-stroke", styles, StringComparison.OrdinalIgnoreCase);

        // Timer bar text uses clean modern typography with restrained text-shadow contour without pill backing
        Assert.Contains(".timer-bar-text {", styles, StringComparison.Ordinal);
        Assert.Contains("font-variant-numeric: tabular-nums;", styles, StringComparison.Ordinal);
        Assert.Contains("color: #ffffff;", styles, StringComparison.Ordinal);
        Assert.Contains("text-shadow:", styles, StringComparison.Ordinal);

        // Pill background and backdrop-filter are completely removed from timer bar text
        Assert.DoesNotContain("background: rgba(18, 25, 22, 0.65);", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("backdrop-filter: blur(4px);", styles, StringComparison.Ordinal);

        // Slice 1 timer component isolation intact (100ms PeriodicTimer)
        Assert.Contains("PeriodicTimer(TimeSpan.FromMilliseconds(100))", timerComponent, StringComparison.Ordinal);
    }

    private string GetTempDbPath() => Path.Combine(_testDirectory, $"test_{Guid.NewGuid():N}.db");

    private sealed class IncrementingClock : IClock
    {
        private long _timestamp;

        public IncrementingClock(long initial) => _timestamp = initial;

        public void Advance(TimeSpan duration) => _timestamp += (long)(duration.TotalMilliseconds * 1000.0);

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds((_timestamp - startTimestamp) / 1000.0);
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
