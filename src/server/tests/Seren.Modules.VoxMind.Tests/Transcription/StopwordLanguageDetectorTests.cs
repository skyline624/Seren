using Seren.Modules.VoxMind.Transcription;
using Shouldly;
using Xunit;

namespace Seren.Modules.VoxMind.Tests.Transcription;

public sealed class StopwordLanguageDetectorTests
{
    private readonly StopwordLanguageDetector _detector = new();

    [Fact]
    public void DetectsFrenchOnTypicalSentence()
    {
        var lang = _detector.DetectLanguage(
            "Le chat est sur le tapis et il dort dans la maison de son maître.");
        lang.ShouldBe("fr");
    }

    [Fact]
    public void DetectsEnglishOnTypicalSentence()
    {
        var lang = _detector.DetectLanguage(
            "The cat is on the mat and it sleeps in the house of its owner.");
        lang.ShouldBe("en");
    }

    [Fact]
    public void DetectsSpanishOnTypicalSentence()
    {
        var lang = _detector.DetectLanguage(
            "El gato está sobre la alfombra y duerme en la casa de su dueño.");
        lang.ShouldBe("es");
    }

    [Fact]
    public void ReturnsUndForEmptyInput()
    {
        _detector.DetectLanguage(string.Empty).ShouldBe("und");
        _detector.DetectLanguage("   ").ShouldBe("und");
    }

    [Fact]
    public void ReturnsUndForVeryShortInput()
    {
        // Below MinUsefulTokens = 3.
        _detector.DetectLanguage("ok").ShouldBe("und");
    }

    [Fact]
    public void ReturnsUndWhenScoreBelowThreshold()
    {
        // No European stopwords overlap → score 0.
        _detector.DetectLanguage("xyz qrs tuv pqr stu vwx").ShouldBe("und");
    }

    [Fact]
    public void RestrictsCandidatesWhenBoundList()
    {
        // Spanish text but candidates restricted to fr/en — picks the closer
        // of the two even though confidence will be lower.
        var lang = _detector.DetectLanguage(
            "el gato está sobre la alfombra y duerme en la casa", new[] { "fr", "en" });
        // "la", "el", "en" are all stopwords in fr and a couple in en.
        // Whatever wins, it should not be "es" since it was excluded.
        lang.ShouldNotBe("es");
    }
}
