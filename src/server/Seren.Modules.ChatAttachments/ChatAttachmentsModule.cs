using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Seren.Application.Chat.Attachments;
using Seren.Application.Modules;

namespace Seren.Modules.ChatAttachments;

/// <summary>
/// Chat attachments module: registers the validator + text-extractor pipeline
/// (PDF, plain text) and exposes the read-only constraint contract used by
/// the UI to keep its client-side validation in sync with the server's
/// authoritative bounds.
/// </summary>
/// <remarks>
/// The implementations live in <c>Seren.Application/Chat/Attachments</c> —
/// the module is a thin DI + endpoint wrapper. New document types are added
/// by registering an additional <see cref="IAttachmentTextExtractor"/> in
/// <see cref="Configure"/>.
/// </remarks>
public sealed class ChatAttachmentsModule : ISerenModule, IEndpointMappingModule
{
    /// <inheritdoc />
    public string Id => "chat-attachments";

    /// <inheritdoc />
    public string Version =>
        typeof(ChatAttachmentsModule).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(ChatAttachmentsModule).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <inheritdoc />
    public void Configure(ModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Validator is stateless; the registry fans out to each
        // IAttachmentTextExtractor implementation. Adding a new document
        // type just means registering another extractor here.
        context.Services.AddSingleton<IAttachmentValidator, AttachmentValidator>();
        context.Services.AddSingleton<IAttachmentTextExtractor, PdfTextExtractor>();
        context.Services.AddSingleton<IAttachmentTextExtractor, PlainTextExtractor>();
        context.Services.AddSingleton<IAttachmentTextExtractorRegistry, AttachmentTextExtractorRegistry>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/chat/attachments/constraints", static () => Results.Ok(BuildResponse()))
            .WithName("GetChatAttachmentConstraints")
            .WithTags("chat");
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
