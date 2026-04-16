namespace Seren.Infrastructure.Realtime;

/// <summary>
/// Options for the Seren WebSocket hub, bound from the <c>Seren:WebSocket</c>
/// section of <c>appsettings.json</c>.
/// </summary>
public sealed class SerenHubOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Seren:WebSocket";

    /// <summary>Absolute path at which the WebSocket endpoint is mapped. Default: <c>/ws</c>.</summary>
    public string Path { get; set; } = "/ws";

    /// <summary>Keep-alive interval for the underlying Kestrel WebSocket in seconds.</summary>
    public int KeepAliveIntervalSeconds { get; set; } = 15;

    /// <summary>Max time without any frame before the session is considered stale.</summary>
    public int ReadTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When <c>true</c>, an incoming <c>module:authenticate</c> event is required
    /// before the peer can send anything else. Phase 1 defaults to <c>false</c>.
    /// </summary>
    public bool RequireAuthentication { get; set; }
}
