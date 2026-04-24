using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Lightweight metadata describing an attachment that travelled with a
/// user message — echoed to every connected peer on
/// <c>output:chat:user</c> so multi-tab / multi-device clients can
/// render the right chip / thumbnail placeholder without receiving the
/// base64 content (originating tab already has the File in memory,
/// peer tabs render a "[image: foo.png (1.2 MB)]" hint).
/// </summary>
[ExportTsClass]
public sealed record ChatAttachmentMetadataDto
{
    /// <summary>IANA MIME type as received and validated by the hub.</summary>
    public required string MimeType { get; init; }

    /// <summary>User-visible filename (safe to render; never used as a path).</summary>
    public required string FileName { get; init; }

    /// <summary>Raw file size in bytes (before base64).</summary>
    public required long ByteSize { get; init; }

    /// <summary>
    /// Stable opaque id minted hub-side at the moment of the echo. Lets
    /// peer UIs key thumbnail placeholders and replace them if we later
    /// add server-side persistence (<c>GET /api/chat/attachments/{id}</c>).
    /// </summary>
    public required string AttachmentId { get; init; }
}
