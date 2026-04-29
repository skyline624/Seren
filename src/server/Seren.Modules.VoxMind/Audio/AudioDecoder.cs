using System.Buffers.Binary;
using FFMpegCore;
using FFMpegCore.Pipes;

namespace Seren.Modules.VoxMind.Audio;

/// <summary>
/// Shared audio decoding helper for the STT engines. Both Parakeet and
/// Whisper expect PCM float32 mono at 16 kHz; centralising the FFmpeg
/// detour + the in-place WAV fast-path keeps both paths consistent and
/// lets us re-use the (small) test surface.
/// </summary>
public static class AudioDecoder
{
    /// <summary>
    /// Decodes any input audio blob to PCM float32 16 kHz mono.
    /// Pure-WAV inputs skip the FFmpeg detour and are parsed in-process
    /// (cheaper, avoids a process-spawn for the common UI path).
    /// </summary>
    public static async Task<float[]> DecodeToFloat32Async(
        byte[] audioData, string format, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audioData);
        ArgumentNullException.ThrowIfNull(format);

        if (audioData.Length == 0)
        {
            return Array.Empty<float>();
        }

        var fmt = format.Trim().ToLowerInvariant();
        if (fmt is "wav" or "wave")
        {
            return ConvertWavToFloat32(audioData);
        }

        using var input = new MemoryStream(audioData, writable: false);
        using var output = new MemoryStream();

        await FFMpegArguments
            .FromPipeInput(new StreamPipeSource(input))
            .OutputToPipe(new StreamPipeSink(output), opts => opts
                .WithAudioSamplingRate(16000)
                .WithCustomArgument("-ac 1 -acodec pcm_s16le")
                .ForceFormat("wav"))
            .CancellableThrough(ct)
            .ProcessAsynchronously(throwOnError: true).ConfigureAwait(false);

        return ConvertWavToFloat32(output.ToArray());
    }

    /// <summary>
    /// Converts a PCM-16 WAV blob into float32 samples normalised to [-1, 1].
    /// Tolerant to extra chunks before "data" (LIST/JUNK).
    /// </summary>
    private static float[] ConvertWavToFloat32(byte[] wav)
    {
        if (wav.Length < 44)
        {
            return Array.Empty<float>();
        }

        int dataOffset = 44;
        for (int i = 12; i < Math.Min(wav.Length - 8, 1024); i++)
        {
            if (wav[i] == 'd' && wav[i + 1] == 'a' && wav[i + 2] == 't' && wav[i + 3] == 'a')
            {
                dataOffset = i + 8;
                break;
            }
        }

        int nSamples = (wav.Length - dataOffset) / 2;
        if (nSamples <= 0)
        {
            return Array.Empty<float>();
        }

        var samples = new float[nSamples];
        for (int i = 0; i < nSamples; i++)
        {
            short s = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(dataOffset + i * 2, 2));
            samples[i] = s / 32768.0f;
        }
        return samples;
    }
}
