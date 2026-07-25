using Etch.Gpu.Native;

namespace Etch.Gpu.SwapChains;

// ═══════════════════════════════════════════════════════════════════════════
// SwapChain wraps surface configuration and the Acquire/Present loop over a
// wgpu-native WGPUSurface. No actual swap-chain handle exists in v29: the
// surface itself carries configuration and hands out WGPUSurfaceTextures.
//
// v29 status codes returned by SurfaceGetCurrentTexture:
//   1 = SuccessOptimal
//   2 = SuccessSuboptimal  (both treated as Ok for rendering)
//   3 = Timeout
//   4 = Outdated
//   5 = Lost
//   6 = OutOfMemory
//   7 = DeviceLost
//   8 = Error
// ═══════════════════════════════════════════════════════════════════════════

public readonly struct SwapChain : IDisposable
{
    private readonly Surface _surface;
    private readonly Device _device;
    private readonly SwapChainConfig _config;

    private SwapChain(Device device, Surface surface, SwapChainConfig config)
    {
        _device = device;
        _surface = surface;
        _config = config;
    }

    public static SwapChain Configure(Device device, Surface surface, SwapChainConfig config)
    {
        if (surface.Handle.IsInvalid)
        {
            Panic.ArgumentOutOfRange(nameof(surface), "Surface is not valid.");
        }

        if (config.Width == 0 || config.Height == 0)
        {
            Panic.ArgumentOutOfRange(nameof(config.Width), "Swap chain width and height must be non-zero.");
        }

        ApplyConfiguration(surface, device, config, config.Width, config.Height);
        return new SwapChain(device, surface, config);
    }

    public unsafe SurfaceTextureResult AcquireFrame(out SurfaceTexture texture)
    {
        WGPUSurfaceTexture nativeTexture = default;
        WebGPU.SurfaceGetCurrentTexture(_surface.Handle, (nint)(&nativeTexture));

        SurfaceTextureResult result = nativeTexture.Status switch
        {
            1 => SurfaceTextureResult.Ok,          // SuccessOptimal
            2 => SurfaceTextureResult.Ok,          // SuccessSuboptimal → still renderable
            3 => SurfaceTextureResult.Timeout,
            4 => SurfaceTextureResult.Outdated,
            5 => SurfaceTextureResult.Lost,
            6 => SurfaceTextureResult.OutOfMemory,
            7 => SurfaceTextureResult.DeviceLost,
            _ => SurfaceTextureResult.Error,
        };

        if (nativeTexture.Texture.IsInvalid)
        {
            texture = default;
            return result;
        }

        TextureViewHandle view = WebGPU.TextureCreateView(nativeTexture.Texture, 0);
        texture = new SurfaceTexture(nativeTexture.Texture, view);
        return result;
    }

    public void Present(SurfaceTexture texture)
    {
        WebGPU.SurfacePresent(_surface.Handle);
        texture.Dispose();
    }

    public void Resize(uint width, uint height)
    {
        if (width == 0 || height == 0)
        {
            return;
        }

        WebGPU.SurfaceUnconfigure(_surface.Handle);
        ApplyConfiguration(_surface, _device, _config, width, height);
    }

    public void Dispose()
    {
        WebGPU.SurfaceUnconfigure(_surface.Handle);
    }

    private static unsafe void ApplyConfiguration(Surface surface, Device device, SwapChainConfig config, uint width, uint height)
    {
        WGPUSurfaceConfiguration nativeConfig = default;
        nativeConfig.NextInChain = null;
        nativeConfig.Device = device.Handle;
        nativeConfig.Format = (uint)config.Format;
        nativeConfig.Usage = (ulong)config.Usage;
        nativeConfig.Width = width;
        nativeConfig.Height = height;
        nativeConfig.ViewFormatCount = 0;
        nativeConfig.ViewFormats = null;
        nativeConfig.AlphaMode = (uint)config.AlphaMode;
        nativeConfig.PresentMode = (uint)config.PresentMode;

        WebGPU.SurfaceConfigure(surface.Handle, (nint)(&nativeConfig));
    }
}
