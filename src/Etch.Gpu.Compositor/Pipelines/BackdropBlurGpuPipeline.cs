using System;
using System.Runtime.InteropServices;
using Etch.Effects.Blur;
using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;
using Etch.Gpu.Pipelines;
using Etch.Primitives;
using Etch.Geometry;

namespace Etch.Gpu.Compositor.Pipelines;

public sealed unsafe class BackdropBlurGpuPipeline : IDisposable
{
    private readonly Device _device;
    private readonly BlurGpuPipeline _blurPipeline;
    private Texture _backdropCopy;
    private int _backdropWidth;
    private int _backdropHeight;
    private bool _disposed;

    public BackdropBlurGpuPipeline(Device device)
    {
        _device = device;
        _blurPipeline = new BlurGpuPipeline(device);
        _backdropWidth = 0;
        _backdropHeight = 0;
        _backdropCopy = new Texture();
    }

    public void EnsureBackdropTexture(int width, int height)
    {
        if (_backdropWidth == width && _backdropHeight == height)
        {
            return;
        }

        if (!_backdropCopy.IsInvalid)
        {
            _backdropCopy.Dispose();
        }

        var descriptor = new TextureDescriptor
        {
            Usage = (ulong)(TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst),
            Size = new Extent3D
            {
                Width = (uint)width,
                Height = (uint)height,
                DepthOrArrayLayers = 1
            },
            Format = TextureFormat.Bgra8UnormSrgb,
            SampleCount = 1,
            MipLevelCount = 1,
            Dimension = TextureDimension.D2
        };

        _backdropCopy = _device.CreateTexture(descriptor);
        _backdropWidth = width;
        _backdropHeight = height;
        _blurPipeline.SetSurfaceSize(width, height);
    }

    public void Record(FrameContext frame, Rect regionPx, float radiusPx, Texture backdrop, Texture destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int width = (int)(regionPx.MaxX - regionPx.MinX);
        int height = (int)(regionPx.MaxY - regionPx.MinY);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (radiusPx <= 0f)
        {
            return;
        }

        EnsureBackdropTexture(width, height);

        var srcOrigin = new WGPUOrigin3D
        {
            X = (uint)regionPx.MinX,
            Y = (uint)regionPx.MinY,
            Z = 0
        };

        var copySize = new Extent3D
        {
            Width = (uint)width,
            Height = (uint)height,
            DepthOrArrayLayers = 1
        };

        var dstOrigin = new WGPUOrigin3D { X = 0, Y = 0, Z = 0 };

        frame.CopyTextureToTexture(backdrop, 0, srcOrigin, _backdropCopy, 0, dstOrigin, copySize);

        _blurPipeline.Record(frame, _backdropCopy, width, height, radiusPx, destination);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _blurPipeline.Dispose();
        if (!_backdropCopy.IsInvalid)
        {
            _backdropCopy.Dispose();
        }
    }
}