namespace Etch.Gpu.SwapChains;

// ═══════════════════════════════════════════════════════════════════════════
// Swap-chain specific enum: high-level status returned by AcquireFrame.
//
// The raw wgpu status (WGPUSurfaceGetCurrentTextureStatus in Etch.Gpu.Enums)
// distinguishes SuccessOptimal (1) and SuccessSuboptimal (2). We collapse
// both to Ok here because the red-triangle path treats them identically
// (the caller may re-configure on Outdated / Lost).
//
// PresentMode and CompositeAlphaMode live in Etch.Gpu.Enums now.
// ═══════════════════════════════════════════════════════════════════════════

public enum SurfaceTextureResult
{
    Ok = 0,
    Suboptimal = 1,
    Timeout = 2,
    Outdated = 3,
    /// <summary>The surface was lost (not the device): reconfigure or recreate it.</summary>
    Lost = 4,
    /// <summary>Not reported by the current wgpu-native (webgpu.h has no such status).</summary>
    OutOfMemory = 5,
    /// <summary>
    /// The device is lost (driver reset, GPU removed, <c>Device.Destroy</c>). Reported by a swap
    /// chain configured with the device's <see cref="Validation.DeviceLossWatch"/>.
    /// </summary>
    DeviceLost = 6,
    Error = 7,
    /// <summary>The window is occluded (wgpu.h); try again once it is visible.</summary>
    Occluded = 8,
}
