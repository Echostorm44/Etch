using Etch.Gpu.Native;
using Etch.Gpu.Validation;

namespace Etch.Gpu.SwapChains;

// ═══════════════════════════════════════════════════════════════════════════
// SwapChain wraps surface configuration and the Acquire/Present loop over a
// wgpu-native WGPUSurface. No actual swap-chain handle exists in v29: the
// surface itself carries configuration and hands out WGPUSurfaceTextures.
//
// Acquire statuses: SurfaceGetCurrentTextureStatus (Enums.cs). Lost means the
// surface, not the device; a lost device's acquire reports Error.
//
// Device loss: with Etch's wgpu-native (native/wgpu-native/patches) acquiring
// from, submitting to, presenting on and configuring with a lost device are
// not fatal. They fail, and the loss reaches the device-lost callback
// (DeviceLossWatch). A swap chain configured with that watch reports
// SurfaceTextureResult.DeviceLost instead of a bare Error.
// ═══════════════════════════════════════════════════════════════════════════

public readonly struct SwapChain : IDisposable
{
    private const uint StatusSuccess = 1; // WGPUStatus_Success

    private readonly Surface _surface;
    private readonly Device _device;
    private readonly SwapChainConfig _config;
    private readonly DeviceLossWatch? _lossWatch;

    private SwapChain(Device device, Surface surface, SwapChainConfig config, DeviceLossWatch? lossWatch)
    {
        _device = device;
        _surface = surface;
        _config = config;
        _lossWatch = lossWatch;
    }

    /// <summary>
    /// Configures <paramref name="surface"/> for presentation with <paramref name="device"/>.
    /// </summary>
    /// <param name="device">The device that renders and presents the frames.</param>
    /// <param name="surface">The window surface.</param>
    /// <param name="config">Format, size, present mode, alpha mode and usage.</param>
    /// <param name="lossWatch">
    /// The watch attached to <paramref name="device"/>'s descriptor, if any. With it,
    /// <see cref="AcquireFrame"/> reports <see cref="SurfaceTextureResult.DeviceLost"/> once the
    /// device is lost; without it, a lost device's acquire reports <see cref="SurfaceTextureResult.Error"/>.
    /// </param>
    public static SwapChain Configure(Device device, Surface surface, SwapChainConfig config, DeviceLossWatch? lossWatch = null)
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
        return new SwapChain(device, surface, config, lossWatch);
    }

    /// <summary>
    /// Acquires the next frame. On anything but <see cref="SurfaceTextureResult.Ok"/>,
    /// <paramref name="texture"/> may still be valid (a suboptimal or outdated frame) and must be
    /// disposed or presented; check <see cref="SurfaceTexture.IsValid"/>.
    /// </summary>
    public unsafe SurfaceTextureResult AcquireFrame(out SurfaceTexture texture)
    {
        // A lost device cannot produce a frame: skip the native call (it would only fail).
        if (_lossWatch is not null && _lossWatch.IsLost)
        {
            texture = default;
            return SurfaceTextureResult.DeviceLost;
        }

        WGPUSurfaceTexture nativeTexture = default;
        WebGPU.SurfaceGetCurrentTexture(_surface.Handle, (nint)(&nativeTexture));

        SurfaceTextureResult result = (SurfaceGetCurrentTextureStatus)nativeTexture.Status switch
        {
            SurfaceGetCurrentTextureStatus.SuccessOptimal => SurfaceTextureResult.Ok,
            SurfaceGetCurrentTextureStatus.SuccessSuboptimal => SurfaceTextureResult.Ok, // still renderable
            SurfaceGetCurrentTextureStatus.Timeout => SurfaceTextureResult.Timeout,
            SurfaceGetCurrentTextureStatus.Outdated => SurfaceTextureResult.Outdated,
            SurfaceGetCurrentTextureStatus.Lost => SurfaceTextureResult.Lost,
            SurfaceGetCurrentTextureStatus.Occluded => SurfaceTextureResult.Occluded,
            _ => SurfaceTextureResult.Error,
        };

        // The acquire itself can be what finds the device lost (the callback has run by now).
        if (result == SurfaceTextureResult.Error && _lossWatch is not null && _lossWatch.IsLost)
        {
            result = SurfaceTextureResult.DeviceLost;
        }

        if (nativeTexture.Texture.IsInvalid)
        {
            texture = default;
            return result;
        }

        TextureViewHandle view = WebGPU.TextureCreateView(nativeTexture.Texture, 0);
        texture = new SurfaceTexture(nativeTexture.Texture, view);
        return result;
    }

    /// <summary>
    /// Presents the acquired frame and releases <paramref name="texture"/>.
    /// </summary>
    /// <returns>
    /// False when the frame could not be presented (a lost device, say: the loss is reported to
    /// the device's <see cref="DeviceLossWatch"/>). The texture is released either way.
    /// </returns>
    public bool Present(SurfaceTexture texture)
    {
        bool presented = Present();
        texture.Dispose();
        return presented;
    }

    /// <summary>
    /// Presents the acquired frame without releasing its texture; the caller still owns it and
    /// disposes it afterwards.
    /// </summary>
    /// <returns>False when the frame could not be presented (a lost device, say).</returns>
    public bool Present()
    {
        return WebGPU.SurfacePresent(_surface.Handle) == StatusSuccess;
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
        // A default (never configured) swap chain has no surface to unconfigure.
        if (!_surface.Handle.IsInvalid)
        {
            WebGPU.SurfaceUnconfigure(_surface.Handle);
        }
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
