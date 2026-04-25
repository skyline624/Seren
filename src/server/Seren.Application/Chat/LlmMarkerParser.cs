using System.Text.RegularExpressions;

namespace Seren.Application.Chat;

/// <summary>
/// Parses LLM output text for special marker tags like
/// <c>&lt;emotion:joy&gt;</c> and <c>&lt;action:wave&gt;</c>,
/// returning the clean text with markers removed alongside
/// the extracted marker lists.
/// </summary>
public sealed class LlmMarkerParser
{
    private static readonly Regex EmotionPattern = new(@"<emotion:(\w+)>", RegexOptions.Compiled);
    private static readonly Regex ActionPattern = new(@"<action:(\w+)>", RegexOptions.Compiled);

    // Closing marker tags should never reach this parser — markers are
    // self-closing by contract — but some models (GLM / small Qwen under
    // tool-use pressure) emit `<action:think>…</action:think>` as a
    // block-style wrapper anyway. Strip the orphan closing form silently
    // so it never lands in user-visible text.
    private static readonly Regex ClosingMarkerPattern = new(
        @"</(?:action|emotion):\w+>",
        RegexOptions.Compiled);

    // Matches `<think>…</think>` across newlines (Qwen3 reasoning models
    // emit these blocks inline in assistant turns). The trailing `\s*`
    // eats the whitespace OpenClaw models commonly insert after the
    // closing tag so we don't leave a dangling blank line when stripping.
    private static readonly Regex ThinkingPattern = new(
        @"<think>.*?</think>\s*",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Pseudo-block form `<action:think>…</action:think>` — alias of
    // <think>…</think> used when a model confuses the action-marker
    // singleton contract with a wrapping tag. Gobbled as a full block
    // (content + tags) so the reasoning never reaches the bubble. The
    // streaming path in SendTextMessageHandler normalizes the same
    // tokens upstream; this one is the safety net for history hydration
    // and any complete chunk that slips past the stream state machine.
    private static readonly Regex ActionThinkingBlockPattern = new(
        @"<action:think>.*?</action:think>\s*",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// One-shot sanitizer for persisted assistant content. Removes
    /// <c>&lt;think&gt;…&lt;/think&gt;</c> reasoning blocks plus any
    /// <c>&lt;emotion:*&gt;</c> / <c>&lt;action:*&gt;</c> markers and
    /// returns the user-visible text only. Used by the history
    /// hydration path where the streaming state machine in
    /// <see cref="SendTextMessageHandler"/> can't run (no chunk
    /// boundaries to defend against).
    /// </summary>
    public static string StripAll(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Block-style patterns first so we remove content wholesale before
        // the singleton patterns scan for leftover opening tags.
        var stripped = ThinkingPattern.Replace(text, string.Empty);
        stripped = ActionThinkingBlockPattern.Replace(stripped, string.Empty);
        stripped = ClosingMarkerPattern.Replace(stripped, string.Empty);
        stripped = EmotionPattern.Replace(stripped, string.Empty);
        stripped = ActionPattern.Replace(stripped, string.Empty);
        return stripped;
    }

    /// <summary>
    /// Parses <paramref name="text"/> for emotion and action markers,
    /// removes them from the output, and returns the extracted markers.
    /// </summary>
    public static ParseResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var emotions = new List<EmotionMarker>();
        var actions = new List<ActionMarker>();

        // Order matters: wipe block-style leaks before the singleton
        // regexes would otherwise record a spurious `<action:think>`
        // action marker and fire a "think" avatar gesture from the
        // model's private reasoning.
        var cleanText = ActionThinkingBlockPattern.Replace(text, string.Empty);
        cleanText = ClosingMarkerPattern.Replace(cleanText, string.Empty);

        foreach (Match match in EmotionPattern.Matches(cleanText))
        {
            emotions.Add(new EmotionMarker(match.Groups[1].Value, match.Index));
        }
        cleanText = EmotionPattern.Replace(cleanText, string.Empty);

        foreach (Match match in ActionPattern.Matches(cleanText))
        {
            actions.Add(new ActionMarker(match.Groups[1].Value, match.Index));
        }
        cleanText = ActionPattern.Replace(cleanText, string.Empty);

        return new ParseResult(cleanText, emotions, actions);
    }
}

/// <summary>
/// Result of parsing LLM output for marker tags.
/// </summary>
/// <param name="CleanText">Original text with all markers removed.</param>
/// <param name="Emotions">Extracted emotion markers in order of appearance.</param>
/// <param name="Actions">Extracted action markers in order of appearance.</param>
public sealed record ParseResult(
    string CleanText,
    IReadOnlyList<EmotionMarker> Emotions,
    IReadOnlyList<ActionMarker> Actions);

/// <summary>
/// An emotion marker extracted from LLM output.
/// </summary>
/// <param name="Emotion">The emotion name (e.g. "joy", "sadness").</param>
/// <param name="Position">The character position of the marker in the original text.</param>
public sealed record EmotionMarker(string Emotion, int Position);

/// <summary>
/// An action marker extracted from LLM output.
/// </summary>
/// <param name="Action">The action name (e.g. "wave", "nod").</param>
/// <param name="Position">The character position of the marker in the original text.</param>
public sealed record ActionMarker(string Action, int Position);
