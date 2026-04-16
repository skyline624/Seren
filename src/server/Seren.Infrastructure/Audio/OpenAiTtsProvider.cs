using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Seren.Application.Abstractions;

namespace Seren.Infrastructure.Audio;

/// <summary>
/// OpenAI TTS-based implementation of <see cref="ITtsProvider"/>.
/// </summary>
public sealed class OpenAiTtsProvider : ITtsProvider
{
    private readonly HttpClient _httpClient;
    private readonly AudioOptions _options;

    public OpenAiTtsProvider(HttpClient httpClient, AudioOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TtsChunk> SynthesizeAsync(
        string text,
        string? voice = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var request = new
        {
            model = _options.TtsModel,
            input = text,
            voice = voice ?? _options.TtsVoice,
            response_format = "mp3",
        };

        var json = JsonSerializer.Serialize(request);
        using var content = new StringContent(json, new MediaTypeHeaderValue("application/json"));

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_options.OpenAiBaseUrl}/audio/speech")
        {
            Content = content,
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAiApiKey);

        var response = await _httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var audioBytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        yield return new TtsChunk(audioBytes, "mp3");
    }
}
