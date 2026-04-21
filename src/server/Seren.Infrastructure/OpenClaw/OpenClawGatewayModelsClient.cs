using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Infrastructure.OpenClaw.Gateway;

namespace Seren.Infrastructure.OpenClaw;

/// <summary>
/// <see cref="IOpenClawClient"/> implementation backed by the gateway
/// <c>models.list</c> RPC. Replaces the former REST client; the gateway is
/// the single transport for runtime interaction with OpenClaw.
/// </summary>
/// <remarks>
/// <para>
/// The gateway's <c>models.list</c> RPC returns the full Pi SDK catalog
/// (~850 entries across every known provider) — far too broad for a
/// Settings dropdown. To surface only models the backend can actually
/// route to, the client cross-references the catalog with
/// <c>config.get models.providers</c>: a model is kept only when its
/// provider key appears in the gateway's configured providers map.
/// Configuring a new provider (adding the <c>openai</c> / <c>anthropic</c>
/// / … block in <c>openclaw.json</c> after setting its API key) is the
/// signal that makes those models visible in the UI.
/// </para>
/// <para>
/// Catalog refresh uses OpenClaw's <c>POST /tools/invoke</c> HTTP endpoint
/// with the <c>gateway</c> tool (enabled via <c>gateway.tools.allow</c> in
/// <c>openclaw.json</c>) to trigger a SIGUSR1 self-restart. This avoids
/// any assumption about how OpenClaw is deployed — container, systemd,
/// bare-host — and keeps the admin surface consistent with the rest of
/// the Gateway protocol.
/// </para>
/// </remarks>
public sealed class OpenClawGatewayModelsClient : IOpenClawClient
{
    private readonly IOpenClawGateway _gateway;
    private readonly HttpClient _http;
    private readonly IOptions<OpenClawOptions> _options;
    private readonly ILogger<OpenClawGatewayModelsClient> _logger;

