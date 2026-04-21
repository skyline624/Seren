namespace Seren.Application.Abstractions;

/// <summary>
/// Direct contract to Ollama's REST API, separate from the
/// <see cref="IOpenClawClient"/> gateway route because OpenClaw's
/// <c>models.list</c> RPC does not enumerate locally-installed Ollama
/// models. Seren calls both and merges them before exposing the
/// catalog to the UI via <c>/api/models</c>.
/// </summary>
public interface IOllamaClient
{
    /// <summary>
    /// Returns the list of models currently installed on the local
    /// Ollama daemon (equivalent of <c>ollama list</c>), each normalised
    /// into the shared <see cref="ModelInfo"/> shape with an
    /// <c>ollama/</c> id prefix so consumers can treat the source as
    /// just another provider.
    /// </summary>
    /// <remarks>
    /// Implementations degrade gracefully: a misconfigured base URL,
    /// network failure, or malformed response yields an empty list
    /// rather than throwing — the caller can always fall back on
    /// OpenClaw's catalog.
    /// </remarks>
    Task<IReadOnlyList<ModelInfo>> GetLocalModelsAsync(CancellationToken ct = default);
}
