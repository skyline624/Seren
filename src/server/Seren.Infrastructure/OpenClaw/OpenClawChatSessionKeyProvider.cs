using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;

namespace Seren.Infrastructure.OpenClaw;

/// <summary>
/// Reads the stable chat session key from <see cref="OpenClawOptions"/>.
/// Persists a rotation counter to the Seren state volume so resets survive
/// restarts and stay shared across devices (the next chat from any client
/// uses the rotated key automatically).
/// </summary>
public sealed class OpenClawChatSessionKeyProvider : IChatSessionKeyProvider, IDisposable
{
    private readonly string _baseKey;
    private readonly string _statePath;
    private readonly ILogger<OpenClawChatSessionKeyProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _currentKey;
    private int _generation;

    public OpenClawChatSessionKeyProvider(
        IOptions<OpenClawOptions> options,
        ILogger<OpenClawChatSessionKeyProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _baseKey = options.Value.MainSessionKey;
        _statePath = ResolveStatePath(options.Value.DeviceIdentityPath);
        _logger = logger;

        // Synchronous load on construction is fine — singleton, called once
        // at host startup, file < 100 bytes.
        var loaded = TryLoadGeneration();
        _generation = loaded;
        _currentKey = ComposeKey(_baseKey, loaded);
        _logger.LogInformation(
            "Chat session key initialised: {SessionKey} (generation {Generation})",
            _currentKey, _generation);
    }

    public string MainSessionKey => Volatile.Read(ref _currentKey!);

    public async Task<string> RotateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _generation++;
            _currentKey = ComposeKey(_baseKey, _generation);
            await PersistGenerationAsync(_generation, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Chat session rotated to {SessionKey} (generation {Generation})",
                _currentKey, _generation);
            return _currentKey;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private static string ResolveStatePath(string identityPath)
    {
        // Co-locate with the device identity (typically /data/) so a single
        // backed-up volume captures both. Same atomic-write conventions.
        var directory = Path.GetDirectoryName(identityPath);
        var filename = "seren-session.json";
        return string.IsNullOrEmpty(directory) ? filename : Path.Combine(directory, filename);
    }

    private static string ComposeKey(string baseKey, int generation) =>
        generation == 0 ? baseKey : $"{baseKey}-g{generation}";

    private int TryLoadGeneration()
    {
        if (!File.Exists(_statePath))
        {
            return 0;
        }
        try
        {
            using var stream = File.OpenRead(_statePath);
            var state = JsonSerializer.Deserialize(stream, ChatSessionStateContext.Default.ChatSessionState);
            return state?.Generation ?? 0;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex,
                "Failed to load chat session state from {Path}; resetting generation to 0",
                _statePath);
            return 0;
        }
    }

    private async Task PersistGenerationAsync(int generation, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tmp = _statePath + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                new ChatSessionState(generation),
                ChatSessionStateContext.Default.ChatSessionState,
                ct).ConfigureAwait(false);
        }
        File.Move(tmp, _statePath, overwrite: true);
    }
}

internal sealed record ChatSessionState(
    [property: JsonPropertyName("generation")] int Generation);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(ChatSessionState))]
internal sealed partial class ChatSessionStateContext : JsonSerializerContext;
