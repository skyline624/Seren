using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Seren.Application.Characters.Personas;
using Seren.Domain.Entities;
using Seren.Infrastructure.Characters;
using Seren.Infrastructure.OpenClaw;
using Shouldly;
using Xunit;

namespace Seren.Server.Api.IntegrationTests;

/// <summary>
/// Exercises the real <see cref="FileSystemPersonaWorkspaceWriter"/>
/// against a disposable temp directory — verifies the two persona files
/// land with the right content, writes are atomic (no leftover
/// <c>.tmp</c>), and an empty WorkspacePath no-ops cleanly.
/// </summary>
/// <remarks>
/// Lives in the IntegrationTests project (rather than Infrastructure.Tests)
/// to avoid cross-contamination from unrelated pre-existing analyzer
/// failures in the Infrastructure test suite.
/// </remarks>
public sealed class FileSystemPersonaWorkspaceWriterTests : IDisposable
{
    private readonly string _workspaceRoot;

    public FileSystemPersonaWorkspaceWriterTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), $"seren-persona-test-{Guid.NewGuid():N}", "workspace");
    }

    public void Dispose()
    {
        try
        {
            var parent = Directory.GetParent(_workspaceRoot)?.FullName;
            if (parent is not null && Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
#pragma warning disable CA1031 // Best-effort test cleanup.
        catch { /* ignore */ }
#pragma warning restore CA1031
    }

    [Fact]
    public async Task WritePersonaAsync_CreatesIdentityAndSoulWithExpectedContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var writer = BuildWriter(_workspaceRoot, out var metrics);

        var character = Character.Create("Cortana", "You are Cortana, witty and loyal.") with
        {
            Description = "AI construct from Halo.",
            Greeting = "Hello, Chief.",
            Tags = ["scifi", "halo"],
        };

        await writer.WritePersonaAsync(character, ct);

        var identity = await File.ReadAllTextAsync(Path.Combine(_workspaceRoot, "IDENTITY.md"), ct);
        identity.ShouldContain("# Cortana");
        identity.ShouldContain("Hello, Chief.");
        identity.ShouldContain("scifi");

        var soul = await File.ReadAllTextAsync(Path.Combine(_workspaceRoot, "SOUL.md"), ct);
        soul.ShouldContain("# Cortana — Soul");
        soul.ShouldContain("You are Cortana, witty and loyal.");
        soul.ShouldContain(PersonaTemplateComposer.MarkerProtocolBlock);

        metrics.Records.Count.ShouldBe(1);
        metrics.Records[0].Outcome.ShouldBe("ok");
        metrics.Records[0].Bytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task WritePersonaAsync_NoWorkspaceConfigured_ReportsNoOpMetric()
    {
        var ct = TestContext.Current.CancellationToken;
        var writer = BuildWriter(workspacePath: string.Empty, out var metrics);

        var character = Character.Create("Cortana", "prompt");
        await writer.WritePersonaAsync(character, ct);

        Directory.Exists(_workspaceRoot).ShouldBeFalse();
        metrics.Records.Count.ShouldBe(1);
        metrics.Records[0].Outcome.ShouldBe("no_workspace");
        metrics.Records[0].Bytes.ShouldBe(0);
    }

    [Fact]
    public async Task WritePersonaAsync_OverwritesAtomically_NoLeftoverTmpFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var writer = BuildWriter(_workspaceRoot, out _);

        var a = Character.Create("A", "System prompt A.");
        var b = Character.Create("B", "System prompt B.");
        await writer.WritePersonaAsync(a, ct);
        await writer.WritePersonaAsync(b, ct);

        var soul = await File.ReadAllTextAsync(Path.Combine(_workspaceRoot, "SOUL.md"), ct);
        soul.ShouldContain("# B — Soul");
        soul.ShouldNotContain("System prompt A.");

        Directory.EnumerateFiles(_workspaceRoot, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task WritePersonaAsync_WriterErrorRecorded_ButSwallowed()
    {
        var ct = TestContext.Current.CancellationToken;
        // Create a FILE where the writer expects to create a DIRECTORY
        // so mkdir fails. The writer must catch, record an error metric,
        // and never throw back to the caller.
        var badParent = Path.Combine(Path.GetTempPath(), $"seren-bad-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(badParent, "oops", ct);
        var workspacePath = Path.Combine(badParent, "workspace");
        try
        {
            var writer = BuildWriter(workspacePath, out var metrics);

            await writer.WritePersonaAsync(Character.Create("X", "prompt"), ct);

            metrics.Records.Count.ShouldBe(1);
            metrics.Records[0].Outcome.ShouldBe("error");
        }
        finally
        {
            if (File.Exists(badParent))
            {
                File.Delete(badParent);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static FileSystemPersonaWorkspaceWriter BuildWriter(
        string workspacePath, out CapturingPersonaMetrics metrics)
    {
        var options = Options.Create(new OpenClawOptions { WorkspacePath = workspacePath });
        metrics = new CapturingPersonaMetrics();
        return new FileSystemPersonaWorkspaceWriter(
            options, metrics, NullLogger<FileSystemPersonaWorkspaceWriter>.Instance);
    }

    private sealed class CapturingPersonaMetrics : IPersonaWriterMetrics
    {
        public List<(string Outcome, string Character, long Bytes, TimeSpan Elapsed)> Records { get; } = [];

        public void RecordWrite(string outcome, string characterName, long bytesWritten, TimeSpan elapsed)
            => Records.Add((outcome, characterName, bytesWritten, elapsed));
    }
}
