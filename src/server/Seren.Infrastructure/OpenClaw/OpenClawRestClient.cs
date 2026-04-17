using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;

namespace Seren.Infrastructure.OpenClaw;

/// <summary>
/// HTTP-based <see cref="IOpenClawClient"/> implementation that communicates
/// with OpenClaw Gateway via its OpenAI-compatible REST API.
/// </summary>
public sealed class OpenClawRestClient : IOpenClawClient
{
    private readonly HttpClient _http;
    private readonly OpenClawOptions _options;
    private readonly ILogger<OpenClawRestClient> _logger;

    public OpenClawRestClient(
        HttpClient http,
        IOptions<OpenClawOptions> options,
        ILogger<OpenClawRestClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        // Auth header is set once — the token does not change at runtime.
        if (!string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.AuthToken);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatCompletionChunk> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string? agentId = null,
        string? sessionKey = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var effectiveAgentId = agentId ?? _options.DefaultAgentId;

        var requestBody = BuildChatRequestBody(messages, effectiveAgentId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrWhiteSpace(effectiveAgentId))
        {
            request.Headers.Add("x-openclaw-model", effectiveAgentId);
        }

        if (!string.IsNullOrWhiteSpace(sessionKey))
        {
            request.Headers.Add("x-openclaw-session-key", sessionKey);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            // SSE spec: only process lines that start with "data: "
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var json = line["data: ".Length..];

            // End-of-stream sentinel
            if (json is "[DONE]")
            {
                yield break;
            }

            // Skip empty or malformed lines
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            ChatCompletionChunk? chunk;
            try
            {
                chunk = ParseSseChunk(json);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Skipping malformed SSE chunk: {Json}", json);
                continue;
            }

            if (chunk is not null)
            {
                yield return chunk;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("/v1/models", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var root = doc.RootElement;
        if (!root.TryGetProperty("data", out var dataElement))
        {
            return [];
        }

        var models = new List<ModelInfo>(dataElement.GetArrayLength());
        foreach (var item in dataElement.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (id is null)
            {
                continue;
            }

            var description = item.TryGetProperty("description", out var descProp)
                ? descProp.GetString()
                : null;

            models.Add(new ModelInfo(id, description));
        }

        return models;
    }

    private static string BuildChatRequestBody(IReadOnlyList<ChatMessage> messages, string? agentId)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WriteBoolean("stream", true);

        // OpenClaw's OpenAI-compatible endpoint requires the body `model`
        // field to be either `"openclaw"` or `"openclaw/<registeredAgentId>"`.
        // The real provider model id (e.g. `ollama/qwen3.5:cloud`) travels
        // in the `x-openclaw-model` header set by the caller — it must NOT
        // go into `model`. Since our `agentId` is a provider/model id, not a
        // registered OpenClaw agent, we always send `"openclaw"` here.
        writer.WriteString("model", "openclaw");

        writer.WriteStartArray("messages");
        foreach (var msg in messages)
        {
            writer.WriteStartObject();
            writer.WriteString("role", msg.Role);
            writer.WriteString("content", msg.Content);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static ChatCompletionChunk? ParseSseChunk(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var firstChoice = choices[0];
        string? content = null;
        string? reasoning = null;
        string? finishReason = null;

        if (firstChoice.TryGetProperty("delta", out var delta))
        {
            if (delta.TryGetProperty("content", out var contentProp)
                && contentProp.ValueKind == JsonValueKind.String)
            {
                var text = contentProp.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    content = text;
                }
            }

            // Some models (GLM, DeepSeek, Qwen) expose their chain-of-thought
            // via a dedicated "reasoning" field that arrives before the final
            // answer. We surface it separately so the UI can show a thinking
            // indicator instead of leaking the internal reasoning into the
            // assistant bubble.
            if (delta.TryGetProperty("reasoning", out var reasoningProp)
                && reasoningProp.ValueKind == JsonValueKind.String)
            {
                var text = reasoningProp.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    reasoning = text;
                }
            }
        }

        if (firstChoice.TryGetProperty("finish_reason", out var finishProp))
        {
            finishReason = finishProp.ValueKind == JsonValueKind.String
                ? finishProp.GetString()
                : null;
        }

        // Reasoning takes precedence when both are present on the same chunk —
        // the handler upstream flips the thinking indicator while it streams.
        if (reasoning is not null)
        {
            return new ChatCompletionChunk(reasoning, finishReason, IsReasoning: true);
        }

        // Skip chunks with no useful data (e.g. role-only deltas at the start)
        if (content is null && finishReason is null)
        {
            return null;
        }

        return new ChatCompletionChunk(content, finishReason);
    }
}
