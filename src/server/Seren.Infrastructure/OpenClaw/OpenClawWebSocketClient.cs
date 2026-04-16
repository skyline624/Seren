using System.Net.WebSockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Seren.Infrastructure.OpenClaw;

/// <summary>
/// Persistent WebSocket client that maintains a connection to OpenClaw Gateway
/// for real-time events (agent responses, state changes, etc.).
/// </summary>
/// <remarks>
/// Registered as a <see cref="BackgroundService"/> — starts with the host and
/// reconnects with exponential backoff on disconnection.
/// </remarks>
public sealed class OpenClawWebSocketClient : BackgroundService
{
    private readonly OpenClawOptions _options;
    private readonly ILogger<OpenClawWebSocketClient> _logger;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private ClientWebSocket? _webSocket;

    public OpenClawWebSocketClient(
        IOptions<OpenClawOptions> options,
        ILogger<OpenClawWebSocketClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Connects the WebSocket to OpenClaw Gateway if not already connected.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_webSocket is { State: WebSocketState.Open })
            {
                return;
            }

            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();

            if (!string.IsNullOrWhiteSpace(_options.AuthToken))
            {
                _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_options.AuthToken}");
            }

            var wsUri = BuildWebSocketUri();
            _logger.LogInformation("Connecting to OpenClaw Gateway at {Uri}", wsUri);

            await _webSocket.ConnectAsync(wsUri, ct).ConfigureAwait(false);
            _logger.LogInformation("Connected to OpenClaw Gateway");
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    /// Gracefully closes the WebSocket connection.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_webSocket is { State: WebSocketState.Open or WebSocketState.CloseReceived })
        {
            try
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "seren: client shutting down",
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (WebSocketException ex)
            {
                _logger.LogDebug(ex, "Error closing OpenClaw WebSocket");
            }
        }

        _webSocket?.Dispose();
        _webSocket = null;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OpenClaw WebSocket client starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync(stoppingToken).ConfigureAwait(false);
                await StartListeningAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — exit the loop.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenClaw WebSocket disconnected, reconnecting...");
            }

            // Exponential backoff: 2^n seconds, capped at 30s.
            var attempt = 0;
            var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), 30));

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Reconnecting to OpenClaw Gateway in {Delay}s (attempt {Attempt})",
                    delay.TotalSeconds, attempt + 1);

                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);

                try
                {
                    await ConnectAsync(stoppingToken).ConfigureAwait(false);
                    break; // Connected — exit backoff loop.
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Reconnection attempt {Attempt} failed",
                        attempt + 1);
                    attempt++;
                    delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), 30));
                }
            }
        }

        await DisconnectAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Reads frames from the WebSocket and deserializes them.
    /// </summary>
    public async Task StartListeningAsync(CancellationToken ct)
    {
        if (_webSocket is null)
        {
            throw new InvalidOperationException("WebSocket is not connected.");
        }

        var buffer = new byte[8192];

        while (_webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            using var ms = new MemoryStream();

            do
            {
                result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct)
                    .ConfigureAwait(false);
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogInformation("OpenClaw Gateway sent close frame");
                break;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                _logger.LogDebug("Skipping non-text frame from OpenClaw Gateway");
                continue;
            }

            var payload = ms.ToArray();
            _logger.LogDebug("Received {ByteCount} bytes from OpenClaw Gateway", payload.Length);

            // TODO (Phase 3): Deserialize payload into domain events and dispatch via Mediator.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("OpenClaw WebSocket client stopping");
        await DisconnectAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private Uri BuildWebSocketUri()
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');

        // Convert http(s) to ws(s) for WebSocket connections.
        var wsUrl = baseUrl switch
        {
            string s when s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) =>
                "wss://" + s["https://".Length..],
            string s when s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) =>
                "ws://" + s["http://".Length..],
            _ => "ws://" + baseUrl,
        };

        return new Uri($"{wsUrl}/ws");
    }
}
