using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Infrastructure.OpenClaw.Gateway;

namespace Seren.Infrastructure.OpenClaw;

/// <summary>
/// <see cref="IOpenClawHistory"/> implementation backed by the gateway
/// <c>chat.history</c> and <c>sessions.reset</c> RPCs. Normalises the
/// upstream message format (which mixes string and structured content
/// arrays) into the flat <see cref="ChatHistoryMessage"/> shape Seren's
/// Application layer consumes.
/// </summary>
public sealed class OpenClawGatewayHistoryClient : IOpenClawHistory
{
    private readonly IOpenClawGateway _gateway;
    private readonly IChatSessionKeyProvider _sessionKeyProvider;
    private readonly ILogger<OpenClawGatewayHistoryClient> _logger;

    public OpenClawGatewayHistoryClient(
        IOpenClawGateway gateway,
        IChatSessionKeyProvider sessionKeyProvider,
        ILogger<OpenClawGatewayHistoryClient> logger)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(sessionKeyProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _gateway = gateway;
        _sessionKeyProvider = sessionKeyProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatHistoryMessage>> LoadAsync(
        int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return Array.Empty<ChatHistoryMessage>();
        }

        JsonElement result;
        try
        {
            result = await _gateway.CallAsync(
                method: "chat.history",
                parameters: new { sessionKey = _sessionKeyProvider.MainSessionKey, limit },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Gateway not ready yet — fall back to empty so a peer that
            // connects before the gateway is up doesn't block on hydration.
            _logger.LogWarning(ex, "chat.history called while gateway not ready; returning empty history");
            return Array.Empty<ChatHistoryMessage>();
        }

        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("messages", out var messages)
            || messages.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("chat.history returned an unexpected payload shape; treating as empty");
            return Array.Empty<ChatHistoryMessage>();
        }

        var list = new List<ChatHistoryMessage>(messages.GetArrayLength());
        foreach (var entry in messages.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var role = TryGetString(entry, "role") ?? "user";
            var ts = TryGetInt64(entry, "timestamp")
                  ?? TryGetInt64(entry, "ts")
                  ?? 0;
            // OpenClaw persists what the LLM actually emitted — including
            // `<think>…</think>` reasoning blocks and `<emotion:*>` /
            // `<action:*>` markers that the live path strips via
            // `SendTextMessageHandler`. Strip them here too so a page
            // reload hydrates the same user-visible content that was on
            // screen during the live stream.
            var content = LlmMarkerParser.StripAll(ExtractContent(entry));
            var messageId = TryGetString(entry, "id")
                         ?? TryGetString(entry, "messageId")
                         ?? StableMessageId(role, content, ts);

            list.Add(new ChatHistoryMessage(
                MessageId: messageId,
                Role: role,
                Content: content,
                Timestamp: ts,
                Emotion: null));
        }

        return list;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Kept on the contract for callers that already hold an admin scope,
    /// but the production reset flow goes through
    /// <see cref="IChatSessionKeyProvider.RotateAsync"/> instead — see
    /// <see cref="Seren.Application.Chat.ResetChatSessionHandler"/>.
    /// </remarks>
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await _gateway.CallAsync(
            method: "sessions.reset",
            parameters: new { key = _sessionKeyProvider.MainSessionKey, reason = "reset" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Reset OpenClaw session {SessionKey} (requires operator.admin scope)",
            _sessionKeyProvider.MainSessionKey);
    }

    private static string? TryGetString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static long? TryGetInt64(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)
            ? l
            : null;

    private static string ExtractContent(JsonElement messageObj)
    {
        // Upstream's `content` is either a plain string or an array of
        // {type, text}. Flatten to plain text either way.
        if (!messageObj.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                return content.GetString() ?? string.Empty;

            case JsonValueKind.Array:
                var sb = new StringBuilder();
                foreach (var entry in content.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    if (entry.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(t.GetString());
                    }
                }
                return sb.ToString();

            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Build a deterministic id when upstream doesn't provide one. SHA-256
    /// of (role|ts|content) gives us a stable identifier that matches
    /// across reloads and lets the client deduplicate against live chunks.
    /// </summary>
    private static string StableMessageId(string role, string content, long ts)
    {
        var input = $"{role}|{ts.ToString(CultureInfo.InvariantCulture)}|{content}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }
}
