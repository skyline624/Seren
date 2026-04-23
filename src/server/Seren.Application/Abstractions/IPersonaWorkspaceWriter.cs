using Seren.Domain.Entities;

namespace Seren.Application.Abstractions;

/// <summary>
/// Writes the active character's persona into OpenClaw's workspace as
/// <c>IDENTITY.md</c> and <c>SOUL.md</c>. OpenClaw re-reads those files
/// on every <c>chat.send</c> via an mtime-indexed cache
/// (<c>src/agents/workspace.ts</c> in the upstream repo), so a simple
/// overwrite is picked up on the next chat turn — no session reset or
/// gateway restart required.
/// </summary>
/// <remarks>
/// Kept intentionally narrow (ISP) — one verb, one object. Consumers
/// call this on character activation and at Seren boot; implementations
/// are responsible for atomic writes, path-traversal defence, and
/// no-op'ing when no workspace is configured.
/// </remarks>
public interface IPersonaWorkspaceWriter
{
    /// <summary>
    /// Compose <c>IDENTITY.md</c> + <c>SOUL.md</c> from
    /// <paramref name="character"/> and overwrite them in the configured
    /// workspace. Idempotent — calling twice with the same character
    /// produces the same bytes on disk.
    /// </summary>
    /// <param name="character">Character whose persona should drive the
    /// workspace. The <see cref="Character.SystemPrompt"/> is used
    /// verbatim (already composed + macro-substituted by upstream
    /// producers).</param>
    /// <param name="cancellationToken">Cancellation propagated to the
    /// file I/O calls.</param>
    Task WritePersonaAsync(Character character, CancellationToken cancellationToken);
}
