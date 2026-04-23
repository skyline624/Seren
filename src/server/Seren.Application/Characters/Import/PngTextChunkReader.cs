using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Seren.Contracts.Characters;

namespace Seren.Application.Characters.Import;

/// <summary>
/// Reads <c>tEXt</c> and <c>zTXt</c> textual metadata chunks out of a
/// PNG (or APNG — same chunk format) byte buffer. Single responsibility:
/// PNG bytes → (keyword, text) sequence. Shared with a future CCv3
/// exporter (DRY).
/// </summary>
/// <remarks>
/// <para>
/// Safe by construction against malformed files: every chunk length is
/// bounded against the remaining buffer before any allocation, so a
/// truncated file or a chunk declaring <c>length = int.MaxValue</c>
/// cannot trigger an <see cref="OutOfMemoryException"/>. CRC footers
/// are skipped rather than validated — cards circulate on disk, not
/// over the wire, and a bit-flipped CRC on an otherwise-good chunk
/// would block legitimate imports for no real security gain.
/// </para>
/// <para>
/// PNG signature ref: <a href="https://www.w3.org/TR/png-3/">W3C PNG
/// spec</a>. Chunk layout : <c>[length:4 BE][type:4][data:length][crc:4]</c>.
/// <c>tEXt</c> data is <c>keyword\0text</c> (keyword is Latin-1,
/// 1–79 bytes, no embedded zeros beyond the separator). <c>zTXt</c> adds
/// a compression-method byte after the separator and zlib-deflates the
/// text — we accept method <c>0</c> (zlib/deflate) and skip anything
/// else with a warning-free continue.
/// </para>
/// </remarks>
public static class PngTextChunkReader
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Returns <c>true</c> when <paramref name="bytes"/> begins with the PNG magic signature.</summary>
    public static bool IsPng(ReadOnlySpan<byte> bytes)
        => bytes.Length >= PngSignature.Length && bytes[..PngSignature.Length].SequenceEqual(PngSignature);

    /// <summary>
    /// Enumerate every textual chunk. Stops at <c>IEND</c> or end of
    /// buffer. Throws <see cref="CharacterImportException"/> with code
    /// <see cref="CharacterImportError.MalformedPng"/> when the chunk
    /// framing is inconsistent — but unknown chunk types are simply
    /// skipped (forward-compat: PNG extensions don't break us).
    /// </summary>
    public static IEnumerable<(string Keyword, string Text)> EnumerateTextChunks(ReadOnlyMemory<byte> bytes)
    {
        if (!IsPng(bytes.Span))
        {
            throw new CharacterImportException(
                CharacterImportError.MalformedPng,
                "File is missing the PNG signature.",
                details: "first 8 bytes do not match 89 50 4E 47 0D 0A 1A 0A");
        }

        var offset = PngSignature.Length;
        var totalLength = bytes.Length;

        // Iterator methods cannot hold a `ReadOnlySpan<byte>` across a
        // `yield`, so we keep `ReadOnlyMemory<byte>` on the stack and
        // call `.Span` inside each non-yielding helper.
        while (offset + 12 <= totalLength)
        {
            var (length, type) = ReadChunkHeader(bytes, offset);
            offset += 8;

            if (length > int.MaxValue || (long)offset + length + 4 > totalLength)
            {
                throw new CharacterImportException(
                    CharacterImportError.MalformedPng,
                    "PNG chunk length exceeds remaining buffer.",
                    details: $"chunk length = {length} at offset {offset}");
            }

            var dataMemory = bytes.Slice(offset, (int)length);
            offset += (int)length + 4; // +4 for CRC

            switch (type)
            {
                case "tEXt":
                    if (TryParseTextChunk(dataMemory.Span, out var textKeyword, out var text))
                    {
                        yield return (textKeyword, text);
                    }
                    break;

                case "zTXt":
                    if (TryParseCompressedTextChunk(dataMemory.Span, out var zKeyword, out var zText))
                    {
                        yield return (zKeyword, zText);
                    }
                    break;

                case "IEND":
                    yield break;

                default:
                    // Ignore unknown chunks (IDAT, IHDR, iTXt, animation…).
                    break;
            }
        }
    }

    /// <summary>
    /// Reads the 4-byte length + 4-byte type out of the PNG chunk header
    /// at <paramref name="offset"/>. Factored out so the caller (an
    /// iterator method) never touches a <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    private static (uint Length, string Type) ReadChunkHeader(ReadOnlyMemory<byte> bytes, int offset)
    {
        var span = bytes.Span;
        var length = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset, 4));
        var type = Encoding.ASCII.GetString(span.Slice(offset + 4, 4));
        return (length, type);
    }

    private static bool TryParseTextChunk(ReadOnlySpan<byte> data, out string keyword, out string text)
    {
        var zero = data.IndexOf((byte)0);
        if (zero <= 0 || zero >= data.Length - 1)
        {
            keyword = string.Empty;
            text = string.Empty;
            return false;
        }

        keyword = Encoding.Latin1.GetString(data[..zero]);
        text = Encoding.Latin1.GetString(data[(zero + 1)..]);
        return true;
    }

    private static bool TryParseCompressedTextChunk(ReadOnlySpan<byte> data, out string keyword, out string text)
    {
        keyword = string.Empty;
        text = string.Empty;

        var zero = data.IndexOf((byte)0);
        if (zero <= 0 || zero >= data.Length - 2)
        {
            return false;
        }

        keyword = Encoding.Latin1.GetString(data[..zero]);

        // Byte immediately after the null is the compression method.
        // PNG only defines method 0 (deflate); anything else → skip.
        var compressionMethod = data[zero + 1];
        if (compressionMethod != 0)
        {
            return false;
        }

        var compressed = data[(zero + 2)..].ToArray();
        try
        {
            using var input = new MemoryStream(compressed);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            text = Encoding.Latin1.GetString(output.ToArray());
            return true;
        }
#pragma warning disable CA1031 // Any decompression failure means "skip this chunk" — not fatal to the parse.
        catch (Exception)
        {
            return false;
        }
#pragma warning restore CA1031
    }
}
