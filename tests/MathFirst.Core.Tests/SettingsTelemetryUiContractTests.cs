namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MathFirst.Application;
using Xunit;

public sealed class SettingsTelemetryUiContractTests
{
    [Fact]
    public void Localization_AllSupportedCultures_ContainTelemetryKeys()
    {
        var requiredKeys = new[]
        {
            "Settings_ExportTelemetry",
            "Settings_ExportTelemetry_InProgress",
            "Settings_ExportTelemetry_Success",
            "Settings_ExportTelemetry_Failure"
        };

        var cultures = new[] { "en", "de", "ru" };
        var localizer = new LocalizationService();

        foreach (var culture in cultures)
        {
            localizer.ApplyLanguagePreference(culture);
            Assert.Equal(culture, localizer.CurrentUiLanguage);

            foreach (var key in requiredKeys)
            {
                var text = localizer[key];
                Assert.False(string.IsNullOrWhiteSpace(text), $"Key '{key}' must not be null or whitespace for culture '{culture}'.");
                Assert.NotEqual(key, text);
            }
        }
    }

    [Fact]
    public void Localization_ExportSuccessCopy_DoesNotClaimTransmission()
    {
        var localizer = new LocalizationService();
        var cultures = new[] { "en", "de", "ru" };

        var forbiddenWords = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = ["sent", "uploaded", "upload", "transmitted", "transmit", "delivered", "deliver", "received", "server"],
            ["de"] = ["gesendet", "hochgeladen", "übertragen", "übermittelt", "zugestellt", "empfangen", "server"],
            ["ru"] = ["отправлен", "загружен", "передан", "доставлен", "получен", "сервер"]
        };

