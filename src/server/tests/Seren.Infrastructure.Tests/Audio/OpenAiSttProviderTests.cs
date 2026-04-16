using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NSubstitute;
using Seren.Application.Abstractions;
using Seren.Infrastructure.Audio;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.Audio;

public sealed class OpenAiSttProviderTests
{
    private static readonly AudioOptions ValidOptions = new()
    {
        OpenAiBaseUrl = "https://api.openai.com/v1",
        OpenAiApiKey = "test-api-key",
        SttModel = "whisper-1",
    };

    [Fact]
    public async Task TranscribeAsync_ShouldSendMultipartRequestAndParseResponse()
    {
        // arrange
        var whisperResponse = JsonSerializer.Serialize(new { text = "Hello world", language = "en" });
        var handler = new MockHttpMessageHandler(whisperResponse, "application/json");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com"),
        };

        var options = ValidOptions;
        var provider = new OpenAiSttProvider(httpClient, options);
        var audioData = Encoding.UTF8.GetBytes("fake-audio-data");

        // act
        var result = await provider.TranscribeAsync(audioData, "wav", CancellationToken.None);

        // assert
        result.Text.ShouldBe("Hello world");
        result.Language.ShouldBe("en");
    }

    [Fact]
    public async Task TranscribeAsync_ShouldSetBearerAuthHeader()
    {
        // arrange
        var whisperResponse = JsonSerializer.Serialize(new { text = "test", language = "en" });
        var handler = new MockHttpMessageHandler(whisperResponse, "application/json");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com"),
        };

        var options = ValidOptions;
        var provider = new OpenAiSttProvider(httpClient, options);
        var audioData = Encoding.UTF8.GetBytes("fake-audio");

        // act
        await provider.TranscribeAsync(audioData, "wav", CancellationToken.None);

        // assert
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest!.Headers.Authorization.ShouldNotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        handler.LastRequest.Headers.Authorization!.Parameter.ShouldBe("test-api-key");
    }

    [Fact]
    public async Task TranscribeAsync_ShouldPostToTranscriptionsEndpoint()
    {
        // arrange
        var whisperResponse = JsonSerializer.Serialize(new { text = "test", language = "en" });
        var handler = new MockHttpMessageHandler(whisperResponse, "application/json");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com"),
        };

        var options = ValidOptions;
        var provider = new OpenAiSttProvider(httpClient, options);
        var audioData = Encoding.UTF8.GetBytes("fake-audio");

        // act
        await provider.TranscribeAsync(audioData, "wav", CancellationToken.None);

        // assert
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest!.RequestUri.ShouldNotBeNull();
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe("/v1/audio/transcriptions");
        handler.LastRequest.Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task TranscribeAsync_ShouldSendMultipartFormDataContent()
    {
        // arrange
        var whisperResponse = JsonSerializer.Serialize(new { text = "test", language = "en" });
        var handler = new MockHttpMessageHandler(whisperResponse, "application/json");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com"),
        };

        var options = ValidOptions;
        var provider = new OpenAiSttProvider(httpClient, options);
        var audioData = Encoding.UTF8.GetBytes("fake-audio");

        // act
        await provider.TranscribeAsync(audioData, "wav", CancellationToken.None);

        // assert
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest!.Content.ShouldNotBeNull();
        handler.LastRequest.Content!.GetType().Name.ShouldBe("MultipartFormDataContent");
    }

    [Fact]
    public async Task TranscribeAsync_ShouldReturnEmptyText_WhenResponseHasNoText()
    {
        // arrange
        var whisperResponse = JsonSerializer.Serialize(new { text = (string?)null, language = (string?)null });
        var handler = new MockHttpMessageHandler(whisperResponse, "application/json");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com"),
        };

        var options = ValidOptions;
        var provider = new OpenAiSttProvider(httpClient, options);
        var audioData = Encoding.UTF8.GetBytes("fake-audio");

        // act
        var result = await provider.TranscribeAsync(audioData, "wav", CancellationToken.None);

        // assert
        result.Text.ShouldBeEmpty();
        result.Language.ShouldBeNull();
    }

    /// <summary>
    /// A mock <see cref="HttpMessageHandler"/> that returns a canned JSON response.
    /// </summary>
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly string _contentType;

        public MockHttpMessageHandler(string body, string contentType)
        {
            _body = body;
            _contentType = contentType;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            var content = new StringContent(_body, Encoding.UTF8, _contentType);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
            };

            return Task.FromResult(response);
        }
    }
}
