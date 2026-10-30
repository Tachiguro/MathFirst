namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using Xunit;

public sealed class UpgradeTerminalComponentContractTests
{
    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", ".."));

    private static string ReadUpgradeTerminalRazor() =>
        File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "UpgradeTerminal.razor"));

    private static string ReadUpgradeTerminalCss() =>
        File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "UpgradeTerminal.razor.css"));

    // =========================================================================
    // 1. Localization Parity and Formatting Tests
    // =========================================================================

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public void UpgradeTerminal_Localization_HasAllRequiredKeysInAllLocales(string languageCode)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(languageCode);

        string[] requiredKeys =
        [
            "UpgradeTerminal_Title",
            "UpgradeTerminal_Subtitle",
            "UpgradeTerminal_LevelLabel",
            "UpgradeTerminal_SkillPointsLabel",
            "UpgradeTerminal_PlayerStatusAriaLabel",
            "UpgradeTerminal_AttackTitle",
            "UpgradeTerminal_AttackDescription",
            "UpgradeTerminal_CurrentRank",
            "UpgradeTerminal_CurrentDamage",
            "UpgradeTerminal_NextRankDamage",
            "UpgradeTerminal_Cost",
            "UpgradeTerminal_MaxRank",
            "UpgradeTerminal_UpgradeAction",
            "UpgradeTerminal_Purchasing",
            "UpgradeTerminal_CloseAction",
            "UpgradeTerminal_Feedback_Success",
            "UpgradeTerminal_Feedback_InsufficientSkillPoints",
            "UpgradeTerminal_Feedback_StaleRank",
            "UpgradeTerminal_Feedback_StaleResetEpoch",
            "UpgradeTerminal_Feedback_MaxRankReached",
            "UpgradeTerminal_Feedback_Busy",
            "UpgradeTerminal_Feedback_Disposed",
            "UpgradeTerminal_Feedback_Cancelled",
            "UpgradeTerminal_Feedback_Error",
            "UpgradeTerminal_FirewallTitle",
            "UpgradeTerminal_FirewallDescription",
            "UpgradeTerminal_PermanentRank",
            "UpgradeTerminal_CurrentCapacity",
            "UpgradeTerminal_NextCapacity",
            "UpgradeTerminal_RequiredLevel",
            "UpgradeTerminal_LevelLocked",
            "UpgradeTerminal_UpgradeFirewallAction",
            "UpgradeTerminal_Feedback_FirewallSuccess",
            "UpgradeTerminal_FirewallActivationNotice",
            "UpgradeTerminal_Feedback_FirewallMaxRank",
            "UpgradeTerminal_Feedback_LockedLevel",
            "UpgradeTerminal_Feedback_FirewallStaleRank",
            "UpgradeTerminal_FirewallPurchaseAriaLabel",
            "UpgradeTerminal_NextRank",
            "UpgradeTerminal_CriticalStrikeTitle",
            "UpgradeTerminal_CriticalStrikeDescription",
            "UpgradeTerminal_CriticalStrikeEffect_Rank0",
            "UpgradeTerminal_CriticalStrikeEffect_Rank1",
            "UpgradeTerminal_CriticalStrikeEffect_Rank2",
            "UpgradeTerminal_UpgradeCriticalStrikeAction",
            "UpgradeTerminal_CriticalStrikePurchaseAriaLabel",
            "UpgradeTerminal_Feedback_CriticalStrikeSuccess",
            "UpgradeTerminal_Feedback_CriticalStrikeMaxRank",
            "UpgradeTerminal_Feedback_CriticalStrikeLockedLevel",
            "UpgradeTerminal_Feedback_CriticalStrikeStaleRank",
            "UpgradeTerminal_OverdriveTitle",
            "UpgradeTerminal_OverdriveDescription",
            "UpgradeTerminal_OverdriveNotice",
            "UpgradeTerminal_OverdriveEffect_Rank0",
            "UpgradeTerminal_OverdriveEffect_Rank1",
            "UpgradeTerminal_OverdriveEffect_Rank2",
            "UpgradeTerminal_UpgradeOverdriveAction",
            "UpgradeTerminal_OverdrivePurchaseAriaLabel",
            "UpgradeTerminal_Feedback_OverdriveSuccess",
            "UpgradeTerminal_Feedback_OverdriveMaxRank",
            "UpgradeTerminal_Feedback_OverdriveLockedLevel",
            "UpgradeTerminal_Feedback_OverdriveStaleRank"
        ];

        foreach (var key in requiredKeys)
        {
            var value = localizer[key];
            Assert.False(string.IsNullOrWhiteSpace(value), $"Localized key '{key}' must not be null or empty for language '{languageCode}'.");
            Assert.NotEqual(key, value); // Must not return fallback key name
        }
    }

    [Fact]
    public void UpgradeTerminal_Markup_ContainsDedicatedFirewallCardAndPurchaseControl()
    {
        var content = ReadUpgradeTerminalRazor();

        Assert.Contains("firewall-upgrade-card", content, StringComparison.Ordinal);
        Assert.Contains("firewall-purchase-btn", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_FirewallTitle", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Upgrade Terminal", "Attack Power", "Close")]
    [InlineData("de", "Upgrade-Terminal", "Angriffskraft", "Schließen")]
    [InlineData("ru", "Терминал улучшений", "Сила атаки", "Закрыть")]
    public void UpgradeTerminal_Localization_MatchesExpectedLanguageSpecificText(
        string languageCode,
        string expectedTitle,
        string expectedAttackTitle,
        string expectedClose)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(languageCode);

        Assert.Equal(expectedTitle, localizer["UpgradeTerminal_Title"]);
        Assert.Equal(expectedAttackTitle, localizer["UpgradeTerminal_AttackTitle"]);
        Assert.Equal(expectedClose, localizer["UpgradeTerminal_CloseAction"]);
    }

    [Theory]
    [InlineData("en", 3, "Rank 3", "Damage: 3", "Next: 4 damage", "Cost: 2 SP")]
    [InlineData("de", 3, "Rang 3", "Schaden: 3", "Nächste Stufe: 4 Schaden", "Kosten: 2 FP")]
    [InlineData("ru", 3, "Ранг 3", "Урон: 3", "Далее: 4 урона", "Стоимость: 2 ОН")]
    public void UpgradeTerminal_Localization_FormatsNumericalArgumentsCorrectly(
        string languageCode,
        int rank,
        string expectedRank,
        string expectedDamage,
        string expectedNextDamage,
        string expectedCost)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(languageCode);

        Assert.Equal(expectedRank, localizer["UpgradeTerminal_CurrentRank", rank]);
        Assert.Equal(expectedDamage, localizer["UpgradeTerminal_CurrentDamage", rank]);
        Assert.Equal(expectedNextDamage, localizer["UpgradeTerminal_NextRankDamage", rank + 1]);
        Assert.Equal(expectedCost, localizer["UpgradeTerminal_Cost", 2]);
    }

    [Fact]
    public void UpgradeTerminal_Localization_AllDictionariesHavePlaceholderParity()
    {
        var enLocalizer = new LocalizationService();
        enLocalizer.ApplyLanguagePreference("en");

        var deLocalizer = new LocalizationService();
        deLocalizer.ApplyLanguagePreference("de");

        var ruLocalizer = new LocalizationService();
        ruLocalizer.ApplyLanguagePreference("ru");

        var placeholderRegex = new Regex(@"\{(\d+)\}");

        string[] templateKeys =
        [
            "UpgradeTerminal_CurrentRank",
            "UpgradeTerminal_CurrentDamage",
            "UpgradeTerminal_NextRankDamage",
            "UpgradeTerminal_Cost",
            "UpgradeTerminal_PermanentRank",
            "UpgradeTerminal_CurrentCapacity",
            "UpgradeTerminal_NextCapacity",
            "UpgradeTerminal_RequiredLevel",
            "UpgradeTerminal_LevelLocked",
            "UpgradeTerminal_FirewallPurchaseAriaLabel",
            "UpgradeTerminal_NextRank",
            "UpgradeTerminal_CriticalStrikeTitle",
            "UpgradeTerminal_CriticalStrikeDescription",
            "UpgradeTerminal_CriticalStrikeEffect_Rank0",
            "UpgradeTerminal_CriticalStrikeEffect_Rank1",
            "UpgradeTerminal_CriticalStrikeEffect_Rank2",
            "UpgradeTerminal_UpgradeCriticalStrikeAction",
            "UpgradeTerminal_CriticalStrikePurchaseAriaLabel",
            "UpgradeTerminal_Feedback_CriticalStrikeSuccess",
            "UpgradeTerminal_Feedback_CriticalStrikeMaxRank",
            "UpgradeTerminal_Feedback_CriticalStrikeLockedLevel",
            "UpgradeTerminal_Feedback_CriticalStrikeStaleRank",
            "UpgradeTerminal_OverdriveTitle",
            "UpgradeTerminal_OverdriveDescription",
            "UpgradeTerminal_OverdriveNotice",
            "UpgradeTerminal_OverdriveEffect_Rank0",
            "UpgradeTerminal_OverdriveEffect_Rank1",
            "UpgradeTerminal_OverdriveEffect_Rank2",
            "UpgradeTerminal_UpgradeOverdriveAction",
            "UpgradeTerminal_OverdrivePurchaseAriaLabel",
            "UpgradeTerminal_Feedback_OverdriveSuccess",
            "UpgradeTerminal_Feedback_OverdriveMaxRank",
            "UpgradeTerminal_Feedback_OverdriveLockedLevel",
            "UpgradeTerminal_Feedback_OverdriveStaleRank"
        ];

        foreach (var key in templateKeys)
        {
            var enMatches = placeholderRegex.Matches(enLocalizer[key]);
            var deMatches = placeholderRegex.Matches(deLocalizer[key]);
            var ruMatches = placeholderRegex.Matches(ruLocalizer[key]);

            Assert.Equal(enMatches.Count, deMatches.Count);
            Assert.Equal(enMatches.Count, ruMatches.Count);

            for (int i = 0; i < enMatches.Count; i++)
            {
                Assert.Equal(enMatches[i].Value, deMatches[i].Value);
                Assert.Equal(enMatches[i].Value, ruMatches[i].Value);
            }
        }
    }

    // =========================================================================
    // 2. Component Structure and Accessibility Contracts
    // =========================================================================

    [Fact]
    public void UpgradeTerminal_ComponentFilesExistInTrainingStructure()
    {
        var razorPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "UpgradeTerminal.razor");
        Assert.True(File.Exists(razorPath), $"UpgradeTerminal.razor must exist at {razorPath}");

        var cssPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "UpgradeTerminal.razor.css");
        Assert.True(File.Exists(cssPath), $"UpgradeTerminal.razor.css must exist at {cssPath}");
    }

    [Fact]
    public void UpgradeTerminal_ImplementsAccessibleDialogMarkupAndAriaAttributes()
    {
        var content = ReadUpgradeTerminalRazor();

        // Accessible dialog root attributes
        Assert.Contains("role=\"dialog\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"upgrade-terminal-title\"", content, StringComparison.Ordinal);
        Assert.Contains("id=\"upgrade-terminal-title\"", content, StringComparison.Ordinal);

        // Card and list semantics
        Assert.Contains("role=\"list\"", content, StringComparison.Ordinal);
        Assert.Contains("role=\"listitem\"", content, StringComparison.Ordinal);
        Assert.Contains("id=\"attack-upgrade-title\"", content, StringComparison.Ordinal);
        Assert.Contains("id=\"firewall-upgrade-title\"", content, StringComparison.Ordinal);
        Assert.Contains("firewall-upgrade-card", content, StringComparison.Ordinal);

        // Accessible live region for purchase announcements
        Assert.Contains("role=\"status\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", content, StringComparison.Ordinal);

        // Buttons
        Assert.Contains("@onclick=\"HandlePurchaseClick\"", content, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"HandleFirewallPurchaseClick\"", content, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"HandleCloseClick\"", content, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@IsPurchaseDisabled\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"@IsPurchaseDisabled\"", content, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@IsFirewallPurchaseDisabled\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"@IsFirewallPurchaseDisabled\"", content, StringComparison.Ordinal);
        Assert.Contains("attack-purchase-btn", content, StringComparison.Ordinal);
        Assert.Contains("firewall-purchase-btn", content, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeTerminal_DeclaresExpectedParametersAndLifecycleContracts()
    {
        var content = ReadUpgradeTerminalRazor();

        // Parameter acceptance
        Assert.Matches(@"\[Parameter[^\]]*\]\s*public\s+UpgradeTerminalViewModel\??\s+ViewModel\s*\{\s*get;\s*set;\s*\}", content);
        Assert.Matches(@"\[Parameter[^\]]*\]\s*public\s+ILocalizationService\??\s+Localizer\s*\{\s*get;\s*set;\s*\}", content);
        Assert.Matches(@"\[Parameter[^\]]*\]\s*public\s+EventCallback\s+OnClose\s*\{\s*get;\s*set;\s*\}", content);

        // Implements IDisposable
        Assert.Contains("@implements IDisposable", content, StringComparison.Ordinal);

        // Unsubscribes on disposal and handles UI synchronization context safely
        Assert.Contains("StateChanged", content, StringComparison.Ordinal);
        Assert.Contains("InvokeAsync", content, StringComparison.Ordinal);

        // Focus handling
        Assert.Contains("FocusAsync", content, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeTerminal_PreservesLearningIsolation_AndContainsNoProhibitedTokens()
    {
        var content = ReadUpgradeTerminalRazor();

        // Strictly forbidden inside Upgrade Terminal component
        string[] prohibitedTokens =
        [
            "FSRS",
            "Curriculum",
            "ILearnerStore",
            "SubmissionChangeSet",
            "DailyStreak",
            "TrainingSession",
            "Home.razor",
            "Shop",
            "Microtransaction",
            "GooglePlay"
        ];

        foreach (var token in prohibitedTokens)
        {
            Assert.DoesNotContain(token, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void UpgradeTerminal_CssUsesDesignTokens_AndDoesNotHardcodeDarkOnlyPalette()
    {
        var css = ReadUpgradeTerminalCss();

        Assert.Contains("var(--color-", css, StringComparison.Ordinal);
        Assert.DoesNotContain("color-scheme: dark", css, StringComparison.Ordinal);

        // Ensure key structural selectors exist
        Assert.Contains(".upgrade-terminal-dialog", css, StringComparison.Ordinal);
        Assert.Contains(".upgrade-card", css, StringComparison.Ordinal);
        Assert.Contains(".upgrade-terminal-feedback", css, StringComparison.Ordinal);
        Assert.Contains(".upgrade-terminal-actions", css, StringComparison.Ordinal);
        Assert.Contains(".firewall-icon-slot", css, StringComparison.Ordinal);
        Assert.Contains(".cost-locked", css, StringComparison.Ordinal);
        Assert.Contains(".upgrade-card-notice", css, StringComparison.Ordinal);

        // Tabular figures for alignment
        Assert.Contains("font-variant-numeric: tabular-nums", css, StringComparison.Ordinal);
    }

    // =========================================================================
    // 3. Functional and Behavioral Contract Assertions
    // =========================================================================

    [Fact]
    public void UpgradeTerminal_BindsAuthoritativeProgressionAndAttackProperties()
    {
        var content = ReadUpgradeTerminalRazor();

        // Authoritative level and SP projection
        Assert.Contains("EffectivePlayerLevel", content, StringComparison.Ordinal);
        Assert.Contains("EffectiveAvailableSp", content, StringComparison.Ordinal);

        // Authoritative rank, damage, next damage, and cost
        Assert.Contains("EffectiveCurrentRank", content, StringComparison.Ordinal);
        Assert.Contains("EffectiveCurrentDamage", content, StringComparison.Ordinal);
        Assert.Contains("EffectiveNextDamage", content, StringComparison.Ordinal);
        Assert.Contains("EffectiveNextCost", content, StringComparison.Ordinal);
        Assert.Contains("IsMaxRank", content, StringComparison.Ordinal);
        Assert.Contains("CanAfford", content, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeTerminal_PurchaseDisabled_EnforcesAllSafetyGates()
    {
        var content = ReadUpgradeTerminalRazor();

        // Must guard against: disposed, busy, null ViewModel, max rank, insufficient SP, canPurchase
        Assert.Contains("_isDisposed", content, StringComparison.Ordinal);
        Assert.Contains("_isProcessingPurchase", content, StringComparison.Ordinal);
        Assert.Contains("ViewModel == null", content, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsDisposed", content, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsBusy", content, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsMaxRank", content, StringComparison.Ordinal);
        Assert.Contains("!ViewModel.HasSufficientSkillPoints", content, StringComparison.Ordinal);
        Assert.Contains("!ViewModel.CanPurchase", content, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeTerminal_PurchaseResultMapping_CoversAllPurchaseStatuses()
    {
        var content = ReadUpgradeTerminalRazor();

        // Every UpgradePurchaseStatus enum value must be handled in ApplyPurchaseResult
        var requiredStatuses = Enum.GetNames<UpgradePurchaseStatus>();
        foreach (var status in requiredStatuses)
        {
            Assert.Contains($"UpgradePurchaseStatus.{status}", content, StringComparison.Ordinal);
        }

        // All localized feedback keys must be referenced
        Assert.Contains("UpgradeTerminal_Feedback_Success", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_InsufficientSkillPoints", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_StaleRank", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_StaleResetEpoch", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_MaxRankReached", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_Busy", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_Disposed", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_Cancelled", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_Error", content, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeTerminal_PreservesViewModelOwnership_AndDoesNotDisposeViewModel()
    {
        var content = ReadUpgradeTerminalRazor();

        // Dispose method of UpgradeTerminal must NOT call ViewModel.Dispose()
        var disposeMatch = Regex.Match(content, @"public\s+void\s+Dispose\s*\(\s*\)\s*\{(?<body>[^}]+)\}", RegexOptions.Singleline);
        Assert.True(disposeMatch.Success, "Dispose method must be implemented.");

        var disposeBody = disposeMatch.Groups["body"].Value;
        Assert.DoesNotContain("ViewModel.Dispose()", disposeBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel?.Dispose()", disposeBody, StringComparison.Ordinal);

        // Component must unregister its event handlers
        Assert.Contains("UnsubscribeFromViewModel()", disposeBody, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeTerminal_InitialFocus_HandlesAffordabilitySensibly()
    {
        var content = ReadUpgradeTerminalRazor();

        // Initial focus targets purchase button when affordable, close button otherwise,
        // without scrolling the terminal (Issue #117; rendered proof in Issue117UpgradeTerminalRenderedLayoutTests).
        Assert.Contains("_purchaseButton.FocusAsync(preventScroll: true)", content, StringComparison.Ordinal);
        Assert.Contains("_closeButton.FocusAsync(preventScroll: true)", content, StringComparison.Ordinal);
        Assert.Contains("_firewallPurchaseButton.FocusAsync(preventScroll: true)", content, StringComparison.Ordinal);
        Assert.Contains("_criticalStrikePurchaseButton.FocusAsync(preventScroll: true)", content, StringComparison.Ordinal);
        Assert.Contains("_overdrivePurchaseButton.FocusAsync(preventScroll: true)", content, StringComparison.Ordinal);
        Assert.DoesNotContain(".FocusAsync()", content, StringComparison.Ordinal);
    }

    // =========================================================================
    // 4. Slice 12: Critical Strike and Overdrive Terminal UI Contracts
    // =========================================================================

    [Fact]
    public void UpgradeTerminal_Markup_RendersAllFourSkillCardsAndPurchaseControls()
    {
        var content = ReadUpgradeTerminalRazor();

        // All four skill card identifiers remain present
        Assert.Contains("attack-upgrade-card", content, StringComparison.Ordinal);
        Assert.Contains("firewall-upgrade-card", content, StringComparison.Ordinal);
        Assert.Contains("critical-strike-upgrade-card", content, StringComparison.Ordinal);
        Assert.Contains("overdrive-upgrade-card", content, StringComparison.Ordinal);

        // Exactly four listitem cards
        var listItemMatches = Regex.Matches(content, @"<article\s+class=""upgrade-card\b[^""]*""\s+role=""listitem""");
        Assert.Equal(4, listItemMatches.Count);

        // Accessible headings for all four cards
        Assert.Contains("id=\"attack-upgrade-title\"", content, StringComparison.Ordinal);
        Assert.Contains("id=\"firewall-upgrade-title\"", content, StringComparison.Ordinal);
        Assert.Contains("id=\"critical-strike-upgrade-title\"", content, StringComparison.Ordinal);
        Assert.Contains("id=\"overdrive-upgrade-title\"", content, StringComparison.Ordinal);

        // Dedicated purchase buttons for all four skills + Close button
        Assert.Contains("attack-purchase-btn", content, StringComparison.Ordinal);
        Assert.Contains("firewall-purchase-btn", content, StringComparison.Ordinal);
        Assert.Contains("critical-strike-purchase-btn", content, StringComparison.Ordinal);
        Assert.Contains("overdrive-purchase-btn", content, StringComparison.Ordinal);
        Assert.Contains("upgrade-close-btn", content, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeTerminal_BindsAuthoritativeCriticalStrikeAndOverdrivePropertiesAndActions()
    {
        var content = ReadUpgradeTerminalRazor();

        // Critical Strike bindings
        Assert.Contains("EffectiveCurrentCriticalStrikeRank", content, StringComparison.Ordinal);
        Assert.Contains("EffectiveNextCriticalStrikeRank", content, StringComparison.Ordinal);
        Assert.Contains("EffectiveNextCriticalStrikeRequiredLevel", content, StringComparison.Ordinal);
        Assert.Contains("EffectiveNextCriticalStrikeCost", content, StringComparison.Ordinal);
        Assert.Contains("IsCriticalStrikeMaxRank", content, StringComparison.Ordinal);
        Assert.Contains("IsCriticalStrikeLevelUnlocked", content, StringComparison.Ordinal);
        Assert.Contains("HasSufficientCriticalStrikeSp", content, StringComparison.Ordinal);
        Assert.Contains("ViewModel.PurchaseCriticalStrikeUpgradeAsync()", content, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"HandleCriticalStrikePurchaseClick\"", content, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@IsCriticalStrikePurchaseDisabled\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"@IsCriticalStrikePurchaseDisabled\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@CriticalStrikePurchaseButtonAriaLabel\"", content, StringComparison.Ordinal);

        // Overdrive bindings
        Assert.Contains("EffectiveCurrentOverdriveRank", content, StringComparison.Ordinal);
        Assert.Contains("EffectiveNextOverdriveRank", content, StringComparison.Ordinal);
        Assert.Contains("EffectiveNextOverdriveRequiredLevel", content, StringComparison.Ordinal);
        Assert.Contains("EffectiveNextOverdriveCost", content, StringComparison.Ordinal);
        Assert.Contains("IsOverdriveMaxRank", content, StringComparison.Ordinal);
        Assert.Contains("IsOverdriveLevelUnlocked", content, StringComparison.Ordinal);
        Assert.Contains("HasSufficientOverdriveSp", content, StringComparison.Ordinal);
        Assert.Contains("ViewModel.PurchaseOverdriveUpgradeAsync()", content, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"HandleOverdrivePurchaseClick\"", content, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@IsOverdrivePurchaseDisabled\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"@IsOverdrivePurchaseDisabled\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@OverdrivePurchaseButtonAriaLabel\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeTerminal_SharedBusyGate_DisablesAllFourPurchaseButtonsWhenAnyPurchaseInFlight()
    {
        var content = ReadUpgradeTerminalRazor();

        Assert.Contains("_isProcessingCriticalStrikePurchase", content, StringComparison.Ordinal);
        Assert.Contains("_isProcessingOverdrivePurchase", content, StringComparison.Ordinal);

        Assert.Contains("!ViewModel.IsCriticalStrikeLevelUnlocked", content, StringComparison.Ordinal);
        Assert.Contains("!ViewModel.HasSufficientCriticalStrikeSkillPoints", content, StringComparison.Ordinal);
        Assert.Contains("!ViewModel.CanPurchaseCriticalStrike", content, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsCriticalStrikeMaxRank", content, StringComparison.Ordinal);

        Assert.Contains("!ViewModel.IsOverdriveLevelUnlocked", content, StringComparison.Ordinal);
        Assert.Contains("!ViewModel.HasSufficientOverdriveSkillPoints", content, StringComparison.Ordinal);
        Assert.Contains("!ViewModel.CanPurchaseOverdrive", content, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsOverdriveMaxRank", content, StringComparison.Ordinal);

        // Localized feedback keys for Critical Strike and Overdrive must be mapped in ApplyPurchaseResult
        Assert.Contains("UpgradeTerminal_Feedback_CriticalStrikeSuccess", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_CriticalStrikeMaxRank", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_CriticalStrikeLockedLevel", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_CriticalStrikeStaleRank", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_OverdriveSuccess", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_OverdriveMaxRank", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_OverdriveLockedLevel", content, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_Feedback_OverdriveStaleRank", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Critical Strike", "Upgrade Critical Strike", "Overdrive", "Upgrade Overdrive", "Next: Rank 2")]
    [InlineData("de", "Kritischer Treffer", "Kritischen Treffer verbessern", "Overdrive", "Overdrive verbessern", "Nächste Stufe: Rang 2")]
    [InlineData("ru", "Критический удар", "Улучшить критический удар", "Овердрайв", "Улучшить овердрайв", "Далее: ранг 2")]
    public void UpgradeTerminal_Localization_CriticalStrikeAndOverdriveTitlesActionsAndNextRank_MatchAllLocales(
        string languageCode,
        string expectedCritTitle,
        string expectedCritAction,
        string expectedOverdriveTitle,
        string expectedOverdriveAction,
        string expectedNextRank)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(languageCode);

        Assert.Equal(expectedCritTitle, localizer["UpgradeTerminal_CriticalStrikeTitle"]);
        Assert.Equal(expectedCritAction, localizer["UpgradeTerminal_UpgradeCriticalStrikeAction"]);
        Assert.Equal(expectedOverdriveTitle, localizer["UpgradeTerminal_OverdriveTitle"]);
        Assert.Equal(expectedOverdriveAction, localizer["UpgradeTerminal_UpgradeOverdriveAction"]);
        Assert.Equal(expectedNextRank, localizer["UpgradeTerminal_NextRank", 2]);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public void UpgradeTerminal_CriticalStrikeAndOverdriveCopy_ExplainsApprovedBalanceAndNeverMentionsReactionSpeedOrCriticalWindow(
        string languageCode)
    {
        var razor = ReadUpgradeTerminalRazor();
        Assert.DoesNotContain("CriticalWindow", razor, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Critical Window", razor, StringComparison.OrdinalIgnoreCase);

        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(languageCode);

        var critDesc = localizer["UpgradeTerminal_CriticalStrikeDescription"];
        var critR1 = localizer["UpgradeTerminal_CriticalStrikeEffect_Rank1"];
        var critR2 = localizer["UpgradeTerminal_CriticalStrikeEffect_Rank2"];
        var overdriveDesc = localizer["UpgradeTerminal_OverdriveDescription"];
        var overdriveNotice = localizer["UpgradeTerminal_OverdriveNotice"];
        var overdriveR1 = localizer["UpgradeTerminal_OverdriveEffect_Rank1"];
        var overdriveR2 = localizer["UpgradeTerminal_OverdriveEffect_Rank2"];

        // Approved Critical Strike numbers: Level 12, 6 SP, 5th, +50%; Level 50, 12 SP, 4th, +100%
        Assert.Contains("12", critDesc, StringComparison.Ordinal);
        Assert.Contains("6", critDesc, StringComparison.Ordinal);
        Assert.Contains("5", critDesc, StringComparison.Ordinal);
        Assert.Contains("50", critDesc, StringComparison.Ordinal);
        Assert.Contains("50", critR1, StringComparison.Ordinal);
        Assert.Contains("5", critR1, StringComparison.Ordinal);
        Assert.Contains("100", critDesc, StringComparison.Ordinal);
        Assert.Contains("4", critR2, StringComparison.Ordinal);
        Assert.Contains("100", critR2, StringComparison.Ordinal);

        // Approved Overdrive numbers: Level 20, 5 SP, 5 consecutive, +50%, 2 attacks; Level 80, 10 SP, 4 consecutive
        Assert.Contains("20", overdriveDesc, StringComparison.Ordinal);
        Assert.Contains("5", overdriveDesc, StringComparison.Ordinal);
        Assert.Contains("80", overdriveDesc, StringComparison.Ordinal);
        Assert.Contains("10", overdriveDesc, StringComparison.Ordinal);
        Assert.Contains("50", overdriveDesc, StringComparison.Ordinal);
        Assert.Contains("2", overdriveDesc, StringComparison.Ordinal);
        Assert.Contains("5", overdriveR1, StringComparison.Ordinal);
        Assert.Contains("50", overdriveR1, StringComparison.Ordinal);
        Assert.Contains("2", overdriveR1, StringComparison.Ordinal);
        Assert.Contains("4", overdriveR2, StringComparison.Ordinal);
        Assert.Contains("50", overdriveR2, StringComparison.Ordinal);
        Assert.Contains("2", overdriveR2, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(overdriveNotice));
        Assert.NotEqual("UpgradeTerminal_OverdriveNotice", overdriveNotice);

        // Never describe Critical Strike as reaction-speed-based
        string[] forbiddenSpeedTerms =
        [
            "reaction",
            "reaktion",
            "реакц",
            "speed",
            "schnell",
            "скорост",
            "fast",
            "timer",
            "таймер",
            "Critical Window",
            "Kritisches Fenster",
            "Критическое окно"
        ];

        foreach (var term in forbiddenSpeedTerms)
        {
            Assert.DoesNotContain(term, critDesc, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(term, critR1, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(term, critR2, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void UpgradeTerminal_Css_SupportsFourCardScrollLayoutWithoutHidingCloseAction()
    {
        var css = ReadUpgradeTerminalCss();

        Assert.Contains(".critical-strike-icon-slot", css, StringComparison.Ordinal);
        Assert.Contains(".overdrive-icon-slot", css, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto", css, StringComparison.Ordinal);
        Assert.Contains(".upgrade-close-btn", css, StringComparison.Ordinal);
    }
}
