using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Etch.Gpu.Descriptors;

namespace Etch.Gpu.Compositor;

public enum BindGroupCacheTier
{
    PerFrame,
    PerPass,
    PerDraw
}

public sealed unsafe class BindGroupCacheScope : IDisposable
{
    private readonly Dictionary<BindGroupKey, BindGroup> _cache;
    private readonly List<BindGroupKey> _accessOrder;
    private readonly int _maxCapacity;
    private int _disposed;

    public BindGroupCacheScope(int maxCapacity = 2048)
    {
        _cache = new Dictionary<BindGroupKey, BindGroup>();
        _accessOrder = new List<BindGroupKey>();
        _maxCapacity = maxCapacity;
    }

    public BindGroup GetOrCreate(Device device, BindGroupLayout layout, ReadOnlySpan<BindGroupEntry> entries)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var key = new BindGroupKey(layout, entries);

        if (_cache.TryGetValue(key, out var existing))
        {
            MoveToFront(key);
            return existing;
        }

        if (_cache.Count >= _maxCapacity)
        {
            EvictOldest();
        }

        var bindGroup = CreateBindGroup(device, layout, entries);
        _cache[key] = bindGroup;
        _accessOrder.Add(key);
        return bindGroup;
    }

    public void BeginFrame()
    {
        ClearTier(BindGroupCacheTier.PerDraw);
    }

    public void ClearTier(BindGroupCacheTier tier)
    {
        if (tier == BindGroupCacheTier.PerDraw)
        {
            foreach (var kvp in _cache)
            {
                kvp.Value.Dispose();
            }
            _cache.Clear();
            _accessOrder.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed != 0)
        {
            return;
        }
        _disposed = 1;

        foreach (var kvp in _cache)
        {
            kvp.Value.Dispose();
        }
        _cache.Clear();
        _accessOrder.Clear();
    }

    private static BindGroup CreateBindGroup(Device device, BindGroupLayout layout, ReadOnlySpan<BindGroupEntry> entries)
    {
        BindGroupEntry* entriesPtr = stackalloc BindGroupEntry[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            entriesPtr[i] = entries[i];
        }

        var descriptor = new BindGroupDescriptor
        {
            NextInChain = IntPtr.Zero,
            Label = default,
            Layout = layout.Handle,
            EntryCount = (UIntPtr)entries.Length,
            Entries = (nint)entriesPtr
        };

        return device.CreateBindGroup(descriptor);
    }

    private void MoveToFront(BindGroupKey key)
    {
        _accessOrder.Remove(key);
        _accessOrder.Add(key);
    }

    private void EvictOldest()
    {
        if (_accessOrder.Count > 0)
        {
            var oldest = _accessOrder[0];
            _accessOrder.RemoveAt(0);
            if (_cache.TryGetValue(oldest, out var old))
            {
                old.Dispose();
                _cache.Remove(oldest);
            }
        }
    }

    private readonly struct BindGroupKey : IEquatable<BindGroupKey>
    {
        private readonly BindGroupLayout _layout;
        private readonly uint[] _bindingHashes;

        public BindGroupKey(BindGroupLayout layout, ReadOnlySpan<BindGroupEntry> entries)
        {
            _layout = layout;
            _bindingHashes = new uint[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                _bindingHashes[i] = ComputeEntryHash(entries[i]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ComputeEntryHash(BindGroupEntry entry)
        {
            uint hash = 17;
            hash = hash * 31 + entry.Binding;
            hash = hash * 31 + (uint)(entry.Buffer.GetHashCode());
            hash = hash * 31 + (uint)(entry.Offset.GetHashCode());
            hash = hash * 31 + (uint)(entry.Size.GetHashCode());
            hash = hash * 31 + (uint)(entry.Sampler.GetHashCode());
            hash = hash * 31 + (uint)(entry.TextureView.GetHashCode());
            return hash;
        }

        public override int GetHashCode()
        {
            uint hash = 23;
            hash = hash * 31 + (uint)_layout.Handle.GetHashCode();
            for (int i = 0; i < _bindingHashes.Length; i++)
            {
                hash = hash * 31 + _bindingHashes[i];
            }
            return (int)hash;
        }

        public bool Equals(BindGroupKey other) => EqualsImpl(other);

        public override bool Equals(object? obj) => obj is BindGroupKey other && EqualsImpl(other);

        private bool EqualsImpl(BindGroupKey other)
        {
            if (!_layout.Handle.Equals(other._layout.Handle))
            {
                return false;
            }
            if (_bindingHashes.Length != other._bindingHashes.Length)
            {
                return false;
            }
            for (int i = 0; i < _bindingHashes.Length; i++)
            {
                if (_bindingHashes[i] != other._bindingHashes[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
