using System.Buffers.Binary;
using System.Text;

namespace Seren.Application.Tests.Characters;

/// <summary>
/// Small helpers that build well-formed Character Card v3 payloads
/// programmatically — avoids checking binary fixtures into the repo and
/// keeps test intent visible. Each helper returns a raw byte array ready
/// to feed into <c>ICharacterCardParser.Parse</c>.
/// </summary>
internal static class CharacterCardTestFixtures
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Build a minimal PNG consisting of signature + one tEXt chunk +
    /// IEND. Structurally incomplete (no IHDR/IDAT) but our tEXt reader
    /// is chunk-agnostic, so this is all we need for testing.
    /// </summary>
    public static byte[] BuildPngWithTextChunk(string keyword, string text)
    {
        using var ms = new MemoryStream();
        ms.Write(PngSignature);

        // tEXt chunk
        var textBytes = Encoding.Latin1.GetBytes(text);
        var keywordBytes = Encoding.Latin1.GetBytes(keyword);
        var dataLength = keywordBytes.Length + 1 + textBytes.Length;
        WriteUInt32BigEndian(ms, (uint)dataLength);
        ms.Write("tEXt"u8);
        ms.Write(keywordBytes);
        ms.WriteByte(0);
        ms.Write(textBytes);
        WriteUInt32BigEndian(ms, 0); // CRC (ignored by our reader)

        // IEND chunk (length 0, type "IEND", CRC 0)
        WriteUInt32BigEndian(ms, 0);
        ms.Write("IEND"u8);
        WriteUInt32BigEndian(ms, 0);

        return ms.ToArray();
    }

    /// <summary>
    /// Build a CCv3 card payload (JSON) with sensible defaults, letting
    /// callers override fields by passing a JSON fragment for
    /// <paramref name="dataFieldsJson"/> — e.g. <c>"system_prompt": "..."</c>.
    /// </summary>
    public static byte[] BuildJsonCard(
        string name = "Cortana",
        string spec = "chara_card_v3",
        string? dataFieldsJson = null)
    {
        var data = dataFieldsJson ?? $"\"name\": {Quote(name)}, \"description\": \"An AI construct.\"";
        if (dataFieldsJson is not null && !dataFieldsJson.Contains("\"name\""))
        {
            data = $"\"name\": {Quote(name)}, {dataFieldsJson}";
        }
        var json = $"{{ \"spec\": {Quote(spec)}, \"spec_version\": \"3.0\", \"data\": {{ {data} }} }}";
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Build a CCv3 PNG: wraps <see cref="BuildJsonCard"/> in base64 and
    /// embeds it as a <c>ccv3</c> tEXt chunk (or <c>chara</c> for v2).
    /// </summary>
    public static byte[] BuildPngCard(
        string name = "Cortana",
        string spec = "chara_card_v3",
        string? dataFieldsJson = null)
    {
        var json = BuildJsonCard(name, spec, dataFieldsJson);
        var base64 = Convert.ToBase64String(json);
        var keyword = spec == "chara_card_v2" ? "chara" : "ccv3";
        return BuildPngWithTextChunk(keyword, base64);
    }

    private static void WriteUInt32BigEndian(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static string Quote(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
