using Seren.Application.Chat.Attachments;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Chat.Attachments;

public sealed class AttachmentTextExtractorRegistryTests
{
    [Fact]
    public void Resolve_PdfMime_ReturnsPdfExtractor()
    {
        var registry = new AttachmentTextExtractorRegistry([
            new PlainTextExtractor(),
            new PdfTextExtractor(),
        ]);

        var resolved = registry.Resolve("application/pdf");

        resolved.ShouldBeOfType<PdfTextExtractor>();
    }

    [Fact]
    public void Resolve_TextPlain_ReturnsPlainExtractor()
    {
        var registry = new AttachmentTextExtractorRegistry([
            new PdfTextExtractor(),
            new PlainTextExtractor(),
        ]);

        var resolved = registry.Resolve("text/plain");

        resolved.ShouldBeOfType<PlainTextExtractor>();
    }

    [Fact]
    public void Resolve_UnknownMime_ReturnsNull()
    {
        var registry = new AttachmentTextExtractorRegistry([
            new PlainTextExtractor(),
            new PdfTextExtractor(),
        ]);

        registry.Resolve("application/x-doom-wad").ShouldBeNull();
    }

    [Fact]
    public void Resolve_EmptyMime_ReturnsNull()
    {
        var registry = new AttachmentTextExtractorRegistry([new PlainTextExtractor()]);

        registry.Resolve(string.Empty).ShouldBeNull();
    }
}
