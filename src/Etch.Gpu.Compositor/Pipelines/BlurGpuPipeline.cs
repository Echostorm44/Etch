using System;
using System.Collections.Generic;
using Etch.Effects.Blur;
using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Pipelines;

namespace Etch.Gpu.Compositor.Pipelines;

/// <summary>
/// Dual-filter blur on the GPU: downsample through a chain of half-size levels, then upsample back
/// into the destination. Matches the CPU reference <c>Etch.Raster.Cpu.Blur.BjorgeBlur</c> to rounding.
/// </summary>
public sealed unsafe class BlurGpuPipeline : IDisposable
{
    private readonly Device _device;
    private readonly BlurPipeline _blurPipeline;
    private readonly List<(Texture Texture, int Width, int Height)> _levels = new();
    private bool _disposed;

    /// <param name="device">Device the passes run on.</param>
    /// <param name="outputFormat">Format of the destination textures passed to <see cref="Record(CommandEncoder, Texture, int, int, float, Texture)"/>.</param>
    public BlurGpuPipeline(Device device, TextureFormat outputFormat = TextureFormat.Bgra8UnormSrgb)
    {
        _device = device;
        _blurPipeline = new BlurPipeline(device, outputFormat);
    }

    public void SetSurfaceSize(float width, float height)
    {
        _blurPipeline.SetSurfaceSize(width, height);
    }

    /// <summary>Records the blur into the frame's encoder.</summary>
    public void Record(FrameContext frame, Texture source, int sourceWidth, int sourceHeight, float radiusPx, Texture destination)
    {
        Record(frame.Encoder, source, sourceWidth, sourceHeight, radiusPx, destination);
    }

    /// <summary>
    /// Blurs <paramref name="source"/> (TextureBinding usage) into <paramref name="destination"/>
    /// (same size, RenderAttachment usage, the output format). Nothing is recorded when the radius
    /// yields no octaves; the caller keeps the unblurred source.
    /// </summary>
    public void Record(CommandEncoder encoder, Texture source, int sourceWidth, int sourceHeight, float radiusPx, Texture destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int octaves = DualFilterBlur.EffectiveOctaves(radiusPx, sourceWidth, sourceHeight);
        if (octaves == 0)
        {
            return;
        }

        EnsureLevels(sourceWidth, sourceHeight, octaves);

        // Views and bind groups must outlive recording; they are released after the encoder records.
        var transient = new List<IDisposable>(4 * octaves);
        try
        {
            TextureView previous = source.CreateView();
            transient.Add(previous);

            for (int k = 1; k <= octaves; k++)
            {
                TextureView target = _levels[k - 1].Texture.CreateView();
                transient.Add(target);
                BindGroup input = _blurPipeline.CreateTextureBindGroup(previous);
                transient.Add(input);
                _blurPipeline.RecordPass(encoder, _blurPipeline.DownPipeline, input, target);
                previous = target;
            }

            for (int k = octaves; k >= 1; k--)
            {
                bool last = k == 1;
                TextureView target = last ? destination.CreateView() : _levels[k - 2].Texture.CreateView();
                transient.Add(target);
                BindGroup input = _blurPipeline.CreateTextureBindGroup(previous);
                transient.Add(input);
                _blurPipeline.RecordPass(encoder, last ? _blurPipeline.OutputUpPipeline : _blurPipeline.UpPipeline, input, target);
                previous = target;
            }
        }
        finally
        {
            // wgpu keeps recorded resources alive until the work completes; releasing our
            // references here is safe.
            foreach (var resource in transient)
            {
                resource.Dispose();
            }
        }
    }

    // Level k (1-based) is half of level k-1; reused across frames while the source size holds.
    private void EnsureLevels(int width, int height, int octaves)
    {
        for (int k = 1; k <= octaves; k++)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            if (k <= _levels.Count && _levels[k - 1].Width == width && _levels[k - 1].Height == height)
            {
                continue;
            }

            if (k <= _levels.Count)
            {
                _levels[k - 1].Texture.Dispose();
            }

            var texture = _device.CreateTexture(new TextureDescriptor
            {
                Usage = (ulong)(TextureUsage.RenderAttachment | TextureUsage.TextureBinding),
                Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
                Format = BlurPipeline.IntermediateFormat,
                SampleCount = 1,
                MipLevelCount = 1,
                Dimension = TextureDimension.D2
            });

            if (k <= _levels.Count)
            {
                _levels[k - 1] = (texture, width, height);
            }
            else
            {
                _levels.Add((texture, width, height));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _blurPipeline.Dispose();
        foreach (var (texture, _, _) in _levels)
        {
            texture.Dispose();
        }
        _levels.Clear();
    }
}
