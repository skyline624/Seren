using Seren.Application.Chat;
using Seren.Domain.Entities;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Chat;

public sealed class SystemPromptBuilderTests
{
    private const string Baseline =
        "Emit <emotion:NAME> and <action:NAME> markers to drive the avatar.";

    private static Character MakeCharacter(string systemPrompt) => new(
        Id: Guid.NewGuid(),
        Name: "Seren",
        SystemPrompt: systemPrompt,
        VrmAssetPath: null,
        Voice: null,
        AgentId: null,
        IsActive: true,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Build_WithoutCharacter_EmitsBaselineOnly()
    {
        var result = SystemPromptBuilder.Build(character: null, Baseline);

        result.Count.ShouldBe(1);
        result[0].Role.ShouldBe("system");
        result[0].Content.ShouldBe(Baseline);
    }

    [Fact]
    public void Build_WithCharacterWithoutMarkers_EmitsBothAsSeparateSystemMessages()
    {
        var character = MakeCharacter("You are a cheerful helper named Seren.");

        var result = SystemPromptBuilder.Build(character, Baseline);

        result.Count.ShouldBe(2);
        result[0].Role.ShouldBe("system");
        result[0].Content.ShouldBe(Baseline); // baseline first
        result[1].Role.ShouldBe("system");
        result[1].Content.ShouldBe(character.SystemPrompt);
    }

    [Fact]
    public void Build_WithCharacterAlreadyTeachingMarkers_SkipsBaseline()
    {
        var character = MakeCharacter(
            "You are Seren. You can emit <emotion:joy> and <action:wave>.");

        var result = SystemPromptBuilder.Build(character, Baseline);

        result.Count.ShouldBe(1);
        result[0].Content.ShouldBe(character.SystemPrompt);
    }

    [Fact]
    public void Build_WithEmptyBaseline_EmitsCharacterOnly()
    {
        var character = MakeCharacter("You are Seren.");

        var result = SystemPromptBuilder.Build(character, defaultSystemPrompt: string.Empty);

        result.Count.ShouldBe(1);
        result[0].Content.ShouldBe(character.SystemPrompt);
    }

    [Fact]
    public void Build_WithEmptyBaselineAndNoCharacter_EmitsNothing()
    {
        var result = SystemPromptBuilder.Build(character: null, defaultSystemPrompt: "  ");

        result.Count.ShouldBe(0);
    }
}
