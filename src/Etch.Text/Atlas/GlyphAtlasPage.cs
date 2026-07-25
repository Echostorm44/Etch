// Licensed under the MIT license.
// Copyright (c) CascadeUI project authors.

namespace Etch.Text.Atlas;

using System;
using System.Numerics;
using Etch.Gpu;
using Etch.Gpu.Descriptors;

/// <summary>
/// A single page of the multi-page glyph atlas.
/// Wraps one texture + one <see cref="LruCache"/>.
/// </summary>
public sealed class GlyphAtlasPage : IDisposable
{
    public Texture Texture { get; }
    public TextureView View { get; }
    internal LruCache Cache { get; }
    public int Dimension { get; }

    private bool disposed;

    public GlyphAtlasPage(Device device, int dimension, TextureFormat format, int rowHeight, int bytesPerPixel)
    {
        Dimension = dimension;
        var descriptor = new TextureDescriptor
        {
            Size = new Extent3D { Width = (uint)dimension, Height = (uint)dimension, DepthOrArrayLayers = 1 },
            Format = format,
            Dimension = TextureDimension.D2,
            Usage = (uint)(TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.CopySrc),
            MipLevelCount = 1,
            SampleCount = 1,
        };
        Texture = device.CreateTexture(descriptor);
        View = Texture.CreateView();
        Cache = new LruCache(dimension * dimension, dimension, dimension, rowHeight);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        View.Dispose();
        Texture.Dispose();
    }
}
