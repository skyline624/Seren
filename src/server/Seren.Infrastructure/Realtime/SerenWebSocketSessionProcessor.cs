using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Application.Audio;
using Seren.Application.Chat;
using Seren.Application.Sessions;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Contracts.Json;
using Seren.Domain.Abstractions;
using Seren.Domain.Entities;
using Seren.Domain.ValueObjects;

namespace Seren.Infrastructure.Realtime;

/// <summary>
/// Handles a single WebSocket session from upgrade to close: creates a <see cref="Peer"/>,
/// registers it, reads incoming frames, dispatches them to Mediator handlers, emits
/// responses and errors, and finally unregisters on disconnect.
/// </summary>
/// <remarks>
/// Registered as a scoped service because it consumes <see cref="IMediator"/>, which
/// resolves scoped handler and validator services.
/// </remarks>
public sealed class SerenWebSocketSessionProcessor
{
    private const int ReceiveBufferSize = 4096;
    private const string HubPluginId = "seren.hub";
    private const string HubInstanceId = "seren-hub";

    private readonly IPeerRegistry _peers;
    private readonly IWebSocketConnectionRegistry _connections;
    private readonly ISerenHub _hub;
    private readonly IMediator _mediator;
    private readonly ITokenService _tokenService;
    private readonly IClock _clock;
    private readonly SerenHubOptions _hubOptions;
    private readonly ILogger<SerenWebSocketSessionProcessor> _logger;

