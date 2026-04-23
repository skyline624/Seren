namespace Seren.Application.Abstractions;

/// <summary>
/// Reads <c>IDENTITY.md</c> + <c>SOUL.md</c> out of OpenClaw's workspace
/// — the exact inverse of <see cref="IPersonaWorkspaceWriter"/>. Used
/// when the user wants to capture the persona currently driving the
/// LLM (e.g. freshly composed by OpenClaw's onboarding, or hand-edited
/// in the container) into a Seren <c>Character</c>.
/// </summary>
/// <remarks>
/// Kept narrow (ISP) — one verb, one object. Implementations share the
/// same path-resolution + traversal defence as the writer (basename
/// <c>workspace</c> enforced by <c>OpenClawOptions</c> validator); they
/// never touch files outside the configured workspace.
/// </remarks>
public interface IPersonaWorkspaceReader
{
    /// <summary>
    /// Load the current workspace persona files. The result
    /// distinguishes three states the caller needs to act on :
    /// <list type="bullet">
    ///   <item><description><see cref="PersonaReadOutcome.Loaded"/> —
    ///     both files present, <c>Snapshot</c> non-null.</description></item>
    ///   <item><description><see cref="PersonaReadOutcome.Empty"/> —
    ///     workspace configured but either <c>IDENTITY.md</c> or
    ///     <c>SOUL.md</c> is missing (common case during onboarding).</description></item>
    ///   <item><description><see cref="PersonaReadOutcome.NotConfigured"/>
    ///     — no <c>OpenClaw:WorkspacePath</c> set; feature disabled.</description></item>
    /// </list>
    /// </summary>
    Task<PersonaReadResult> ReadCurrentPersonaAsync(CancellationToken cancellationToken);
}

/// <summary>Raw in-memory view of IDENTITY.md + SOUL.md.</summary>
public sealed record WorkspacePersonaSnapshot(string IdentityMarkdown, string SoulMarkdown);

/// <summary>Tri-state outcome of a workspace read.</summary>
public enum PersonaReadOutcome
{
    Loaded,
    Empty,
    NotConfigured,
}

/// <summary>
/// Reader result — either a loaded snapshot or a typed absence reason.
/// Using a record over <c>null</c> + enum lets the handler pattern-match
/// on the outcome without losing the snapshot in the <c>Loaded</c> case.
/// </summary>
public sealed record PersonaReadResult(PersonaReadOutcome Outcome, WorkspacePersonaSnapshot? Snapshot)
{
    public static PersonaReadResult NotConfigured { get; } = new(PersonaReadOutcome.NotConfigured, null);
    public static PersonaReadResult Empty { get; } = new(PersonaReadOutcome.Empty, null);
    public static PersonaReadResult Loaded(WorkspacePersonaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new PersonaReadResult(PersonaReadOutcome.Loaded, snapshot);
    }
}
