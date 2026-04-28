using Shouldly;
using Xunit;

namespace Seren.Modules.VoxMind.Tests;

public sealed class VoxMindOptionsValidatorTests
{
    private readonly VoxMindOptionsValidator _validator = new();

    [Fact]
    public void Defaults_AreValid()
    {
        var result = _validator.Validate(new VoxMindOptions());
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("fra")]
    [InlineData("french")]
    public void Rejects_DefaultLanguage_NotIso6391(string lang)
    {
        var opts = new VoxMindOptions { DefaultLanguage = lang };
        var result = _validator.Validate(opts);
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(VoxMindOptions.DefaultLanguage));
    }

    [Fact]
    public void Rejects_NonPositive_FlowMatchingSteps()
    {
        var opts = new VoxMindOptions();
        opts.Tts.FlowMatchingSteps = 0;
        var result = _validator.Validate(opts);
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName.EndsWith("FlowMatchingSteps", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_NonPositive_CacheCapacity()
    {
        var opts = new VoxMindOptions();
        opts.Tts.CacheCapacity = 0;
        var result = _validator.Validate(opts);
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Rejects_NonPositive_MaxChunkSeconds()
    {
        var opts = new VoxMindOptions();
        opts.Stt.MaxChunkSeconds = 0;
        var result = _validator.Validate(opts);
        result.IsValid.ShouldBeFalse();
    }
}
