using Seren.Application.Chat.Attachments;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Chat.Attachments;

public sealed class PdfTextExtractorTests
{
    private readonly PdfTextExtractor _extractor = new();

    [Fact]
    public void CanHandle_AcceptsPdf()
        => _extractor.CanHandle("application/pdf").ShouldBeTrue();

    [Theory]
    [InlineData("text/plain")]
    [InlineData("image/jpeg")]
    [InlineData("")]
    public void CanHandle_RejectsOthers(string mime)
        => _extractor.CanHandle(mime).ShouldBeFalse();

    [Fact]
    public async Task ExtractAsync_SimplePdf_ReturnsText()
    {
        var pdf = AttachmentFixtures.MinimalPdf("The quick brown fox");

        var result = await _extractor.ExtractAsync(pdf, "doc.pdf", TestContext.Current.CancellationToken);

        result.ShouldContain("quick brown fox");
    }

    [Fact]
    public async Task ExtractAsync_MultiPage_NumbersEachPage()
    {
        var pdf = AttachmentFixtures.MinimalPdf("page content", pages: 3);

        var result = await _extractor.ExtractAsync(pdf, "multi.pdf", TestContext.Current.CancellationToken);

        result.ShouldContain("[Page 1]");
        result.ShouldContain("[Page 2]");
        result.ShouldContain("[Page 3]");
    }

    [Fact]
    public async Task ExtractAsync_CorruptedPdf_ThrowsExtractionException()
    {
        // "%PDF-" header then junk — PdfPig will reject.
        var bytes = new byte[64];
        "%PDF-1.4\n"u8.CopyTo(bytes);
        for (var i = 9; i < bytes.Length; i++)
        {
            bytes[i] = 0xFF;
        }

        await Should.ThrowAsync<AttachmentExtractionException>(async () =>
            await _extractor.ExtractAsync(bytes, "corrupt.pdf", TestContext.Current.CancellationToken));
    }
}
