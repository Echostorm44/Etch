namespace Etch.Raster.Cpu.Blur;

/// <summary>
/// Kernel weights of the dual-filter blur, shared by the CPU reference and the GPU shaders
/// (shaders/blur/down.wgsl, up.wgsl). Each kernel sums to 1 so a pass preserves brightness.
/// </summary>
public static class BlurTaps
{
    /// <summary>Downsample: plain average of the 2x2 source texels under a destination texel.</summary>
    public const float DownWeight = 1f / 4f;

    /// <summary>Upsample 3x3 tent around the source texel: centre 4, edges 2, corners 1, over 16.</summary>
    public const float UpCenterWeight = 4f / 16f;
    public const float UpEdgeWeight = 2f / 16f;
    public const float UpCornerWeight = 1f / 16f;
}
