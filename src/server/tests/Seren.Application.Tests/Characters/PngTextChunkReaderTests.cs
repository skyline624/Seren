using System.Buffers.Binary;
using System.Text;
using Seren.Application.Characters.Import;
using Seren.Contracts.Characters;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Characters;

/// <summary>
/// Direct tests of the low-level PNG chunk reader. Happy paths live in
/// <see cref="CharacterCardV3ParserTests"/>; this file focuses on the
/// malformed / malicious framing cases that must not crash or OOM.
/// </summary>
public sealed class PngTextChunkReaderTests
{
    [Fact]
    public void EnumerateTextChunks_NoSignature_Throws()
    {
        var bytes = Encoding.UTF8.GetBytes("not a png");

        var ex = Should.Throw<CharacterImportException>(() =>
            PngTextChunkReader.EnumerateTextChunks(bytes).ToList());
        ex.Code.ShouldBe(CharacterImportError.MalformedPng);
    }

    [Fact]
    public void EnumerateTextChunks_MultipleTextChunks_ReturnsAllInOrder()
    {
        var bytes = BuildPngWithChunks(
            ("tEXt", BuildTextData("author", "someone")),
            ("tEXt", BuildTextData("ccv3", "payload-1")),
            ("IEND", []));

        var chunks = PngTextChunkReader.EnumerateTextChunks(bytes).ToList();

        chunks.Count.ShouldBe(2);
        chunks[0].Keyword.ShouldBe("author");
        chunks[1].Keyword.ShouldBe("ccv3");
    }

    [Fact]
    public void EnumerateTextChunks_StopsAtIEnd()
    {
        // A tEXt chunk *after* IEND must be ignored — ensures the reader
        // honours the terminator and doesn't keep walking garbage bytes.
        var bytes = BuildPngWithChunks(
            ("tEXt", BuildTextData("ccv3", "real")),
            ("IEND", []),
            ("tEXt", BuildTextData("noise", "should-not-appear")));

        var chunks = PngTextChunkReader.EnumerateTextChunks(bytes).ToList();

        chunks.Count.ShouldBe(1);
        chunks[0].Keyword.ShouldBe("ccv3");
    }

    [Fact]
    public void EnumerateTextChunks_CorruptedLength_Throws()
    {
        // Build a PNG signature + a bogus chunk whose declared length
        // exceeds the remaining buffer. Must throw, not OOM, not hang.
        // Minimum 20 bytes (8 sig + 4 length + 4 type + 4 pad) so the
        // reader's `offset + 12 <= totalLength` pre-check lets us into
        // the loop where the real guard fires.
        var prefix = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, 0x7FFFFFFF);
        var typeBytes = Encoding.ASCII.GetBytes("tEXt");
        var pad = new byte[8]; // not enough for the declared 2 GB of data

        var bytes = prefix.Concat(lengthBytes).Concat(typeBytes).Concat(pad).ToArray();

        var ex = Should.Throw<CharacterImportException>(() =>
            PngTextChunkReader.EnumerateTextChunks(bytes).ToList());
        ex.Code.ShouldBe(CharacterImportError.MalformedPng);
    }

    [Fact]
    public void EnumerateTextChunks_SkipsUnknownChunks()
    {
        var bytes = BuildPngWithChunks(
            ("IHDR", new byte[13]), // 13 is IHDR's real size; content doesn't matter for us
            ("tEXt", BuildTextData("ccv3", "real")),
            ("IDAT", new byte[32]),
            ("IEND", []));

        var chunks = PngTextChunkReader.EnumerateTextChunks(bytes).ToList();

        chunks.Count.ShouldBe(1);
        chunks[0].Keyword.ShouldBe("ccv3");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static byte[] BuildTextData(string keyword, string text)
    {
        var k = Encoding.Latin1.GetBytes(keyword);
        var t = Encoding.Latin1.GetBytes(text);
        var buffer = new byte[k.Length + 1 + t.Length];
        Array.Copy(k, 0, buffer, 0, k.Length);
        buffer[k.Length] = 0;
        Array.Copy(t, 0, buffer, k.Length + 1, t.Length);
        return buffer;
    }

    private static byte[] BuildPngWithChunks(params (string Type, byte[] Data)[] chunks)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var lengthBuf = new byte[4];
        foreach (var (type, data) in chunks)
        {
            BinaryPrimitives.WriteUInt32BigEndian(lengthBuf, (uint)data.Length);
            ms.Write(lengthBuf);
            ms.Write(Encoding.ASCII.GetBytes(type));
            ms.Write(data);
            ms.Write(new byte[4]); // CRC stub
        }
        return ms.ToArray();
    }
}
