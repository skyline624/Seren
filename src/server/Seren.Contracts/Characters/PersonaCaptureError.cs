namespace Seren.Contracts.Characters;

/// <summary>
/// Stable, machine-readable error codes surfaced by the persona-capture
/// pipeline (<c>POST /api/characters/capture</c>). Plain
/// <see cref="string"/> constants — the TypeScript client consumes
/// them as a union-typed field and maps each value to an i18n key.
/// </summary>
/// <remarks>
/// Parallel to <see cref="CharacterImportError"/> but with its own
/// taxonomy: capture fails in different ways than an import (no file
/// to parse — the issue is always "the workspace on disk is missing
/// or malformed"). Values are part of the REST contract; never
/// rename or repurpose an existing code.
/// </remarks>
public static class PersonaCaptureError
{
    /// <summary>
    /// Workspace directory exists but either <c>IDENTITY.md</c> or
    /// <c>SOUL.md</c> is missing. Most common case: user clicked
    /// Capture before OpenClaw finished its onboarding flow, or
    /// manually deleted the files.
    /// </summary>
    public const string WorkspaceEmpty = "workspace_empty";

    /// <summary>
    /// No workspace path is configured in <c>OpenClaw:WorkspacePath</c>
    /// — capture is disabled. Distinct from <see cref="WorkspaceEmpty"/>
    /// so the UI can suggest "ask your ops team" vs "try the capture
    /// later" independently.
    /// </summary>
    public const string NoWorkspaceConfigured = "no_workspace_configured";

    /// <summary>
    /// Files exist but the markdown doesn't carry the minimum structure
    /// the extractor needs (no <c># Heading</c>, or blank system prompt
    /// after stripping the Seren bandeau + protocol annex).
    /// </summary>
    public const string InvalidPersona = "invalid_persona";
}
