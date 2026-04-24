namespace Seren.Application.Chat.Attachments;

/// <summary>
/// Classification of a validated attachment for downstream routing.
/// </summary>
public enum AttachmentKind
{
    /// <summary>Image (jpeg / png / webp / gif / heic / heif) — forwarded raw to OpenClaw.</summary>
    Image,

    /// <summary>Text document (pdf / txt / md / csv) — extracted server-side into the user message.</summary>
    Document,
}

/// <summary>
/// A single attachment that has passed <see cref="AttachmentValidator"/>.
/// Decoded bytes are carried in-memory; the DTO with the base64 payload
/// is no longer needed downstream.
/// </summary>
public sealed record ValidatedAttachment(
    AttachmentKind Kind,
    string MimeType,
    string FileName,
    byte[] Content);

/// <summary>
/// Partition of an inbound attachment list by downstream destination.
/// Images travel on to OpenClaw's <c>chat.send.attachments</c>;
/// documents are extracted by <c>IAttachmentTextExtractor</c>s and the
/// extracted text is concatenated to the user message before OpenClaw is
/// called.
/// </summary>
public sealed record ValidatedAttachmentBundle(
    IReadOnlyList<ValidatedAttachment> Images,
    IReadOnlyList<ValidatedAttachment> Documents)
{
    /// <summary>Empty bundle — no attachments, no-op downstream.</summary>
    public static readonly ValidatedAttachmentBundle Empty = new([], []);

    /// <summary><c>true</c> when neither images nor documents are present.</summary>
    public bool IsEmpty => Images.Count == 0 && Documents.Count == 0;
}
