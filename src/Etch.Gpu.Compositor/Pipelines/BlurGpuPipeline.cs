using System;
using System.Runtime.InteropServices;
using Etch.Effects.Blur;
using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Pipelines;

namespace Etch.Gpu.Compositor.Pipelines;

public sealed unsafe class BlurGpuPipeline : IDisposable
{
    private readonly Device _device;
    private readonly BlurPipeline _blurPipeline;
    private Texture _scratchA;
    private Texture _scratchB;
    private int _scratchWidth;
    private int _scratchHeight;
    private bool _disposed;

    public BlurGpuPipeline(Device device)
    {
        _device = device;
        _blurPipeline = new BlurPipeline(device);
        _scratchA = new Texture();
        _scratchB = new Texture();
        _scratchWidth = 0;
        _scratchHeight = 0;
    }

    public void EnsureScratchTextures(int width, int height)
    {
        int halfW = Math.Max(1, width / 2);
        int halfH = Math.Max(1, height / 2);

        if (_scratchWidth == halfW && _scratchHeight == halfH)
        {
            return;
        }

        if (!_scratchA.IsInvalid)
        {
            _scratchA.Dispose();
        }
        if (!_scratchB.IsInvalid)
        {
            _scratchB.Dispose();
        }

        _scratchA = CreateHalfResTexture(halfW, halfH);
        _scratchB = CreateHalfResTexture(halfW, halfH);
        _scratchWidth = halfW;
        _scratchHeight = halfH;
    }

    private Texture CreateHalfResTexture(int width, int height)
    {
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

        return _device.CreateTexture(descriptor);
    }

    public void SetSurfaceSize(float width, float height)
    {
        float halfW = Math.Max(1f, width / 2f);
        float halfH = Math.Max(1f, height / 2f);
        _blurPipeline.SetSurfaceSize(halfW, halfH);
    }

    public void Record(FrameContext frame, Texture source, int sourceWidth, int sourceHeight, float radiusPx, Texture destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (radiusPx <= 0f)
        {
            return;
        }

        int octaveCount = DualFilterBlur.OctaveCount(radiusPx);
        if (octaveCount == 0)
        {
            return;
        }

        EnsureScratchTextures(sourceWidth, sourceHeight);

        int numPasses = 2 * octaveCount;
        Texture current = source;
        Texture scratchA = _scratchA;
        Texture scratchB = _scratchB;

        for (int pass = 0; pass < numPasses; pass++)
        {
            bool isDownPass = pass < octaveCount;
            bool isLastPass = pass == numPasses - 1;
            bool useScratchAAsDest = pass % 2 == 0;

            Texture dest;
            if (isLastPass)
            {
                dest = destination;
            }
            else if (useScratchAAsDest)
            {
                dest = scratchA;
            }
            else
            {
                dest = scratchB;
            }

            if (isDownPass)
            {
                RenderDownPass(frame, current, dest);
            }
            else
            {
                RenderUpPass(frame, current, dest);
            }

            if (!isLastPass)
            {
                current = dest;
            }
        }
    }

    private void RenderDownPass(FrameContext frame, Texture source, Texture destination)
    {
        using TextureView srcView = CreateTextureView(source);
        using TextureView dstView = CreateTextureView(destination);

        var colorAttachment = new RenderPassColorAttachment
        {
            View = dstView.Handle,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0, G = 0, B = 0, A = 1 }
        };

        var pass = frame.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = (IntPtr)(&colorAttachment)
        });

        pass.SetPipeline(_blurPipeline.DownPipeline);
        pass.Draw(4);
        pass.End();
    }

    private void RenderUpPass(FrameContext frame, Texture source, Texture destination)
    {
        using TextureView srcView = CreateTextureView(source);
        using TextureView dstView = CreateTextureView(destination);

        var colorAttachment = new RenderPassColorAttachment
        {
            View = dstView.Handle,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0, G = 0, B = 0, A = 1 }
        };

        var pass = frame.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = (IntPtr)(&colorAttachment)
        });

        pass.SetPipeline(_blurPipeline.UpPipeline);
        pass.Draw(4);
        pass.End();
    }

    private static TextureView CreateTextureView(Texture texture)
    {
        var descriptor = new TextureViewDescriptor
        {
            Format = TextureFormat.Bgra8UnormSrgb,
            Dimension = TextureViewDimension.D2,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1
        };

        return texture.CreateView(descriptor);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _blurPipeline.Dispose();
        if (!_scratchA.IsInvalid)
        {
            _scratchA.Dispose();
        }
        if (!_scratchB.IsInvalid)
        {
            _scratchB.Dispose();
        }
    }
}