    public OpenClawGatewayModelsClient(
        IOpenClawGateway gateway,
        HttpClient http,
        IOptions<OpenClawOptions> options,
        ILogger<OpenClawGatewayModelsClient> logger)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _gateway = gateway;
        _http = http;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
    {
        JsonElement catalogResult;
        try
        {
            catalogResult = await _gateway.CallAsync(
                method: "models.list",
                parameters: new { },
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (!ct.IsCancellationRequested)
        {
            // Gateway not ready yet (initial startup race). Prefer an empty
            // list to an exception so UI's /api/models can degrade gracefully.
            _logger.LogWarning(ex, "Models listed while gateway not ready; returning empty list.");
            return Array.Empty<ModelInfo>();
        }
        catch (OpenClawGatewayException ex) when (
            IsScopeOrAuthError(ex) || IsStartupTransient(ex))
        {
            // Three classes of transient gateway errors we absorb into an
            // empty list rather than surface as a 500:
            //   1. Scope/auth mismatches (operator token missing a scope).
            //   2. UNAVAILABLE during startup — common for ~3-5 s after an
            //      Apply-driven SIGUSR1 restart while the gateway rebuilds
            //      its catalog.
            _logger.LogWarning(
                "models.list rejected by gateway ({Code}: {Reason}); returning empty list.",
                ex.Code, ex.Message);
            return Array.Empty<ModelInfo>();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // RPC timed out (e.g. 30s default) while the gateway was
            // restarting. Treat as startup-transient — the next UI poll
            // (2-3 s later) will succeed.
            _logger.LogWarning("models.list timed out; returning empty list.");
            return Array.Empty<ModelInfo>();
        }

        if (catalogResult.ValueKind != JsonValueKind.Object
            || !catalogResult.TryGetProperty("models", out var models)
            || models.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("models.list returned an unexpected shape; forwarding empty list.");
            return Array.Empty<ModelInfo>();
        }

        var configuredProviders = await GetConfiguredProvidersAsync(ct).ConfigureAwait(false);
        return BuildList(models, configuredProviders);
    }

    /// <summary>
    /// Returns the set of provider keys currently declared under
    /// <c>models.providers</c> in OpenClaw's runtime config. An empty set
    /// means "no filter" (the config block was missing or unreadable) and
    /// the caller should return the full catalog rather than hide
    /// everything.
    /// </summary>
    private async Task<HashSet<string>?> GetConfiguredProvidersAsync(CancellationToken ct)
    {
        try
        {
            var result = await _gateway.CallAsync(
                method: "config.get",
                parameters: new { },
                cancellationToken: ct).ConfigureAwait(false);

            if (result.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // config.get returns the redacted snapshot. The providers map
            // lives under one of several possible parent branches depending
            // on the OpenClaw version (`runtimeConfig` in recent builds,
            // `config` / `parsed` on older ones). Try each in order and
            // collect the first non-empty providers object.
            JsonElement providersNode = default;
            var found = false;
            foreach (var branch in new[] { "runtimeConfig", "config", "parsed", "sourceConfig" })
            {
                if (!result.TryGetProperty(branch, out var branchNode)
                    || branchNode.ValueKind != JsonValueKind.Object
                    || !branchNode.TryGetProperty("models", out var modelsNode)
                    || modelsNode.ValueKind != JsonValueKind.Object
                    || !modelsNode.TryGetProperty("providers", out var candidate)
                    || candidate.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                providersNode = candidate;
                found = true;
                break;
            }

            if (!found)
            {
                _logger.LogDebug("config.get returned no models.providers node; skipping provider filter.");
                return null;
            }

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in providersNode.EnumerateObject())
            {
                if (!string.IsNullOrEmpty(provider.Name))
                {
                    set.Add(provider.Name);
                }
            }

            return set.Count == 0 ? null : set;
        }
        catch (InvalidOperationException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "config.get unavailable; skipping provider filter.");
            return null;
        }
        catch (OpenClawGatewayException ex)
        {
            _logger.LogDebug(
                "config.get rejected by gateway ({Code}: {Reason}); skipping provider filter.",
                ex.Code, ex.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task RefreshCatalogAsync(CancellationToken ct = default)
    {
        var opts = _options.Value;
        var baseUrl = opts.BaseUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            throw new InvalidOperationException(
                "OpenClaw:BaseUrl is not configured; cannot refresh the model catalog.");
        }

        // DelayMs=0 is important: with the default 2 s delay, the gateway
        // schedules the restart in a setTimeout callback that never fires
        // for reasons internal to OpenClaw's task registry. Passing 0 takes
        // the synchronous emit path and restarts immediately — empirically
        // verified against OpenClaw 2026.4.15.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/tools/invoke")
        {
            Content = JsonContent.Create(
                new ToolInvokeRequest(
                    Tool: "gateway",
                    Action: "restart",
                    Args: new ToolInvokeRestartArgs(
                        Reason: "seren:/api/models/refresh",
                        DelayMs: 0)),
                ToolsInvokeJsonContext.Default.ToolInvokeRequest),
        };

        if (!string.IsNullOrEmpty(opts.AuthToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.AuthToken);
        }

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // /tools/invoke returns the gateway tool's own error envelope as
            // a 200 + {ok:false,…} when the tool itself fails, so a non-2xx
            // here means the HTTP surface rejected the request (auth, tool
            // not allowed, body parse). Surface the status + body to the
            // caller — the endpoint turns this into a 502/503.
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"/tools/invoke rejected the refresh request ({(int)response.StatusCode}): {body}");
        }

        var payload = await response.Content
            .ReadFromJsonAsync(ToolsInvokeJsonContext.Default.ToolInvokeResponse, ct)
            .ConfigureAwait(false);

        if (payload is not { Ok: true })
        {
            throw new InvalidOperationException(
                $"Gateway tool refused the restart request: {payload?.Error?.Message ?? "unknown error"}");
        }

        _logger.LogInformation(
            "OpenClaw catalog refresh requested (reason=seren:/api/models/refresh).");
    }

    private static bool IsScopeOrAuthError(OpenClawGatewayException ex) =>
        ex.Message.Contains("missing scope", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ex.Code, "UNAUTHORIZED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ex.Code, "FORBIDDEN", StringComparison.OrdinalIgnoreCase);

    private static bool IsStartupTransient(OpenClawGatewayException ex) =>
        string.Equals(ex.Code, "UNAVAILABLE", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("unavailable during gateway startup", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("gateway starting", StringComparison.OrdinalIgnoreCase);

    private static List<ModelInfo> BuildList(JsonElement models, HashSet<string>? configuredProviders)
    {
        var list = new List<ModelInfo>(models.GetArrayLength());
        foreach (var entry in models.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = entry.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                ? idProp.GetString()
                : null;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            // OpenClaw serialises catalog entries with `id` and `provider` as
            // separate fields (e.g. {id:"seren-qwen:latest", provider:"ollama"}).
            // The UI and the rest of Seren expect a fully-qualified `provider/id`
            // key, so compose it here when the provider is present and the id
            // does not already carry a slash.
            var provider = entry.TryGetProperty("provider", out var providerProp)
                && providerProp.ValueKind == JsonValueKind.String
                    ? providerProp.GetString()
                    : null;
            if (configuredProviders is not null
                && !string.IsNullOrEmpty(provider)
                && !configuredProviders.Contains(provider))
            {
                continue;
            }

            var qualifiedId = !string.IsNullOrEmpty(provider) && !id.Contains('/', StringComparison.Ordinal)
                ? $"{provider}/{id}"
                : id;

            var name = entry.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                ? nameProp.GetString()
                : null;

            list.Add(new ModelInfo(Id: qualifiedId, Description: name));
        }

        return list;
    }
}

internal sealed record ToolInvokeRequest(
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("args")] ToolInvokeRestartArgs? Args);

internal sealed record ToolInvokeRestartArgs(
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("delayMs")] int DelayMs);

internal sealed record ToolInvokeResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("error")] ToolInvokeError? Error);

internal sealed record ToolInvokeError(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("message")] string? Message);

[JsonSerializable(typeof(ToolInvokeRequest))]
[JsonSerializable(typeof(ToolInvokeResponse))]
internal sealed partial class ToolsInvokeJsonContext : JsonSerializerContext;
