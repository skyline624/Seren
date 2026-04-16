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
}
