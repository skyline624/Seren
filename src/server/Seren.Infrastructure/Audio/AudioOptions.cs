namespace Seren.Infrastructure.Audio;

/// <summary>
/// Options for the audio (STT/TTS) subsystem, bound from the
/// <c>Audio</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class AudioOptions
{
    public const string SectionName = "Audio";

    /// <summary>Provider name for speech-to-text (default: "openai").</summary>
    public string SttProvider { get; set; } = "openai";

    /// <summary>Provider name for text-to-speech (default: "openai").</summary>
    public string TtsProvider { get; set; } = "openai";

    /// <summary>Base URL for the OpenAI-compatible audio API.</summary>
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>API key for the OpenAI-compatible audio API.</summary>
    public string OpenAiApiKey { get; set; } = string.Empty;

    /// <summary>Model name for STT (default: "whisper-1").</summary>
    public string SttModel { get; set; } = "whisper-1";

    /// <summary>Model name for TTS (default: "tts-1").</summary>
    public string TtsModel { get; set; } = "tts-1";

    /// <summary>Voice name for TTS (default: "nova").</summary>
    public string TtsVoice { get; set; } = "nova";
}
