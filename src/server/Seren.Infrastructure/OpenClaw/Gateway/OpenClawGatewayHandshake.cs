using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Seren.Infrastructure.OpenClaw.Identity;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// Single-shot coordinator that drives the OpenClaw gateway handshake:
/// waits for the pre-handshake <c>connect.challenge</c> event (to pick up
/// the gateway nonce), signs the V3 device-auth payload with Seren's
/// persistent Ed25519 identity, sends the <c>connect</c> request with the
/// device block (plus an optional bootstrap token for first-boot pairing),
/// and awaits the matching <c>hello-ok</c> response before returning the
/// negotiated policy.
/// </summary>
internal static class OpenClawGatewayHandshake
{
    /// <summary>
    /// Maximum time to wait for the pre-handshake <c>connect.challenge</c>
    /// event before falling back to a locally-generated nonce. Upstream
    /// servers typically send it &lt; 50 ms after the socket opens; 5 s is
    /// a safe ceiling for overloaded environments.
    /// </summary>
    internal static readonly TimeSpan ChallengeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Handshake modes. <see cref="HandshakeMode.Standard"/> requests the
    /// scopes Seren needs (<c>operator.write</c>); <see cref="HandshakeMode.BootstrapPairing"/>
    /// presents empty scopes and role <c>node</c>, which is the narrow shape
    /// OpenClaw's silent auto-approval flow expects on first boot.
    /// </summary>
    public enum HandshakeMode
    {
        Standard,
        BootstrapPairing,
    }

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
        IDeviceIdentityStore identityStore,
        string clientVersion,
        TimeSpan timeout,
        ILogger logger,
        CancellationToken cancellationToken,
        HandshakeMode mode = HandshakeMode.Standard)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(identityStore);
        ArgumentNullException.ThrowIfNull(logger);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var identity = await identityStore.LoadOrCreateAsync(timeoutCts.Token).ConfigureAwait(false);
        logger.LogDebug("Using device identity {DeviceId} for handshake", identity.DeviceId);

        // Step 1 — wait for the challenge event carrying the server nonce.
        // Some deployments may skip it; fall back to a locally-generated
        // nonce on timeout so the handshake still proceeds.
        var (nonce, bufferedResponses) = await CollectChallengeAsync(
            socket, logger, timeoutCts.Token).ConfigureAwait(false);

        // Step 2 — build the V3 auth payload, sign it, and send the connect
        // request. ClientId / mode / platform must match what we actually
        // declare in the connect body, otherwise the signature doesn't
        // verify on the server side (it rebuilds the payload verbatim).
        var clientInfo = BuildClientInfo(clientVersion);
        // BootstrapPairing mode matches OpenClaw's silent auto-approval
        // predicate: role=node + scopes=[] (see message-handler.ts:895-900).
        // Standard mode requests the operator scopes Seren actually needs.
        var isBootstrap = mode == HandshakeMode.BootstrapPairing;
        var role = isBootstrap ? "node" : "operator";
        var scopes = isBootstrap
            ? Array.Empty<string>()
            : OpenClawGatewayProtocol.BackendOperatorScopes;
        var signedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // The signed payload's `token` slot must match what the gateway will
        // pick up via resolveSignatureToken (handshake-auth-helpers.ts:218):
        // priority is auth.token → auth.deviceToken → auth.bootstrapToken.
        // In bootstrap mode we omit auth.token, so the server falls back to
        // bootstrapToken — and our signature must therefore use the bootstrap
        // token as the `token` slot too. In standard mode we send and sign the
        // shared-secret token instead.
        var token = isBootstrap
            ? (options.BootstrapToken ?? string.Empty)
            : (options.AuthToken ?? string.Empty);

        var payload = DeviceAuthPayloadBuilder.BuildV3(
            deviceId: identity.DeviceId,
            clientId: clientInfo.Id,
            clientMode: clientInfo.Mode,
            role: role,
            scopes: scopes,
            signedAtMs: signedAtMs,
            token: token,
            nonce: nonce,
            platform: clientInfo.Platform,
            deviceFamily: null);

        var signature = Ed25519Signer.Sign(identity.PrivateKey, payload);

        var device = new ConnectDevice(
            Id: identity.DeviceId,
            PublicKey: Base64UrlEncoder.Encode(identity.PublicKey),
            Signature: Base64UrlEncoder.Encode(signature),
            SignedAt: signedAtMs,
            Nonce: nonce);

        // Bootstrap token is only relevant to the pairing handshake — sending
        // it in standard mode risks consuming it unnecessarily if the device
        // is somehow not yet paired on that code path. In bootstrap mode we
        // *must not* also send the shared-secret token: upstream's
        // resolveAuthProvidedKind sees password > token > bootstrapToken in
        // priority order, so any non-empty `token` would mask the bootstrap
        // token and skip the silent auto-pairing path entirely.
        var auth = isBootstrap
            ? new ConnectAuth(Token: null, BootstrapToken: options.BootstrapToken)
            : new ConnectAuth(
                Token: string.IsNullOrWhiteSpace(options.AuthToken) ? null : options.AuthToken,
                BootstrapToken: null);

        var connectId = Guid.NewGuid().ToString("N");
        var paramsElement = JsonSerializer.SerializeToElement(
            new ConnectParams(
                MinProtocol: OpenClawGatewayProtocol.ProtocolVersion,
                MaxProtocol: OpenClawGatewayProtocol.ProtocolVersion,
                Client: clientInfo,
                Role: role,
                Scopes: scopes.Count == 0 ? null : scopes,
                Device: device,
                Auth: auth),
            OpenClawGatewayJsonContext.Default.ConnectParams);

        var request = new GatewayRequest(connectId, OpenClawGatewayProtocol.ConnectMethodName, paramsElement);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            request, OpenClawGatewayJsonContext.Default.GatewayRequest);

        logger.LogInformation(
            "Sending OpenClaw connect request (id={ConnectId}, protocol={Protocol}, deviceId={DeviceId}, mode={Mode}, hasBootstrap={HasBootstrap})",
            connectId,
            OpenClawGatewayProtocol.ProtocolVersion,
            identity.DeviceId,
            mode,
            auth.BootstrapToken is not null);

        await socket.SendTextAsync(bytes, timeoutCts.Token).ConfigureAwait(false);

        return await WaitForHelloOkAsync(
            socket, connectId, bufferedResponses, options, logger, timeoutCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Pumps pre-handshake frames until we either receive the
    /// <c>connect.challenge</c> event (returning its nonce) or
    /// <see cref="ChallengeTimeout"/> expires. Any <c>res</c> frames that
    /// arrive in this window are buffered and forwarded to the hello-ok
    /// waiter — otherwise a fast gateway could slip its response in
    /// before we finish reading.
    /// </summary>
    private static async Task<(string Nonce, List<string> BufferedResponses)> CollectChallengeAsync(
        IGatewaySocket socket,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ChallengeTimeout);

        var buffered = new List<string>();
        try
        {
            while (true)
            {
                cts.Token.ThrowIfCancellationRequested();
                var received = await socket.ReceiveTextAsync(cts.Token).ConfigureAwait(false);
                if (received.IsClose)
                {
                    throw new OpenClawGatewayException(
                        code: "handshake.closed",
                        message: $"Gateway closed the connection before issuing connect.challenge "
                               + $"(code={received.CloseStatus}, reason={received.CloseStatusDescription ?? "n/a"}).");
                }
                if (string.IsNullOrWhiteSpace(received.Text))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(received.Text);
                if (!doc.RootElement.TryGetProperty("type", out var typeProp)
                    || typeProp.ValueKind != JsonValueKind.String)
                {
                    logger.LogDebug("Skipping malformed pre-handshake frame: {Frame}", received.Text);
                    continue;
                }

                var type = typeProp.GetString();
                if (type == OpenClawGatewayProtocol.FrameTypeEvent)
                {
                    var ev = doc.RootElement.Deserialize(
                        OpenClawGatewayJsonContext.Default.GatewayEvent);
                    if (ev?.Event == OpenClawGatewayProtocol.ConnectChallengeEventName
                        && ev.Payload is { ValueKind: JsonValueKind.Object } challengePayload)
                    {
                        var challenge = challengePayload.Deserialize(
                            OpenClawGatewayJsonContext.Default.ConnectChallengePayload);
                        if (!string.IsNullOrEmpty(challenge?.Nonce))
                        {
                            logger.LogDebug("Received connect.challenge nonce from gateway");
                            return (challenge!.Nonce, buffered);
                        }
                    }
                    // Any other event — safe to ignore during the pre-handshake window.
                    continue;
                }

                if (type == OpenClawGatewayProtocol.FrameTypeResponse)
                {
                    // Unlikely (the server doesn't send a res until we send a req)
                    // but keep a copy so the hello-ok waiter can still see it.
                    buffered.Add(received.Text!);
                    continue;
                }

                logger.LogDebug("Skipping pre-handshake frame of type {Type}", type);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // ChallengeTimeout elapsed — fall back to a locally-generated nonce.
            // Upstream accepts any non-empty nonce as long as the signature
            // matches the same string, so this keeps us compatible with older
            // gateway builds that don't emit connect.challenge.
            var fallback = Guid.NewGuid().ToString("N");
            logger.LogInformation(
                "No connect.challenge received within {Timeout}; falling back to client-generated nonce",
                ChallengeTimeout);
            return (fallback, buffered);
        }
    }

    private static async Task<HelloOkPayload> WaitForHelloOkAsync(
        IGatewaySocket socket,
        string expectedId,
        List<string> bufferedResponses,
        OpenClawOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Drain any frames buffered during the challenge collection phase first.
        foreach (var bufferedFrame in bufferedResponses)
        {
            var result = TryConsume(bufferedFrame, expectedId, options, logger);
            if (result is not null)
            {
                return result;
            }
        }

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

            var result = TryConsume(received.Text!, expectedId, options, logger);
            if (result is not null)
            {
                return result;
            }
        }
    }

    /// <summary>
    /// Try to consume a frame during hello-ok waiting. Returns the parsed
    /// payload when the frame is our matching <c>res</c>; returns
    /// <c>null</c> when the frame is unrelated (and should be skipped);
    /// throws when the gateway rejects the handshake.
    /// </summary>
    private static HelloOkPayload? TryConsume(
        string frameText, string expectedId, OpenClawOptions options, ILogger logger)
    {
        using var doc = JsonDocument.Parse(frameText);
        if (!doc.RootElement.TryGetProperty("type", out var typeProp)
            || typeProp.ValueKind != JsonValueKind.String)
        {
            logger.LogDebug("Skipping malformed gateway frame during handshake: {Frame}", frameText);
            return null;
        }

        var type = typeProp.GetString();
        if (type == OpenClawGatewayProtocol.FrameTypeEvent)
        {
            // Late challenge / noise — ignore.
            return null;
        }

        if (type != OpenClawGatewayProtocol.FrameTypeResponse)
        {
            logger.LogDebug("Skipping frame of type {Type} during handshake", type);
            return null;
        }

        var response = doc.RootElement.Deserialize(
            OpenClawGatewayJsonContext.Default.GatewayResponse);
        if (response is null || !string.Equals(response.Id, expectedId, StringComparison.Ordinal))
        {
            logger.LogDebug(
                "Skipping unrelated response frame (id={Id}) during handshake",
                response?.Id);
            return null;
        }

        if (!response.Ok)
        {
            var code = response.Error?.Code ?? "handshake.rejected";
            var message = response.Error?.Message ?? "Gateway rejected the connect request.";
            if (string.Equals(code, "NOT_PAIRED", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(options.BootstrapToken))
            {
                message +=
                    " — this Seren device is not yet paired with OpenClaw. "
                    + "Generate a bootstrap token via `docker compose exec openclaw node /app/openclaw.mjs qr --setup-code-only --json`, "
                    + "set OPENCLAW_BOOTSTRAP_TOKEN in .env, and restart seren-api.";
            }
            throw new OpenClawGatewayException(
                code: code,
                message: message,
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
