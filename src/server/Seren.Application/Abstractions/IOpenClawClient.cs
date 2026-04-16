namespace Seren.Application.Abstractions;

/// <summary>
/// Application-layer contract for communicating with OpenClaw Gateway.
/// Implemented by the infrastructure layer (DIP).
/// </summary>
public interface IOpenClawClient
{
    /// <summary>
    /// Streams a chat completion request to OpenClaw Gateway, yielding chunks
    /// as they arrive over the SSE connection.
    /// </summary>
    /// <param name="messages">The conversation messages to send.</param>
    /// <param name="agentId">Optional agent/model identifier (maps to <c>x-openclaw-model</c> header).</param>
    /// <param name="sessionKey">Optional session key for conversation continuity (maps to <c>x-openclaw-session-key</c> header). When set, OpenClaw maintains conversation history server-side.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<ChatCompletionChunk> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string? agentId = null,
        string? sessionKey = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves the list of available models from OpenClaw Gateway.
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default);
}

/// <summary>
/// A single message in a chat completion request.
/// </summary>
public sealed record ChatMessage(string Role, string Content);

/// <summary>
/// A single chunk streamed back from OpenClaw Gateway.
/// Either <see cref="Content"/> is populated (partial text) or <see cref="FinishReason"/> signals stream end.
/// </summary>
public sealed record ChatCompletionChunk(string? Content, string? FinishReason);

/// <summary>
/// Metadata about a model available in OpenClaw Gateway.
/// </summary>
public sealed record ModelInfo(string Id, string? Description);
