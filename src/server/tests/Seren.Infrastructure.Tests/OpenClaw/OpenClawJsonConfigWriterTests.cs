using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Seren.Infrastructure.OpenClaw;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw;

/// <summary>
/// Focused tests for the regex-based JSON5 rewriter behind
/// <c>POST /api/models/apply</c>. The writer must:
/// <list type="bullet">
///   <item>Replace the <c>agents.defaults.model.primary</c> value without touching comments.</item>
///   <item>Restore the <c>${OPENCLAW_DEFAULT_MODEL}</c> token when clearing the pin.</item>
///   <item>Leave the file untouched and raise when the anchor is missing.</item>
/// </list>
/// </summary>
public sealed class OpenClawJsonConfigWriterTests
{
    private static readonly string SampleConfig =
        """
        // comment block
        {
          gateway: {
            port: 18789,
            tools: { allow: ["gateway"] },
          },

          models: {
            providers: {
              ollama: { baseUrl: "${OLLAMA_BASE_URL}", api: "ollama", models: [] },
            },
          },

          agents: {
            defaults: {
              workspace: "/home/node/.openclaw/workspace",
              model: {
                // trailing-comment guard
                primary: "${OPENCLAW_DEFAULT_MODEL}",
              },
            },
          },
        }
        """;

    [Fact]
    public async Task SetDefaultModelAsync_WritesNewPrimary_PreservingComments()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"seren-writer-{Guid.NewGuid():N}.json5");
        await File.WriteAllTextAsync(path, SampleConfig, ct);

        try
        {
            var writer = CreateWriter(path);

            await writer.SetDefaultModelAsync("ollama/seren-gemma:latest", ct);

            var after = await File.ReadAllTextAsync(path, ct);
            after.ShouldContain("primary: \"ollama/seren-gemma:latest\"");
            after.ShouldContain("// comment block");
            after.ShouldContain("// trailing-comment guard");
            after.ShouldNotContain("${OPENCLAW_DEFAULT_MODEL}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SetDefaultModelAsync_WithNull_RestoresEnvToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"seren-writer-{Guid.NewGuid():N}.json5");
        // Start from a pinned state.
        await File.WriteAllTextAsync(
            path,
            SampleConfig.Replace("${OPENCLAW_DEFAULT_MODEL}", "ollama/seren-qwen:latest", StringComparison.Ordinal),
            ct);

        try
        {
            var writer = CreateWriter(path);

            await writer.SetDefaultModelAsync(model: null, ct);

            var after = await File.ReadAllTextAsync(path, ct);
            after.ShouldContain("primary: \"${OPENCLAW_DEFAULT_MODEL}\"");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SetDefaultModelAsync_DoesNotTouchImageModelPrimary()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"seren-writer-{Guid.NewGuid():N}.json5");
        // imageModel block listed BEFORE model so the lookbehind has to
        // distinguish `imageModel:` from the standalone `model:` key.
        const string ConfigWithImageModel =
            """
            {
              agents: {
                defaults: {
                  imageModel: {
                    primary: "openai/dall-e-3",
                  },
                  model: {
                    primary: "${OPENCLAW_DEFAULT_MODEL}",
                  },
                },
              },
            }
            """;
        await File.WriteAllTextAsync(path, ConfigWithImageModel, ct);

        try
        {
            var writer = CreateWriter(path);

            await writer.SetDefaultModelAsync("ollama/seren-gemma:latest", ct);

            var after = await File.ReadAllTextAsync(path, ct);
            after.ShouldContain("primary: \"ollama/seren-gemma:latest\"");
            // imageModel.primary must survive untouched.
            after.ShouldContain("primary: \"openai/dall-e-3\"");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SetDefaultModelAsync_WithMissingFile_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var writer = CreateWriter("/nonexistent/path/openclaw.json");

        await Should.ThrowAsync<FileNotFoundException>(() =>
            writer.SetDefaultModelAsync("ollama/x", ct));
    }

    [Fact]
    public async Task SetDefaultModelAsync_WithEmptyPath_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var writer = CreateWriter(string.Empty);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            writer.SetDefaultModelAsync("ollama/x", ct));
    }

    [Fact]
    public async Task SetDefaultModelAsync_RefusesCorruptedHeader()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"seren-writer-{Guid.NewGuid():N}.json5");
        // Stray path line at the top (the exact corruption observed in
        // production from an earlier `/tools/invoke config.*` experiment).
        const string CorruptedHeader = "/home/node/.openclaw/openclaw.json\n";
        await File.WriteAllTextAsync(path, CorruptedHeader + SampleConfig, ct);

        try
        {
            var writer = CreateWriter(path);

            var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
                writer.SetDefaultModelAsync("ollama/seren-gemma:latest", ct));
            ex.Message.ShouldContain("git checkout");

            // File must NOT have been rewritten — a corruption-preserving
            // regex pass would silently double down on the damage.
            var after = await File.ReadAllTextAsync(path, ct);
            after.ShouldStartWith(CorruptedHeader);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SetDefaultModelAsync_AnchorMissing_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"seren-writer-{Guid.NewGuid():N}.json5");
        // No `model: { ... primary: }` node.
        await File.WriteAllTextAsync(path, "{ gateway: { port: 123 } }", ct);

        try
        {
            var writer = CreateWriter(path);

            await Should.ThrowAsync<InvalidOperationException>(() =>
                writer.SetDefaultModelAsync("ollama/x", ct));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static OpenClawJsonConfigWriter CreateWriter(string path)
    {
        var options = Options.Create(new OpenClawOptions { ConfigFilePath = path });
        return new OpenClawJsonConfigWriter(options, NullLogger<OpenClawJsonConfigWriter>.Instance);
    }
}
