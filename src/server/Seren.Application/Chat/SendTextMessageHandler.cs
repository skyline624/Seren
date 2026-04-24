using System.Globalization;
using System.Text;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.Chat.Attachments;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.ValueObjects;

namespace Seren.Application.Chat;

/// <summary>
/// Handles <see cref="SendTextMessageCommand"/> by forwarding the user's
/// message to <see cref="IChatStreamPipeline"/> and contributing only the
/// domain-specific logic: parsing <c>&lt;think&gt;</c> tags and
/// <c>&lt;emotion:…&gt;</c> / <c>&lt;action:…&gt;</c> markers off the
/// streamed chunks, and broadcasting the decomposed envelopes
/// (<c>output:chat:chunk</c>, <c>avatar:emotion</c>, <c>avatar:action</c>,
/// thinking-start/end transitions) to connected peers.
/// </summary>
/// <remarks>
/// Transport-level concerns (timeout, retry, fallback, stream-end, error
/// envelopes, metrics) all live in the shared pipeline — this class stays
/// focused on the "what does a text turn <i>mean</i>" domain logic.
/// <para/>
/// Attachments: validated up-front. Images flow verbatim into the pipeline
/// (then on to OpenClaw as <c>chat.send.attachments</c>); documents are
/// extracted into text via <see cref="IAttachmentTextExtractorRegistry"/>
/// and concatenated to the user message so the LLM sees their content
/// even when the chosen model is text-only. A validation failure is sent
/// as a typed error to the originating peer; the multi-device echo is
/// only broadcast once validation passes, preventing ghost bubbles.
/// </remarks>
public sealed class SendTextMessageHandler : ICommandHandler<SendTextMessageCommand>
{
    private readonly IChatStreamPipeline _pipeline;
    private readonly ICharacterRepository _characterRepository;
    private readonly ISerenHub _hub;
    private readonly IChatSessionKeyProvider _sessionKeyProvider;
    private readonly IAttachmentValidator _attachmentValidator;
    private readonly IAttachmentTextExtractorRegistry _extractorRegistry;
    private readonly ILogger<SendTextMessageHandler> _logger;

    public SendTextMessageHandler(
        IChatStreamPipeline pipeline,
        ICharacterRepository characterRepository,
        ISerenHub hub,
        IChatSessionKeyProvider sessionKeyProvider,
        IAttachmentValidator attachmentValidator,
        IAttachmentTextExtractorRegistry extractorRegistry,
        ILogger<SendTextMessageHandler> logger)
    {
        _pipeline = pipeline;
        _characterRepository = characterRepository;
        _hub = hub;
        _sessionKeyProvider = sessionKeyProvider;
        _attachmentValidator = attachmentValidator;
        _extractorRegistry = extractorRegistry;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(SendTextMessageCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidatedAttachmentBundle bundle;
        try
        {
            bundle = _attachmentValidator.Validate(request.Attachments);
        }
        catch (AttachmentValidationException ex)
        {
            _logger.LogWarning(
                "Attachment validation failed: code={Code}, message={Message}", ex.Code, ex.Message);
            await EmitValidationErrorAsync(request.PeerId, ex, cancellationToken).ConfigureAwait(false);
            return Unit.Value;
        }

        // Document attachments don't have a native channel through OpenClaw's
        // chat.send — extract their text server-side and fold the result into
        // the user message so the LLM sees the content.
        var effectiveText = await ComposeEffectiveTextAsync(
            request.Text, bundle.Documents, cancellationToken).ConfigureAwait(false);

        var character = await _characterRepository.GetActiveAsync(cancellationToken);
        var sessionKey = _sessionKeyProvider.MainSessionKey;
        var characterId = character?.Id.ToString();
        var effectiveAgentId = request.Model ?? character?.AgentId;

        _logger.LogInformation(
            "Starting chat for session {SessionKey} (agentId={AgentId}, images={ImageCount}, docs={DocCount})",
            sessionKey, effectiveAgentId, bundle.Images.Count, bundle.Documents.Count);

        // Echo the user turn to every other peer now that validation has
        // passed — peers see the message + its attachment metadata before
        // the LLM reply starts arriving. Skipped for empty-text-no-attachment
        // requests (the downstream validator will reject them anyway).
        var echoBundle = BuildEchoMetadata(bundle);
        if (!string.IsNullOrWhiteSpace(request.Text) || echoBundle.Count > 0)
        {
            await BroadcastEchoAsync(
                request, echoBundle, cancellationToken).ConfigureAwait(false);
        }

        // Images flow as structured attachments on chat.send.
        var imagePayloads = bundle.Images.Count == 0
            ? null
            : bundle.Images
                .Select(i => new ChatImageAttachment(i.MimeType, i.FileName, i.Content))
                .ToList();

        var streamState = new StreamState();

        var pipelineRequest = new ChatStreamRequest(
            SessionKey: sessionKey,
            UserText: effectiveText,
            PrimaryModel: effectiveAgentId,
            ClientMessageId: request.ClientMessageId,
            CharacterId: characterId,
            OnContent: (content, ct) => ProcessContentAsync(streamState, characterId, content, ct),
            OnTeardown: ct => FlushStateAsync(streamState, characterId, ct),
            OnSuccess: null,
            ImageAttachments: imagePayloads);

        await _pipeline.RunAsync(pipelineRequest, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }

    /// <summary>
    /// Concatenate extracted document text to the user's prompt. Each
    /// document is labelled with filename + MIME so the LLM can anchor its
    /// reply back to the right source. Extraction failures surface a note
    /// in-band rather than aborting the whole message.
    /// </summary>
    private async Task<string> ComposeEffectiveTextAsync(
        string userText,
        IReadOnlyList<ValidatedAttachment> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return userText;
        }

        var builder = new StringBuilder(userText);
        foreach (var doc in documents)
        {
            var extractor = _extractorRegistry.Resolve(doc.MimeType);
            builder.Append("\n\n--- Attachment: ")
                .Append(doc.FileName)
                .Append(" (")
                .Append(doc.MimeType)
                .Append(") ---\n");

            if (extractor is null)
            {
                builder.Append(
                    CultureInfo.InvariantCulture,
                    $"[note: no extractor registered for '{doc.MimeType}'; content skipped]");
                _logger.LogWarning(
                    "No text extractor registered for MIME '{Mime}' (file={File})",
                    doc.MimeType, doc.FileName);
                continue;
            }

            try
            {
                var text = await extractor.ExtractAsync(doc.Content, doc.FileName, cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text))
                {
                    builder.Append(
                        CultureInfo.InvariantCulture,
                        $"[note: '{doc.FileName}' contained no extractable text]");
                }
                else
                {
                    builder.Append(text);
                }
            }
            catch (AttachmentExtractionException ex)
            {
                builder.Append(
                    CultureInfo.InvariantCulture,
                    $"[note: '{doc.FileName}' could not be parsed — {ex.Message}]");
                _logger.LogWarning(ex,
                    "Extraction failed for '{File}' ({Mime})", doc.FileName, doc.MimeType);
            }
        }

        return builder.ToString();
    }

