namespace Seren.Application.Chat.Attachments;

/// <summary>
/// Single source of truth for attachment validation caps + whitelisted
/// MIME types. Mirrored verbatim in the client via
/// <c>src/ui/packages/seren-ui-shared/src/composables/useAttachmentConstraints.ts</c>.
/// A contract test hits <c>GET /api/chat/attachments/constraints</c> and
/// diff's the two — any drift fails the test, preventing silent
/// desynchronization between client-side pre-validation and server-side
/// enforcement.
/// </summary>
/// <remarks>
/// <para>Caps are expressed in raw bytes, not base64-inflated size.</para>
/// <para>Total cap (20 MiB) is deliberately lower than the WebSocket
/// <c>Seren:WebSocket:MaxMessageSize</c> cap (25 MiB) to leave headroom
/// for base64 overhead (~33 %) + envelope JSON metadata.</para>
/// </remarks>
public static class AttachmentConstraints
{
    /// <summary>Max size of a single attachment, in raw (pre-base64) bytes.</summary>
    public const int MaxPerAttachmentBytes = 5 * 1024 * 1024;

    /// <summary>Max aggregated size of all attachments on one message, in raw bytes.</summary>
    public const int MaxTotalBytes = 20 * 1024 * 1024;

    /// <summary>Hard cap on the number of attachments per message.</summary>
    public const int MaxCount = 8;

    /// <summary>
    /// Images forwarded verbatim to OpenClaw via <c>chat.send.attachments</c>.
    /// Same list OpenClaw accepts inline (see
    /// <c>/home/pc/developpement/openclaw/src/gateway/chat-attachments.ts</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> ImageMimeTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "image/heic",
        "image/heif",
    ];

    /// <summary>
    /// Documents that Seren extracts text from server-side before forwarding
    /// the result concatenated to the user message (OpenClaw remains text-only
    /// for these).
    /// </summary>
    public static readonly IReadOnlyList<string> DocumentMimeTypes =
    [
        "application/pdf",
        "text/plain",
        "text/markdown",
        "text/csv",
    ];

    /// <summary>All MIME types accepted by the validator (images + documents).</summary>
    public static IEnumerable<string> AllMimeTypes => ImageMimeTypes.Concat(DocumentMimeTypes);

    /// <summary>
    /// Max number of pages processed by <c>PdfTextExtractor</c>. Beyond this,
    /// extraction stops and a warning is concatenated to the extracted text
    /// so the LLM can acknowledge the truncation.
    /// </summary>
    public const int MaxPdfPages = 50;

    /// <summary>
    /// Per-document cap on the number of characters concatenated to the
    /// user message from a single text document. Prevents a pathological
    /// CSV / TXT from eating the model's context window.
    /// </summary>
    public const int MaxDocumentTextCharacters = 64 * 1024;
}
