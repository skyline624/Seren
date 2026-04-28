using System.Buffers.Binary;
using Seren.Modules.VoxMind.Audio;
using Shouldly;
using Xunit;

namespace Seren.Modules.VoxMind.Tests.Audio;

public sealed class WavWriterTests
{
    [Fact]
    public void ToBytes_EmitsValidRiffHeader()
    {
        var pcm = new float[] { 0f, 0.5f, -0.5f, 1f, -1f };
        var wav = WavWriter.ToBytes(pcm, sampleRate: 24000);

        wav.Length.ShouldBe(44 + pcm.Length * 2);
        wav[0].ShouldBe((byte)'R');
        wav[1].ShouldBe((byte)'I');
        wav[2].ShouldBe((byte)'F');
        wav[3].ShouldBe((byte)'F');
        wav[8].ShouldBe((byte)'W');
        wav[36].ShouldBe((byte)'d');
        wav[37].ShouldBe((byte)'a');
        wav[38].ShouldBe((byte)'t');
        wav[39].ShouldBe((byte)'a');

        BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24, 4)).ShouldBe(24000);
        BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22, 2)).ShouldBe<short>(1);
    }

    [Fact]
    public void Clamping_PreventsOverflowOnExtremeSamples()
    {
        var input = new float[] { 2f, -2f }; // outside [-1, 1]
        var wav = WavWriter.ToBytes(input, sampleRate: 16000);
        var (samples, _) = WavReader.ReadPcm16(wav);

        samples[0].ShouldBe(0.999f, tolerance: 1e-2f);
        samples[1].ShouldBe(-0.999f, tolerance: 1e-2f);
    }
}
