using Seren.Application.Chat;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Chat;

public sealed class LlmMarkerParserTests
{
    [Fact]
    public void Parse_TextWithoutMarkers_ShouldReturnOriginalTextWithEmptyMarkerLists()
    {
        var result = LlmMarkerParser.Parse("Hello, how are you?");

        result.CleanText.ShouldBe("Hello, how are you?");
        result.Emotions.ShouldBeEmpty();
        result.Actions.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_EmotionMarker_ShouldExtractEmotionAndRemoveTag()
    {
        var result = LlmMarkerParser.Parse("I'm so <emotion:joy>happy!");

        result.CleanText.ShouldBe("I'm so happy!");
        result.Emotions.Count.ShouldBe(1);
        result.Emotions[0].Emotion.ShouldBe("joy");
        result.Emotions[0].Position.ShouldBe(7);
        result.Actions.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_ActionMarker_ShouldExtractActionAndRemoveTag()
    {
        var result = LlmMarkerParser.Parse("She <action:wave>greets you.");

        result.CleanText.ShouldBe("She greets you.");
        result.Emotions.ShouldBeEmpty();
        result.Actions.Count.ShouldBe(1);
        result.Actions[0].Action.ShouldBe("wave");
        result.Actions[0].Position.ShouldBe(4);
    }

    [Fact]
    public void Parse_MultipleMarkers_ShouldExtractAllInOrder()
    {
        var result = LlmMarkerParser.Parse("<emotion:joy>I'm happy! <action:wave>Hello!");

        result.Emotions.Count.ShouldBe(1);
        result.Emotions[0].Emotion.ShouldBe("joy");

        result.Actions.Count.ShouldBe(1);
        result.Actions[0].Action.ShouldBe("wave");

        result.CleanText.ShouldBe("I'm happy! Hello!");
    }

    [Fact]
    public void Parse_UnknownMarkerPrefix_ShouldBeIgnored()
    {
        var result = LlmMarkerParser.Parse("Hello <thought:pondering>world");

        result.CleanText.ShouldBe("Hello <thought:pondering>world");
        result.Emotions.ShouldBeEmpty();
        result.Actions.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_EmptyString_ShouldReturnEmptyResult()
    {
        var result = LlmMarkerParser.Parse(string.Empty);

        result.CleanText.ShouldBeEmpty();
        result.Emotions.ShouldBeEmpty();
        result.Actions.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_MarkerWithSpacesInName_ShouldNotMatch()
    {
        var result = LlmMarkerParser.Parse("<emotion:big smile>hello");

        result.CleanText.ShouldBe("<emotion:big smile>hello");
        result.Emotions.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_SameEmotionMultipleTimes_ShouldExtractAll()
    {
        var result = LlmMarkerParser.Parse("<emotion:joy>Yay! <emotion:joy>Again!");

        result.Emotions.Count.ShouldBe(2);
        result.Emotions[0].Emotion.ShouldBe("joy");
        result.Emotions[1].Emotion.ShouldBe("joy");
        result.CleanText.ShouldBe("Yay! Again!");
    }

    [Fact]
    public void Parse_MixedEmotionAndActionMarkers_ShouldExtractBoth()
    {
        var result = LlmMarkerParser.Parse("<emotion:joy>Smile! <action:nod>I agree <emotion:love>Love it!");

        result.Emotions.Count.ShouldBe(2);
        result.Emotions[0].Emotion.ShouldBe("joy");
        result.Emotions[1].Emotion.ShouldBe("love");

        result.Actions.Count.ShouldBe(1);
        result.Actions[0].Action.ShouldBe("nod");

        result.CleanText.ShouldBe("Smile! I agree Love it!");
    }

    [Fact]
    public void Parse_NullText_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => LlmMarkerParser.Parse(null!));
    }

    [Fact]
    public void StripAll_EmptyThinkBlock_ShouldRemoveTagsAndTrailingWhitespace()
    {
        // Qwen3 default output when reasoning is off — empty <think></think>.
        LlmMarkerParser.StripAll("<think>\n\n</think>\n\nBonjour !")
            .ShouldBe("Bonjour !");
    }

    [Fact]
    public void StripAll_ThinkBlockWithContent_ShouldRemoveBlockEntirely()
    {
        LlmMarkerParser.StripAll("<think>reasoning step one\nstep two</think>\nVisible answer.")
            .ShouldBe("Visible answer.");
    }

    [Fact]
    public void StripAll_MultipleThinkBlocks_ShouldRemoveAll()
    {
        LlmMarkerParser.StripAll("<think>a</think>first <think>b</think>second")
            .ShouldBe("first second");
    }

    [Fact]
    public void StripAll_RemovesEmotionAndActionMarkersToo()
    {
        LlmMarkerParser.StripAll("<think>ignore</think>Hello <emotion:joy>everyone <action:wave>.")
            .ShouldBe("Hello everyone .");
    }

    [Fact]
    public void StripAll_TextWithoutMarkers_ShouldReturnUnchanged()
    {
        LlmMarkerParser.StripAll("Just a regular assistant reply.")
            .ShouldBe("Just a regular assistant reply.");
    }

    [Fact]
    public void StripAll_NullOrEmpty_ShouldReturnInputAsIs()
    {
        LlmMarkerParser.StripAll(string.Empty).ShouldBe(string.Empty);
        LlmMarkerParser.StripAll(null!).ShouldBeNull();
    }

    // ── Block-style `<action:think>…</action:think>` alias ─────────────

    [Fact]
    public void Parse_ActionThinkBlock_ShouldBeStripped_AndNotFireAction()
    {
        // A model that wraps reasoning in <action:think>…</action:think>
        // must NOT surface the reasoning or fire a "think" avatar action.
        var result = LlmMarkerParser.Parse(
            "<action:think>internal reasoning here</action:think>Visible answer.");

        result.CleanText.ShouldBe("Visible answer.");
        result.Actions.ShouldBeEmpty();
    }

    [Fact]
    public void StripAll_ActionThinkBlock_ShouldBeRemoved()
    {
        LlmMarkerParser.StripAll(
            "<action:think>noise\nmore noise</action:think>\nHello user.")
            .ShouldBe("Hello user.");
    }

    [Fact]
    public void Parse_OrphanClosingActionTag_ShouldBeStripped()
    {
        // A leaked closing tag (e.g. the opening already consumed in a
        // previous chunk) must not end up in the user-visible bubble.
        var result = LlmMarkerParser.Parse("Some text</action:think> continues.");

        result.CleanText.ShouldBe("Some text continues.");
        result.Actions.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_OrphanClosingEmotionTag_ShouldBeStripped()
    {
        var result = LlmMarkerParser.Parse("Hello</emotion:joy> world.");

        result.CleanText.ShouldBe("Hello world.");
        result.Emotions.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_ActionThinkBlock_StillExtractsSiblingMarkers()
    {
        // Block-stripping must not swallow neighbouring singleton markers.
        var result = LlmMarkerParser.Parse(
            "<action:think>ignored</action:think><action:wave>Hi <emotion:joy>!");

        result.CleanText.ShouldBe("Hi !");
        result.Actions.Count.ShouldBe(1);
        result.Actions[0].Action.ShouldBe("wave");
        result.Emotions.Count.ShouldBe(1);
        result.Emotions[0].Emotion.ShouldBe("joy");
    }

    [Fact]
    public void StripAll_MixedThinkAndActionThinkBlocks_ShouldRemoveBoth()
    {
        LlmMarkerParser.StripAll(
            "<think>A</think><action:think>B</action:think>C")
            .ShouldBe("C");
    }
}