    private static List<ChatAttachmentMetadataDto> BuildEchoMetadata(ValidatedAttachmentBundle bundle)
    {
        if (bundle.IsEmpty)
        {
            return [];
        }

        return bundle.Images.Concat(bundle.Documents)
            .Select(a => new ChatAttachmentMetadataDto
            {
                MimeType = a.MimeType,
                FileName = a.FileName,
                ByteSize = a.Content.LongLength,
                AttachmentId = Guid.NewGuid().ToString("N"),
            })
            .ToList();
    }

    private async Task BroadcastEchoAsync(
        SendTextMessageCommand request,
        List<ChatAttachmentMetadataDto> attachments,
        CancellationToken cancellationToken)
    {
        var messageId = string.IsNullOrWhiteSpace(request.ClientMessageId)
            ? Guid.NewGuid().ToString("N")
            : request.ClientMessageId;

        var payload = new UserEchoPayload
        {
            MessageId = messageId,
            Text = request.Text,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Attachments = attachments.Count > 0 ? attachments : null,
        };

        var envelope = CreateEnvelope(EventTypes.OutputChatUser, payload);

        PeerId? excluding = string.IsNullOrWhiteSpace(request.PeerId)
            ? null
            : new PeerId(request.PeerId);

        await _hub.BroadcastAsync(envelope, excluding, cancellationToken).ConfigureAwait(false);
    }

