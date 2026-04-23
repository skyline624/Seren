using Seren.Application.Abstractions;
using Seren.Application.Characters.Personas;
using Seren.Contracts.Characters;
using Seren.Domain.Entities;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Characters;

/// <summary>
/// Pure unit tests for <see cref="PersonaTemplateExtractor"/>. The
/// round-trip test against <see cref="PersonaTemplateComposer"/> is
/// the critical invariant : compose → extract must yield a
/// byte-equivalent <see cref="Character.SystemPrompt"/> (modulo trim)
/// so successive captures + activations don't erode the persona.
/// </summary>
public sealed class PersonaTemplateExtractorTests
{
    private static Character BuildCharacter(
        string name = "Cortana",
        string systemPrompt = "You are Cortana, witty and loyal.",
        string? greeting = null,
        string? description = null,
        IReadOnlyList<string>? tags = null)
    {
        return Character.Create(name, systemPrompt) with
        {
            Greeting = greeting,
            Description = description,
            Tags = tags ?? [],
        };
    }

    [Fact]
    public void Extract_RoundTrip_PreservesSystemPromptExactly()
    {
        var original = BuildCharacter(
            systemPrompt: "You are Cortana.\n\nTwo paragraphs, dashes — and em dashes too.");
        var snapshot = new WorkspacePersonaSnapshot(
            PersonaTemplateComposer.ComposeIdentity(original),
            PersonaTemplateComposer.ComposeSoul(original));

        var extracted = PersonaTemplateExtractor.Extract(snapshot);

        extracted.Name.ShouldBe("Cortana");
        extracted.SystemPrompt.ShouldBe(original.SystemPrompt.Trim());
    }

    [Fact]
    public void Extract_RoundTrip_PreservesEveryOptionalField()
    {
        var original = BuildCharacter(
            name: "Cortana",
            systemPrompt: "Short prompt.",
            greeting: "Cortana en ligne.",
            description: "CTN-0452-9 — IA UNSC.",
            tags: ["scifi", "halo", "ai-construct"]);
        var snapshot = new WorkspacePersonaSnapshot(
            PersonaTemplateComposer.ComposeIdentity(original),
            PersonaTemplateComposer.ComposeSoul(original));

        var extracted = PersonaTemplateExtractor.Extract(snapshot);

        extracted.Name.ShouldBe("Cortana");
        extracted.SystemPrompt.ShouldBe("Short prompt.");
        extracted.Description.ShouldBe("CTN-0452-9 — IA UNSC.");
        extracted.Greeting.ShouldBe("Cortana en ligne.");
        extracted.Tags.ShouldBe(["scifi", "halo", "ai-construct"]);
    }

    [Fact]
    public void Extract_AlwaysStripsMarkerProtocolAnnex()
    {
        var original = BuildCharacter(systemPrompt: "Persona body.");
        var soul = PersonaTemplateComposer.ComposeSoul(original);
        var snapshot = new WorkspacePersonaSnapshot(
            PersonaTemplateComposer.ComposeIdentity(original),
            soul);

        var extracted = PersonaTemplateExtractor.Extract(snapshot);

        soul.ShouldContain(PersonaTemplateExtractor.MarkerProtocolHeader);
        extracted.SystemPrompt.ShouldNotContain(PersonaTemplateExtractor.MarkerProtocolHeader);
        extracted.SystemPrompt.ShouldNotContain("<emotion:NAME>");
        extracted.SystemPrompt.ShouldBe("Persona body.");
    }

