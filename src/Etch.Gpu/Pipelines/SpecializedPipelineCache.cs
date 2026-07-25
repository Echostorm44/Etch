using System;
using System.Collections.Generic;
using Etch.Shaders;

namespace Etch.Gpu.Pipelines;

public sealed class SpecializedPipelineCache
{
    private const int MaxPipelines = 256;

    private readonly Dictionary<int, LinkedListNode<CacheEntry>> _cache = new();
    private readonly LinkedList<CacheEntry> _lru = new();

    public RenderPipeline GetOrCreate<TKey>(TKey key, Func<TKey, RenderPipeline> factory)
        where TKey : struct, IShaderSpecKey
    {
        int hash = key.Hash;

        if (_cache.TryGetValue(hash, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.ValueRef.Pipeline;
        }

        var pipeline = factory(key);

        var entry = new CacheEntry(hash, pipeline);
        var newNode = _lru.AddFirst(entry);
        _cache[hash] = newNode;

        if (_cache.Count > MaxPipelines)
        {
            var lruNode = _lru.Last;
            if (lruNode != null)
            {
                _lru.RemoveLast();
                _cache.Remove(lruNode.Value.Hash);
                lruNode.Value.Pipeline.Dispose();
            }
        }

        return pipeline;
    }

    private readonly struct CacheEntry
    {
        public CacheEntry(int hash, RenderPipeline pipeline)
        {
            Hash = hash;
            Pipeline = pipeline;
        }

        public int Hash { get; }
        public RenderPipeline Pipeline { get; }
    }
}