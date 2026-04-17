namespace Seren.Application.Chat;

/// <summary>
/// Options for the chat pipeline, bound from the <c>Chat</c> section of
/// <c>appsettings.json</c>. Controls baseline prompts and marker semantics
/// that steer the LLM towards emitting animation cues.
/// </summary>
public sealed class ChatOptions
{
    public const string SectionName = "Chat";

    /// <summary>
    /// Baseline system prompt prepended to every chat turn (in addition to
    /// any character-specific system prompt). Used to teach the LLM the
    /// Seren marker contract (<c>&lt;emotion:NAME&gt;</c>, <c>&lt;action:NAME&gt;</c>)
    /// so the avatar can react expressively. When the active character's
    /// own prompt already mentions these markers, the baseline is skipped
    /// to avoid duplication.
    /// </summary>
    public string DefaultSystemPrompt { get; set; } = string.Empty;
}
