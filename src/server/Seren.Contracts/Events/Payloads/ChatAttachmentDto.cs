using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload fragment describing a single attachment joined to an
/// <c>input:text</c> event — an image, a PDF, or a small text document.
/// </summary>
/// <remarks>
/// Attachments travel as inline base64 in the WebSocket envelope; the
/// hub validates size + MIME + magic-bytes, then either forwards images
/// to OpenClaw as a structured attachment (<c>chat.send.attachments</c>)
/// or extracts the text from documents (PDF / TXT / MD / CSV) and folds
/// the result into the user message before calling OpenClaw.
/// <para/>
/// Constraints (caps on size, count, whitelisted MIME) live in the
/// application layer (<c>AttachmentConstraints</c>) and are mirrored
/// client-side in <c>useAttachmentConstraints.ts</c>. Exposed to clients
/// via <c>GET /api/chat/attachments/constraints</c> so a contract test
/// catches any drift.
/// </remarks>
[ExportTsClass]
public sealed record ChatAttachmentDto
{
    /// <summary>IANA MIME type the client claims for the attachment.</summary>
    public required string MimeType { get; init; }

    /// <summary>
    /// Display filename preserved verbatim for echo and extraction context.
    /// Never used as a filesystem path — the hub does not persist bytes.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Raw size in bytes of the original file (<b>before</b> base64 encoding).
    /// Used to short-circuit validation without decoding <see cref="Content"/>.
    /// </summary>
    public required long ByteSize { get; init; }

    /// <summary>
    /// Base64-encoded content. No <c>data:</c> URI prefix — pure base64
    /// (upstream OpenClaw expects the same convention).
    /// </summary>
    public required string Content { get; init; }
}
