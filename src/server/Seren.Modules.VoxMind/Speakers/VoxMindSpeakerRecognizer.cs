using Seren.Application.Abstractions;

namespace Seren.Modules.VoxMind.Speakers;

/// <summary>
/// Adapter from the application-level <see cref="ISpeakerRecognizer"/>
/// abstraction to the VoxMind module's
/// <see cref="ISpeakerIdentificationService"/>. Lets the application
/// layer stay agnostic of sherpa-onnx + EF Core while the module owns the
/// concrete implementation.
/// </summary>
/// <remarks>
/// The adapter folds the rich
/// <see cref="SpeakerIdentificationOutcome"/> states down to the simple
/// "tagged or not tagged" shape the pipeline cares about. Telemetry stays
/// inside the module via <c>VoxMindMetrics</c>; the application doesn't
/// see the outcome distinction.
/// </remarks>
public sealed class VoxMindSpeakerRecognizer : ISpeakerRecognizer
{
    private readonly ISpeakerIdentificationService _service;

    public VoxMindSpeakerRecognizer(ISpeakerIdentificationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    public async Task<SpeakerRecognitionResult> RecognizeAsync(
        byte[] audioData, CancellationToken ct = default)
    {
        if (audioData is null || audioData.Length == 0)
        {
            return SpeakerRecognitionResult.NotIdentified;
        }

        var result = await _service.IdentifyFromAudioAsync(audioData, ct).ConfigureAwait(false);
        if (!result.HasSpeaker)
        {
            return new SpeakerRecognitionResult(null, null, result.Confidence);
        }

        return new SpeakerRecognitionResult(
            SpeakerId: result.ProfileId!.Value.ToString("N"),
            SpeakerName: result.SpeakerName,
            Confidence: result.Confidence);
    }
}
