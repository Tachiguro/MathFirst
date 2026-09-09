namespace MathFirst.Application.Copy;

/// <summary>
/// Provides the available copy variants for the contextual copy system.
/// Implementations supply message ID lists and localized text for each trigger.
///
/// Contract:
/// - <see cref="GetMessageIds"/> must return a stable, ordered list of stable language-independent
///   message IDs for the given trigger and locale. ID ordering should be consistent.
/// - <see cref="GetText"/> must return the physical localized text for a given message ID and
///   normalized locale, or <c>null</c> if that locale dictionary does not contain the ID.
///   It must not hide missing translations behind an English fallback.
/// - Implementations are expected to be singletons with no per-call state mutation.
/// </summary>
public interface IPracticeCopyLibrary
{
    /// <summary>
    /// Returns the ordered list of stable message IDs available for the given trigger and locale.
    /// An empty list is valid and will trigger fallback selection in the selector.
    /// </summary>
    IReadOnlyList<string> GetMessageIds(PracticeCopyTrigger trigger, string locale);

    /// <summary>
    /// Returns the localized text template for the given message ID and locale,
    /// or <c>null</c> if not found. Parameters use <c>{0}</c>, <c>{1}</c> format placeholders.
    /// </summary>
    string? GetText(string messageId, string locale);
}
