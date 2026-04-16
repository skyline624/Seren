using System.Net.Http.Headers;
using System.Text.Json;
using Seren.Application.Abstractions;

namespace Seren.Infrastructure.Audio;

/// <summary>
/// OpenAI Whisper-based implementation of <see cref="ISttProvider"/>.
/// </summary>
public sealed class OpenAiSttProvider : ISttProvider
{
    private readonly HttpClient _httpClient;
    private readonly AudioOptions _options;

    public OpenAiSttProvider(HttpClient httpClient, AudioOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<SttResult> TranscribeAsync(
        byte[] audioData,
        string format,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audioData);
        ArgumentNullException.ThrowIfNull(format);

        using var content = new MultipartFormDataContent();
        using var audioContent = new ByteArrayContent(audioData);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue(GetMediaType(format));
        content.Add(audioContent, "file", $"audio.{format}");
        content.Add(new StringContent(_options.SttModel), "model");
        content.Add(new StringContent(format), "response_format");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.OpenAiBaseUrl}/audio/transcriptions")
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAiApiKey);

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<WhisperResponse>(
                json, WhisperJsonOptions, cancellationToken: ct)
            .ConfigureAwait(false);

        return new SttResult(result?.Text ?? string.Empty, result?.Language);
    }

    private static string GetMediaType(string format) => format.ToLowerInvariant() switch
    {
        "wav" => "audio/wav",
        "mp3" => "audio/mpeg",
        "ogg" => "audio/ogg",
        "webm" => "audio/webm",
        _ => "application/octet-stream",
    };

    private sealed class WhisperResponse
    {
        public string? Text { get; set; }
        public string? Language { get; set; }
    }

    private static readonly JsonSerializerOptions WhisperJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
