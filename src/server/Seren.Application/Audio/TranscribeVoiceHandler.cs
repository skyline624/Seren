using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.ValueObjects;

namespace Seren.Application.Audio;

/// <summary>
/// Handles <see cref="TranscribeVoiceCommand"/>: runs the audio through
/// <see cref="ISttProvider"/> and unicasts the result back to the
/// originating peer. Idempotent — silent or unintelligible audio yields a
/// payload with empty <c>Text</c>; the UI decides how to react.
/// </summary>
public sealed class TranscribeVoiceHandler : ICommandHandler<TranscribeVoiceCommand>
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly ModuleIdentityDto HubSource = new() { Id = "seren-hub", PluginId = "seren" };

    private readonly ISttProvider _sttProvider;
    private readonly ISerenHub _hub;
    private readonly ILogger<TranscribeVoiceHandler> _logger;

    public TranscribeVoiceHandler(
        ISttProvider sttProvider,
        ISerenHub hub,
        ILogger<TranscribeVoiceHandler> logger)
    {
        _sttProvider = sttProvider;
        _hub = hub;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(TranscribeVoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var transcription = await _sttProvider
            .TranscribeAsync(
                command.AudioData,
                command.Format,
                command.SttEngine,
                command.SttLanguage,
                cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrEmpty(transcription.ErrorCode))
        {
            _logger.LogWarning(
                "Voice dictation STT failed (code={Code}, message={Message}, engine={Engine}, audioBytes={AudioBytes}, requestId={RequestId}).",
                transcription.ErrorCode, transcription.ErrorMessage, command.SttEngine,
                command.AudioData.Length, command.RequestId);
        }
        else
        {
            _logger.LogInformation(
                "Voice dictation transcribed: '{Text}' (length={Length}, language={Language}, audioBytes={AudioBytes}, requestId={RequestId})",
                transcription.Text, transcription.Text.Length, transcription.Language,
                command.AudioData.Length, command.RequestId);
        }

        if (string.IsNullOrWhiteSpace(command.PeerId))
        {
            // Without a peer id we cannot unicast; this should not happen
            // in production (the WS session processor always sets it).
            _logger.LogWarning("TranscribeVoiceCommand has no PeerId — dropping transcript reply.");
            return Unit.Value;
        }

        // Always send the reply — even on error — because the UI's
        // pending dictate promise must resolve (or reject) on every
        // request. ErrorCode/ErrorMessage carry the typed failure so
        // the client can surface a localized message instead of a
        // ghost "empty transcription".
        var payload = new VoiceTranscriptPayload
        {
            RequestId = command.RequestId,
            Text = transcription.Text,
            Language = transcription.Language,
            Confidence = transcription.Confidence,
            ErrorCode = transcription.ErrorCode,
            ErrorMessage = transcription.ErrorMessage,
        };

        var json = JsonSerializer.Serialize(payload, payload.GetType(), CamelCaseOptions);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.Clone();

        var envelope = new WebSocketEnvelope
        {
            Type = EventTypes.OutputVoiceTranscript,
            Data = data,
            Metadata = new EventMetadata
            {
                Source = HubSource,
                Event = new EventIdentity { Id = Guid.NewGuid().ToString("N") },
            },
        };

        await _hub.SendAsync(new PeerId(command.PeerId), envelope, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}
