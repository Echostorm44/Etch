// Licensed under the MIT license.
// Copyright (c) CascadeUI project authors.

namespace Etch.Text.Atlas;

using System;
using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;

/// <summary>
/// A single page of the multi-page glyph atlas: one <see cref="LruCache"/> plus its texels — a GPU
/// texture for the GPU composer, or plain memory (<see cref="Pixels"/>) for the CPU composer.
/// The page can grow (<see cref="Grow"/>): its texels are copied to the top-left of the larger
/// page, so every glyph keeps its texel coordinates.
/// </summary>
public sealed class GlyphAtlasPage : IDisposable
{
    private readonly Device device;
    private readonly TextureFormat format;
    private readonly int bytesPerPixel;
    private readonly bool gpu;
    private readonly TextureUploadBatch? uploads;

    /// <summary>The page texture (GPU pages only; invalid otherwise). Replaced when the page grows.</summary>
    public Texture Texture { get; private set; }

    /// <summary>The page's texture view (GPU pages only; invalid otherwise). Replaced when the page grows.</summary>
    public TextureView View { get; private set; }

    /// <summary>The page's texels, row-major, stride <c>Dimension × bytes per pixel</c> (memory pages only). Replaced when the page grows.</summary>
    public byte[]? Pixels { get; private set; }

    internal LruCache Cache { get; }

    /// <summary>Page edge length in texels.</summary>
    public int Dimension { get; private set; }

    /// <summary>Bytes the page's texels occupy (GPU texture or memory).</summary>
    public long Bytes => (long)Dimension * Dimension * bytesPerPixel;

    private bool disposed;

    /// <summary>Creates a GPU page.</summary>
    public GlyphAtlasPage(Device device, int dimension, TextureFormat format, int rowHeight, int bytesPerPixel)
    {
        this.device = device;
        gpu = true;
        uploads = new TextureUploadBatch(device);
        this.format = format;
        this.bytesPerPixel = bytesPerPixel;
        Dimension = dimension;
        (Texture, View) = CreateTexture(dimension);
        Cache = new LruCache(dimension * dimension, dimension, dimension, rowHeight);
    }

    /// <summary>Creates a memory page.</summary>
    public GlyphAtlasPage(int dimension, int rowHeight, int bytesPerPixel)
    {
        this.bytesPerPixel = bytesPerPixel;
        Dimension = dimension;
        Pixels = new byte[dimension * dimension * bytesPerPixel];
        Cache = new LruCache(dimension * dimension, dimension, dimension, rowHeight);
    }

    /// <summary>
    /// Enlarges the page to <paramref name="dimension"/>² texels, keeping every glyph where it is
    /// (the old texels become the top-left corner; the rest starts empty).
    /// </summary>
    internal unsafe void Grow(int dimension)
    {
        if (dimension <= Dimension)
        {
            return;
        }
        int old = Dimension;
        if (!gpu)
        {
            var grown = new byte[dimension * dimension * bytesPerPixel];
            int oldRow = old * bytesPerPixel;
            int newRow = dimension * bytesPerPixel;
            for (int row = 0; row < old; row++)
            {
                Pixels!.AsSpan(row * oldRow, oldRow).CopyTo(grown.AsSpan(row * newRow, oldRow));
            }
            Pixels = grown;
        }
        else
        {
            var (texture, view) = CreateTexture(dimension);
            // Uploads queued for the old texture run before this submission's copy.
            using var encoder = device.CreateCommandEncoder();
            encoder.CopyTextureToTexture(Texture, 0, default, texture, 0, default,
                new Extent3D { Width = (uint)old, Height = (uint)old, DepthOrArrayLayers = 1 });
            using var commands = encoder.Finish();
            Span<CommandBuffer> submit = stackalloc CommandBuffer[1];
            submit[0] = commands;
            device.Queue.Submit(submit);
            ReleaseTexture();
            Texture = texture;
            View = view;
        }
        Dimension = dimension;
        Cache.Grow(dimension);
    }

    /// <summary>Glyph uploads waiting for <see cref="FlushUploads"/> (GPU pages).</summary>
    public int PendingUploads => uploads?.PendingCount ?? 0;

    /// <summary>
    /// Queues a GPU page's upload of a glyph bitmap (rows tightly packed) into <paramref name="region"/>;
    /// <see cref="FlushUploads"/> writes the texture.
    /// </summary>
    internal void QueueUpload(AtlasRegion region, ReadOnlySpan<byte> bitmap)
        => uploads!.Add(region.U, region.V, 0, region.W, region.H, bytesPerPixel, bitmap);

    /// <summary>Records the queued glyph uploads into <paramref name="encoder"/> (GPU pages; no-op when none).</summary>
    public void FlushUploads(CommandEncoder encoder) => uploads?.Flush(encoder, Texture);

    /// <summary>Forgets every glyph and replaces the texels with an empty page of <paramref name="dimension"/>² texels.</summary>
    internal void Recreate(int dimension)
    {
        Cache.Reset(dimension);
        uploads?.Clear();
        if (dimension == Dimension)
        {
            return;
        }
        if (!gpu)
        {
            Pixels = new byte[dimension * dimension * bytesPerPixel];
        }
        else
        {
            ReleaseTexture();
            (Texture, View) = CreateTexture(dimension);
        }
        Dimension = dimension;
    }

    private (Texture Texture, TextureView View) CreateTexture(int dimension)
    {
        var texture = device.CreateTexture(new TextureDescriptor
        {
            Size = new Extent3D { Width = (uint)dimension, Height = (uint)dimension, DepthOrArrayLayers = 1 },
            Format = format,
            Dimension = TextureDimension.D2,
            Usage = (uint)(TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.CopySrc),
            MipLevelCount = 1,
            SampleCount = 1,
        });
        return (texture, texture.CreateView());
    }

    private void ReleaseTexture()
    {
        if (!View.IsInvalid)
        {
            View.Dispose();
        }
        if (!Texture.IsInvalid)
        {
            Texture.Dispose();
        }
        View = default;
        Texture = default;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        uploads?.Dispose();
        ReleaseTexture();
        Pixels = null;
    }
}
