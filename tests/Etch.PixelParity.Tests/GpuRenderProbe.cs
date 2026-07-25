using System;
using System.IO;
using Etch.Gpu;
using Etch.Gpu.Compositor.Pipelines;
using Etch.Gpu.SwapChains;
using Etch.Raster.Cpu;
using Etch.Scene;
using Etch.Scene.Serialization;
using Etch.Testing;

namespace Etch.PixelParity.Tests;

internal sealed class GpuRenderProbe : IDisposable
{
    private readonly Device _device;
    private readonly SwapChain _swapChain;
    private readonly StripCoverageGpuPipeline _pipeline;
    private bool _disposed;

    public GpuRenderProbe(Instance instance, Adapter adapter, Surface surface)
    {
        var deviceResult = AsyncRequest.RequestDeviceSync(instance, adapter);
        if (deviceResult.Status != RequestDeviceStatus.Success || deviceResult.Device.IsInvalid)
        {
            throw new InvalidOperationException("Failed to create device");
        }

        _device = deviceResult.Device;

        var swapChainConfig = new SwapChainConfig
        {
            Format = TextureFormat.Bgra8UnormSrgb,
            Width = 1920,
            Height = 1080,
            PresentMode = PresentMode.Fifo,
            AlphaMode = CompositeAlphaMode.Auto,
            Usage = TextureUsage.RenderAttachment
        };

        _swapChain = SwapChain.Configure(_device, surface, swapChainConfig);
        _pipeline = new StripCoverageGpuPipeline(_device);
    }

    public Rgba16f[] Render(string scenePath, uint width = 1920, uint height = 1080)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!File.Exists(scenePath))
            return Array.Empty<Rgba16f>();

        try
        {
            byte[] sceneBytes = File.ReadAllBytes(scenePath);
            var scene = SceneReader.Read(sceneBytes);
            byte[] rgba8 = SceneGpuRenderer.RenderToRgba8(scene, (int)width, (int)height);

            var result = new Rgba16f[rgba8.Length / 4];
            for (int i = 0; i < result.Length; i++)
            {
                int idx = i * 4;
                float b = rgba8[idx + 0] / 255.0f;
                float g = rgba8[idx + 1] / 255.0f;
                float r = rgba8[idx + 2] / 255.0f;
                float a = rgba8[idx + 3] / 255.0f;
                result[i] = Rgba16f.From(r, g, b, a);
            }
            return result;
        }
        catch
        {
            return Array.Empty<Rgba16f>();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pipeline.Dispose();
        _swapChain.Dispose();
        _device.Dispose();
    }
}