    private async Task EmitValidationErrorAsync(
        string? peerId,
        AttachmentValidationException ex,
        CancellationToken cancellationToken)
    {
        var payload = new ErrorPayload
        {
            Code = ex.Code,
            Message = ex.Message,
            Category = StreamErrorCategory.Permanent,
        };
        var envelope = CreateEnvelope(EventTypes.Error, payload);

        if (!string.IsNullOrWhiteSpace(peerId))
        {
            await _hub.SendAsync(new PeerId(peerId), envelope, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await _hub.BroadcastAsync(envelope, null, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Mutable state threaded through <see cref="OnContent"/> calls and the
    /// final teardown. Lives on the stack of a single <see cref="Handle"/>
    /// invocation — no thread-safety concerns.
    /// </summary>
    private sealed class StreamState
    {
        public bool IsThinking;
        public string TextBuffer = string.Empty;
        public string MarkerBuffer = string.Empty;
    }

    private async Task ProcessContentAsync(
        StreamState state, string? characterId, string content, CancellationToken ct)
    {
        state.TextBuffer += content;
        var (visibleText, thinkingTransition) = ExtractThinkingSegments(state.TextBuffer, ref state.IsThinking);
        state.TextBuffer = thinkingTransition.Remainder;

        foreach (var transition in thinkingTransition.Events)
        {
            await _hub.BroadcastAsync(
                CreateEnvelope(
                    transition ? EventTypes.OutputChatThinkingStart : EventTypes.OutputChatThinkingEnd,
                    new ChatEndPayload { CharacterId = characterId }),
                null, ct).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(visibleText))
        {
            return;
        }

        state.MarkerBuffer += visibleText;
        var safeCut = SafeMarkerBoundary(state.MarkerBuffer);
        var readyForParse = state.MarkerBuffer[..safeCut];
        state.MarkerBuffer = state.MarkerBuffer[safeCut..];
        if (string.IsNullOrEmpty(readyForParse))
        {
            return;
        }

        await BroadcastParsed(readyForParse, characterId, ct).ConfigureAwait(false);
    }

    private async Task FlushStateAsync(
        StreamState state, string? characterId, CancellationToken ct)
    {
        // Flush the last buffered text (trailing characters after the last
        // marker, or a dangling "<" that never completed).
        if (!string.IsNullOrEmpty(state.MarkerBuffer))
        {
            await BroadcastParsed(state.MarkerBuffer, characterId, ct).ConfigureAwait(false);
            state.MarkerBuffer = string.Empty;
        }

        // Close any open thinking state so UIs stop animating dots.
        if (state.IsThinking)
        {
            await _hub.BroadcastAsync(
                CreateEnvelope(
                    EventTypes.OutputChatThinkingEnd,
                    new ChatEndPayload { CharacterId = characterId }),
                null, ct).ConfigureAwait(false);
            state.IsThinking = false;
        }
    }

    private async Task BroadcastParsed(string text, string? characterId, CancellationToken ct)
    {
        var parseResult = LlmMarkerParser.Parse(text);

        if (!string.IsNullOrEmpty(parseResult.CleanText))
        {
            await _hub.BroadcastAsync(
                CreateEnvelope(
                    EventTypes.OutputChatChunk,
                    new ChatChunkPayload { Content = parseResult.CleanText, CharacterId = characterId }),
                null, ct).ConfigureAwait(false);
        }

        foreach (var emotion in parseResult.Emotions)
        {
            await _hub.BroadcastAsync(
                CreateEnvelope(
                    EventTypes.AvatarEmotion,
                    new AvatarEmotionPayload { Emotion = emotion.Emotion, CharacterId = characterId }),
                null, ct).ConfigureAwait(false);
        }

        foreach (var action in parseResult.Actions)
        {
            await _hub.BroadcastAsync(
                CreateEnvelope(
                    EventTypes.AvatarAction,
                    new AvatarActionPayload { Action = action.Action, CharacterId = characterId }),
                null, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Scans <paramref name="buffer"/> for <c>&lt;think&gt;</c> / <c>&lt;/think&gt;</c>
    /// tags that may be split across streamed chunks. Returns the visible
    /// portion ready to forward, the unconsumed suffix for the next chunk,
    /// and the transitions (true = entered, false = left).
    /// </summary>
    private static (string Visible, ThinkingTransition Transition) ExtractThinkingSegments(
        string buffer, ref bool isThinking)
    {
        var visible = new StringBuilder();
        var transitions = new List<bool>();
        var index = 0;

        while (index < buffer.Length)
        {
            if (isThinking)
            {
                var close = buffer.IndexOf("</think>", index, StringComparison.Ordinal);
                if (close < 0)
                {
                    index = buffer.Length;
                    break;
                }
                index = close + "</think>".Length;
                isThinking = false;
                transitions.Add(false);
            }
            else
            {
                var open = buffer.IndexOf("<think>", index, StringComparison.Ordinal);
                if (open < 0)
                {
                    var safeEnd = SafeVisibleEnd(buffer, index);
                    visible.Append(buffer, index, safeEnd - index);
                    index = safeEnd;
                    break;
                }
                visible.Append(buffer, index, open - index);
                index = open + "<think>".Length;
                isThinking = true;
                transitions.Add(true);
            }
        }

        return (visible.ToString(), new ThinkingTransition(transitions, buffer[index..]));
    }

    private static int SafeVisibleEnd(string buffer, int start)
    {
        const string tag = "<think>";
        for (var prefixLen = Math.Min(tag.Length - 1, buffer.Length - start); prefixLen > 0; prefixLen--)
        {
            var candidateStart = buffer.Length - prefixLen;
            if (candidateStart < start)
            {
                break;
            }

            var span = buffer.AsSpan(candidateStart, prefixLen);
            if (span.SequenceEqual(tag.AsSpan(0, prefixLen)))
            {
                return candidateStart;
            }
        }
        return buffer.Length;
    }

    private sealed record ThinkingTransition(IReadOnlyList<bool> Events, string Remainder);

    private static int SafeMarkerBoundary(string buffer)
    {
        var lastOpen = buffer.LastIndexOf('<');
        if (lastOpen < 0)
        {
            return buffer.Length;
        }

        var matchingClose = buffer.IndexOf('>', lastOpen);
        return matchingClose >= 0 ? buffer.Length : lastOpen;
    }

    private static WebSocketEnvelope CreateEnvelope(string eventType, object payload)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType(), CamelCaseOptions);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.Clone();

        return new WebSocketEnvelope
        {
            Type = eventType,
            Data = data,
            Metadata = new EventMetadata
            {
                Source = HubSource,
                Event = new EventIdentity { Id = Guid.NewGuid().ToString("N") },
            },
        };
    }

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly ModuleIdentityDto HubSource = new() { Id = "seren-hub", PluginId = "seren" };
}
