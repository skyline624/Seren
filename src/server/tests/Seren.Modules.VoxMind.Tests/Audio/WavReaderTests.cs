using Seren.Modules.VoxMind.Audio;
using Shouldly;
using Xunit;

namespace Seren.Modules.VoxMind.Tests.Audio;

public sealed class WavReaderTests
{
    [Fact]
    public void ReadPcm16_RoundTripsToleratesQuantization()
    {
        var input = new float[] { 0f, 0.25f, -0.25f, 0.5f };
        var wav = WavWriter.ToBytes(input, sampleRate: 16000);

        var (samples, rate) = WavReader.ReadPcm16(wav);

        rate.ShouldBe(16000);
        samples.Length.ShouldBe(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            samples[i].ShouldBe(input[i], tolerance: 1e-3f);
        }
    }

    [Fact]
    public void ReadPcm16_ReturnsEmptyForMalformedInput()
    {
        var (samples, rate) = WavReader.ReadPcm16([0, 1, 2]);

        samples.ShouldBeEmpty();
        rate.ShouldBe(0);
    }

    [Fact]
    public void Resample_NoOpWhenRatesMatch()
    {
        var samples = new float[] { 0.1f, 0.2f, 0.3f };
        var output = WavReader.Resample(samples, 16000, 16000);

        output.ShouldBeSameAs(samples);
    }

    [Fact]
    public void Resample_ProducesExpectedLengthForUpAndDown()
    {
        var samples = new float[100];
        WavReader.Resample(samples, 16000, 24000).Length.ShouldBe(150);
        WavReader.Resample(samples, 24000, 16000).Length.ShouldBe(66);
    }

    [Fact]
    public void Resample_NoOpOnEmptyInput()
    {
        var samples = Array.Empty<float>();
        var output = WavReader.Resample(samples, 16000, 24000);

        output.ShouldBeSameAs(samples);
    }
}
