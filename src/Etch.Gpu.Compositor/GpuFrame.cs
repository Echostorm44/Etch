using Etch.Gpu.SwapChains;

namespace Etch.Gpu.Compositor;

public static class GpuFrame
{
    public static FrameContext Begin(Device device, Surface surface, uint frameIndex, SwapChainConfig config)
    {
        var swapChain = SwapChain.Configure(device, surface, config);

        SurfaceTextureResult result = swapChain.AcquireFrame(out SurfaceTexture texture);
        if (result == SurfaceTextureResult.Outdated)
        {
            swapChain.Resize(config.Width, config.Height);
            texture.Dispose();
            result = swapChain.AcquireFrame(out texture);
        }

        CommandEncoder encoder = device.CreateCommandEncoder();
        return new FrameContext(swapChain, encoder, texture, result, ownsTexture: true);
    }
}