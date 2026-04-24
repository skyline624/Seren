using Seren.Contracts.Events.Payloads;

namespace Seren.Application.Chat.Attachments;

/// <summary>
/// Validates an inbound <c>TextInputPayload.Attachments</c> bundle against
/// <see cref="AttachmentConstraints"/>. Performs, in order:
/// <list type="number">
///   <item>per-attachment filename + declared size sanity,</item>
///   <item>MIME whitelist check,</item>
///   <item>base64 decode,</item>
///   <item>decoded size matches declared size,</item>
///   <item>per-attachment size cap,</item>
///   <item>magic-bytes coherent with declared MIME (anti-spoof),</item>
///   <item>global count + aggregate size caps.</item>
/// </list>
/// Throws <see cref="AttachmentValidationException"/> on first failure — the
/// bundle is rejected as a whole, the client re-uploads after fixing.
/// </summary>
public sealed class AttachmentValidator : IAttachmentValidator
{
    /// <inheritdoc />
    public ValidatedAttachmentBundle Validate(IReadOnlyList<ChatAttachmentDto>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return ValidatedAttachmentBundle.Empty;
        }

        if (attachments.Count > AttachmentConstraints.MaxCount)
        {
            throw new AttachmentValidationException(
                AttachmentValidationError.TooMany,
                $"At most {AttachmentConstraints.MaxCount} attachments per message.",
                $"received = {attachments.Count}");
        }

        var images = new List<ValidatedAttachment>();
        var documents = new List<ValidatedAttachment>();
        long totalBytes = 0;

        foreach (var rawAttachment in attachments)
        {
            var attachment = rawAttachment ?? throw new AttachmentValidationException(
                AttachmentValidationError.UnsupportedMime,
                "Attachment entry is null.");

            if (string.IsNullOrWhiteSpace(attachment.FileName))
            {
                throw new AttachmentValidationException(
                    AttachmentValidationError.InvalidFileName,
                    "Attachment file name is required.");
            }

            var mimeType = attachment.MimeType?.Trim().ToLowerInvariant() ?? string.Empty;
            var kind = ClassifyMimeType(mimeType) ?? throw new AttachmentValidationException(
                AttachmentValidationError.UnsupportedMime,
                $"MIME type '{attachment.MimeType}' is not allowed.",
                $"fileName = {attachment.FileName}");

            if (attachment.ByteSize <= 0
                || attachment.ByteSize > AttachmentConstraints.MaxPerAttachmentBytes)
            {
                throw new AttachmentValidationException(
                    AttachmentValidationError.TooLarge,
                    $"Attachment exceeds the {AttachmentConstraints.MaxPerAttachmentBytes / (1024 * 1024)} MiB cap.",
                    $"fileName = {attachment.FileName}, declared = {attachment.ByteSize} bytes");
            }

            byte[] content;
            try
            {
                content = Convert.FromBase64String(attachment.Content ?? string.Empty);
            }
            catch (FormatException ex)
            {
                throw new AttachmentValidationException(
                    AttachmentValidationError.InvalidBase64,
                    $"Base64 content could not be decoded for '{attachment.FileName}'.",
                    ex.Message);
            }

            if (content.Length != attachment.ByteSize)
            {
                throw new AttachmentValidationException(
                    AttachmentValidationError.SizeMismatch,
                    $"Declared byte size does not match decoded content for '{attachment.FileName}'.",
                    $"declared = {attachment.ByteSize}, decoded = {content.Length}");
            }

            if (content.Length > AttachmentConstraints.MaxPerAttachmentBytes)
            {
                throw new AttachmentValidationException(
                    AttachmentValidationError.TooLarge,
                    $"Attachment exceeds the {AttachmentConstraints.MaxPerAttachmentBytes / (1024 * 1024)} MiB cap after decoding.",
                    $"fileName = {attachment.FileName}, decoded = {content.Length} bytes");
            }

            totalBytes += content.Length;
            if (totalBytes > AttachmentConstraints.MaxTotalBytes)
            {
                throw new AttachmentValidationException(
                    AttachmentValidationError.TotalTooLarge,
                    $"Aggregate attachment size exceeds the {AttachmentConstraints.MaxTotalBytes / (1024 * 1024)} MiB cap.",
                    $"total = {totalBytes} bytes");
            }

            if (!AttachmentMagicBytes.Matches(mimeType, content))
            {
                throw new AttachmentValidationException(
                    AttachmentValidationError.MagicMismatch,
                    $"File content does not match the declared MIME type '{mimeType}'.",
                    $"fileName = {attachment.FileName}");
            }

            var validated = new ValidatedAttachment(kind, mimeType, attachment.FileName, content);
            if (kind == AttachmentKind.Image)
            {
                images.Add(validated);
            }
            else
            {
                documents.Add(validated);
            }
        }

        return new ValidatedAttachmentBundle(images, documents);
    }

    private static AttachmentKind? ClassifyMimeType(string mimeType)
    {
        if (AttachmentConstraints.ImageMimeTypes.Contains(mimeType))
        {
            return AttachmentKind.Image;
        }

        if (AttachmentConstraints.DocumentMimeTypes.Contains(mimeType))
        {
            return AttachmentKind.Document;
        }

        return null;
    }
}

/// <summary>
/// Abstraction over <see cref="AttachmentValidator"/> so the
/// <c>SendTextMessageHandler</c> can substitute it in tests.
/// </summary>
public interface IAttachmentValidator
{
    /// <summary>
    /// Validates and partitions <paramref name="attachments"/>. Returns
    /// <see cref="ValidatedAttachmentBundle.Empty"/> when the input is null
    /// or empty. Throws <see cref="AttachmentValidationException"/> when
    /// any check fails.
    /// </summary>
    ValidatedAttachmentBundle Validate(IReadOnlyList<ChatAttachmentDto>? attachments);
}
