using System.Text.Json;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Infrastructure.OpenClaw.Gateway;

namespace Seren.Infrastructure.OpenClaw;

/// <summary>
/// <see cref="IOpenClawClient"/> implementation backed by the gateway
/// <c>models.list</c> RPC. Replaces the former REST client; the gateway is
/// the single transport for runtime interaction with OpenClaw.
/// </summary>
public sealed class OpenClawGatewayModelsClient : IOpenClawClient
{
    private readonly IOpenClawGateway _gateway;
    private readonly ILogger<OpenClawGatewayModelsClient> _logger;

    public OpenClawGatewayModelsClient(
        IOpenClawGateway gateway, ILogger<OpenClawGatewayModelsClient> logger)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(logger);
        _gateway = gateway;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
    {
        JsonElement result;
        try
        {
            result = await _gateway.CallAsync(
                method: "models.list",
                parameters: new { },
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (!ct.IsCancellationRequested)
        {
            // Gateway not ready yet (initial startup race). Prefer an empty
            // list to an exception so UI's /v1/models can degrade gracefully.
            _logger.LogWarning(ex, "Models listed while gateway not ready; returning empty list.");
            return Array.Empty<ModelInfo>();
        }
        catch (OpenClawGatewayException ex) when (IsScopeOrAuthError(ex))
        {
            // Operator tokens don't always carry the scope required to
            // enumerate models (gateway decides per-deployment). Log once
            // and surface an empty list so the UI doesn't 500 on settings.
            _logger.LogWarning(
                "Models listing rejected by gateway ({Code}: {Reason}); returning empty list.",
                ex.Code, ex.Message);
            return Array.Empty<ModelInfo>();
        }

        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("models", out var models)
            || models.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("models.list returned an unexpected shape; forwarding empty list.");
            return Array.Empty<ModelInfo>();
        }

        return BuildList(models);
    }

    private static bool IsScopeOrAuthError(OpenClawGatewayException ex) =>
        ex.Message.Contains("missing scope", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ex.Code, "UNAUTHORIZED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ex.Code, "FORBIDDEN", StringComparison.OrdinalIgnoreCase);

    private static List<ModelInfo> BuildList(JsonElement models)
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

            var name = entry.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                ? nameProp.GetString()
                : null;

            list.Add(new ModelInfo(Id: id, Description: name));
        }

        return list;
    }
}