        var positiveIndicators = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = ["share", "dialog", "system", "prepared", "opened"],
            ["de"] = ["freigabe", "system", "vorbereitet", "geöffnet", "dialog"],
            ["ru"] = ["систем", "диалог", "экспорт", "подготов", "открыт"]
        };

        foreach (var culture in cultures)
        {
            localizer.ApplyLanguagePreference(culture);
            var successText = localizer["Settings_ExportTelemetry_Success"];
            Assert.False(string.IsNullOrWhiteSpace(successText));

            var lower = successText.ToLowerInvariant();
            foreach (var forbidden in forbiddenWords[culture])
            {
                Assert.DoesNotContain(forbidden, lower);
            }

            var hasPositive = positiveIndicators[culture].Any(indicator => lower.Contains(indicator));
            Assert.True(hasPositive, $"Culture '{culture}' success copy '{successText}' should indicate system share dialog handoff.");
        }
    }

    [Fact]
    public void Localization_ExportFailureCopy_DoesNotExposeRawException()
    {
        var localizer = new LocalizationService();
        var cultures = new[] { "en", "de", "ru" };

        foreach (var culture in cultures)
        {
            localizer.ApplyLanguagePreference(culture);
            var failureText = localizer["Settings_ExportTelemetry_Failure"];
            Assert.False(string.IsNullOrWhiteSpace(failureText));
            Assert.DoesNotContain("{0}", failureText);
            Assert.DoesNotContain("{1}", failureText);
            Assert.DoesNotContain("exception", failureText, StringComparison.OrdinalIgnoreCase);
        }

        var settingsSource = ReadSourceWithoutComments("src", "MathFirst.App", "Components", "Pages", "Settings.razor");
        Assert.DoesNotContain("ErrorMessage", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.Message", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.ToString()", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.ToString()", settingsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_ExportAction_DisablesButtonDuringExport()
    {
        var settingsSource = ReadSourceWithoutComments("src", "MathFirst.App", "Components", "Pages", "Settings.razor");

        // 1. Injects TelemetryExportCoordinator
        Assert.True(
            Regex.IsMatch(settingsSource, @"@inject\s+TelemetryExportCoordinator\b"),
            "Settings.razor must inject TelemetryExportCoordinator.");

        // 2. Invokes ExportAndShareAsync
        Assert.Contains("ExportAndShareAsync", settingsSource, StringComparison.Ordinal);

        // 3. Button disabled attribute bound to busy flag
        Assert.True(
            Regex.IsMatch(settingsSource, @"disabled\s*=\s*""@_isExporting""|disabled\s*=\s*""@\(_isExporting\)"""),
            "Export button disabled attribute must be bound to _isExporting flag.");

        // 4. Sets _isExporting before await and resets in finally
        Assert.True(
            Regex.IsMatch(settingsSource, @"_isExporting\s*=\s*true;[\s\S]*?await\s+[\w\.]*ExportAndShareAsync"),
            "_isExporting must be set to true before awaiting ExportAndShareAsync.");

        Assert.True(
            Regex.IsMatch(settingsSource, @"finally\s*\{[\s\S]*?_isExporting\s*=\s*false;"),
            "_isExporting must be reset to false in a finally block.");

        // 5. Guard check at start of export action
        Assert.True(
            Regex.IsMatch(settingsSource, @"if\s*\(\s*_isExporting\s*\)\s*\{\s*return;?\s*\}|if\s*\(\s*_isExporting\s*\)\s*return;"),
            "Export action must guard against concurrent invocation when _isExporting is true.");
    }

    [Fact]
    public void Settings_FullReset_InvokesAppResetCoordinator()
    {
        var settingsSource = ReadSourceWithoutComments("src", "MathFirst.App", "Components", "Pages", "Settings.razor");

        // 1. Injects IAppResetCoordinator
        Assert.True(
            Regex.IsMatch(settingsSource, @"@inject\s+IAppResetCoordinator\b"),
            "Settings.razor must inject IAppResetCoordinator.");

        // 2. ExecuteFullLocalReset invokes ExecuteFullResetAsync
        Assert.True(
            Regex.IsMatch(settingsSource, @"ExecuteFullLocalReset[\s\S]*?await\s+[\w\.]*ExecuteFullResetAsync"),
            "ExecuteFullLocalReset must await ExecuteFullResetAsync on the coordinator.");

        // 3. Does not duplicate coordinator steps in ExecuteFullLocalReset
        var match = Regex.Match(settingsSource, @"ExecuteFullLocalReset\s*\([^\)]*\)\s*\{(?<body>[\s\S]*?)\}");
        Assert.True(match.Success, "ExecuteFullLocalReset method must be found.");
        var body = match.Groups["body"].Value;

        Assert.DoesNotContain("ResetLearningProgressAsync", body);
        Assert.DoesNotContain("ResetAllPreferences", body);
        Assert.DoesNotContain("ClearInstallationId", body);
        Assert.DoesNotContain("PurgeShareCache", body);
    }

    [Fact]
    public void Settings_FullReset_AfterSuccessfulCoordinator_ResetsThemeLanguageAndNavigatesHome()
    {
        var settingsSource = ReadSourceWithoutComments("src", "MathFirst.App", "Components", "Pages", "Settings.razor");

        var match = Regex.Match(settingsSource, @"ExecuteFullLocalReset\s*\([^\)]*\)\s*\{(?<body>[\s\S]*?)\}");
        Assert.True(match.Success, "ExecuteFullLocalReset method must be found.");
        var body = match.Groups["body"].Value;

        var resetIndex = body.IndexOf("ExecuteFullResetAsync", StringComparison.Ordinal);
        var themeIndex = body.IndexOf("ThemeService.ResetPreference()", StringComparison.Ordinal);
        var langIndex = body.IndexOf("ApplyLanguagePreference(\"system\")", StringComparison.Ordinal);
        var navIndex = body.IndexOf("NavigateTo(\"/\", replace: true)", StringComparison.Ordinal);

        Assert.True(resetIndex >= 0, "ExecuteFullResetAsync must be called.");
        Assert.True(themeIndex > resetIndex, "ThemeService.ResetPreference() must be called after ExecuteFullResetAsync.");
        Assert.True(langIndex > themeIndex, "ApplyLanguagePreference(\"system\") must be called after ThemeService.ResetPreference().");
        Assert.True(navIndex > langIndex, "Navigation.NavigateTo(\"/\", replace: true) must be called after language reset.");

        Assert.DoesNotContain("finally", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DependencyInjection_AppBuildInfoInterface_ResolvesSameSingletonInstance()
    {
        var mauiSource = ReadSourceWithoutComments("src", "MathFirst.App", "MauiProgram.cs");

        var concreteMatches = Regex.Matches(mauiSource, @"AddSingleton<AppBuildInfo>\s*\(\s*\)");
        Assert.Single(concreteMatches);

        var interfaceMatches = Regex.Matches(mauiSource, @"AddSingleton<IAppBuildInfo>\s*\(\s*(?<param>\w+)\s*=>\s*\k<param>\.GetRequiredService<AppBuildInfo>\s*\(\s*\)\s*\)");
        Assert.Single(interfaceMatches);

        Assert.False(
            Regex.IsMatch(mauiSource, @"AddSingleton<IAppBuildInfo,\s*AppBuildInfo>"),
            "MauiProgram must not register AddSingleton<IAppBuildInfo, AppBuildInfo>() separately.");

        Assert.False(
            Regex.IsMatch(mauiSource, @"AddSingleton<IAppBuildInfo>[\s\S]*?new\s+AppBuildInfo"),
            "MauiProgram must not instantiate 'new AppBuildInfo()' in IAppBuildInfo factory.");
    }

    [Fact]
    public void DependencyInjection_TimeProvider_ResolvesSystemInstance()
    {
        var mauiSource = ReadSourceWithoutComments("src", "MathFirst.App", "MauiProgram.cs");

        var timeProviderMatches = Regex.Matches(mauiSource, @"AddSingleton<TimeProvider>\s*\(\s*TimeProvider\.System\s*\)");
        Assert.Single(timeProviderMatches);

        Assert.Single(Regex.Matches(mauiSource, @"AddSingleton<IInstallationIdStore,\s*MauiInstallationIdStore>\s*\(\s*\)"));
        Assert.Single(Regex.Matches(mauiSource, @"AddSingleton<IInstallationIdProvider,\s*PreferenceInstallationIdProvider>\s*\(\s*\)"));
        Assert.Single(Regex.Matches(mauiSource, @"AddSingleton<ITelemetryJsonSerializer,\s*TelemetryJsonSerializer>\s*\(\s*\)"));
        Assert.Single(Regex.Matches(mauiSource, @"AddSingleton<ITelemetryShareService,\s*MauiTelemetryShareService>\s*\(\s*\)"));
        Assert.Single(Regex.Matches(mauiSource, @"AddSingleton<TelemetryExportCoordinator>\s*\(\s*\)"));
        Assert.Single(Regex.Matches(mauiSource, @"AddSingleton<IAppResetCoordinator,\s*AppResetCoordinator>\s*\(\s*\)"));

        Assert.DoesNotContain("DateTime.Now", mauiSource);
        Assert.DoesNotContain("DateTime.UtcNow", mauiSource);
        Assert.DoesNotContain("DateTimeOffset.Now", mauiSource);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", mauiSource);
    }

    private static string ReadSourceWithoutComments(params string[] pathSegments)
    {
        var fullPath = GetRepositoryPath(pathSegments);
        Assert.True(File.Exists(fullPath), $"Expected source file at {fullPath}");
        var text = File.ReadAllText(fullPath);
        var noRazorComments = Regex.Replace(text, @"@\*[\s\S]*?\*@", "");
        var noBlockComments = Regex.Replace(noRazorComments, @"/\*[\s\S]*?\*/", "");
        var noLineComments = Regex.Replace(noBlockComments, @"//.*$", "", RegexOptions.Multiline);
        return noLineComments;
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
