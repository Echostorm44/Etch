using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Etch.Text.Shape;

/// <summary>
/// LRU cache for shaped text runs.
/// Keyed by the full shaping signature (text hash, face id, size, BiDi level, script tag).
/// On cache hit the original text is re-compared to guard against hash collisions.
/// </summary>
public sealed class ShapingCache
{
    private readonly int _capacity;
    private readonly Dictionary<ShapingKey, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _lru;

    // Thread-local xxHash3 instance to avoid allocation per hash.
    [ThreadStatic]
    private static XxHash3? s_hasher;

    public ShapingCache(int capacity = 4096)
    {
        _capacity = capacity;
        _map = new Dictionary<ShapingKey, LinkedListNode<Entry>>(capacity);
        _lru = new LinkedList<Entry>();
    }

    /// <summary>
    /// Attempt to retrieve a cached <see cref="ShapedRun"/> for <paramref name="request"/>.
    /// On miss, shapes via <see cref="Shaper.Shape"/> and inserts the result.
    /// </summary>
    public bool TryShape(ShapeRequest request, out ShapedRun result)
    {
        var key = BuildKey(request);
        string text = request.Text.ToString();

        lock (_map)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // Collision guard: compare stored text against request text.
                if (node.ValueRef.Text == text)
                {
                    // Hit — move to front of LRU list.
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    result = node.ValueRef.Run;
                    return true;
                }

                // Hash collision — evict the colliding entry and fall through to miss.
                _lru.Remove(node);
                _map.Remove(key);
            }

            // Miss — shape and insert at front.
            result = Shaper.Shape(request);
            var newNode = _lru.AddFirst(new Entry(key, text, result));
            _map[key] = newNode;

            while (_lru.Count > _capacity)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
            }

            return false;
        }
    }

    /// <summary>Current number of cached entries.</summary>
    public int Count
    {
        get { lock (_map) { return _lru.Count; } }
    }

    /// <summary>Maximum number of entries before LRU eviction.</summary>
    public int Capacity => _capacity;

    // ------------------------------------------------------------------
    // Key building
    // ------------------------------------------------------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ShapingKey BuildKey(ShapeRequest request)
    {
        ulong textHash = HashText(request.Text);
        int faceId = request.Face.Id;
        ushort sizeUnits = (ushort)(request.Face.PointSize * 64f);
        byte level = (byte)request.BiDiLevel;
        ushort scriptTag = HashScriptTag(request.ScriptTag);
        return new ShapingKey(textHash, faceId, sizeUnits, level, scriptTag);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong HashText(ReadOnlySpan<char> text)
    {
        var hasher = s_hasher ?? new XxHash3();
        s_hasher = hasher;
        hasher.Reset();

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(text);
        hasher.Append(bytes);
        return hasher.GetCurrentHashAsUInt64();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort HashScriptTag(string tag)
    {
        // FNV-1a 16-bit hash — good enough for the small keyspace of script tags.
        ushort hash = 0x811C;
        for (int i = 0; i < tag.Length; i++)
        {
            hash ^= (ushort)tag[i];
            hash *= 0x0101;
        }
        return hash;
    }

    // ------------------------------------------------------------------
    // Entry
    // ------------------------------------------------------------------
    private readonly struct Entry
    {
        public ShapingKey Key { get; }
        public string Text { get; }
        public ShapedRun Run { get; }

        public Entry(ShapingKey key, string text, ShapedRun run)
        {
            Key = key;
            Text = text;
            Run = run;
        }
    }
}
