using Seren.Application.Abstractions;
using Seren.Domain.Entities;

namespace Seren.Application.Chat;

/// <summary>
/// Assembles the list of <c>system</c> messages sent to the LLM for a single
/// chat turn: the Seren marker contract baseline and/or the active
/// character's personality prompt.
/// </summary>
/// <remarks>
/// Extracted from the two request handlers (<c>SendTextMessageHandler</c>
/// and <c>SubmitVoiceInputHandler</c>) so both paths stay in lock-step and
/// the policy is trivially unit-testable.
/// </remarks>
public static class SystemPromptBuilder
{
    /// <summary>
    /// Builds the system messages preceding the user turn.
    /// </summary>
    /// <param name="character">Active character (optional).</param>
    /// <param name="defaultSystemPrompt">
    /// Baseline prompt that teaches the LLM the marker contract. Empty
    /// string disables the baseline injection entirely.
    /// </param>
    /// <returns>
    /// An ordered list of <see cref="ChatMessage"/>s all with <c>role="system"</c>.
    /// Prepends the baseline so the character voice (more specific) comes
    /// last and dominates the LLM's persona reasoning.
    /// </returns>
    public static IReadOnlyList<ChatMessage> Build(
        Character? character,
        string defaultSystemPrompt)
    {
        var messages = new List<ChatMessage>(capacity: 2);
        var characterPrompt = character?.SystemPrompt;
        var characterHasMarkers = !string.IsNullOrEmpty(characterPrompt)
            && (characterPrompt.Contains("<emotion:", StringComparison.Ordinal)
                || characterPrompt.Contains("<action:", StringComparison.Ordinal));

        // Baseline is emitted unless the character already teaches the
        // marker contract (avoids sending conflicting instructions).
        if (!string.IsNullOrWhiteSpace(defaultSystemPrompt) && !characterHasMarkers)
        {
            messages.Add(new ChatMessage("system", defaultSystemPrompt));
        }

        if (!string.IsNullOrEmpty(characterPrompt))
        {
            messages.Add(new ChatMessage("system", characterPrompt));
        }

        return messages;
    }
}
