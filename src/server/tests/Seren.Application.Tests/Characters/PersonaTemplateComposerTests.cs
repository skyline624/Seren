using Seren.Application.Characters.Personas;
using Seren.Domain.Entities;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Characters;

/// <summary>
/// Pure unit tests for <see cref="PersonaTemplateComposer"/>. Every
/// branch of identity/soul composition + the marker protocol invariant.
/// </summary>
public sealed class PersonaTemplateComposerTests
{
    private static Character BuildCharacter(
        string name = "Cortana",
        string systemPrompt = "You are Cortana, a witty AI construct.",
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
    public void ComposeIdentity_WithNameOnly_OmitsOptionalSections()
    {
        var output = PersonaTemplateComposer.ComposeIdentity(BuildCharacter());
        output.ShouldContain("# Cortana");
        output.ShouldNotContain("## Description");
        output.ShouldNotContain("## Tags");
        output.ShouldNotContain("## Greeting");
    }

    [Fact]
    public void ComposeIdentity_WithAllFields_RendersEverySection()
    {
        var output = PersonaTemplateComposer.ComposeIdentity(BuildCharacter(
            description: "AI construct from Halo.",
            greeting: "Hello, Chief.",
            tags: ["scifi", "halo"]));

        output.ShouldContain("# Cortana");
        output.ShouldContain("## Description");
        output.ShouldContain("AI construct from Halo.");
        output.ShouldContain("## Tags");
        output.ShouldContain("- scifi");
        output.ShouldContain("- halo");
        output.ShouldContain("## Greeting");
        output.ShouldContain("> Hello, Chief.");
    }

    [Fact]
    public void ComposeIdentity_CarriesSerenManagedHeader()
    {
        var output = PersonaTemplateComposer.ComposeIdentity(BuildCharacter());
        output.ShouldStartWith(PersonaTemplateComposer.SerenManagedHeader);
    }

    [Fact]
    public void ComposeIdentity_EmptyName_Throws()
    {
        var invalid = Character.Create("placeholder", "prompt") with { };
        // Rebuild with blank name via `with` so the factory's own guard doesn't fire first.
        Should.Throw<ArgumentException>(() => PersonaTemplateComposer.ComposeIdentity(invalid with { Name = "   " }));
    }

    [Fact]
    public void ComposeSoul_IncludesSystemPromptAndMarkerProtocol()
    {
        var output = PersonaTemplateComposer.ComposeSoul(BuildCharacter(
            systemPrompt: "You are Cortana, witty and loyal."));

        output.ShouldContain("# Cortana — Soul");
        output.ShouldContain("You are Cortana, witty and loyal.");
        output.ShouldContain("## Expression markers — Seren protocol");
        output.ShouldContain("<emotion:NAME>");
        output.ShouldContain("<action:NAME>");
    }

    [Fact]
    public void ComposeSoul_AlwaysInjectsMarkerProtocol_EvenForImportedChubCards()
    {
        // A Chub card whose system prompt doesn't mention markers should
        // still get them appended — that's the whole point of the writer.
        var output = PersonaTemplateComposer.ComposeSoul(BuildCharacter(
            systemPrompt: "A tavern-keeper who hates bards."));

        output.ShouldContain("A tavern-keeper who hates bards.");
        output.ShouldContain(PersonaTemplateComposer.MarkerProtocolBlock);
    }

    [Fact]
    public void ComposeSoul_CarriesSerenManagedHeader()
    {
        var output = PersonaTemplateComposer.ComposeSoul(BuildCharacter());
        output.ShouldStartWith(PersonaTemplateComposer.SerenManagedHeader);
    }

    [Fact]
    public void ComposeSoul_EmptySystemPrompt_Throws()
    {
        var invalid = Character.Create("Cortana", "placeholder") with { };
        Should.Throw<ArgumentException>(() =>
            PersonaTemplateComposer.ComposeSoul(invalid with { SystemPrompt = "   " }));
    }

    [Fact]
    public void ComposeSoul_TrimsPromptButPreservesMeaningfulNewlines()
    {
        var output = PersonaTemplateComposer.ComposeSoul(BuildCharacter(
            systemPrompt: "  Line one.\n\nLine two.  "));

        output.ShouldContain("Line one.\n\nLine two.");
        output.ShouldNotContain("  Line one.");
    }

    [Fact]
    public void ComposeSoul_EndsWithNewline()
    {
        var output = PersonaTemplateComposer.ComposeSoul(BuildCharacter());
        output.EndsWith('\n').ShouldBeTrue();
    }
}
