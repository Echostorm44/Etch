// Licensed under the MIT license.
// Copyright (c) CascadeUI project authors.

namespace Etch.Text.Atlas;

using System;
using Etch.Gpu;
using Etch.Gpu.Descriptors;

/// <summary>
/// A single page of the multi-page glyph atlas: one <see cref="LruCache"/> plus its texels — a GPU
/// texture for the GPU composer, or plain memory (<see cref="Pixels"/>) for the CPU composer.
/// </summary>
public sealed class GlyphAtlasPage : IDisposable
{
    /// <summary>The page texture (GPU pages only; invalid otherwise).</summary>
    public Texture Texture { get; }

    /// <summary>The page's texture view (GPU pages only; invalid otherwise).</summary>
    public TextureView View { get; }

    /// <summary>The page's texels, row-major, stride <c>Dimension × bytes per pixel</c> (memory pages only).</summary>
    public byte[]? Pixels { get; }

    internal LruCache Cache { get; }

    /// <summary>Page edge length in texels.</summary>
    public int Dimension { get; }

    private bool disposed;

    /// <summary>Creates a GPU page.</summary>
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

    /// <summary>Creates a memory page.</summary>
    public GlyphAtlasPage(int dimension, int rowHeight, int bytesPerPixel)
    {
        Dimension = dimension;
        Pixels = new byte[dimension * dimension * bytesPerPixel];
        Cache = new LruCache(dimension * dimension, dimension, dimension, rowHeight);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (!View.IsInvalid)
        {
            View.Dispose();
        }
        if (!Texture.IsInvalid)
        {
            Texture.Dispose();
        }
    }
}