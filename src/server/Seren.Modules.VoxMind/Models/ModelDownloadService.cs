using Microsoft.Extensions.Logging;

namespace Seren.Modules.VoxMind.Models;

/// <summary>
/// Streams variant bundles from HuggingFace into the local install dir.
/// Owns per-variant lifecycle state so polling endpoints and the UI can
/// render progress bars without the file transfer blocking the request
/// pipeline.
/// </summary>
public interface IModelDownloadService
{
    /// <summary>
    /// Kicks off (or returns the existing in-flight) download of
    /// <paramref name="variant"/>. Idempotent for concurrent callers — a
    /// second invocation while <see cref="ModelDownloadStatus.Downloading"/>
    /// is a no-op and returns the live state.
    /// </summary>
    /// <returns>The state captured at the moment the call was accepted.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="ModelVariant.IsSystemManaged"/> is true or the
    /// variant lacks a HuggingFace repository configuration.
    /// </exception>
    ModelDownloadState Start(ModelVariant variant);

    /// <summary>Snapshots the current state of <paramref name="variant"/>'s download.</summary>
    /// <returns>The state, or an <see cref="ModelDownloadStatus.Idle"/> placeholder when nothing has been requested.</returns>
    ModelDownloadState Snapshot(ModelVariant variant);

    /// <summary>
    /// Drops the in-memory record for <paramref name="variant"/> — used by
    /// the delete endpoint so a subsequent <c>GET /status</c> reports
    /// <see cref="ModelDownloadStatus.Idle"/> rather than a stale
    /// <see cref="ModelDownloadStatus.Completed"/>.
    /// </summary>
    void Forget(string variantId);
}

/// <inheritdoc />
public sealed class ModelDownloadService : IModelDownloadService, IDisposable
{
    private const string HttpClientName = "voxmind-downloads";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IModelStorage _storage;
    private readonly ILogger<ModelDownloadService> _logger;
    private readonly ConcurrentDictionary<string, DownloadEntry> _entries = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

    public ModelDownloadService(
        IHttpClientFactory httpClientFactory,
        IModelStorage storage,
        ILogger<ModelDownloadService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(storage);
        _httpClientFactory = httpClientFactory;
        _storage = storage;
        _logger = logger;
    }

    /// <inheritdoc />
    public ModelDownloadState Start(ModelVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);

        if (variant.IsSystemManaged)
        {
            throw new InvalidOperationException(
                $"Variant '{variant.Id}' is system-managed and cannot be downloaded by the UI.");
        }

        if (string.IsNullOrWhiteSpace(variant.HfRepo) || variant.Files.Count == 0)
        {
            throw new InvalidOperationException(
                $"Variant '{variant.Id}' has no HuggingFace bundle configured.");
        }

        var entry = _entries.AddOrUpdate(
            variant.Id,
            _ => CreateAndStart(variant),
            (_, existing) =>
            {
                if (existing.Snapshot().Status == ModelDownloadStatus.Downloading)
                {
                    return existing;
                }

                existing.Dispose();
                return CreateAndStart(variant);
            });

