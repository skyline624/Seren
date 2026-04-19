using System.Text.Json;
using Seren.Contracts.Events;

namespace Seren.Application.OpenClaw.Handlers;

/// <summary>
/// Shared helper that builds a <see cref="WebSocketEnvelope"/> ready to be
/// broadcast from one of the OpenClaw → UI relay handlers. Centralizes the
/// camelCase serialization policy and the <see cref="EventMetadata"/> shape
/// so all four handlers stay in lock-step.
/// </summary>
internal static class OpenClawRelayEnvelope
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly ModuleIdentityDto RelaySource = new()
    {
        Id = "seren-openclaw-relay",
        PluginId = "seren",
    };

    public static WebSocketEnvelope Create(string eventType, object payload)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType(), CamelCase);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.Clone();

        return new WebSocketEnvelope
        {
            Type = eventType,
            Data = data,
            Metadata = new EventMetadata
            {
                Source = RelaySource,
                Event = new EventIdentity { Id = Guid.NewGuid().ToString("N") },
            },
        };
    }
}
