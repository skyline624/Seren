using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// Single-shot coordinator that drives the OpenClaw gateway handshake:
/// sends the <c>connect</c> request, ignores any pre-handshake
/// <c>connect.challenge</c> event, and awaits the matching
/// <c>hello-ok</c> response before returning the negotiated policy.
/// </summary>
internal static class OpenClawGatewayHandshake
{
    /// <summary>
    /// Performs the handshake. Blocks until either the <c>hello-ok</c> is
    /// received, the gateway returns an error response, or
    /// <paramref name="timeout"/> elapses.
    /// </summary>
    /// <exception cref="OpenClawGatewayException">The gateway rejected the handshake.</exception>
    /// <exception cref="OperationCanceledException">Timeout or caller cancellation.</exception>
    public static async Task<HelloOkPayload> PerformAsync(
        IGatewaySocket socket,
        OpenClawOptions options,
        string clientVersion,
        TimeSpan timeout,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var connectId = Guid.NewGuid().ToString("N");
        var paramsElement = JsonSerializer.SerializeToElement(
            new ConnectParams(
                MinProtocol: OpenClawGatewayProtocol.ProtocolVersion,
                MaxProtocol: OpenClawGatewayProtocol.ProtocolVersion,
                Client: BuildClientInfo(clientVersion),
                Role: "operator",
                Auth: string.IsNullOrWhiteSpace(options.AuthToken)
                    ? null
                    : new ConnectAuth(options.AuthToken)),
            OpenClawGatewayJsonContext.Default.ConnectParams);

        var request = new GatewayRequest(connectId, OpenClawGatewayProtocol.ConnectMethodName, paramsElement);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            request, OpenClawGatewayJsonContext.Default.GatewayRequest);

        logger.LogInformation(
            "Sending OpenClaw connect request (id={ConnectId}, protocol={Protocol}, clientId={ClientId}, mode={Mode})",
            connectId, OpenClawGatewayProtocol.ProtocolVersion,
            OpenClawGatewayProtocol.ClientId, OpenClawGatewayProtocol.ClientMode);

        await socket.SendTextAsync(bytes, cancellationToken).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        return await WaitForHelloOkAsync(socket, connectId, logger, timeoutCts.Token).ConfigureAwait(false);
    }

    private static async Task<HelloOkPayload> WaitForHelloOkAsync(
        IGatewaySocket socket,
        string expectedId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var received = await socket.ReceiveTextAsync(cancellationToken).ConfigureAwait(false);

            if (received.IsClose)
            {
                throw new OpenClawGatewayException(
                    code: "handshake.closed",
                    message: $"Gateway closed the connection during handshake "
                           + $"(code={received.CloseStatus}, reason={received.CloseStatusDescription ?? "n/a"}).");
            }

            if (string.IsNullOrWhiteSpace(received.Text))
            {
                continue;
            }

            // Discriminator-first parse: we only want res/event, and we only want
            // the connect response with the id we sent. Everything else is logged
            // and skipped until we hit our response or time out.
            using var doc = JsonDocument.Parse(received.Text);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp)
                || typeProp.ValueKind != JsonValueKind.String)
            {
                logger.LogDebug("Skipping malformed gateway frame during handshake: {Frame}", received.Text);
                continue;
            }

            var type = typeProp.GetString();
            if (type == OpenClawGatewayProtocol.FrameTypeEvent)
            {
                // connect.challenge arrives before our response. Shared-secret
                // auth doesn't use its nonce, so we ignore it silently.
                continue;
            }

            if (type != OpenClawGatewayProtocol.FrameTypeResponse)
            {
                logger.LogDebug("Skipping pre-handshake frame of type {Type}", type);
                continue;
            }

            var response = doc.RootElement.Deserialize(
                OpenClawGatewayJsonContext.Default.GatewayResponse);

            if (response is null || !string.Equals(response.Id, expectedId, StringComparison.Ordinal))
            {
                logger.LogDebug(
                    "Skipping unrelated response frame (id={Id}) during handshake",
                    response?.Id);
                continue;
            }

            if (!response.Ok)
            {
                throw new OpenClawGatewayException(
                    code: response.Error?.Code ?? "handshake.rejected",
                    message: response.Error?.Message ?? "Gateway rejected the connect request.",
                    retryable: response.Error?.Retryable,
                    retryAfterMs: response.Error?.RetryAfterMs);
            }

            if (response.Payload is null)
            {
                throw new OpenClawGatewayException(
                    code: "handshake.payload-missing",
                    message: "Gateway accepted the connect request but returned no hello-ok payload.");
            }

            return response.Payload.Value.Deserialize(
                OpenClawGatewayJsonContext.Default.HelloOkPayload)
                ?? throw new OpenClawGatewayException(
                    code: "handshake.payload-invalid",
                    message: "Gateway returned a hello-ok payload that could not be deserialized.");
        }
    }

    private static ConnectClient BuildClientInfo(string clientVersion)
    {
        var platform = RuntimeInformation.OSDescription;
        // OSDescription can be long and noisy; shorten to something the
        // gateway UI can display on its operator screens.
        if (platform.Length > 64)
        {
            platform = platform[..64];
        }

        return new ConnectClient(
            Id: OpenClawGatewayProtocol.ClientId,
            Version: clientVersion,
            Platform: platform,
            Mode: OpenClawGatewayProtocol.ClientMode,
            DisplayName: "Seren Hub",
            InstanceId: Environment.MachineName);
    }
}

