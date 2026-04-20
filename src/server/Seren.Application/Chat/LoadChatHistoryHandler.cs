using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.OpenClaw.Handlers;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;

namespace Seren.Application.Chat;

/// <summary>
/// Handles <see cref="LoadChatHistoryCommand"/> by reading the persisted
/// transcript from OpenClaw and pushing each message to the requesting
/// peer. Terminates the burst with an <c>output:chat:history:end</c>
/// event carrying the cursor (<c>oldestMessageId</c>) and a
/// <c>hasMore</c> hint for further pagination.
/// </summary>
/// <remarks>
/// Upstream <c>chat.history</c> doesn't accept a "before" cursor, so we
/// over-fetch (limit × 3, capped) and filter client-side. For typical
/// usage (50 initial + 30 per scroll-back) this stays well under the
/// upstream payload caps.
/// </remarks>
public sealed class LoadChatHistoryHandler : ICommandHandler<LoadChatHistoryCommand>
{
    /// <summary>Multiplier applied to <see cref="LoadChatHistoryCommand.Limit"/> when paginating with a cursor.</summary>
    public const int PaginationOverFetchFactor = 3;

    /// <summary>Hard ceiling on the upstream fetch to keep payloads sane.</summary>
    public const int PaginationOverFetchCap = 500;

    private readonly IOpenClawHistory _history;
    private readonly ISerenHub _hub;
    private readonly ILogger<LoadChatHistoryHandler> _logger;

    public LoadChatHistoryHandler(
        IOpenClawHistory history,
        ISerenHub hub,
        ILogger<LoadChatHistoryHandler> logger)
    {
        _history = history;
        _hub = hub;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(LoadChatHistoryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit <= 0)
        {
            return Unit.Value;
        }

        var fetchLimit = request.Before is null
            ? request.Limit
            : Math.Min(request.Limit * PaginationOverFetchFactor, PaginationOverFetchCap);

        IReadOnlyList<ChatHistoryMessage> upstream;
        try
        {
            upstream = await _history.LoadAsync(fetchLimit, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Upstream may be temporarily unavailable (gateway reconnecting);
            // tell the peer the load failed so it doesn't spin forever.
            _logger.LogWarning(ex,
                "Failed to load chat history for peer {PeerId}; sending empty hydration",
                request.TargetPeer);
            await SendEndAsync(request.TargetPeer, hasMore: false, oldest: null, cancellationToken)
                .ConfigureAwait(false);
            return Unit.Value;
        }

        // Upstream returns oldest → newest. When the caller paginates with
        // `before`, slice off everything older than the cursor and keep the
        // last `Limit` items so the client receives the page right above the
        // cursor.
        var filtered = (request.Before is null
                ? upstream
                : (IReadOnlyList<ChatHistoryMessage>)upstream
                    .Where(m => string.CompareOrdinal(m.MessageId, request.Before) < 0)
                    .ToList())
            .ToList();

        var page = filtered.Count <= request.Limit
            ? filtered
            : filtered.GetRange(filtered.Count - request.Limit, request.Limit);

        foreach (var msg in page)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = new ChatHistoryItemPayload
            {
                MessageId = msg.MessageId,
                Role = msg.Role,
                Content = msg.Content,
                Timestamp = msg.Timestamp,
                Emotion = msg.Emotion,
            };
            await _hub.SendAsync(
                request.TargetPeer,
                OpenClawRelayEnvelope.Create(EventTypes.OutputChatHistoryItem, payload),
                cancellationToken).ConfigureAwait(false);
        }

        // hasMore is approximated: when we got back as many items as we
        // asked for at the upstream layer, assume there are more — the UI
        // can keep offering scroll-back. When upstream returned fewer than
        // we asked, we've reached the end of the persisted transcript.
        var hasMore = upstream.Count >= fetchLimit && page.Count > 0;
        await SendEndAsync(
            request.TargetPeer,
            hasMore,
            oldest: page.Count > 0 ? page[0].MessageId : null,
            cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Sent {Count} history items to peer {PeerId} (hasMore={HasMore}, before={Before})",
            page.Count, request.TargetPeer, hasMore, request.Before ?? "<none>");

        return Unit.Value;
    }

    private Task<bool> SendEndAsync(
        Domain.ValueObjects.PeerId peer, bool hasMore, string? oldest, CancellationToken ct)
    {
        var payload = new ChatHistoryEndPayload
        {
            HasMore = hasMore,
            OldestMessageId = oldest,
        };
        return _hub.SendAsync(
            peer,
            OpenClawRelayEnvelope.Create(EventTypes.OutputChatHistoryEnd, payload),
            ct);
    }
}
