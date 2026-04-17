using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Infrastructure.OpenClaw.Gateway;

namespace Seren.Infrastructure.OpenClaw;

/// <summary>
/// Persistent WebSocket link to OpenClaw Gateway. Runs the OpenClaw
/// frame-level handshake, services RPC calls via
/// <see cref="IOpenClawGateway"/>, and publishes server-pushed events to
/// the Application layer as Mediator notifications.
/// </summary>
/// <remarks>
/// Registered as a singleton + <see cref="BackgroundService"/>. Kept
/// intentionally small — the heavy lifting lives in the
/// <c>Gateway</c> sub-namespace so each concern (handshake / rpc /
/// tick / event bridge) stays single-purpose and testable in isolation.
/// </remarks>
public sealed class OpenClawWebSocketClient : BackgroundService, IOpenClawGateway
{
    private static readonly string ClientVersion =
        typeof(OpenClawWebSocketClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? typeof(OpenClawWebSocketClient).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

    private readonly OpenClawOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OpenClawWebSocketClient> _logger;
    private readonly ILoggerFactory _loggerFactory;

    // Per-session state. Rebuilt each time we (re)connect. Reads from
    // IOpenClawGateway callers race with the read loop so we treat the
    // reference writes as atomic and gate them on Status.
    private volatile OpenClawGatewayStatus _status = OpenClawGatewayStatus.Disconnected;
    private IGatewaySocket? _currentSocket;
    private OpenClawGatewayRpc? _currentRpc;

    public OpenClawWebSocketClient(
        IOptions<OpenClawOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<OpenClawWebSocketClient> logger,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public OpenClawGatewayStatus Status => _status;

    /// <inheritdoc />
    public Task<JsonElement> CallAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var socket = _currentSocket;
        var rpc = _currentRpc;
        if (_status != OpenClawGatewayStatus.Ready || socket is null || rpc is null)
        {
            throw new InvalidOperationException(
                $"OpenClaw gateway is not ready (status={_status}). Wait for OpenClawGatewayReadyNotification before issuing RPC calls.");
        }

        return rpc.CallAsync(
            sendAsync: (request, ct) => SendRequestAsync(socket, request, ct),
            method: method,
            parameters: parameters,
            timeout: timeout,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OpenClaw gateway client starting (clientVersion={ClientVersion})", ClientVersion);

        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var handshakeComplete = false;
            var disconnectReason = "unknown";

            try
            {
                _status = OpenClawGatewayStatus.Connecting;

                await using var socket = await ConnectSocketAsync(stoppingToken).ConfigureAwait(false);

                _status = OpenClawGatewayStatus.Handshaking;
                var bridge = new OpenClawGatewayEventBridge(_scopeFactory, _loggerFactory.CreateLogger<OpenClawGatewayEventBridge>());

                HelloOkPayload helloOk;
                try
                {
                    helloOk = await OpenClawGatewayHandshake.PerformAsync(
                        socket,
                        _options,
                        ClientVersion,
                        _options.WebSocket.HandshakeTimeout,
                        _loggerFactory.CreateLogger(nameof(OpenClawGatewayHandshake)),
                        stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    disconnectReason = $"handshake: {ex.Message}";
                    throw;
                }

                handshakeComplete = true;
                attempt = 0; // successful handshake resets backoff

                _logger.LogInformation(
                    "OpenClaw handshake complete (protocol={Protocol}, serverVersion={Version}, connId={ConnId}, tickIntervalMs={TickInterval})",
                    helloOk.Protocol, helloOk.Server.Version, helloOk.Server.ConnId, helloOk.Policy.TickIntervalMs);

                await using var rpc = new OpenClawGatewayRpc(
                    _loggerFactory.CreateLogger<OpenClawGatewayRpc>(),
                    _options.WebSocket.RpcTimeout);

                await using var tickMonitor = new OpenClawGatewayTickMonitor(
                    tickIntervalMs: helloOk.Policy.TickIntervalMs,
                    graceMultiplier: _options.WebSocket.TickGraceMultiplier,
                    closeAsync: (code, reason, ct) => socket.CloseAsync(code, reason, ct),
                    logger: _loggerFactory.CreateLogger<OpenClawGatewayTickMonitor>());

                _currentSocket = socket;
                _currentRpc = rpc;
                _status = OpenClawGatewayStatus.Ready;

                await bridge.PublishReadyAsync(helloOk, stoppingToken).ConfigureAwait(false);

                try
                {
                    disconnectReason = await ReadLoopAsync(
                        socket, rpc, tickMonitor, bridge, stoppingToken).ConfigureAwait(false);
                }
                finally
                {
                    // Snap state back so CallAsync fails fast before we actually
                    // dispose the rpc/socket at the enclosing `await using`.
                    _currentSocket = null;
                    _currentRpc = null;
                    _status = OpenClawGatewayStatus.Reconnecting;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                disconnectReason = $"{ex.GetType().Name}: {ex.Message}";
                _logger.LogWarning(ex, "OpenClaw gateway session failed ({Stage}). Will back off and retry.",
                    handshakeComplete ? "read-loop" : "connect/handshake");
            }

            // Always notify listeners so UI status can flip.
            try
            {
                var bridge = new OpenClawGatewayEventBridge(_scopeFactory, _loggerFactory.CreateLogger<OpenClawGatewayEventBridge>());
                await bridge.PublishDisconnectedAsync(disconnectReason, handshakeComplete, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await BackoffAsync(attempt, stoppingToken).ConfigureAwait(false);
            attempt = Math.Min(attempt + 1, 10); // cap growth past ~17 min
        }

        _status = OpenClawGatewayStatus.Disconnected;
        _logger.LogInformation("OpenClaw gateway client stopped");
    }

    /// <summary>
    /// Read loop: pumps frames until the socket closes or cancellation
    /// fires. Returns a human-readable disconnect reason for the caller
    /// to log + publish.
    /// </summary>
    private async Task<string> ReadLoopAsync(
        IGatewaySocket socket,
        OpenClawGatewayRpc rpc,
        OpenClawGatewayTickMonitor tickMonitor,
        OpenClawGatewayEventBridge bridge,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            GatewayReceiveResult received;
            try
            {
                received = await socket.ReceiveTextAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException ex)
            {
                var reason = $"socket exception: {ex.Message}";
                rpc.FailAllPending(new OpenClawGatewayException("gateway.disconnected", reason, ex));
                return reason;
            }

            if (received.IsClose)
            {
                var reason = received.CloseStatusDescription is null
                    ? $"close frame ({received.CloseStatus})"
                    : $"close frame ({received.CloseStatus}): {received.CloseStatusDescription}";
                rpc.FailAllPending(new OpenClawGatewayException("gateway.disconnected", reason));
                return reason;
            }

            tickMonitor.OnFrameReceived();

            if (string.IsNullOrWhiteSpace(received.Text))
            {
                continue;
            }

            await DispatchFrameAsync(received.Text!, rpc, bridge, cancellationToken).ConfigureAwait(false);
        }

        return "cancellation requested";
    }

    private async Task DispatchFrameAsync(
        string frameText,
        OpenClawGatewayRpc rpc,
        OpenClawGatewayEventBridge bridge,
        CancellationToken cancellationToken)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(frameText);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Dropping malformed gateway frame: {Frame}", frameText);
            return;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("type", out var typeProp)
                || typeProp.ValueKind != JsonValueKind.String)
            {
                _logger.LogDebug("Dropping gateway frame without 'type' discriminator: {Frame}", frameText);
                return;
            }

            switch (typeProp.GetString())
            {
                case OpenClawGatewayProtocol.FrameTypeResponse:
                    var response = doc.RootElement.Deserialize(
                        OpenClawGatewayJsonContext.Default.GatewayResponse);
                    if (response is not null)
                    {
                        if (!rpc.CompletePending(response))
                        {
                            _logger.LogDebug(
                                "Received response for unknown RPC id {Id}", response.Id);
                        }
                    }
                    break;

                case OpenClawGatewayProtocol.FrameTypeEvent:
                    var ev = doc.RootElement.Deserialize(
                        OpenClawGatewayJsonContext.Default.GatewayEvent);
                    if (ev is null)
                    {
                        _logger.LogDebug("Dropping unparseable event frame");
                        break;
                    }
                    if (ev.Event == OpenClawGatewayProtocol.TickEventName)
                    {
                        // Tick monitor already reset by OnFrameReceived — no extra work needed here.
                        break;
                    }
                    await bridge.PublishAsync(ev, cancellationToken).ConfigureAwait(false);
                    break;

                case OpenClawGatewayProtocol.FrameTypeRequest:
                    // The gateway currently does not push reqs to backend
                    // clients. Log so we notice if that changes upstream.
                    _logger.LogWarning(
                        "Received unexpected gateway request frame (not yet supported). Frame: {Frame}",
                        frameText);
                    break;

                default:
                    _logger.LogDebug(
                        "Dropping gateway frame with unknown type {Type}", typeProp.GetString());
                    break;
            }
        }
    }

    private async Task<IGatewaySocket> ConnectSocketAsync(CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();

        if (!string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            socket.Options.SetRequestHeader("Authorization", $"Bearer {_options.AuthToken}");
        }

        var uri = BuildWebSocketUri(_options.BaseUrl);
        _logger.LogInformation("Connecting to OpenClaw gateway at {Uri}", uri);

        try
        {
            await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new ClientWebSocketAdapter(socket);
    }

    private static async Task SendRequestAsync(
        IGatewaySocket socket, GatewayRequest request, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            request, OpenClawGatewayJsonContext.Default.GatewayRequest);
        await socket.SendTextAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task BackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        var seconds = Math.Min(Math.Pow(2, attempt), _options.WebSocket.ReconnectMaxBackoff.TotalSeconds);
        var delay = TimeSpan.FromSeconds(Math.Max(1, seconds));
        _logger.LogInformation(
            "Reconnecting to OpenClaw gateway in {DelaySeconds}s (attempt {Attempt})",
            delay.TotalSeconds, attempt + 1);
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Caller loop checks cancellation again after we return.
        }
    }

    private static Uri BuildWebSocketUri(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        var wsUrl = trimmed switch
        {
            string s when s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                => "wss://" + s["https://".Length..],
            string s when s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                => "ws://" + s["http://".Length..],
            _ => "ws://" + trimmed,
        };
        return new Uri($"{wsUrl}/ws");
    }
}
