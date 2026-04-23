using System.Text;
using Seren.Domain.Entities;

namespace Seren.Application.Characters.Personas;

/// <summary>
/// Pure composition of the two Markdown files that drive OpenClaw's
/// persona : <c>IDENTITY.md</c> (short presentation card) and
/// <c>SOUL.md</c> (authoritative system prompt + Seren marker
/// protocol). No I/O, no external state — fully unit-testable with
/// the <see cref="Character"/> value type alone.
/// </summary>
/// <remarks>
/// Every generated file carries the <see cref="SerenManagedHeader"/>
/// comment so a human opening the file understands it will be
/// regenerated the next time a character is activated — manual edits
/// here don't stick.
/// </remarks>
public static class PersonaTemplateComposer
{
    /// <summary>
    /// Pre-pended to every generated file. HTML-comment form so it's
    /// invisible to the LLM (Markdown renderers ignore it) while still
    /// screaming "don't edit" to any dev who opens the file.
    /// </summary>
    public const string SerenManagedHeader =
        "<!-- SEREN-MANAGED — regenerated on character activation, do not edit by hand -->";

    /// <summary>
    /// The Seren animation-marker protocol injected at the end of every
    /// <c>SOUL.md</c>. Source of truth — tweak this constant to evolve
    /// the vocabulary across all generated personas at once.
    /// </summary>
    public const string MarkerProtocolBlock = """
## Expression markers — Seren protocol

When you want to express an emotion, emit a marker inline at the
relevant point in your reply : `<emotion:NAME>` where NAME is one of
`joy`, `sad`, `anger`, `surprise`, `relaxed`, `neutral`.

When you want to perform a body action, emit : `<action:NAME>` where
NAME is one of `wave`, `nod`, `bow`, `shake`, `think`.

Guidelines:
- At most one emotion marker per paragraph. Use sparingly.
- Never place markers inside code blocks, quotations, or URLs.
- Markers do not render in the chat — they drive the avatar only.

Examples:
- `<emotion:joy> Hello! <action:wave> Nice to meet you.`
- `<action:nod> That makes sense.`
""";

    /// <summary>
    /// Build <c>IDENTITY.md</c> from the character's display fields.
    /// Missing sections are omitted cleanly — never produces
    /// <c>"## Description\n\nnull"</c>-style artifacts.
    /// </summary>
    public static string ComposeIdentity(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentException.ThrowIfNullOrWhiteSpace(character.Name);

        var sb = new StringBuilder();
        sb.Append(SerenManagedHeader).Append("\n\n");
        sb.Append("# ").Append(character.Name).Append('\n');

        if (!string.IsNullOrWhiteSpace(character.Description))
        {
            sb.Append("\n## Description\n\n").Append(character.Description.Trim()).Append('\n');
        }

        if (character.Tags.Count > 0)
        {
            sb.Append("\n## Tags\n\n");
            foreach (var tag in character.Tags)
            {
                sb.Append("- ").Append(tag).Append('\n');
            }
        }

        if (!string.IsNullOrWhiteSpace(character.Greeting))
        {
            sb.Append("\n## Greeting\n\n");
            foreach (var line in character.Greeting.Trim().Split('\n'))
            {
                sb.Append("> ").Append(line).Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Build <c>SOUL.md</c> — header + composed system prompt +
    /// marker-protocol annex. The prompt content is used verbatim:
    /// upstream producers (the CCv3 parser, the manual character-create
    /// form) are responsible for its composition + macro substitution.
    /// </summary>
    public static string ComposeSoul(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentException.ThrowIfNullOrWhiteSpace(character.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(character.SystemPrompt);

        var sb = new StringBuilder();
        sb.Append(SerenManagedHeader).Append("\n\n");
        sb.Append("# ").Append(character.Name).Append(" — Soul\n\n");
        sb.Append(character.SystemPrompt.Trim()).Append("\n\n");
        sb.Append(MarkerProtocolBlock);
        if (!sb.ToString().EndsWith('\n'))
        {
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
