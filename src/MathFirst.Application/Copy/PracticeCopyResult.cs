namespace MathFirst.Application.Copy;

/// <summary>
/// The result of a contextual copy selection.
/// </summary>
/// <param name="MessageId">
/// Stable, language-independent message ID in the format <c>{Trigger}.{Tone}.{Index}</c>.
/// Used for tests, repetition avoidance, and logging. Never shown to the user.
/// </param>
/// <param name="LocalizedText">
/// Fully localized, presentation-ready text string. This is what appears in the UI.
/// </param>
/// <param name="Trigger">The trigger that drove this selection.</param>
/// <param name="IsFallback">
/// True when the result was produced by the static-key fallback path rather than
/// a corpus message.
/// </param>
public sealed record PracticeCopyResult(
    string MessageId,
    string LocalizedText,
    PracticeCopyTrigger Trigger,
    bool IsFallback = false);
