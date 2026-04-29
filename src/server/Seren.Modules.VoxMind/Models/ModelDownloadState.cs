namespace Seren.Modules.VoxMind.Models;

/// <summary>
/// Lifecycle of an in-flight (or terminal) bundle download tracked by
/// <see cref="ModelDownloadService"/>. Persisted in memory only — a server
/// restart drops in-progress state, and the partial directory cleanup
/// guard runs at next start.
/// </summary>
public enum ModelDownloadStatus
{
    /// <summary>No active download. Default value when no entry exists for a variant.</summary>
    Idle = 0,

    /// <summary>HTTP transfer in progress. <see cref="ModelDownloadState.BytesDone"/> updated continuously.</summary>
    Downloading = 1,

    /// <summary>All files written, partial directory promoted, recognizer cache invalidated.</summary>
    Completed = 2,

    /// <summary>Network error, disk error, or cancellation. <see cref="ModelDownloadState.Error"/> set.</summary>
    Failed = 3,
}

/// <summary>
/// Snapshot of a download's progress. The service keeps one of these per
/// variant id and returns deep copies to callers (never the live mutable
/// instance) so the polling endpoint cannot tear partial reads.
/// </summary>
/// <param name="Status">Current lifecycle stage.</param>
/// <param name="BytesDone">Cumulative bytes written across every file in the bundle so far.</param>
/// <param name="BytesTotal">Total bundle size in bytes — known after the first HEAD/GET response, 0 before that.</param>
/// <param name="Error">Human-readable error when <see cref="Status"/> is <see cref="ModelDownloadStatus.Failed"/>; null otherwise.</param>
public sealed record ModelDownloadState(
    ModelDownloadStatus Status,
    long BytesDone,
    long BytesTotal,
    string? Error);