        return entry.Snapshot();
    }

    /// <inheritdoc />
    public ModelDownloadState Snapshot(ModelVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return _entries.TryGetValue(variant.Id, out var entry)
            ? entry.Snapshot()
            : new ModelDownloadState(ModelDownloadStatus.Idle, 0, 0, null);
    }

    /// <inheritdoc />
    public void Forget(string variantId)
    {
        if (_entries.TryRemove(variantId, out var entry))
        {
            entry.Dispose();
        }
    }

    private DownloadEntry CreateAndStart(ModelVariant variant)
    {
        var entry = new DownloadEntry(variant);
        _ = RunAsync(entry, _shutdown.Token);
        return entry;
    }

    private async Task RunAsync(DownloadEntry entry, CancellationToken ct)
    {
        var variant = entry.Variant;
        var dir = _storage.GetLocalDir(variant);
        if (string.IsNullOrWhiteSpace(dir))
        {
            entry.Fail("Whisper RootDir is not configured.");
            return;
        }

        var partialDir = $"{dir}.partial";

        try
        {
            CleanDirectory(partialDir);
            Directory.CreateDirectory(partialDir);

            var client = _httpClientFactory.CreateClient(HttpClientName);

            // First pass: discover total bundle size with HEAD requests so
            // the progress bar has a denominator from the very first poll.
            long totalBytes = 0;
            var sizes = new long[variant.Files.Count];
            for (var i = 0; i < variant.Files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var size = await ProbeContentLengthAsync(client, variant, variant.Files[i], ct).ConfigureAwait(false);
                sizes[i] = size;
                totalBytes += size;
            }

            entry.SetTotal(totalBytes);

            // Second pass: actual streaming download.
            for (var i = 0; i < variant.Files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var fileName = variant.Files[i];
                var url = BuildHfUrl(variant.HfRepo, fileName);
                var dest = Path.Combine(partialDir, fileName);

                _logger.LogInformation(
                    "ModelDownloadService: streaming {Url} → {Dest} ({SizeBytes} bytes).",
                    url, dest, sizes[i]);

                using var response = await client
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var http = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var file = new FileStream(
                    dest, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true);

                var buffer = new byte[81920];
                int read;
                while ((read = await http.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    entry.AdvanceBytes(read);
                }
            }

            // Atomically promote the partial directory to the final
            // location. If the destination exists from a half-finished
            // earlier attempt, replace it.
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }

            Directory.Move(partialDir, dir);
            entry.Complete();

            _logger.LogInformation(
                "ModelDownloadService: variant '{Variant}' downloaded ({TotalBytes} bytes).",
                variant.Id, totalBytes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            CleanDirectory(partialDir);
            entry.Fail("Download cancelled.");
        }
        catch (Exception ex)
        {
            CleanDirectory(partialDir);
            _logger.LogWarning(ex,
                "ModelDownloadService: variant '{Variant}' failed.", variant.Id);
            entry.Fail(ex.Message);
        }
    }

    private static async Task<long> ProbeContentLengthAsync(
        HttpClient client, ModelVariant variant, string file, CancellationToken ct)
    {
        // HuggingFace serves /resolve/main/* via a redirect to a CDN; HEAD
        // returns the actual ContentLength for both LFS and inline assets.
        using var head = new HttpRequestMessage(HttpMethod.Head, BuildHfUrl(variant.HfRepo, file));
        using var response = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return response.Content.Headers.ContentLength ?? 0;
    }

    private static string BuildHfUrl(string repo, string file)
        => $"https://huggingface.co/{repo}/resolve/main/{file}";

    private static void CleanDirectory(string dir)
    {
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();

        foreach (var entry in _entries.Values)
        {
            entry.Dispose();
        }

        _entries.Clear();
        _shutdown.Dispose();
    }

    /// <summary>
    /// Mutable per-variant ledger. Locks aren't needed for the byte
    /// counter (Interlocked) but the status transitions are guarded so
    /// snapshots see a consistent (status, error) pair.
    /// </summary>
    private sealed class DownloadEntry : IDisposable
    {
        private readonly Lock _gate = new();
        private long _bytesDone;
        private long _bytesTotal;
        private ModelDownloadStatus _status = ModelDownloadStatus.Downloading;
        private string? _error;
        private bool _disposed;

        public DownloadEntry(ModelVariant variant)
        {
            Variant = variant;
        }

        public ModelVariant Variant { get; }

        public void SetTotal(long total)
        {
            Interlocked.Exchange(ref _bytesTotal, total);
        }

        public void AdvanceBytes(int count)
        {
            Interlocked.Add(ref _bytesDone, count);
        }

        public void Complete()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _status = ModelDownloadStatus.Completed;
                _error = null;
                Interlocked.Exchange(ref _bytesDone, _bytesTotal);
            }
        }

        public void Fail(string error)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _status = ModelDownloadStatus.Failed;
                _error = error;
            }
        }

        public ModelDownloadState Snapshot()
        {
            lock (_gate)
            {
                return new ModelDownloadState(
                    _status,
                    Interlocked.Read(ref _bytesDone),
                    Interlocked.Read(ref _bytesTotal),
                    _error);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
            }
        }
    }
}
