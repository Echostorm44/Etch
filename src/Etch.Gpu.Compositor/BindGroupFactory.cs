using System;
using System.Collections.Generic;
using Etch.Gpu.Descriptors;

namespace Etch.Gpu.Compositor;

public sealed class BindGroupFactory : IDisposable
{
    private readonly Device _device;
    private readonly BindGroupCacheScope _perFrame;
    private readonly BindGroupCacheScope _perPass;
    private readonly BindGroupCacheScope _perDraw;
    private bool _disposed;

    public BindGroupFactory(Device device)
    {
        _device = device;
        _perFrame = new BindGroupCacheScope(maxCapacity: 256);
        _perPass = new BindGroupCacheScope(maxCapacity: 512);
        _perDraw = new BindGroupCacheScope(maxCapacity: 2048);
    }

    public BindGroup GetOrCreate(BindGroupLayout layout, ReadOnlySpan<BindGroupEntry> entries, BindGroupCacheTier tier)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return tier switch
        {
            BindGroupCacheTier.PerFrame => _perFrame.GetOrCreate(_device, layout, entries),
            BindGroupCacheTier.PerPass => _perPass.GetOrCreate(_device, layout, entries),
            BindGroupCacheTier.PerDraw => _perDraw.GetOrCreate(_device, layout, entries),
            _ => default
        };
    }

    public void BeginFrame()
    {
        _perDraw.BeginFrame();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _perDraw.Dispose();
        _perPass.Dispose();
        _perFrame.Dispose();
    }
}
