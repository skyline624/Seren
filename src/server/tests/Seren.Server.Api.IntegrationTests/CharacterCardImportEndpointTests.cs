using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Seren.Contracts.Characters;
using Shouldly;
using Xunit;

namespace Seren.Server.Api.IntegrationTests;

/// <summary>
/// End-to-end tests for <c>POST /api/characters/import</c> +
/// <c>GET /api/characters/{id}/avatar</c>. Validates the multipart
/// upload path, the typed error response contract, the 2D avatar
/// streaming endpoint, and the cleanup on DELETE.
/// </summary>
public sealed class CharacterCardImportEndpointTests
    : IClassFixture<SerenTestFactory>
{
    private readonly SerenTestFactory _factory;

    public CharacterCardImportEndpointTests(SerenTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Import_JsonCard_Creates_Character_VisibleInList()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var cardBytes = BuildJsonCard(name: "Cortana");
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(cardBytes), "file", "cortana.json" },
        };

        var response = await client.PostAsync("/api/characters/import", content, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var json = await response.Content.ReadAsStringAsync(ct);
        json.ShouldContain("Cortana");

        var listResponse = await client.GetAsync("/api/characters", ct);
        var listJson = await listResponse.Content.ReadAsStringAsync(ct);
        listJson.ShouldContain("Cortana");
    }

    [Fact]
    public async Task Import_PngCard_Saves_Avatar_RetrievableViaAvatarEndpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var cardBytes = BuildPngCard(name: "Chell");
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(cardBytes), "file", "chell.png" },
        };

        var response = await client.PostAsync("/api/characters/import", content, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var idString = doc.RootElement.GetProperty("character").GetProperty("id").GetString();
        idString.ShouldNotBeNull();
        var id = Guid.Parse(idString!);

        var avatarResponse = await client.GetAsync($"/api/characters/{id}/avatar", ct);
        avatarResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        avatarResponse.Content.Headers.ContentType?.MediaType.ShouldBe("image/png");
        var avatarBytes = await avatarResponse.Content.ReadAsByteArrayAsync(ct);
        avatarBytes.Length.ShouldBeGreaterThan(0);
        avatarBytes[..4].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }

    [Fact]
    public async Task Import_MalformedJson_Returns_TypedErrorResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var garbage = Encoding.UTF8.GetBytes("{ not valid json }");
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(garbage), "file", "broken.json" },
        };

        var response = await client.PostAsync("/api/characters/import", content, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        doc.RootElement.GetProperty("code").GetString().ShouldBe(CharacterImportError.MalformedJson);
        doc.RootElement.GetProperty("message").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Import_MissingFilePart_Returns_InvalidCard_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent
        {
            // No "file" part — just a stray form field.
            { new StringContent("true"), "activateOnImport" },
        };

        var response = await client.PostAsync("/api/characters/import", content, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        doc.RootElement.GetProperty("code").GetString().ShouldBe(CharacterImportError.InvalidCard);
    }

    [Fact]
    public async Task Delete_ImportedCharacter_Removes_AvatarFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var cardBytes = BuildPngCard(name: "GLaDOS");
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(cardBytes), "file", "glados.png" },
        };

        var createResponse = await client.PostAsync("/api/characters/import", content, ct);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync(ct));
        var id = Guid.Parse(created.RootElement.GetProperty("character").GetProperty("id").GetString()!);

        var avatarBefore = await client.GetAsync($"/api/characters/{id}/avatar", ct);
        avatarBefore.StatusCode.ShouldBe(HttpStatusCode.OK);

        var deleteResponse = await client.DeleteAsync($"/api/characters/{id}", ct);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var avatarAfter = await client.GetAsync($"/api/characters/{id}/avatar", ct);
        avatarAfter.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ── Card builders (duplicated from Application.Tests — no cross-project refs) ──

    private static byte[] BuildJsonCard(string name, string spec = "chara_card_v3")
    {
        var json = $$"""
            {
              "spec": "{{spec}}",
              "spec_version": "3.0",
              "data": {
                "name": "{{name}}",
                "description": "Integration test character."
              }
            }
            """;
        return Encoding.UTF8.GetBytes(json);
    }

    private static byte[] BuildPngCard(string name, string spec = "chara_card_v3")
    {
        var json = BuildJsonCard(name, spec);
        var base64 = Convert.ToBase64String(json);
        var keyword = spec == "chara_card_v2" ? "chara" : "ccv3";
        return BuildPngWithTextChunk(keyword, base64);
    }

    private static byte[] BuildPngWithTextChunk(string keyword, string text)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var keywordBytes = Encoding.Latin1.GetBytes(keyword);
        var textBytes = Encoding.Latin1.GetBytes(text);
        var dataLength = keywordBytes.Length + 1 + textBytes.Length;

        var lengthBuf = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBuf, (uint)dataLength);
        ms.Write(lengthBuf);
        ms.Write("tEXt"u8);
        ms.Write(keywordBytes);
        ms.WriteByte(0);
        ms.Write(textBytes);
        ms.Write(new byte[4]); // CRC stub

        // IEND
        BinaryPrimitives.WriteUInt32BigEndian(lengthBuf, 0);
        ms.Write(lengthBuf);
        ms.Write("IEND"u8);
        ms.Write(new byte[4]);

        return ms.ToArray();
    }
}
