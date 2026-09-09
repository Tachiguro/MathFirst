namespace MathFirst.Core.Tests;

using System.Text.RegularExpressions;
using MathFirst.Application.Copy;
using Xunit;

/// <summary>
/// TDD tests for MF-UX-002: PracticeCopyLibrary corpus quality, localization parity,
/// placeholder parity, length contract, and static tone-safety tripwire.
/// </summary>
public sealed class PracticeCopyLibraryTests
{
    private static readonly PracticeCopyLibrary Library = new();

    private static readonly string[] SupportedLocales = ["en", "de", "ru"];

    private static readonly PracticeCopyTrigger[] AllTriggers =
    [
        PracticeCopyTrigger.FirstEverReady,
        PracticeCopyTrigger.InitialReady,
        PracticeCopyTrigger.ReturnShortAbsence,
        PracticeCopyTrigger.ReturnLongAbsence,
        PracticeCopyTrigger.ResumeManualPause,
        PracticeCopyTrigger.ResumeBackground,
        PracticeCopyTrigger.NeutralReady,
        PracticeCopyTrigger.NeutralPaused,
    ];

    // ---------------------------------------------------------------------------
    // D. Locale parity — all message IDs present in all 3 locales, non-empty
    // ---------------------------------------------------------------------------

    [Fact]
    public void Library_AllLocales_HaveIdenticalMessageIdSets()
    {
        foreach (var trigger in AllTriggers)
        {
            var enIds = Library.GetMessageIds(trigger, "en").OrderBy(id => id).ToList();
            var deIds = Library.GetMessageIds(trigger, "de").OrderBy(id => id).ToList();
            var ruIds = Library.GetMessageIds(trigger, "ru").OrderBy(id => id).ToList();

            Assert.True(enIds.Count > 0,
                $"English has 0 variants for trigger {trigger}. Expected at least 1.");

            Assert.True(enIds.SequenceEqual(deIds),
                $"German message IDs for trigger {trigger} differ from English.\n" +
                $"  EN: [{string.Join(", ", enIds)}]\n" +
                $"  DE: [{string.Join(", ", deIds)}]");

            Assert.True(enIds.SequenceEqual(ruIds),
                $"Russian message IDs for trigger {trigger} differ from English.\n" +
                $"  EN: [{string.Join(", ", enIds)}]\n" +
                $"  RU: [{string.Join(", ", ruIds)}]");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Library_AllVariants_HaveNonEmptyText(string locale)
    {
        foreach (var trigger in AllTriggers)
        {
            var ids = Library.GetMessageIds(trigger, locale);
            foreach (var id in ids)
            {
                var text = Library.GetText(id, locale);
                Assert.False(string.IsNullOrWhiteSpace(text),
                    $"Variant '{id}' for locale '{locale}' has null or empty text.");
            }
        }
    }

    // ---------------------------------------------------------------------------
    // E. Placeholder parity — {N} placeholders identical across locales
    // ---------------------------------------------------------------------------

    [Fact]
    public void Library_AllMessageIds_HavePlaceholderParityAcrossLocales()
    {
        var enIds = AllTriggers
            .SelectMany(t => Library.GetMessageIds(t, "en"))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var id in enIds)
        {
            var enText = Library.GetText(id, "en")!;
            var deText = Library.GetText(id, "de");
            var ruText = Library.GetText(id, "ru");

            var enPlaceholders = ExtractPlaceholders(enText);

            if (deText is not null)
            {
                var dePlaceholders = ExtractPlaceholders(deText);
                Assert.True(enPlaceholders.SequenceEqual(dePlaceholders),
                    $"Placeholder mismatch for ID '{id}' between EN and DE. " +
                    $"EN: [{string.Join(",", enPlaceholders)}], DE: [{string.Join(",", dePlaceholders)}]");
            }

            if (ruText is not null)
            {
                var ruPlaceholders = ExtractPlaceholders(ruText);
                Assert.True(enPlaceholders.SequenceEqual(ruPlaceholders),
                    $"Placeholder mismatch for ID '{id}' between EN and RU. " +
                    $"EN: [{string.Join(",", enPlaceholders)}], RU: [{string.Join(",", ruPlaceholders)}]");
            }
        }
    }

    // ---------------------------------------------------------------------------
    // F. Static tone safety — prohibited literal pattern tripwire for English
    // ---------------------------------------------------------------------------

    [Fact]
    public void Library_English_DoesNotContainObviouslyProhibitedPhrases()
    {
        string[] prohibitedPatterns =
        [
            "amazing", "brilliant", "great job", "well done", "you're crushing",
            "haven't practiced", "you haven't", "you've been away",
            "you missed", "don't forget", "you should",
            "champion", "hero", "superstar",
        ];

        foreach (var trigger in AllTriggers)
        {
            foreach (var id in Library.GetMessageIds(trigger, "en"))
            {
                var text = Library.GetText(id, "en")?.ToLowerInvariant() ?? "";
                foreach (var pattern in prohibitedPatterns)
                {
                    Assert.False(text.Contains(pattern, StringComparison.OrdinalIgnoreCase),
                        $"Variant '{id}' contains prohibited pattern '{pattern}': {text}");
                }
            }
        }
    }

    // ---------------------------------------------------------------------------
    // I. Length contract — English variants must not exceed 55 characters
    // ---------------------------------------------------------------------------

    [Fact]
    public void Library_English_NoVariantExceeds55Characters()
    {
        foreach (var trigger in AllTriggers)
        {
            foreach (var id in Library.GetMessageIds(trigger, "en"))
            {
                var text = Library.GetText(id, "en") ?? "";
                Assert.True(text.Length <= 55,
                    $"Variant '{id}' is {text.Length} chars (max 55): \"{text}\"");
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Pool size — each trigger must have at least N variants for meaningful variety
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(PracticeCopyTrigger.FirstEverReady, 2)]
    [InlineData(PracticeCopyTrigger.InitialReady, 4)]
    [InlineData(PracticeCopyTrigger.ReturnShortAbsence, 3)]
    [InlineData(PracticeCopyTrigger.ReturnLongAbsence, 3)]
    [InlineData(PracticeCopyTrigger.ResumeManualPause, 3)]
    [InlineData(PracticeCopyTrigger.ResumeBackground, 2)]
    [InlineData(PracticeCopyTrigger.NeutralReady, 2)]
    [InlineData(PracticeCopyTrigger.NeutralPaused, 2)]
    public void Library_PoolSize_MeetsMinimumVarietyRequirement(
        PracticeCopyTrigger trigger, int minimumCount)
    {
        var enIds = Library.GetMessageIds(trigger, "en");
        Assert.True(enIds.Count >= minimumCount,
            $"Trigger {trigger} has {enIds.Count} variants; expected at least {minimumCount}.");
    }

    // ---------------------------------------------------------------------------
    // Message ID format — IDs must follow "{Trigger}.{Tone}.{NNN}" convention
    // ---------------------------------------------------------------------------

    [Fact]
    public void Library_AllMessageIds_FollowNamingConvention()
    {
        var pattern = new Regex(@"^[A-Za-z]+\.[A-Za-z]+\.\d{3}$", RegexOptions.Compiled);

        foreach (var trigger in AllTriggers)
        {
            foreach (var id in Library.GetMessageIds(trigger, "en"))
            {
                Assert.Matches(pattern, id);
                Assert.StartsWith(trigger.ToString() + ".", id);
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Selector integration — production library returns non-fallback results
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Selector_WithProductionLibrary_ReturnsNonFallbackForAllTriggers(string locale)
    {
        var selector = new PracticeCopySelector(Library);

        foreach (var trigger in AllTriggers)
        {
            var ctx = new PracticeCopyContext(
                Trigger: trigger,
                PracticePosition: 100,
                SessionCorrectCount: 0,
                SessionTotalCount: 0,
                AbsenceBucket: AbsenceBucket.SameSession,
                IsFirstEverSession: false,
                Locale: locale);

            var result = selector.Select(ctx);

            Assert.False(result.IsFallback,
                $"Selector fell back to static string for trigger {trigger} locale {locale}. " +
                $"MessageId: {result.MessageId}");
            Assert.NotEmpty(result.LocalizedText);
            Assert.Equal(trigger, result.Trigger);
        }
    }

    // ---------------------------------------------------------------------------
    // Private helper
    // ---------------------------------------------------------------------------

    private static List<string> ExtractPlaceholders(string text)
    {
        return Regex.Matches(text, @"\{(\d+)\}")
            .Select(m => m.Value)
            .OrderBy(p => p)
            .ToList();
    }
}
