using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace Seren.Server.Api.IntegrationTests;

/// <summary>
/// End-to-end tests for <c>POST /api/characters/capture</c> +
/// <c>GET /api/characters/{id}/download</c>. Exercises the real reader
/// against a disposable <c>workspace</c> directory, the typed error
/// contract, and the download <c>Content-Disposition</c> header.
/// </summary>
public sealed class PersonaCaptureEndpointTests
    : IClassFixture<PersonaCaptureEndpointTests.WorkspaceFactory>, IDisposable
{
    private readonly WorkspaceFactory _factory;

    public PersonaCaptureEndpointTests(WorkspaceFactory factory)
    {
        _factory = factory;
    }

    public void Dispose() => _factory.ResetWorkspace();

    [Fact]
    public async Task Capture_WithPopulatedWorkspace_Returns201_WithCharacter()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.SeedWorkspace(
            identity: "# Cortana\n\n## Description\n\nUNSC Smart AI.\n\n## Tags\n\n- scifi\n- halo\n",
            soul: "# Cortana\n\nYou are Cortana, witty and loyal.\n");

        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/characters/capture", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        doc.RootElement.GetProperty("character").GetProperty("name").GetString().ShouldBe("Cortana");
    }

    [Fact]
    public async Task Capture_WithEmptyWorkspace_Returns404_TypedCode()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.ResetWorkspace();
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/characters/capture", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync(ct);
        body.ShouldContain("workspace_empty");
    }

    [Fact]
    public async Task Capture_WithInvalidMarkdown_Returns400_TypedCode()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.SeedWorkspace(
            identity: "no heading here",
            soul: "no heading either");
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/characters/capture", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(ct);
        body.ShouldContain("invalid_persona");
    }

    [Fact]
    public async Task Download_ExistingCharacter_ReturnsJsonWithContentDisposition()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.ResetWorkspace();
        var client = _factory.CreateClient();

        // Create a character via the normal POST to get a stable id.
        var createBody = new
        {
            name = "Cortana",
            systemPrompt = "You are Cortana.",
            avatarModelPath = (string?)null,
            voice = (string?)null,
            agentId = (string?)null,
        };
        var createRes = await client.PostAsJsonAsync("/api/characters", createBody, ct);
        createRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync(ct));
        var id = createDoc.RootElement.GetProperty("id").GetString();
        id.ShouldNotBeNull();

        var response = await client.GetAsync($"/api/characters/{id}/download", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        var disposition = response.Content.Headers.ContentDisposition;
        disposition.ShouldNotBeNull();
        // ASP.NET Core emits both `filename=` (ASCII fallback) and
        // `filename*=UTF-8''...` — accept whichever the runtime chose.
        var filename = (disposition!.FileNameStar
                        ?? disposition.FileName?.Trim('"')
                        ?? string.Empty);
        filename.ShouldContain("cortana");
        filename.ShouldEndWith(".character.json");

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("name").GetString().ShouldBe("Cortana");
    }

    [Fact]
    public async Task Download_UnknownId_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/characters/{Guid.NewGuid()}/download", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Test-only factory that plumbs a disposable <c>workspace</c>
    /// directory into <c>OpenClaw:WorkspacePath</c>. The directory
    /// basename is literally <c>workspace</c> so it passes the
    /// <c>OpenClawOptions</c> validator (same rule as prod).
    /// </summary>
    public sealed class WorkspaceFactory : WebApplicationFactory<Program>
    {
        public string WorkspaceRoot { get; }
        private readonly string _charactersPath;

        public WorkspaceFactory()
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"seren_test_ws_{Guid.NewGuid():N}");
            WorkspaceRoot = Path.Combine(tmp, "workspace");
            Directory.CreateDirectory(WorkspaceRoot);
            _charactersPath = Path.Combine(tmp, "characters.json");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Seren:Characters:StorePath", _charactersPath);
            builder.UseSetting("Seren:WebSocket:ReadTimeoutSeconds", "0");
            builder.UseSetting("OpenClaw:WorkspacePath", WorkspaceRoot);
        }

        public void SeedWorkspace(string identity, string soul)
        {
            Directory.CreateDirectory(WorkspaceRoot);
            File.WriteAllText(Path.Combine(WorkspaceRoot, "IDENTITY.md"), identity);
            File.WriteAllText(Path.Combine(WorkspaceRoot, "SOUL.md"), soul);
        }

        public void ResetWorkspace()
        {
            if (!Directory.Exists(WorkspaceRoot))
            {
                return;
            }
            foreach (var file in Directory.EnumerateFiles(WorkspaceRoot))
            {
                try { File.Delete(file); }
#pragma warning disable CA1031 // Best-effort cleanup.
                catch { /* ignore */ }
#pragma warning restore CA1031
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
            {
                return;
            }

            try { File.Delete(_charactersPath); }
#pragma warning disable CA1031 // Best-effort cleanup.
            catch { /* ignore */ }
            try { Directory.Delete(Path.GetDirectoryName(WorkspaceRoot)!, recursive: true); }
            catch { /* ignore */ }
#pragma warning restore CA1031
        }
    }
}
