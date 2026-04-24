using System.Text;
using Seren.Contracts.Events.Payloads;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Seren.Application.Tests.Chat.Attachments;

/// <summary>
/// Small deterministic byte arrays + helpers to build validated
/// attachment payloads in tests without coupling to binary fixture files.
/// </summary>
internal static class AttachmentFixtures
{
    /// <summary>JPEG SOI + APP0 header + padding. 32 bytes — valid magic, minimal body.</summary>
    public static byte[] MinimalJpeg()
    {
        var bytes = new byte[]
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F',
            (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x48,
            0x00, 0x48, 0x00, 0x00, 0xFF, 0xD9, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        return bytes;
    }

    /// <summary>PNG signature + IHDR stub. 16 bytes of signature + filler.</summary>
    public static byte[] MinimalPng()
    {
        return
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        ];
    }

    /// <summary>GIF87a signature + logical screen descriptor.</summary>
    public static byte[] MinimalGif()
    {
        var header = Encoding.ASCII.GetBytes("GIF87a");
        var rest = new byte[] { 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x3B };
        return [.. header, .. rest];
    }

    /// <summary>RIFF...WEBP container. 20 bytes.</summary>
    public static byte[] MinimalWebp()
    {
        var riff = Encoding.ASCII.GetBytes("RIFF");
        var size = new byte[] { 0x20, 0x00, 0x00, 0x00 };
        var webp = Encoding.ASCII.GetBytes("WEBP");
        var vp8 = Encoding.ASCII.GetBytes("VP8 ");
        var padding = new byte[] { 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        return [.. riff, .. size, .. webp, .. vp8, .. padding];
    }

    /// <summary>HEIC: ISO BMFF container with ftyp box + heic brand.</summary>
    public static byte[] MinimalHeic()
    {
        var boxSize = new byte[] { 0x00, 0x00, 0x00, 0x20 };
        var ftyp = Encoding.ASCII.GetBytes("ftyp");
        var brand = Encoding.ASCII.GetBytes("heic");
        var minorVersion = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        var compat = Encoding.ASCII.GetBytes("heicmif1");
        var filler = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        return [.. boxSize, .. ftyp, .. brand, .. minorVersion, .. compat, .. filler];
    }

    /// <summary>Build a minimal single-page PDF with the given text.</summary>
    public static byte[] MinimalPdf(string text, int pages = 1)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.TimesRoman);
        for (var i = 0; i < pages; i++)
        {
            var page = builder.AddPage(595, 842);
            page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        }
        return builder.Build();
    }

    /// <summary>Turn a raw byte array into a base64-backed <see cref="ChatAttachmentDto"/>.</summary>
    public static ChatAttachmentDto AsDto(string mimeType, string fileName, byte[] content) => new()
    {
        MimeType = mimeType,
        FileName = fileName,
        ByteSize = content.LongLength,
        Content = Convert.ToBase64String(content),
    };
}
