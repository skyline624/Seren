using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Seren.Infrastructure.Ollama;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.Ollama;

public sealed class OllamaRestClientTests
{
    [Fact]
    public async Task GetLocalModelsAsync_WithEmptyBaseUrl_ReturnsEmptyListAndSkipsHttp()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new CountingHandler((_, _) => throw new InvalidOperationException("should not be called"));
        var client = BuildClient(baseUrl: "", handler);

        var result = await client.GetLocalModelsAsync(ct);

        result.ShouldBeEmpty();
        handler.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetLocalModelsAsync_WithValidResponse_NormalisesIdsAndDescriptions()
    {
        var ct = TestContext.Current.CancellationToken;
        const string body = """
        {
          "models": [
            {
              "name": "seren-qwen:latest",
              "details": { "family": "qwen", "parameter_size": "9B", "quantization_level": "Q8_0" }
            },
            {
              "name": "seren-gemma:latest",
              "details": { "family": "gemma4", "parameter_size": "7.5B", "quantization_level": "Q8_0" }
            }
          ]
        }
        """;
        var client = BuildClient("http://ollama.local:11434",
            new CountingHandler((_, _) => Ok(body)));

        var result = await client.GetLocalModelsAsync(ct);

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe("ollama/seren-qwen:latest");
        result[0].Description.ShouldBe("qwen 9B Q8_0");
        result[1].Id.ShouldBe("ollama/seren-gemma:latest");
        result[1].Description.ShouldBe("gemma4 7.5B Q8_0");
    }

    [Fact]
    public async Task GetLocalModelsAsync_WithMissingDetails_StillReturnsEntryWithNullDescription()
    {
        var ct = TestContext.Current.CancellationToken;
        const string body = """{"models":[{"name":"bare-model:latest"}]}""";
        var client = BuildClient("http://ollama.local:11434",
            new CountingHandler((_, _) => Ok(body)));

        var result = await client.GetLocalModelsAsync(ct);

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe("ollama/bare-model:latest");
        result[0].Description.ShouldBeNull();
    }

    [Fact]
    public async Task GetLocalModelsAsync_DropsEntriesWithEmptyNames()
    {
        var ct = TestContext.Current.CancellationToken;
        const string body = """
        {"models":[{"name":""},{"name":"valid:latest","details":{"family":"x"}}]}
        """;
        var client = BuildClient("http://ollama.local:11434",
            new CountingHandler((_, _) => Ok(body)));

        var result = await client.GetLocalModelsAsync(ct);

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe("ollama/valid:latest");
    }

    [Fact]
    public async Task GetLocalModelsAsync_OnHttpFailure_ReturnsEmptyListWithoutThrowing()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = BuildClient("http://ollama.local:11434",
            new CountingHandler((_, _) => throw new HttpRequestException("connection refused")));

        var result = await client.GetLocalModelsAsync(ct);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetLocalModelsAsync_OnMalformedJson_ReturnsEmptyListWithoutThrowing()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = BuildClient("http://ollama.local:11434",
            new CountingHandler((_, _) => Ok("not-json")));

        var result = await client.GetLocalModelsAsync(ct);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetLocalModelsAsync_OnEmptyModelsArray_ReturnsEmptyList()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = BuildClient("http://ollama.local:11434",
            new CountingHandler((_, _) => Ok("""{"models":[]}""")));

        var result = await client.GetLocalModelsAsync(ct);

        result.ShouldBeEmpty();
    }

    private static OllamaRestClient BuildClient(string baseUrl, CountingHandler handler)
    {
        var http = new HttpClient(handler);
        if (!string.IsNullOrEmpty(baseUrl))
        {
            http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        }
        http.Timeout = TimeSpan.FromSeconds(5);

        var opts = Options.Create(new OllamaOptions { BaseUrl = baseUrl, TimeoutSeconds = 5 });
        return new OllamaRestClient(http, opts, NullLogger<OllamaRestClient>.Instance);
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class CountingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder = responder;
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            try
            {
                return Task.FromResult(_responder(request, cancellationToken));
            }
            catch (Exception ex)
            {
                return Task.FromException<HttpResponseMessage>(ex);
            }
        }
    }
}
