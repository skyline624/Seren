namespace Seren.Application.Abstractions;

/// <summary>
/// Writes structural edits to OpenClaw's on-disk configuration file.
/// Exists because the gateway's <c>config.patch</c> / <c>sessions.patch</c>
/// RPCs require <c>operator.admin</c> scope, which Seren does not hold by
/// default (granting it would force every user to re-pair their device).
/// Direct file edits sidestep the scope while preserving the
/// deployment-agnostic intent — Seren still does not shell out to the
/// OpenClaw host.
/// </summary>
/// <remarks>
/// Implementations must:
/// <list type="bullet">
///   <item>Preserve comments and formatting where feasible (the file is JSON5).</item>
///   <item>Write atomically (temp file + rename) so a partial write cannot wedge the gateway.</item>
///   <item>Fail loudly when the target path is missing or unwritable — callers need to surface a clear error to the UI.</item>
/// </list>
/// Callers typically chain a <see cref="IOpenClawClient.RefreshCatalogAsync"/>
/// afterwards so the gateway picks up the new value on its next boot.
/// </remarks>
public interface IOpenClawConfigWriter
{
    /// <summary>
    /// Set the <c>agents.defaults.model.primary</c> value in OpenClaw's
    /// config. Passing <c>null</c> restores the original <c>${OPENCLAW_DEFAULT_MODEL}</c>
    /// env-var reference so the gateway falls back to whatever the
    /// operator configured in <c>.env</c>.
    /// </summary>
    /// <param name="model">
    /// Fully-qualified <c>provider/model</c> id (e.g. <c>ollama/seren-qwen:latest</c>)
    /// or <c>null</c> to clear the pin.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task SetDefaultModelAsync(string? model, CancellationToken ct = default);
}
