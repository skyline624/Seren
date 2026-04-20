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

    // Matches `<think>…</think>` across newlines (Qwen3 reasoning models
    // emit these blocks inline in assistant turns). The trailing `\s*`
    // eats the whitespace OpenClaw models commonly insert after the
    // closing tag so we don't leave a dangling blank line when stripping.
    private static readonly Regex ThinkingPattern = new(
        @"<think>.*?</think>\s*",
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

        var stripped = ThinkingPattern.Replace(text, string.Empty);
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
        var cleanText = text;

        var emotionMatches = EmotionPattern.Matches(text);
        foreach (Match match in emotionMatches)
        {
            emotions.Add(new EmotionMarker(match.Groups[1].Value, match.Index));
        }

        cleanText = EmotionPattern.Replace(cleanText, string.Empty);

        var actionMatches = ActionPattern.Matches(text);
        foreach (Match match in actionMatches)
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
