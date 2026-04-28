using System.Buffers.Binary;

namespace Seren.Modules.VoxMind.Audio;

/// <summary>
/// Minimal WAV PCM 16-bit writer.
/// </summary>
/// <remarks>
/// We need a cross-platform WAV writer for the TTS module output and the RIFF/
/// WAVE PCM format is trivial — no need to pull in a full audio library.
/// Read-side concerns (parsing, resampling) live in <see cref="WavReader"/>.
/// </remarks>
public static class WavWriter
{
    /// <summary>
    /// Writes a PCM 16-bit WAV stream to <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">Output stream (left open).</param>
    /// <param name="pcm">Float32 samples normalised in [-1, 1].</param>
    /// <param name="sampleRate">Sample rate in Hz (24000 for F5-TTS).</param>
    /// <param name="channels">Channel count (1 for Seren's mono TTS).</param>
    public static void Write(
        Stream destination,
        ReadOnlySpan<float> pcm,
        int sampleRate,
        int channels = 1)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        const int bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;
        int dataSize = pcm.Length * 2;
        int riffSize = 36 + dataSize;

        Span<byte> header = stackalloc byte[44];
        header[0] = (byte)'R'; header[1] = (byte)'I'; header[2] = (byte)'F'; header[3] = (byte)'F';
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(4, 4), riffSize);
        header[8] = (byte)'W'; header[9] = (byte)'A'; header[10] = (byte)'V'; header[11] = (byte)'E';
        header[12] = (byte)'f'; header[13] = (byte)'m'; header[14] = (byte)'t'; header[15] = (byte)' ';
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(22, 2), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(32, 2), (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(header.Slice(34, 2), bitsPerSample);
        header[36] = (byte)'d'; header[37] = (byte)'a'; header[38] = (byte)'t'; header[39] = (byte)'a';
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(40, 4), dataSize);

        destination.Write(header);

        Span<byte> sampleBytes = stackalloc byte[2];
        foreach (var sample in pcm)
        {
            float clamped = Math.Clamp(sample, -1f, 1f);
            short s16 = (short)(clamped * 32767f);
            BinaryPrimitives.WriteInt16LittleEndian(sampleBytes, s16);
            destination.Write(sampleBytes);
        }
    }

    /// <summary>
    /// In-memory variant — returns the full WAV blob. Convenient for streaming
    /// over HTTP/WS in a single chunk.
    /// </summary>
    public static byte[] ToBytes(ReadOnlySpan<float> pcm, int sampleRate, int channels = 1)
    {
        using var ms = new MemoryStream(44 + pcm.Length * 2);
        Write(ms, pcm, sampleRate, channels);
        return ms.ToArray();
    }
}
