namespace Seren.Modules.VoxMind.F5Tts;

/// <summary>
/// Thread-safe LRU cache for resident TTS engines.
/// </summary>
/// <remarks>
/// One F5 engine per language is loaded on demand (~2-4 s cold-load for the
/// 3 ONNX models). Once loaded, the engine stays resident for follow-up calls.
/// When the cache overflows (default capacity = 2, FR + EN), the least-recently
/// used language is evicted and its ONNX sessions are released.
/// </remarks>
public sealed class LruEngineCache<TEngine> : IDisposable where TEngine : IDisposable
{
    private readonly int _capacity;
    private readonly LinkedList<(string Key, TEngine Engine)> _order = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, TEngine Engine)>> _index = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private bool _disposed;

    public LruEngineCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public int Count
    {
        get { lock (_gate)
            {
                return _index.Count;
            }
        }
    }

    public IReadOnlyList<string> ResidentKeys
    {
        get { lock (_gate)
            {
                return _index.Keys.ToList();
            }
        }
    }

    public bool TryGet(string key, out TEngine engine)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_gate)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                engine = node.Value.Engine;
                return true;
            }
            engine = default!;
            return false;
        }
    }

    /// <summary>
    /// Returns the cached engine for <paramref name="key"/>, or loads it via
    /// <paramref name="factory"/>. The factory is called outside the lock so
    /// it does not block other callers during the (slow) cold-load.
    /// </summary>
    public TEngine GetOrLoad(string key, Func<TEngine> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (TryGet(key, out var existing))
        {
            return existing;
        }

        var newEngine = factory();

        lock (_gate)
        {
            if (_index.TryGetValue(key, out var existingNode))
            {
                newEngine.Dispose();
                _order.Remove(existingNode);
                _order.AddFirst(existingNode);
                return existingNode.Value.Engine;
            }

            var node = new LinkedListNode<(string, TEngine)>((key, newEngine));
            _order.AddFirst(node);
            _index[key] = node;

            while (_index.Count > _capacity && _order.Last is { } evict)
            {
                _order.RemoveLast();
                _index.Remove(evict.Value.Key);
                evict.Value.Engine.Dispose();
            }

            return newEngine;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            foreach (var (Key, Engine) in _order)
            {
                Engine.Dispose();
            }

            _order.Clear();
            _index.Clear();
        }
    }
}
