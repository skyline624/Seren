using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;

namespace Seren.Infrastructure.Ollama;

/// <summary>
/// <see cref="IOllamaClient"/> implementation backed by Ollama's REST API.
/// Calls <c>GET {BaseUrl}/api/tags</c> and normalises each entry into a
/// <see cref="ModelInfo"/> with an <c>ollama/</c> id prefix.
/// </summary>
/// <remarks>
/// <para>
/// Failure handling: any network error, timeout, non-2xx status, or
/// malformed JSON logs a warning and returns an empty list. Callers must
/// never see an exception — <c>/api/models</c> should always return at
/// least the OpenClaw catalog, never 500.
/// </para>
/// </remarks>
public sealed class OllamaRestClient : IOllamaClient
{
    private readonly HttpClient _http;
    private readonly IOptions<OllamaOptions> _options;
    private readonly ILogger<OllamaRestClient> _logger;

    public OllamaRestClient(
        HttpClient http,
        IOptions<OllamaOptions> options,
        ILogger<OllamaRestClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelInfo>> GetLocalModelsAsync(CancellationToken ct = default)
    {
        var baseUrl = _options.Value.BaseUrl;
        if (string.IsNullOrEmpty(baseUrl))
        {
            // Empty BaseUrl is a valid configuration: Ollama integration
            // disabled. Silent no-op — don't emit warnings every minute.
            return Array.Empty<ModelInfo>();
        }

        OllamaTagsResponse? payload;
        try
        {
            payload = await _http
                .GetFromJsonAsync(
                    "api/tags",
                    OllamaJsonContext.Default.OllamaTagsResponse,
                    ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to reach Ollama at {BaseUrl}; returning empty model list.", baseUrl);
            return Array.Empty<ModelInfo>();
        }
        catch (TaskCanceledException ex)
        {
            // HttpClient timeout — different from user-requested cancellation.
            _logger.LogWarning(ex, "Ollama /api/tags timed out after {TimeoutSeconds}s; returning empty list.",
                _options.Value.TimeoutSeconds);
            return Array.Empty<ModelInfo>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Ollama /api/tags returned malformed JSON; returning empty list.");
            return Array.Empty<ModelInfo>();
        }

        if (payload?.Models is null || payload.Models.Count == 0)
        {
            return Array.Empty<ModelInfo>();
        }

        var result = new List<ModelInfo>(payload.Models.Count);
        foreach (var m in payload.Models)
        {
            if (string.IsNullOrEmpty(m.Name))
            {
                continue;
            }

            result.Add(new ModelInfo(
                Id: $"ollama/{m.Name}",
                Description: BuildDescription(m.Details)));
        }

        return result;
    }

    private static string? BuildDescription(OllamaModelDetails? details)
    {
        if (details is null)
        {
            return null;
        }

        var parts = new List<string>(3);
        if (!string.IsNullOrEmpty(details.Family))
        {
            parts.Add(details.Family);
        }
        if (!string.IsNullOrEmpty(details.ParameterSize))
        {
            parts.Add(details.ParameterSize);
        }
        if (!string.IsNullOrEmpty(details.QuantizationLevel))
        {
            parts.Add(details.QuantizationLevel);
        }
        return parts.Count > 0 ? string.Join(' ', parts) : null;
    }
}

internal sealed record OllamaTagsResponse(
    [property: JsonPropertyName("models")] IReadOnlyList<OllamaModelEntry>? Models);

internal sealed record OllamaModelEntry(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("details")] OllamaModelDetails? Details);

internal sealed record OllamaModelDetails(
    [property: JsonPropertyName("family")] string? Family,
    [property: JsonPropertyName("parameter_size")] string? ParameterSize,
    [property: JsonPropertyName("quantization_level")] string? QuantizationLevel);

[JsonSerializable(typeof(OllamaTagsResponse))]
internal sealed partial class OllamaJsonContext : JsonSerializerContext;
