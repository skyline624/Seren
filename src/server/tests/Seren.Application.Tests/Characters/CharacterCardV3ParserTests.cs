using Seren.Application.Characters.Import;
using Seren.Contracts.Characters;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Characters;

/// <summary>
/// Unit tests for <see cref="CharacterCardV3Parser"/>. Covers the matrix
/// pinned in the chantier plan (spec variants, PNG/APNG dispatch, error
/// taxonomy, lorebook handling, macro substitution, truncation).
/// </summary>
public sealed class CharacterCardV3ParserTests
{
    private static readonly string[] ExpectedCortanaTags = ["scifi", "halo"];

    private readonly CharacterCardV3Parser _parser = new();

    [Fact]
    public void Parse_V3Json_HappyPath_MapsFields()
    {
        var bytes = CharacterCardTestFixtures.BuildJsonCard(
            name: "Cortana",
            dataFieldsJson: """
                "name": "Cortana",
                "description": "AI construct.",
                "personality": "Witty.",
                "scenario": "On the Pillar of Autumn.",
                "first_mes": "Hello, Chief.",
                "tags": ["scifi", "halo"],
                "creator": "343i",
                "character_version": "1.0"
                """);

        var result = _parser.Parse(bytes, "cortana.json");

        result.SpecVersion.ShouldBe("chara_card_v3");
        result.Name.ShouldBe("Cortana");
        result.Greeting.ShouldBe("Hello, Chief.");
        result.Description.ShouldBe("AI construct.");
        result.Tags.ShouldBe(ExpectedCortanaTags);
        result.Creator.ShouldBe("343i");
        result.CharacterVersion.ShouldBe("1.0");
        result.AvatarPng.ShouldBeNull();
        result.SystemPrompt.ShouldContain("AI construct.");
        result.SystemPrompt.ShouldContain("Witty.");
        result.SystemPrompt.ShouldContain("On the Pillar of Autumn.");
    }

    [Fact]
    public void Parse_V2Json_LegacySpec_MapsSameShape()
    {
        var bytes = CharacterCardTestFixtures.BuildJsonCard(
            name: "Alyx",
            spec: "chara_card_v2",
            dataFieldsJson: "\"name\": \"Alyx\", \"description\": \"Hacker.\"");

        var result = _parser.Parse(bytes, "alyx.json");

        result.SpecVersion.ShouldBe("chara_card_v2");
        result.Name.ShouldBe("Alyx");
    }

    [Fact]
    public void Parse_V3Png_ExtractsFromCcv3TextChunk()
    {
        var bytes = CharacterCardTestFixtures.BuildPngCard(name: "Chell");

        var result = _parser.Parse(bytes, "chell.png");

        result.SpecVersion.ShouldBe("chara_card_v3");
        result.Name.ShouldBe("Chell");
        result.AvatarPng.ShouldNotBeNull();
        result.AvatarPng!.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Parse_V2Png_ExtractsFromCharaTextChunk()
    {
        var bytes = CharacterCardTestFixtures.BuildPngCard(name: "GLaDOS", spec: "chara_card_v2");

        var result = _parser.Parse(bytes, "glados.png");

        result.SpecVersion.ShouldBe("chara_card_v2");
        result.Name.ShouldBe("GLaDOS");
    }

    [Fact]
    public void Parse_PngWithoutCharacterChunk_Rejects()
    {
        var bytes = CharacterCardTestFixtures.BuildPngWithTextChunk("author", "some other metadata");

        var ex = Should.Throw<CharacterImportException>(() => _parser.Parse(bytes, "plain.png"));
        ex.Code.ShouldBe(CharacterImportError.InvalidCard);
    }

    [Fact]
    public void Parse_CorruptedBase64_Rejects()
    {
        var bytes = CharacterCardTestFixtures.BuildPngWithTextChunk("ccv3", "not@valid#base64!!!");

        var ex = Should.Throw<CharacterImportException>(() => _parser.Parse(bytes, "bad.png"));
        ex.Code.ShouldBe(CharacterImportError.MalformedJson);
    }

    [Fact]
    public void Parse_UnsupportedSpec_Rejects()
    {
        var bytes = CharacterCardTestFixtures.BuildJsonCard(spec: "chara_card_v1");

        var ex = Should.Throw<CharacterImportException>(() => _parser.Parse(bytes, "v1.json"));
        ex.Code.ShouldBe(CharacterImportError.UnsupportedSpec);
    }

    [Fact]
    public void Parse_MissingName_Rejects()
    {
        var bytes = CharacterCardTestFixtures.BuildJsonCard(
            dataFieldsJson: "\"name\": \"\", \"description\": \"nameless\"");

        var ex = Should.Throw<CharacterImportException>(() => _parser.Parse(bytes, "noname.json"));
        ex.Code.ShouldBe(CharacterImportError.InvalidCard);
    }

    [Fact]
    public void Parse_EmptyPrompt_Rejects()
    {
        var bytes = CharacterCardTestFixtures.BuildJsonCard(
            dataFieldsJson: "\"name\": \"Silent\"");

        var ex = Should.Throw<CharacterImportException>(() => _parser.Parse(bytes, "silent.json"));
        ex.Code.ShouldBe(CharacterImportError.EmptyPrompt);
    }

    [Fact]
    public void Parse_MalformedJson_Rejects()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("{ not valid json ");

        var ex = Should.Throw<CharacterImportException>(() => _parser.Parse(bytes, "broken.json"));
        ex.Code.ShouldBe(CharacterImportError.MalformedJson);
    }

