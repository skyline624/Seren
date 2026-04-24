using System.Text;
using Seren.Application.Chat.Attachments;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Chat.Attachments;

public sealed class PlainTextExtractorTests
{
    private readonly PlainTextExtractor _extractor = new();

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/markdown")]
    [InlineData("text/csv")]
    public void CanHandle_AcceptsSupportedMimeTypes(string mime)
        => _extractor.CanHandle(mime).ShouldBeTrue();

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("")]
    public void CanHandle_RejectsOthers(string mime)
        => _extractor.CanHandle(mime).ShouldBeFalse();

    [Fact]
    public async Task ExtractAsync_Utf8WithoutBom_ReturnsText()
    {
        var bytes = Encoding.UTF8.GetBytes("hello world");
        var result = await _extractor.ExtractAsync(bytes, "n.txt", TestContext.Current.CancellationToken);
        result.ShouldBe("hello world");
    }

    [Fact]
    public async Task ExtractAsync_Utf8WithBom_StripsBom()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var text = Encoding.UTF8.GetBytes("with bom");
        var bytes = new byte[bom.Length + text.Length];
        bom.CopyTo(bytes, 0);
        text.CopyTo(bytes, bom.Length);

        var result = await _extractor.ExtractAsync(bytes, "n.txt", TestContext.Current.CancellationToken);

        result.ShouldBe("with bom");
        result.ShouldNotStartWith("﻿");
    }

    [Fact]
    public async Task ExtractAsync_InvalidUtf8_ThrowsExtractionException()
    {
        // 0xFF is never a valid UTF-8 lead byte.
        var bytes = new byte[] { 0xFF, 0xFE, 0xFD };

        await Should.ThrowAsync<AttachmentExtractionException>(async () =>
            await _extractor.ExtractAsync(bytes, "broken.txt", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExtractAsync_OversizedText_TruncatesWithMarker()
    {
        var text = new string('a', AttachmentConstraints.MaxDocumentTextCharacters + 500);
        var bytes = Encoding.UTF8.GetBytes(text);

        var result = await _extractor.ExtractAsync(bytes, "big.txt", TestContext.Current.CancellationToken);

        result.Length.ShouldBeGreaterThan(AttachmentConstraints.MaxDocumentTextCharacters);
        result.ShouldContain("[…truncated at");
    }
}
