using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Seren.Application.Abstractions;
using Seren.Infrastructure.OpenClaw;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw;

public sealed class OpenClawRestClientTests
{
    private static readonly OpenClawOptions ValidOptions = new()
    {
        BaseUrl = "http://localhost:18789",
        AuthToken = "test-token",
        DefaultAgentId = "openclaw/default",
    };

    [Fact]
    public async Task StreamChatAsync_ShouldParseSseLinesCorrectly()
    {
        // arrange
        var sseResponse = """
            data: {"choices":[{"delta":{"content":"Hello"}}]}

            data: {"choices":[{"delta":{"content":" world"}}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]


            """;

        var handler = new MockHttpMessageHandler(sseResponse);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:18789"),
        };

        var options = Options.Create(ValidOptions);
        var logger = Substitute.For<ILogger<OpenClawRestClient>>();

        var client = new OpenClawRestClient(httpClient, options, logger);
        var messages = new List<ChatMessage>
        {
            new("user", "Hi"),
        };

        // act
        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in client.StreamChatAsync(messages, ct: CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // assert
        chunks.Count.ShouldBe(3);
        chunks[0].Content.ShouldBe("Hello");
        chunks[0].FinishReason.ShouldBeNull();
        chunks[1].Content.ShouldBe(" world");
        chunks[1].FinishReason.ShouldBeNull();
        chunks[2].Content.ShouldBeNull();
        chunks[2].FinishReason.ShouldBe("stop");
    }

    [Fact]
    public async Task StreamChatAsync_ShouldSkipMalformedLines()
    {
        // arrange
        var sseResponse = """
            data: {"choices":[{"delta":{"content":"First"}}]}

            data: not-json

            data: {"choices":[{"delta":{"content":"Second"}}]}

            data: [DONE]


            """;

        var handler = new MockHttpMessageHandler(sseResponse);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:18789"),
        };

        var options = Options.Create(ValidOptions);
        var logger = Substitute.For<ILogger<OpenClawRestClient>>();

        var client = new OpenClawRestClient(httpClient, options, logger);
        var messages = new List<ChatMessage>
        {
            new("user", "Hi"),
        };

        // act
        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in client.StreamChatAsync(messages, ct: CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // assert — malformed line should be skipped, not crash
        chunks.Count.ShouldBe(2);
        chunks[0].Content.ShouldBe("First");
        chunks[1].Content.ShouldBe("Second");
    }

    [Fact]
    public async Task StreamChatAsync_ShouldSetAuthHeader()
    {
        // arrange
        var sseResponse = "data: [DONE]\n\n";
        var handler = new MockHttpMessageHandler(sseResponse);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:18789"),
        };

        var options = Options.Create(ValidOptions);
        var logger = Substitute.For<ILogger<OpenClawRestClient>>();

        var client = new OpenClawRestClient(httpClient, options, logger);
        var messages = new List<ChatMessage>
        {
            new("user", "Hi"),
        };

        // act
        await foreach (var _ in client.StreamChatAsync(messages, ct: CancellationToken.None))
        {
            // consume stream
        }

        // assert
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest!.Headers.Authorization.ShouldNotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        handler.LastRequest.Headers.Authorization!.Parameter.ShouldBe("test-token");
    }

    [Fact]
    public async Task StreamChatAsync_ShouldSetOpenClawModelHeader_WhenAgentIdProvided()
    {
        // arrange
        var sseResponse = "data: [DONE]\n\n";
        var handler = new MockHttpMessageHandler(sseResponse);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:18789"),
        };

        var options = Options.Create(ValidOptions);
        var logger = Substitute.For<ILogger<OpenClawRestClient>>();

        var client = new OpenClawRestClient(httpClient, options, logger);
        var messages = new List<ChatMessage>
        {
            new("user", "Hi"),
        };

        // act
        await foreach (var _ in client.StreamChatAsync(messages, agentId: "custom-agent", ct: CancellationToken.None))
        {
            // consume stream
        }

        // assert
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest!.Headers.Contains("x-openclaw-model").ShouldBeTrue();
        handler.LastRequest.Headers.GetValues("x-openclaw-model").First().ShouldBe("custom-agent");
    }

    /// <summary>
    /// A mock <see cref="HttpMessageHandler"/> that returns a canned SSE response
    /// with <c>StatusCode.OK</c> and <c>Content-Type: text/event-stream</c>.
    /// </summary>
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _sseBody;

        public MockHttpMessageHandler(string sseBody)
        {
            _sseBody = sseBody;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            var content = new StringContent(_sseBody, Encoding.UTF8, "text/event-stream");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
            };

            return Task.FromResult(response);
        }
    }
}
