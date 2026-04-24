using System.Text;
using Seren.Application.Chat.Attachments;
using Seren.Contracts.Events.Payloads;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Chat.Attachments;

public sealed class AttachmentValidatorTests
{
    private readonly AttachmentValidator _validator = new();

    [Fact]
    public void Validate_NullInput_ReturnsEmptyBundle()
    {
        var bundle = _validator.Validate(null);
        bundle.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyList_ReturnsEmptyBundle()
    {
        var bundle = _validator.Validate([]);
        bundle.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OneJpeg_ClassifiesAsImage()
    {
        var jpeg = AttachmentFixtures.MinimalJpeg();
        var dto = AttachmentFixtures.AsDto("image/jpeg", "screenshot.jpg", jpeg);

        var bundle = _validator.Validate([dto]);

        bundle.Images.Count.ShouldBe(1);
        bundle.Documents.Count.ShouldBe(0);
        bundle.Images[0].MimeType.ShouldBe("image/jpeg");
        bundle.Images[0].FileName.ShouldBe("screenshot.jpg");
        bundle.Images[0].Content.ShouldBe(jpeg);
    }

    [Fact]
    public void Validate_OnePdf_ClassifiesAsDocument()
    {
        var pdf = AttachmentFixtures.MinimalPdf("hello");
        var dto = AttachmentFixtures.AsDto("application/pdf", "cv.pdf", pdf);

        var bundle = _validator.Validate([dto]);

        bundle.Documents.Count.ShouldBe(1);
        bundle.Images.Count.ShouldBe(0);
        bundle.Documents[0].Kind.ShouldBe(AttachmentKind.Document);
    }

    [Fact]
    public void Validate_MixedImageAndDocument_PartitionsCorrectly()
    {
        var jpeg = AttachmentFixtures.AsDto("image/jpeg", "a.jpg", AttachmentFixtures.MinimalJpeg());
        var png = AttachmentFixtures.AsDto("image/png", "b.png", AttachmentFixtures.MinimalPng());
        var txt = AttachmentFixtures.AsDto(
            "text/plain",
            "notes.txt",
            Encoding.UTF8.GetBytes("hello"));

        var bundle = _validator.Validate([jpeg, png, txt]);

        bundle.Images.Count.ShouldBe(2);
        bundle.Documents.Count.ShouldBe(1);
    }

    [Fact]
    public void Validate_UnknownMime_Throws_UnsupportedMime()
    {
        var dto = AttachmentFixtures.AsDto(
            "application/x-msdownload",
            "malware.exe",
            [0x4D, 0x5A, 0x90]);

        var ex = Should.Throw<AttachmentValidationException>(
            () => _validator.Validate([dto]));
        ex.Code.ShouldBe(AttachmentValidationError.UnsupportedMime);
    }

    [Fact]
    public void Validate_TooManyAttachments_Throws_TooMany()
    {
        var oneJpeg = AttachmentFixtures.MinimalJpeg();
        var list = Enumerable.Range(0, AttachmentConstraints.MaxCount + 1)
            .Select(i => AttachmentFixtures.AsDto("image/jpeg", $"f{i}.jpg", oneJpeg))
            .ToList();

        var ex = Should.Throw<AttachmentValidationException>(
            () => _validator.Validate(list));
        ex.Code.ShouldBe(AttachmentValidationError.TooMany);
    }

    [Fact]
    public void Validate_SizeOverCap_Throws_TooLarge()
    {
        var oversized = new byte[AttachmentConstraints.MaxPerAttachmentBytes + 1];
        // Put valid JPEG magic so we reach the size-cap check after magic, not before.
        oversized[0] = 0xFF; oversized[1] = 0xD8; oversized[2] = 0xFF;
        var dto = AttachmentFixtures.AsDto("image/jpeg", "huge.jpg", oversized);

        var ex = Should.Throw<AttachmentValidationException>(
            () => _validator.Validate([dto]));
        ex.Code.ShouldBe(AttachmentValidationError.TooLarge);
    }

    [Fact]
    public void Validate_TotalOverCap_Throws_TotalTooLarge()
    {
        // 5 attachments × 4.5 MiB = 22.5 MiB aggregate > 20 MiB cap.
        var payload = new byte[(int)(4.5 * 1024 * 1024)];
        payload[0] = 0xFF; payload[1] = 0xD8; payload[2] = 0xFF;
        var list = Enumerable.Range(0, 5)
            .Select(i => AttachmentFixtures.AsDto("image/jpeg", $"big{i}.jpg", payload))
            .ToList();

        var ex = Should.Throw<AttachmentValidationException>(
            () => _validator.Validate(list));
        ex.Code.ShouldBe(AttachmentValidationError.TotalTooLarge);
    }

    [Fact]
    public void Validate_InvalidBase64_Throws_InvalidBase64()
    {
        var dto = new ChatAttachmentDto
        {
            MimeType = "image/jpeg",
            FileName = "broken.jpg",
            ByteSize = 10,
            Content = "this is not base64 ***",
        };

        var ex = Should.Throw<AttachmentValidationException>(
            () => _validator.Validate([dto]));
        ex.Code.ShouldBe(AttachmentValidationError.InvalidBase64);
    }

    [Fact]
    public void Validate_SizeMismatch_Throws_SizeMismatch()
    {
        var jpeg = AttachmentFixtures.MinimalJpeg();
        var dto = new ChatAttachmentDto
        {
            MimeType = "image/jpeg",
            FileName = "lied.jpg",
            ByteSize = jpeg.Length + 99,          // ← lie
            Content = Convert.ToBase64String(jpeg),
        };

        var ex = Should.Throw<AttachmentValidationException>(
            () => _validator.Validate([dto]));
        ex.Code.ShouldBe(AttachmentValidationError.SizeMismatch);
    }

    [Fact]
    public void Validate_MagicMismatch_Throws_MagicMismatch()
    {
        // Plain text labelled as JPEG.
        var text = Encoding.UTF8.GetBytes("not a jpeg!");
        var dto = AttachmentFixtures.AsDto("image/jpeg", "fake.jpg", text);

        var ex = Should.Throw<AttachmentValidationException>(
            () => _validator.Validate([dto]));
        ex.Code.ShouldBe(AttachmentValidationError.MagicMismatch);
    }

    [Fact]
    public void Validate_BlankFileName_Throws_InvalidFileName()
    {
        var jpeg = AttachmentFixtures.MinimalJpeg();
        var dto = new ChatAttachmentDto
        {
            MimeType = "image/jpeg",
            FileName = "   ",
            ByteSize = jpeg.Length,
            Content = Convert.ToBase64String(jpeg),
        };

        var ex = Should.Throw<AttachmentValidationException>(
            () => _validator.Validate([dto]));
        ex.Code.ShouldBe(AttachmentValidationError.InvalidFileName);
    }

    [Fact]
    public void Validate_MimeCaseInsensitive()
    {
        var jpeg = AttachmentFixtures.MinimalJpeg();
        var dto = new ChatAttachmentDto
        {
            MimeType = "IMAGE/JPEG",
            FileName = "upper.jpg",
            ByteSize = jpeg.Length,
            Content = Convert.ToBase64String(jpeg),
        };

        var bundle = _validator.Validate([dto]);
        bundle.Images.Count.ShouldBe(1);
        bundle.Images[0].MimeType.ShouldBe("image/jpeg");
    }

    [Fact]
    public void Validate_ZeroByteSize_Throws_TooLarge()
    {
        var dto = new ChatAttachmentDto
        {
            MimeType = "image/jpeg",
            FileName = "empty.jpg",
            ByteSize = 0,
            Content = string.Empty,
        };

        var ex = Should.Throw<AttachmentValidationException>(
            () => _validator.Validate([dto]));
        ex.Code.ShouldBe(AttachmentValidationError.TooLarge);
    }

    [Fact]
    public void Validate_TextPlain_AcceptedWithoutMagicCheck()
    {
        var content = Encoding.UTF8.GetBytes("# markdown\n\nplain text");
        var dto = AttachmentFixtures.AsDto("text/markdown", "notes.md", content);

        var bundle = _validator.Validate([dto]);
        bundle.Documents.Count.ShouldBe(1);
        bundle.Documents[0].MimeType.ShouldBe("text/markdown");
    }
}
