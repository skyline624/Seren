namespace Seren.Application.Chat.Attachments;

/// <summary>
/// Anti-spoof check: compare the first few bytes of a decoded attachment
/// against the well-known magic signature for its declared MIME type.
/// </summary>
/// <remarks>
/// <para>This is a best-effort defence, not a full format parser. We reject
/// obviously lying clients (a text file relabelled <c>image/png</c>) but
/// do not claim to validate that a byte stream is a complete, non-corrupt
/// image/PDF. Downstream consumers (PdfPig, the LLM provider) perform
/// deeper validation.</para>
/// <para>Text MIME types (<c>text/plain</c>, <c>text/markdown</c>,
/// <c>text/csv</c>) have no magic bytes — we accept them if the content
/// is valid UTF-8 (BOM optional), which is checked in the extractor.</para>
/// </remarks>
public static class AttachmentMagicBytes
{
    /// <summary>
    /// Returns <c>true</c> when the content's magic signature matches (or is
    /// not enforced for) the declared MIME type. Must already receive a
    /// MIME type that passed <see cref="AttachmentConstraints"/>' whitelist.
    /// </summary>
    public static bool Matches(string mimeType, ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            // Empty files are rejected at the size-cap check; getting here
            // means a caller bypassed the pipeline — be conservative.
            return false;
        }

        return mimeType switch
        {
            "image/jpeg" => StartsWith(content, [0xFF, 0xD8, 0xFF]),
            "image/png" => StartsWith(content, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            "image/gif" => StartsWith(content, "GIF87a"u8) || StartsWith(content, "GIF89a"u8),
            "image/webp" => content.Length >= 12
                && StartsWith(content, "RIFF"u8)
                && content[8..12].SequenceEqual("WEBP"u8),
            // HEIC / HEIF sit in an ISO BMFF container; bytes 4-7 are
            // always "ftyp" and bytes 8-11 carry the brand. Accept every
            // brand OpenClaw's upstream also accepts.
            "image/heic" or "image/heif" => content.Length >= 12
                && content[4..8].SequenceEqual("ftyp"u8)
                && IsHeifBrand(content[8..12]),
            "application/pdf" => StartsWith(content, "%PDF-"u8),
            // Text MIME types: no magic signature. UTF-8 validity is checked
            // during extraction (PlainTextExtractor), not here.
            "text/plain" or "text/markdown" or "text/csv" => true,
            _ => false,
        };
    }

    private static bool StartsWith(ReadOnlySpan<byte> content, ReadOnlySpan<byte> prefix)
        => content.Length >= prefix.Length && content[..prefix.Length].SequenceEqual(prefix);

    private static bool IsHeifBrand(ReadOnlySpan<byte> brand)
    {
        // Most common brands — full list at
        // https://www.iana.org/assignments/media-types/image/heic
        return brand.SequenceEqual("heic"u8)
            || brand.SequenceEqual("heix"u8)
            || brand.SequenceEqual("mif1"u8)
            || brand.SequenceEqual("msf1"u8)
            || brand.SequenceEqual("heim"u8)
            || brand.SequenceEqual("heis"u8)
            || brand.SequenceEqual("hevc"u8)
            || brand.SequenceEqual("hevx"u8);
    }
}