    [Fact]
    public void Extract_VanillaOpenClawMarkdown_TakesBodyAsIs()
    {
        // Sample "vanilla" OpenClaw output — no Seren bandeau, no
        // `— Soul` suffix, no protocol annex. Extractor must degrade
        // gracefully and just take the body.
        var identity = "# Ada\n\n## Description\n\nA quiet mathematician.\n";
        var soul = "# Ada\n\nYou are Ada, patient and precise.\n";
        var extracted = PersonaTemplateExtractor.Extract(new WorkspacePersonaSnapshot(identity, soul));

        extracted.Name.ShouldBe("Ada");
        extracted.SystemPrompt.ShouldBe("You are Ada, patient and precise.");
        extracted.Description.ShouldBe("A quiet mathematician.");
        extracted.Greeting.ShouldBeNull();
        extracted.Tags.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_NoHeadingAnywhere_ThrowsInvalidPersona()
    {
        var snapshot = new WorkspacePersonaSnapshot(
            "No heading here, just text.",
            "Same for SOUL — no heading.");

        var ex = Should.Throw<PersonaCaptureException>(() => PersonaTemplateExtractor.Extract(snapshot));
        ex.Code.ShouldBe(PersonaCaptureError.InvalidPersona);
    }

    [Fact]
    public void Extract_EmptyPromptAfterStripping_ThrowsInvalidPersona()
    {
        // SOUL.md contains only the Seren bandeau + heading + annex —
        // no actual persona body. Extractor must refuse.
        var identity = "# Cortana\n";
        var soul = $"{PersonaTemplateComposer.SerenManagedHeader}\n\n"
                   + "# Cortana — Soul\n\n"
                   + PersonaTemplateComposer.MarkerProtocolBlock;

        var ex = Should.Throw<PersonaCaptureException>(() =>
            PersonaTemplateExtractor.Extract(new WorkspacePersonaSnapshot(identity, soul)));
        ex.Code.ShouldBe(PersonaCaptureError.InvalidPersona);
    }

    [Fact]
    public void Extract_NameFallsBackToSoulWithSuffixStripped()
    {
        // Identity has no heading — fallback to SOUL. The `— Soul`
        // suffix must be stripped so the Character name stays clean.
        var identity = "Just a description, no heading.";
        var soul = "# Cortana — Soul\n\nPrompt body.\n";

        var extracted = PersonaTemplateExtractor.Extract(new WorkspacePersonaSnapshot(identity, soul));

        extracted.Name.ShouldBe("Cortana");
    }

    [Fact]
    public void Extract_GreetingBlockquote_StripsLeadingGt()
    {
        var identity = "# Cortana\n\n## Greeting\n\n> First line.\n> Second line.\n";
        var soul = "# Cortana\n\nPrompt body.\n";

        var extracted = PersonaTemplateExtractor.Extract(new WorkspacePersonaSnapshot(identity, soul));

        extracted.Greeting.ShouldBe("First line.\nSecond line.");
    }

    [Fact]
    public void Extract_TagsSection_KeepsOnlyHyphenListItems()
    {
        var identity = "# X\n\n## Tags\n\n- a\n- b\nnot-a-tag\n- c\n";
        var soul = "# X\n\nBody.\n";

        var extracted = PersonaTemplateExtractor.Extract(new WorkspacePersonaSnapshot(identity, soul));

        extracted.Tags.ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void Extract_OptionalSectionsAbsent_ReturnNullOrEmpty()
    {
        var identity = "# X\n";
        var soul = "# X\n\nBody.\n";

        var extracted = PersonaTemplateExtractor.Extract(new WorkspacePersonaSnapshot(identity, soul));

        extracted.Description.ShouldBeNull();
        extracted.Greeting.ShouldBeNull();
        extracted.Tags.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_Null_Throws()
    {
        Should.Throw<ArgumentNullException>(() => PersonaTemplateExtractor.Extract(null!));
    }

    [Fact]
    public void MarkerProtocolHeader_MatchesFirstLineOfComposerBlock()
    {
        // Single source of truth: the extractor's cut-point header MUST
        // be exactly the first line of the composer's block.
        var firstLine = PersonaTemplateComposer.MarkerProtocolBlock.Split('\n', 2)[0].TrimEnd();
        PersonaTemplateExtractor.MarkerProtocolHeader.ShouldBe(firstLine);
    }
}
