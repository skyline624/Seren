using Seren.Infrastructure.Audio;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.Audio;

public sealed class SyntheticVisemeGeneratorTests
{
    [Fact]
    public void GenerateFromText_WithEmptyString_ReturnsEmptyTrack()
    {
        var track = SyntheticVisemeGenerator.GenerateFromText(string.Empty);
        track.ShouldBeEmpty();
    }

    [Fact]
    public void GenerateFromText_WithSingleVowel_EmitsOpenMouthViseme()
    {
        var track = SyntheticVisemeGenerator.GenerateFromText("A", totalDurationMs: 100);

        track.Length.ShouldBe(1);
        track[0].Viseme.ShouldBe("aa");
        track[0].Weight.ShouldBe(0.8f);
        track[0].StartTime.ShouldBe(0f);
    }

    [Fact]
    public void GenerateFromText_SpacesFramesEvenly()
    {
        var track = SyntheticVisemeGenerator.GenerateFromText("aei", totalDurationMs: 300);

        track.Length.ShouldBe(3);
        // 100 ms each → 0 s, 0.1 s, 0.2 s
        track[0].StartTime.ShouldBe(0f, 1e-4);
        track[1].StartTime.ShouldBe(0.1f, 1e-4);
        track[2].StartTime.ShouldBe(0.2f, 1e-4);
        track.ShouldAllBe(f => f.Duration > 0);
    }

    [Fact]
    public void GenerateFromText_MapsConsonantsToSilence()
    {
        var track = SyntheticVisemeGenerator.GenerateFromText("bt", totalDurationMs: 200);

        track.Length.ShouldBe(2);
        track[0].Viseme.ShouldBe("-");
        track[0].Weight.ShouldBe(0f);
        track[1].Viseme.ShouldBe("-");
    }

    [Fact]
    public void GenerateFromText_CoversAllCanonicalVowels()
    {
        var track = SyntheticVisemeGenerator.GenerateFromText("aeiou");

        var visemes = track.Select(f => f.Viseme).ToArray();
        visemes.ShouldContain("aa");
        visemes.ShouldContain("ee");
        visemes.ShouldContain("ih");
        visemes.ShouldContain("oh");
        visemes.ShouldContain("ou");
    }

    [Fact]
    public void GenerateFromText_WithNullDuration_FallsBackToPerCharDefault()
    {
        var track = SyntheticVisemeGenerator.GenerateFromText("ab");

        track.Length.ShouldBe(2);
        // Default = 70 ms per char → total 140 ms, each 70 ms
        track[1].StartTime.ShouldBe(0.07f, 1e-3);
    }
}
