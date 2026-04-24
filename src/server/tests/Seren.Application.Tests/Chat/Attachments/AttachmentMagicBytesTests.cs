using Seren.Application.Chat.Attachments;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Chat.Attachments;

public sealed class AttachmentMagicBytesTests
{
    [Fact]
    public void Matches_Jpeg_Accepts()
        => AttachmentMagicBytes.Matches("image/jpeg", AttachmentFixtures.MinimalJpeg()).ShouldBeTrue();

    [Fact]
    public void Matches_Png_Accepts()
        => AttachmentMagicBytes.Matches("image/png", AttachmentFixtures.MinimalPng()).ShouldBeTrue();

    [Fact]
    public void Matches_Gif_Accepts()
        => AttachmentMagicBytes.Matches("image/gif", AttachmentFixtures.MinimalGif()).ShouldBeTrue();

    [Fact]
    public void Matches_Webp_Accepts()
        => AttachmentMagicBytes.Matches("image/webp", AttachmentFixtures.MinimalWebp()).ShouldBeTrue();

    [Fact]
    public void Matches_Heic_Accepts()
        => AttachmentMagicBytes.Matches("image/heic", AttachmentFixtures.MinimalHeic()).ShouldBeTrue();

    [Fact]
    public void Matches_Pdf_Accepts()
    {
        var pdf = AttachmentFixtures.MinimalPdf("x");
        AttachmentMagicBytes.Matches("application/pdf", pdf).ShouldBeTrue();
    }

    [Fact]
    public void Matches_TextPlainSkipsMagic()
    {
        var bytes = "hello"u8.ToArray();
        AttachmentMagicBytes.Matches("text/plain", bytes).ShouldBeTrue();
    }

    [Fact]
    public void Matches_JpegLabelButPngBytes_Rejects()
    {
        var png = AttachmentFixtures.MinimalPng();
        AttachmentMagicBytes.Matches("image/jpeg", png).ShouldBeFalse();
    }

    [Fact]
    public void Matches_Empty_Rejects()
    {
        AttachmentMagicBytes.Matches("image/jpeg", ReadOnlySpan<byte>.Empty).ShouldBeFalse();
    }
}
