using System.Buffers.Binary;

namespace Seren.Modules.VoxMind.Audio;

/// <summary>
/// Minimal WAV PCM 16-bit reader + nearest-neighbour resampler.
/// </summary>
/// <remarks>
/// Counterpart to <see cref="WavWriter"/>. Kept in a separate static class so
/// callers that only need to encode (TTS output path) don't carry the parser
/// surface, and vice-versa for callers that only decode (voice-prompt input
/// path).
/// </remarks>
public static class WavReader
{
    /// <summary>
    /// Reads a PCM 16-bit WAV blob into mono float32 samples normalised to [-1, 1].
    /// Stereo / multi-channel input is mixed down to mono.
    /// </summary>
    /// <returns>(samples, sourceSampleRate). Returns an empty array for malformed input.</returns>
    public static (float[] Samples, int SampleRate) ReadPcm16(byte[] wav)
    {
        ArgumentNullException.ThrowIfNull(wav);
        if (wav.Length < 44)
        {
            return (Array.Empty<float>(), 0);
        }

        int sampleRate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24, 4));
        short channels = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22, 2));
        short bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34, 2));

        int dataOffset = 44;
        for (int i = 12; i < Math.Min(wav.Length - 8, 256); i++)
        {
            if (wav[i] == 'd' && wav[i + 1] == 'a' && wav[i + 2] == 't' && wav[i + 3] == 'a')
            {
                dataOffset = i + 8;
                break;
            }
        }

        if (bitsPerSample != 16)
        {
            throw new NotSupportedException($"WAV: only PCM 16-bit is supported, got {bitsPerSample}.");
        }

        int bytesPerFrame = channels * 2;
        int totalFrames = (wav.Length - dataOffset) / bytesPerFrame;
        if (totalFrames <= 0)
        {
            return (Array.Empty<float>(), sampleRate);
        }

        var mono = new float[totalFrames];
        for (int i = 0; i < totalFrames; i++)
        {
            int sum = 0;
            for (int c = 0; c < channels; c++)
            {
                sum += BinaryPrimitives.ReadInt16LittleEndian(
                    wav.AsSpan(dataOffset + i * bytesPerFrame + c * 2, 2));
            }

            mono[i] = sum / channels / 32768.0f;
        }

        return (mono, sampleRate);
    }

    /// <summary>
    /// Naive nearest-neighbour resampler. Good enough for short voice prompts;
    /// callers needing better quality should pre-resample upstream (e.g. via FFmpeg).
    /// </summary>
    public static float[] Resample(float[] samples, int sourceRate, int targetRate)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (sourceRate == targetRate || samples.Length == 0)
        {
            return samples;
        }

        int targetLen = (int)((long)samples.Length * targetRate / sourceRate);
        var resampled = new float[targetLen];
        for (int i = 0; i < targetLen; i++)
        {
            int src = (int)((long)i * sourceRate / targetRate);
            if (src >= samples.Length)
            {
                src = samples.Length - 1;
            }

            resampled[i] = samples[src];
        }
        return resampled;
    }
}
