using System;

namespace Etch.Effects.Blur;

/// <summary>
/// Octave arithmetic for the dual-filter blur. The blur itself runs on the GPU
/// (<c>Etch.Gpu.Compositor.Pipelines.BlurGpuPipeline</c>) with a CPU reference
/// (<c>Etch.Raster.Cpu.Blur.BjorgeBlur</c>); both use these to agree on the pass count.
/// </summary>
public static class DualFilterBlur
{
    public const int MaxOctaves = 6;

    /// <summary>Down/up octave pairs for <paramref name="radiusPx"/>: ceil(log2(radius + 1)), at most <see cref="MaxOctaves"/>.</summary>
    public static int OctaveCount(float radiusPx)
    {
        if (radiusPx <= 0f)
            return 0;

        double octaves = Math.Ceiling(Math.Log2(radiusPx + 1.0));
        int result = (int)octaves;
        return result > MaxOctaves ? MaxOctaves : result;
    }

    /// <summary>
    /// Octaves actually run on a <paramref name="width"/> x <paramref name="height"/> image:
    /// <see cref="OctaveCount"/>, stopped before a level would shrink below one texel.
    /// </summary>
    public static int EffectiveOctaves(float radiusPx, int width, int height)
    {
        int octaves = OctaveCount(radiusPx);
        while (octaves > 0 && ((width >> octaves) == 0 || (height >> octaves) == 0))
        {
            octaves--;
        }
        return octaves;
    }
}