    public SerenWebSocketSessionProcessor(
        IPeerRegistry peers,
        IWebSocketConnectionRegistry connections,
        ISerenHub hub,
        IMediator mediator,
        ITokenService tokenService,
        IClock clock,
        IOptions<SerenHubOptions> hubOptions,
        ILogger<SerenWebSocketSessionProcessor> logger)
    {
        _peers = peers;
        _connections = connections;
        _hub = hub;
        _mediator = mediator;
        _tokenService = tokenService;
        _clock = clock;
        _hubOptions = hubOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full session lifecycle for <paramref name="socket"/>.
    /// Returns when the peer disconnects, the <paramref name="cancellationToken"/> fires,
    /// or an unrecoverable error occurs.
    /// </summary>
    public async Task ProcessAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var peerId = PeerId.New();
        var peer = Peer.CreateNew(peerId, _clock.UtcNow, authRequired: false);

        _peers.Add(peer);
        _connections.TryRegister(peerId, socket);

        using var activity = Activity.Current?.Source.StartActivity("seren.ws.session");
        activity?.SetTag("seren.peer_id", peerId.Value);

        _logger.LogInformation("Peer {PeerId} connected", peerId);

        try
        {
            await SendHelloAsync(peerId, cancellationToken).ConfigureAwait(false);
            await ReceiveLoopAsync(socket, peerId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Peer {PeerId} session cancelled", peerId);
        }
        catch (WebSocketException ex)
        {
            _logger.LogDebug(ex, "Peer {PeerId} WebSocket closed abnormally", peerId);
        }
        finally
        {
            _connections.TryUnregister(peerId);
            _peers.Remove(peerId);
            _logger.LogInformation("Peer {PeerId} disconnected", peerId);

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "seren: session ended",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                    // best-effort close
                }
            }
        }
    }

    private async Task ReceiveLoopAsync(
        WebSocket socket,
        PeerId peerId,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            while (!cancellationToken.IsCancellationRequested
                   && socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();

                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    _logger.LogDebug(
                        "Ignoring non-text frame from peer {PeerId}", peerId);
                    continue;
                }

                await DispatchAsync(ms.ToArray(), peerId, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task DispatchAsync(
        byte[] frame,
        PeerId peerId,
        CancellationToken cancellationToken)
    {
        WebSocketEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(
                frame, SerenJsonContext.Default.WebSocketEnvelope);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Peer {PeerId} sent invalid JSON", peerId);
            await SendErrorAsync(
                peerId, "Invalid JSON payload.", parentEventId: null, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (envelope is null)
        {
            await SendErrorAsync(
                peerId, "Empty envelope.", parentEventId: null, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // Authentication gate: when RequireAuthentication is enabled, only
        // heartbeat and authenticate events are allowed from unauthenticated peers.
        if (_hubOptions.RequireAuthentication
            && envelope.Type is not EventTypes.TransportHeartbeat
            && envelope.Type is not EventTypes.ModuleAuthenticate)
        {
            if (_peers.TryGet(peerId, out var gatePeer) && gatePeer is not null && !gatePeer.IsAuthenticated)
            {
                await SendErrorAsync(
                    peerId, "Authentication required.", envelope.Metadata.Event.Id, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
        }

        try
        {
            switch (envelope.Type)
            {
                case EventTypes.TransportHeartbeat:
                    await HandleHeartbeatAsync(peerId, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case EventTypes.ModuleAuthenticate:
                    await HandleAuthenticateAsync(peerId, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case EventTypes.ModuleAnnounce:
                    await HandleAnnounceAsync(peerId, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case EventTypes.InputText:
                    await HandleTextInputAsync(peerId, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case EventTypes.InputVoice:
                    await HandleVoiceInputAsync(peerId, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case EventTypes.InputChatHistoryRequest:
                    await HandleChatHistoryRequestAsync(peerId, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case EventTypes.InputChatReset:
                    await HandleChatResetAsync(peerId, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                default:
                    _logger.LogDebug(
                        "Unhandled event '{Type}' from peer {PeerId}",
                        envelope.Type, peerId);
                    break;
            }
        }
        catch (ValidationException vex)
        {
            _logger.LogWarning(
                vex,
                "Validation failed for envelope '{Type}' from peer {PeerId}",
                envelope.Type, peerId);
            var message = vex.Errors.FirstOrDefault()?.ErrorMessage ?? "Validation failed.";
            await SendErrorAsync(peerId, message, envelope.Metadata.Event.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException opex)
        {
            _logger.LogWarning(
                opex,
                "Handler rejected envelope '{Type}' from peer {PeerId}",
                envelope.Type, peerId);
            await SendErrorAsync(peerId, opex.Message, envelope.Metadata.Event.Id, cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Catch general exception — session must survive handler failures
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception processing envelope '{Type}' from peer {PeerId}",
                envelope.Type, peerId);
            await SendErrorAsync(peerId, "Internal server error.", envelope.Metadata.Event.Id, cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning restore CA1031
    }

    private async Task HandleAnnounceAsync(
        PeerId peerId,
        WebSocketEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var payload = envelope.Data.Deserialize(SerenJsonContext.Default.AnnouncePayload);
        if (payload is null)
        {
            await SendErrorAsync(
                peerId,
                "module:announce payload is required.",
                envelope.Metadata.Event.Id,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var command = new AnnouncePeerCommand(peerId, payload, envelope.Metadata.Event.Id);
        var announced = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);

        var responseEnvelope = new WebSocketEnvelope
        {
            Type = EventTypes.ModuleAnnounced,
            Data = JsonSerializer.SerializeToElement(
                announced, SerenJsonContext.Default.AnnouncedPayload),
            Metadata = CreateServerMetadata(envelope.Metadata.Event.Id),
        };

        await _hub.SendAsync(peerId, responseEnvelope, cancellationToken).ConfigureAwait(false);
        await _hub.BroadcastAsync(responseEnvelope, excluding: peerId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleAuthenticateAsync(
        PeerId peerId,
        WebSocketEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var payload = envelope.Data.Deserialize(SerenJsonContext.Default.AuthenticatePayload);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Token))
        {
            await SendErrorAsync(
                peerId,
                "module:authenticate requires a token.",
                envelope.Metadata.Event.Id,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var principal = _tokenService.ValidateToken(payload.Token);
        if (principal is null)
        {
            _logger.LogWarning("Peer {PeerId} authentication failed: invalid token", peerId);
            await SendErrorAsync(
                peerId,
                "Authentication failed: invalid or expired token.",
                envelope.Metadata.Event.Id,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        // Mark peer as authenticated
        if (_peers.TryGet(peerId, out var peer) && peer is not null)
        {
            _peers.Update(peer.Authenticate());
        }

        var role = principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        _logger.LogInformation("Peer {PeerId} authenticated (role={Role})", peerId, role ?? "none");

        var responseEnvelope = new WebSocketEnvelope
        {
            Type = EventTypes.ModuleAuthenticated,
            Data = JsonSerializer.SerializeToElement(
                new AuthenticatedPayload { PeerId = peerId.Value, Role = role },
                SerenJsonContext.Default.AuthenticatedPayload),
            Metadata = CreateServerMetadata(envelope.Metadata.Event.Id),
        };

        await _hub.SendAsync(peerId, responseEnvelope, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleTextInputAsync(
        PeerId peerId,
        WebSocketEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var payload = envelope.Data.Deserialize(SerenJsonContext.Default.TextInputPayload);
        if (payload is null)
        {
            await SendErrorAsync(
                peerId,
                "input:text payload is required.",
                envelope.Metadata.Event.Id,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogDebug(
            "Peer {PeerId} sent text input ({Length} chars)",
            peerId, payload.Text.Length);

        var command = new SendTextMessageCommand(
            payload.Text,
            payload.SessionId,
            peerId.Value,
            payload.Model);

        await _mediator.Send(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleVoiceInputAsync(
        PeerId peerId,
        WebSocketEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var payload = envelope.Data.Deserialize(SerenJsonContext.Default.VoiceInputPayload);
        if (payload is null)
        {
            await SendErrorAsync(
                peerId,
                "input:voice payload is required.",
                envelope.Metadata.Event.Id,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogDebug(
            "Peer {PeerId} sent voice input ({Bytes} bytes, format={Format})",
            peerId, payload.AudioData.Length, payload.Format);

        var command = new SubmitVoiceInputCommand(
            payload.AudioData,
            payload.Format,
            payload.SessionId,
            peerId.Value,
            payload.Model);

        await _mediator.Send(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleChatHistoryRequestAsync(
        PeerId peerId,
        WebSocketEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var payload = envelope.Data.Deserialize(SerenJsonContext.Default.ChatHistoryRequestPayload)
                      ?? new ChatHistoryRequestPayload();

        // Default page sizes: 50 for the initial empty hydration request,
        // 30 for paginated scroll-back. Cap defensively at 200 to keep
        // any malicious / over-eager client from triggering a huge upstream
        // call.
        var defaultLimit = payload.Before is null ? 50 : 30;
        var limit = Math.Clamp(payload.Limit ?? defaultLimit, 1, 200);

        _logger.LogDebug(
            "Peer {PeerId} requested chat history (before={Before}, limit={Limit})",
            peerId, payload.Before ?? "<none>", limit);

        await _mediator.Send(
            new LoadChatHistoryCommand(peerId, payload.Before, limit),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleChatResetAsync(PeerId peerId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Peer {PeerId} requested chat session reset", peerId);
        await _mediator.Send(new ResetChatSessionCommand(), cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleHeartbeatAsync(
        PeerId peerId,
        WebSocketEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (_peers.TryGet(peerId, out var peer) && peer is not null)
        {
            _peers.Update(peer.Beat(_clock.UtcNow));
        }

        var incoming = envelope.Data.Deserialize(SerenJsonContext.Default.HeartbeatPayload);
        if (incoming?.Kind != "ping")
        {
            return;
        }

        var pong = new HeartbeatPayload
        {
            Kind = "pong",
            At = _clock.UtcNow.ToUnixTimeMilliseconds(),
        };

        var pongEnvelope = new WebSocketEnvelope
        {
            Type = EventTypes.TransportHeartbeat,
            Data = JsonSerializer.SerializeToElement(pong, SerenJsonContext.Default.HeartbeatPayload),
            Metadata = CreateServerMetadata(envelope.Metadata.Event.Id),
        };

        await _hub.SendAsync(peerId, pongEnvelope, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendHelloAsync(PeerId peerId, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse("{}");
        var envelope = new WebSocketEnvelope
        {
            Type = EventTypes.TransportHello,
            Data = doc.RootElement.Clone(),
            Metadata = CreateServerMetadata(parentEventId: null),
        };

        await _hub.SendAsync(peerId, envelope, cancellationToken).ConfigureAwait(false);
    }

    private Task<bool> SendErrorAsync(
        PeerId peerId,
        string message,
        string? parentEventId,
        CancellationToken cancellationToken)
    {
        var payload = new ErrorPayload { Message = message };
        var envelope = new WebSocketEnvelope
        {
            Type = EventTypes.Error,
            Data = JsonSerializer.SerializeToElement(payload, SerenJsonContext.Default.ErrorPayload),
            Metadata = CreateServerMetadata(parentEventId),
        };

        return _hub.SendAsync(peerId, envelope, cancellationToken);
    }

    private static EventMetadata CreateServerMetadata(string? parentEventId) => new()
    {
        Source = new ModuleIdentityDto
        {
            Id = HubInstanceId,
            PluginId = HubPluginId,
            Version = "0.1.0",
        },
        Event = new EventIdentity
        {
            Id = Guid.NewGuid().ToString("N"),
            ParentId = parentEventId,
        },
    };
}
