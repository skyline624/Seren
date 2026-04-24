using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Seren.Application.Chat.Attachments;

namespace Seren.Server.Api.Endpoints;

/// <summary>
/// Exposes the authoritative <see cref="AttachmentConstraints"/> values to
/// clients. The UI fetches this endpoint once at startup and compares it
/// against its local <c>useAttachmentConstraints.ts</c> mirror via a
/// vitest contract test — any drift fails the test, preventing a client
/// that validates too loosely from surprising the server with payloads
/// it cannot accept.
/// </summary>
public static class ChatAttachmentsEndpoints
{
    public static IEndpointRouteBuilder MapChatAttachmentsEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/chat/attachments/constraints", static () => Results.Ok(BuildResponse()))
            .WithName("GetChatAttachmentConstraints")
            .WithTags("chat");

        return routes;
    }

    private static AttachmentConstraintsResponse BuildResponse() => new(
        MaxPerAttachmentBytes: AttachmentConstraints.MaxPerAttachmentBytes,
        MaxTotalBytes: AttachmentConstraints.MaxTotalBytes,
        MaxCount: AttachmentConstraints.MaxCount,
        ImageMimeTypes: AttachmentConstraints.ImageMimeTypes,
        DocumentMimeTypes: AttachmentConstraints.DocumentMimeTypes);
}

/// <summary>DTO returned by <c>GET /api/chat/attachments/constraints</c>.</summary>
public sealed record AttachmentConstraintsResponse(
    int MaxPerAttachmentBytes,
    int MaxTotalBytes,
    int MaxCount,
    IReadOnlyList<string> ImageMimeTypes,
    IReadOnlyList<string> DocumentMimeTypes);