    [Fact]
    public void Parse_NonPngNonJson_Rejects()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("garbage binary file");

        var ex = Should.Throw<CharacterImportException>(() => _parser.Parse(bytes, "weird.bin"));
        ex.Code.ShouldBe(CharacterImportError.InvalidCard);
    }

    [Fact]
    public void Parse_FileOverSizeCap_Rejects()
    {
        var bytes = new byte[11 * 1024 * 1024];

        var ex = Should.Throw<CharacterImportException>(() => _parser.Parse(bytes, "huge.png"));
        ex.Code.ShouldBe(CharacterImportError.CardTooLarge);
    }

    [Fact]
    public void Parse_ConstantLorebookEntries_FlattenedIntoSystemPrompt()
    {
        var bytes = CharacterCardTestFixtures.BuildJsonCard(
            dataFieldsJson: """
                "name": "Cortana",
                "description": "AI.",
                "character_book": {
                  "entries": [
                    {
                      "content": "The UNSC is the United Nations Space Command.",
                      "constant": true,
                      "enabled": true,
                      "insertion_order": 0
                    },
                    {
                      "content": "The Covenant is a religious alliance of alien races.",
                      "constant": true,
                      "enabled": true,
                      "insertion_order": 1
                    }
                  ]
                }
                """);

        var result = _parser.Parse(bytes, "cortana.json");

        result.SystemPrompt.ShouldContain("UNSC is the United Nations Space Command.");
        result.SystemPrompt.ShouldContain("Covenant is a religious alliance of alien races.");
        // Insertion order respected.
        var unscPos = result.SystemPrompt.IndexOf("UNSC", StringComparison.Ordinal);
        var covenantPos = result.SystemPrompt.IndexOf("Covenant", StringComparison.Ordinal);
        unscPos.ShouldBeLessThan(covenantPos);
    }

    [Fact]
    public void Parse_NonConstantLorebookEntries_PreservedAsWarningAndMetadata()
    {
        var bytes = CharacterCardTestFixtures.BuildJsonCard(
            dataFieldsJson: """
                "name": "Cortana",
                "description": "AI.",
                "character_book": {
                  "entries": [
                    {
                      "keys": ["flood", "parasite"],
                      "content": "The Flood is a parasitic lifeform.",
                      "constant": false,
                      "enabled": true
                    }
                  ]
                }
                """);

        var result = _parser.Parse(bytes, "cortana.json");

        result.Warnings.ShouldContain(CharacterCardV3Parser.WarningLorebookDeferred);
        result.SystemPrompt.ShouldNotContain("Flood");
        result.ImportMetadataJson.ShouldContain("character_book");
        result.ImportMetadataJson.ShouldContain("\"flood\"");
    }

    [Fact]
    public void Parse_MacroSubstitution_ReplacesCharAndUser()
    {
        var bytes = CharacterCardTestFixtures.BuildJsonCard(
            dataFieldsJson: """
                "name": "Cortana",
                "system_prompt": "You are {{char}} talking to {{user}}.",
                "first_mes": "Hello {{user}}, I am {{char}}."
                """);

        var result = _parser.Parse(bytes, "cortana.json");

        result.SystemPrompt.ShouldBe("You are Cortana talking to user.");
        result.Greeting.ShouldBe("Hello user, I am Cortana.");
    }

    [Fact]
    public void Parse_SystemPromptExceedingLimit_TruncatesAndWarns()
    {
        var longPrompt = new string('x', 5000);
        var bytes = CharacterCardTestFixtures.BuildJsonCard(
            dataFieldsJson: $"\"name\": \"Cortana\", \"system_prompt\": \"{longPrompt}\"");

        var result = _parser.Parse(bytes, "cortana.json");

        result.SystemPrompt.Length.ShouldBe(4000);
        result.Warnings.ShouldContain(CharacterCardV3Parser.WarningPromptTruncated);
    }

    [Fact]
    public void Parse_ExplicitSystemPrompt_TakesPrecedenceOverComposedFallback()
    {
        var bytes = CharacterCardTestFixtures.BuildJsonCard(
            dataFieldsJson: """
                "name": "Cortana",
                "system_prompt": "Explicit prompt wins.",
                "description": "Some description.",
                "personality": "Some personality."
                """);

        var result = _parser.Parse(bytes, "cortana.json");

        result.SystemPrompt.ShouldStartWith("Explicit prompt wins.");
        result.SystemPrompt.ShouldNotContain("Some description.");
    }
}
