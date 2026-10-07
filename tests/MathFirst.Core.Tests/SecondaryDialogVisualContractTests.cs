namespace MathFirst.Core.Tests;

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Visual contract tests for Secondary Dialogs and Overlays (Ready Gate, Pause Summary,
/// Teaching Intervention, Session Check-in, Feedback/Errors, Not Found)
/// under P5 Slice 2 (Dialogs & Secondary Overlay Visual Alignment).
/// </summary>
public sealed class SecondaryDialogVisualContractTests
{
    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private static string ReadAppCss() =>
        File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css"));

    private static string ReadHomeRazor() =>
        File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

    private static string ReadTeachingDialogRazor() =>
        File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Shared", "TeachingInterventionDialog.razor"));

    private static string ReadNotFoundRazor() =>
        File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "NotFound.razor"));

    [Fact]
    public void P5_Slice2_ReadyGateAndPauseSummary_DefineCyberTelemetryAlignmentAndTabularFigures()
    {
        var css = ReadAppCss();
        var home = ReadHomeRazor();

        // 1. Ready Gate base and dark theme rules in app.css
        Assert.Contains(".ready-progress-overview", css, StringComparison.Ordinal);
        Assert.Contains(".ready-progress-entry", css, StringComparison.Ordinal);
        Assert.Contains(".ready-progress-symbol", css, StringComparison.Ordinal);
        Assert.Contains(".ready-progress-stage", css, StringComparison.Ordinal);

        var darkReadyMatch = Regex.Match(css, @"\.app-theme-root\[data-theme=""dark""\]\s+\.ready-progress-overview\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(darkReadyMatch.Success, "Dark theme rule for .ready-progress-overview must exist in app.css");
        var darkReadyRules = darkReadyMatch.Groups["rules"].Value;
        Assert.Contains("border-color:", darkReadyRules, StringComparison.Ordinal);

        var darkEntryMatch = Regex.Match(css, @"\.app-theme-root\[data-theme=""dark""\]\s+\.ready-progress-entry\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(darkEntryMatch.Success, "Dark theme rule for .ready-progress-entry must exist in app.css");

        // Tabular figures for progression stage
        Assert.Contains("tabular-nums", css, StringComparison.Ordinal);

        // 2. Pause Summary base and dark theme rules
        Assert.Contains(".pause-session-summary", css, StringComparison.Ordinal);
        Assert.Contains(".pause-summary-row", css, StringComparison.Ordinal);
        Assert.Contains(".pause-summary-label", css, StringComparison.Ordinal);
        Assert.Contains(".pause-summary-value", css, StringComparison.Ordinal);

        var darkPauseMatch = Regex.Match(css, @"\.app-theme-root\[data-theme=""dark""\]\s+\.pause-session-summary\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(darkPauseMatch.Success, "Dark theme rule for .pause-session-summary must exist in app.css");
        var darkPauseRules = darkPauseMatch.Groups["rules"].Value;
        Assert.Contains("border-color:", darkPauseRules, StringComparison.Ordinal);

        var pauseValueMatch = Regex.Match(css, @"\.pause-summary-value\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(pauseValueMatch.Success, ".pause-summary-value rule must exist in app.css");
        Assert.Contains("tabular-nums", pauseValueMatch.Groups["rules"].Value, StringComparison.Ordinal);

        // 3. Preserved Home.razor structure and accessibility
        Assert.Contains("role=\"dialog\"", home, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", home, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"practice-gate-title\"", home, StringComparison.Ordinal);
        Assert.Contains("role=\"list\"", home, StringComparison.Ordinal);
        Assert.Contains("role=\"listitem\"", home, StringComparison.Ordinal);
        Assert.Contains("role=\"region\"", home, StringComparison.Ordinal);
    }

    [Fact]
    public void P5_Slice2_TeachingInterventionDialog_DefinesThemeAwareFramingAndPreservesMarkup()
    {
        var css = ReadAppCss();
        var teachingRazor = ReadTeachingDialogRazor();

        // 1. Base rules
        Assert.Contains(".teaching-dialog", css, StringComparison.Ordinal);
        Assert.Contains(".teaching-explanation", css, StringComparison.Ordinal);
        Assert.Contains(".teaching-equation", css, StringComparison.Ordinal);
        Assert.Contains(".teaching-continue-button", css, StringComparison.Ordinal);

        // 2. Dark theme framing
        var darkTeachingMatch = Regex.Match(css, @"\.app-theme-root\[data-theme=""dark""\]\s+\.teaching-dialog\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(darkTeachingMatch.Success, "Dark theme rule for .teaching-dialog must exist in app.css");
        var darkTeachingRules = darkTeachingMatch.Groups["rules"].Value;
        Assert.Contains("border-color:", darkTeachingRules, StringComparison.Ordinal);

        // 3. High-contrast equation in dark mode
        var darkEquationMatch = Regex.Match(css, @"\.app-theme-root\[data-theme=""dark""\]\s+\.teaching-dialog\s+\.teaching-equation\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(darkEquationMatch.Success, "Dark theme rule for .teaching-dialog .teaching-equation must exist in app.css");

        // 4. Preserved Razor semantics and countdown contract
        Assert.Contains("role=\"dialog\"", teachingRazor, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", teachingRazor, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"teaching-dialog-title\"", teachingRazor, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(!_isUnlocked)\"", teachingRazor, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"@(!_isUnlocked)\"", teachingRazor, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"HandleContinueClick\"", teachingRazor, StringComparison.Ordinal);
        Assert.Contains("TeachingLockTracker", teachingRazor, StringComparison.Ordinal);
    }

    [Fact]
    public void P5_Slice2_SessionCheckInDialog_DefinesStructuredHierarchyAndDarkThemeAlignment()
    {
        var css = ReadAppCss();
        var home = ReadHomeRazor();

        // 1. Base rules
        Assert.Contains(".checkin-dialog", css, StringComparison.Ordinal);
        Assert.Contains(".checkin-stats", css, StringComparison.Ordinal);
        Assert.Contains(".checkin-stat", css, StringComparison.Ordinal);
        Assert.Contains(".checkin-progress", css, StringComparison.Ordinal);
        Assert.Contains(".checkin-progress-change", css, StringComparison.Ordinal);
        Assert.Contains(".checkin-actions", css, StringComparison.Ordinal);

        // 2. Dark theme rules
        var darkCheckinMatch = Regex.Match(css, @"\.app-theme-root\[data-theme=""dark""\]\s+\.checkin-dialog\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(darkCheckinMatch.Success, "Dark theme rule for .checkin-dialog must exist in app.css");
        var darkCheckinRules = darkCheckinMatch.Groups["rules"].Value;
        Assert.Contains("border-color:", darkCheckinRules, StringComparison.Ordinal);

        var darkProgressMatch = Regex.Match(css, @"\.app-theme-root\[data-theme=""dark""\]\s+\.checkin-progress\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(darkProgressMatch.Success, "Dark theme rule for .checkin-progress must exist in app.css");

        // 3. Preserved check-in accessibility and actions
        Assert.Contains("role=\"dialog\"", home, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"checkin-dialog-title\"", home, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"KeepGoingAsync\"", home, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"TakeBreakAsync\"", home, StringComparison.Ordinal);
    }

    [Fact]
    public void P5_Slice2_FeedbackDialog_DefinesRestrainedThemeAwareDangerFraming()
    {
        var css = ReadAppCss();
        var home = ReadHomeRazor();

        // 1. Base rules and selectors
        Assert.Contains(".feedback-dialog", css, StringComparison.Ordinal);
        Assert.Contains(".feedback-expression", css, StringComparison.Ordinal);
        Assert.Contains(".feedback-user-answer", css, StringComparison.Ordinal);
        Assert.Contains(".feedback-correct-answer", css, StringComparison.Ordinal);

        // 2. Dark theme restrained danger surface framing
        var darkFeedbackMatch = Regex.Match(css, @"\.app-theme-root\[data-theme=""dark""\]\s+\.feedback-dialog\s*\{(?<rules>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(darkFeedbackMatch.Success, "Dark theme rule for .feedback-dialog must exist in app.css");
        var darkFeedbackRules = darkFeedbackMatch.Groups["rules"].Value;
        Assert.Contains("border-color:", darkFeedbackRules, StringComparison.Ordinal);

        // 3. Preserved Home.razor feedback structure
        Assert.Contains("role=\"dialog\"", home, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"feedback-dialog-title\"", home, StringComparison.Ordinal);
        Assert.Contains("feedback-dialog", home, StringComparison.Ordinal);
        Assert.Contains("feedback-incorrect", home, StringComparison.Ordinal);
        Assert.Contains("feedback-timeout", home, StringComparison.Ordinal);
        Assert.Contains("SubmittedAnswerDisplay", home, StringComparison.Ordinal);
    }

    [Fact]
    public void P5_Slice2_StartupAndPersistenceErrors_DefineTechnicalErrorFraming()
    {
        var css = ReadAppCss();
        var home = ReadHomeRazor();

        // 1. Error dialog base and dark theme rules
        Assert.Contains(".startup-error-dialog", css, StringComparison.Ordinal);
        Assert.Contains(".persistence-error-dialog", css, StringComparison.Ordinal);

        var darkErrorMatch = Regex.Match(css, @"\.app-theme-root\[data-theme=""dark""\]\s+\.(startup-error-dialog|persistence-error-dialog)", RegexOptions.Singleline);
        Assert.True(darkErrorMatch.Success, "Dark theme rules for startup/persistence errors must exist in app.css");

        // 2. Preserved Home.razor error handling and retry actions
        Assert.Contains("aria-labelledby=\"startup-error-title\"", home, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"persistence-error-title\"", home, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"RetryInitializationAsync\"", home, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"RecoverFromPersistenceFailureAsync\"", home, StringComparison.Ordinal);
    }

    [Fact]
    public void P5_Slice2_NotFoundPage_IntegratesWithSecondarySurfaceCardFraming()
    {
        var notFound = ReadNotFoundRazor();

        // 1. Preserved routing and layout directives
        Assert.Contains("@page \"/not-found\"", notFound, StringComparison.Ordinal);
        Assert.Contains("@layout MainLayout", notFound, StringComparison.Ordinal);
        Assert.Contains("@implements IDisposable", notFound, StringComparison.Ordinal);
        Assert.Contains("IAppBackNavigationCoordinator", notFound, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(\"/\")", notFound, StringComparison.Ordinal);

        // 2. Preserved localized copy keys
        Assert.Contains("@Localizer[\"NotFound_Title\"]", notFound, StringComparison.Ordinal);
        Assert.Contains("@Localizer[\"NotFound_Description\"]", notFound, StringComparison.Ordinal);

        // 3. Secondary surface styling integration
        Assert.Contains("settings-page", notFound, StringComparison.Ordinal);
        Assert.Contains("settings-card", notFound, StringComparison.Ordinal);
    }
}
