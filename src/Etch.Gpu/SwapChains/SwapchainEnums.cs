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
    Lost = 4,
    OutOfMemory = 5,
    DeviceLost = 6,
    Error = 7,
}
